using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Reactive.Subjects;
using Dnbn.Configuration;
using Dnbn.Filters;
using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

/// <summary>
/// TCPクライアント実装
/// </summary>
public partial class TcpClient : ITcpClient, IAsyncDisposable
{
  private ClientConfig _config;
  private readonly ILogger<TcpClient>? _logger;
  private readonly List<IMessageFilter> _filters;
  private readonly ITransport _transport;
  private readonly MessageParser _parser;
  private readonly Subject<Message> _messageReceivedSubject = new();
  private Channel<SendRequest> _sendQueue = null!;
  private ChannelWriter<SendRequest> _sendQueueWriter = null!;
  private ChannelReader<SendRequest> _sendQueueReader = null!;
  private Task? _sendLoopTask;
  private readonly LinkedList<SendRequest> _pendingResponseRequests = new();
  private readonly object _pendingResponseRequestsLock = new();
  private CancellationTokenSource? _keepAliveTimerCts;
  private CancellationTokenSource _cancellationTokenSource = new();
  private CancellationTokenRegistration? _externalCancellationTokenRegistration;
  private bool _disposed = false;
  private TaskCompletionSource<Message>? _keepAliveResponseTcs;
  private bool _isIntentionalDisconnect = false;
  private Task? _reconnectTask;
  private readonly object _reconnectLock = new();
  private readonly object _configLock = new();

  // 統計情報
  private readonly ClientStats _stats = new();
  private readonly object _statsLock = new();

  private sealed class ClientStats
  {
    public DateTime? ConnectedAt;
    public DateTime? LastMessageReceivedAt;
    public long MessagesSent;
    public long MessagesReceived;
    public DateTime? LastKeepAliveSentAt;
    public DateTime? LastKeepAliveResponseReceivedAt;
    public int KeepAliveTimeoutCount;
    public int ErrorCount;
    public string? LastError;
    public DateTime? LastErrorAt;
    public int ConnectionRetryAttempts;
    public DateTime? LastRetryAttemptAt;
  }

  /// <summary>
  /// クライアント名
  /// </summary>
  public string Name => _config.Name;

  /// <summary>
  /// 接続状態
  /// </summary>
  public bool IsConnected => _transport.IsConnected;

  /// <summary>
  /// メッセージ受信イベント
  /// </summary>
  public event EventHandler<Message>? OnMessageReceived;

  /// <summary>
  /// 接続イベント
  /// </summary>
  public event EventHandler? OnConnected;

  /// <summary>
  /// 切断イベント
  /// </summary>
  public event EventHandler? OnDisconnected;

  /// <summary>
  /// エラーイベント
  /// </summary>
  public event EventHandler<Exception>? OnError;

  /// <summary>
  /// キープアライブ応答受信イベント
  /// </summary>
  public event EventHandler<Message>? OnKeepAliveResponseReceived;

  /// <summary>
  /// メッセージ受信のObservable
  /// </summary>
  public IObservable<Message> MessageReceived => _messageReceivedSubject;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="config">クライアント設定</param>
  /// <param name="transport">トランスポート実装</param>
  /// <param name="logger">ロガー（オプション）</param>
  /// <param name="filters">メッセージフィルター（オプション）</param>
  public TcpClient(
      ClientConfig config,
      ITransport transport,
      ILogger<TcpClient>? logger = null,
      IEnumerable<IMessageFilter>? filters = null)
  {
    _config = config;
    _transport = transport;
    _logger = logger;
    _filters = filters?.ToList() ?? new List<IMessageFilter>();

    var encoding = TcpMessageUtils.GetEncoding(config.Encoding);
    // 受信時の終端文字を決定：ReceiveMessageTerminatorが設定されている場合はそれを使用、未設定の場合はMessageTerminatorを使用
    string[]? receiveTerminators = config.ReceiveMessageTerminator ??
        (config.MessageTerminator != null ? new[] { config.MessageTerminator } : null);
    _parser = new MessageParser(
        encoding,
        receiveTerminators,
        config.FixedHeaderLength,
        config.FixedBodyLength,
        config.LengthFieldOffset,
        config.LengthFieldLength,
        config.MaxReceiveBufferBytes);

    // 送信キューを初期化
    InitializeSendQueue();
  }

  /// <summary>
  /// 送信キューを初期化
  /// </summary>
  private void InitializeSendQueue()
  {
    var channelOptions = new BoundedChannelOptions(1000)
    {
      FullMode = BoundedChannelFullMode.Wait
    };
    var channel = Channel.CreateBounded<SendRequest>(channelOptions);
    _sendQueue = channel;
    _sendQueueWriter = channel.Writer;
    _sendQueueReader = channel.Reader;
  }

  /// <summary>
  /// サーバーに接続
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task ConnectAsync(CancellationToken cancellationToken = default)
  {
    if (IsConnected)
    {
      return;
    }

    cancellationToken.ThrowIfCancellationRequested();

    // 既存の登録を破棄
    _externalCancellationTokenRegistration?.Dispose();

    // 外部CancellationTokenがキャンセルされたときに内部CancellationTokenSourceもキャンセルする
    _externalCancellationTokenRegistration = cancellationToken.Register(() =>
    {
      if (!_cancellationTokenSource.IsCancellationRequested)
      {
        _cancellationTokenSource.Cancel();
      }
    });

    // 外部CancellationTokenと内部CancellationTokenSourceを統合
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);

    // 接続リトライポリシーが設定されている場合は、リトライを実行
    if (_config.ConnectionRetryPolicy != null)
    {
      await RetryHelper.ExecuteConnectionRetryAsync(
          async () =>
          {
            await _transport.ConnectAsync(linkedCts.Token);
            lock (_statsLock)
            {
              _stats.ConnectedAt = DateTime.UtcNow;
              _stats.ConnectionRetryAttempts = 0; // 接続成功時にリセット
            }
            _logger?.LogInformation("TCP Client '{Name}' connected to {Host}:{Port}", Name, _config.RemoteHost, _config.RemotePort);

            OnConnected?.Invoke(this, EventArgs.Empty);

            // 送信ループを開始
            _sendLoopTask = Task.Run(SendLoopAsync, _cancellationTokenSource.Token);

            // 受信ループを開始
            _ = Task.Run(ReceiveLoopAsync, _cancellationTokenSource.Token);

            // キープアライブを開始
            if (_config.KeepAlive?.Enabled == true)
            {
              StartKeepAlive();
            }
          },
          _config.ConnectionRetryPolicy,
          linkedCts.Token,
          _logger);
    }
    else
    {
      // リトライポリシーが設定されていない場合は、従来通り1回だけ試行
      await _transport.ConnectAsync(linkedCts.Token);
      lock (_statsLock)
      {
        _stats.ConnectedAt = DateTime.UtcNow;
        _stats.ConnectionRetryAttempts = 0; // 接続成功時にリセット
      }
      _logger?.LogInformation("TCP Client '{Name}' connected to {Host}:{Port}", Name, _config.RemoteHost, _config.RemotePort);

      OnConnected?.Invoke(this, EventArgs.Empty);

      // 送信ループを開始
      _sendLoopTask = Task.Run(SendLoopAsync, _cancellationTokenSource.Token);

      // 受信ループを開始
      _ = Task.Run(ReceiveLoopAsync, _cancellationTokenSource.Token);

      // キープアライブを開始
      if (_config.KeepAlive?.Enabled == true)
      {
        StartKeepAlive();
      }
    }
  }

  /// <summary>
  /// サーバーから切断
  /// </summary>
  /// <param name="isIntentional">意図的な切断かどうか</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task DisconnectAsync(bool isIntentional = true, CancellationToken cancellationToken = default)
  {
    if (!IsConnected)
    {
      return;
    }

    cancellationToken.ThrowIfCancellationRequested();

    _isIntentionalDisconnect = isIntentional;

    // 外部CancellationTokenの登録を破棄
    _externalCancellationTokenRegistration?.Dispose();
    _externalCancellationTokenRegistration = null;

    // CancellationTokenSourceを先にキャンセル（これによりキープアライブタイマーのElapsedイベント内のチェックが機能する）
    _cancellationTokenSource.Cancel();

    // キープアライブを停止（タイマーを確実に停止）
    StopKeepAlive();

    // 送信キューを閉じる
    _sendQueueWriter.Complete();

    // 送信ループの完了を待つ
    if (_sendLoopTask != null)
    {
      try
      {
        await _sendLoopTask;
      }
      catch (OperationCanceledException)
      {
        // 正常な終了
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "Error waiting for send loop to complete in client {Name}", Name);
      }
    }

    await _transport.DisconnectAsync(cancellationToken);

    // 接続時刻をクリア（統計情報は保持）
    lock (_statsLock)
    {
      _stats.ConnectedAt = null;
    }

    // 待機中のリクエストをキャンセル
    lock (_pendingResponseRequestsLock)
    {
      foreach (var request in _pendingResponseRequests)
      {
        if (request.ResponseTcs != null && !request.ResponseTcs.Task.IsCompleted)
        {
          request.ResponseTcs.TrySetCanceled();
        }
      }
      _pendingResponseRequests.Clear();
    }

    if (isIntentional)
    {
      _logger?.LogInformation("TCP Client '{Name}' disconnected", Name);
    }
    else
    {
      _logger?.LogError("TCP Client '{Name}' disconnected unexpectedly (network error)", Name);
    }
    OnDisconnected?.Invoke(this, EventArgs.Empty);
  }

  /// <summary>
  /// メッセージをキューに追加して送信し、応答を待つ（HTTPクライアントのように）
  /// 応答が来るまで次のメッセージは送信されない
  /// 応答メッセージはOnMessageReceivedイベントを発行しない
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="timeout">タイムアウト時間。指定しない場合はClientConfigのTimeoutMillisecondsを使用</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>応答メッセージ</returns>
  public async Task<Message> SendAsync(Message message, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
  {
    if (!IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    cancellationToken.ThrowIfCancellationRequested();

    // タイムアウトが指定されていない場合は、デフォルト値を使用
    var actualTimeout = timeout ?? TimeSpan.FromMilliseconds(_config.TimeoutMilliseconds);

    return await SendAndWaitAsync(message, _ => true, actualTimeout, cancellationToken);
  }

  /// <summary>
  /// メッセージを送信して、条件を満たす応答を待つ
  /// </summary>
  /// <param name="requestMessage">送信するメッセージ</param>
  /// <param name="responsePredicate">応答の条件判定関数</param>
  /// <param name="timeout">タイムアウト時間</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>条件を満たす応答メッセージ</returns>
  public async Task<Message> SendAndWaitAsync(
      Message requestMessage,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout,
      CancellationToken cancellationToken = default)
  {
    if (!IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    cancellationToken.ThrowIfCancellationRequested();

    // 外部CancellationTokenと内部CancellationTokenSourceを統合
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);

    // リトライポリシーが設定されている場合は、それを使用
    if (_config.RetryPolicy != null)
    {
      return await RetryHelper.ExecuteWithRetryAsync(
          async () => await SendAndWaitSingleAsync(requestMessage, responsePredicate, timeout, linkedCts.Token),
          _config.RetryPolicy,
          responsePredicate,
          linkedCts.Token,
          _logger);
    }

    return await SendAndWaitSingleAsync(requestMessage, responsePredicate, timeout, linkedCts.Token);
  }

  /// <summary>
  /// 文字列を送信して応答を待つ（設定のEncodingを使用）
  /// 応答が来るまで次のメッセージは送信されない
  /// 応答メッセージはOnMessageReceivedイベントを発行しない
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="timeout">タイムアウト時間。指定しない場合はClientConfigのTimeoutMillisecondsを使用</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>応答メッセージ</returns>
  public async Task<Message> SendAsync(string text, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
  {
    var encoding = TcpMessageUtils.GetEncoding(_config.Encoding);
    var message = Message.FromString(text, encoding);
    return await SendAsync(message, timeout, cancellationToken);
  }

  /// <summary>後方互換性のためのオーバーロード</summary>
  public Task<Message> SendAsync(string text, CancellationToken cancellationToken)
      => SendAsync(text, (TimeSpan?)null, cancellationToken);

  /// <summary>
  /// 文字列を送信して応答を待つ（設定のEncodingを使用）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="responsePredicate">応答の条件判定関数</param>
  /// <param name="timeout">タイムアウト時間</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>条件を満たす応答メッセージ</returns>
  public async Task<Message> SendAndWaitAsync(
      string text,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout,
      CancellationToken cancellationToken = default)
  {
    var encoding = TcpMessageUtils.GetEncoding(_config.Encoding);
    var message = Message.FromString(text, encoding);
    return await SendAndWaitAsync(message, responsePredicate, timeout, cancellationToken);
  }

  private async Task<Message> SendAndWaitSingleAsync(
      Message requestMessage,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout,
      CancellationToken cancellationToken = default)
  {
    var tcs = new TaskCompletionSource<Message>();
    var request = new SendRequest
    {
      Message = requestMessage,
      ResponseTcs = tcs,
      ResponsePredicate = responsePredicate,
      Timeout = timeout,
      EnqueuedAt = DateTime.UtcNow,
      CancellationToken = cancellationToken
    };

    // タイムアウト処理
    using var timeoutCts = new CancellationTokenSource(timeout);
    timeoutCts.Token.Register(() =>
    {
      if (!tcs.Task.IsCompleted)
      {
        // キューから削除
        lock (_pendingResponseRequestsLock)
        {
          _pendingResponseRequests.Remove(request);
        }
        tcs.TrySetException(new TimeoutException($"Request timed out after {timeout.TotalSeconds} seconds"));
      }
    });

    // キューに追加
    await _sendQueueWriter.WriteAsync(request, cancellationToken);

    // 応答を待つ
    try
    {
      return await tcs.Task;
    }
    catch (TimeoutException ex)
    {
      // タイムアウトエラーを統計に記録
      Interlocked.Increment(ref _stats.ErrorCount);
      lock (_statsLock)
      {
        _stats.LastError = ex.Message;
        _stats.LastErrorAt = DateTime.UtcNow;
      }
      _logger?.LogWarning("Request timeout for client {Name}: {Message}", Name, ex.Message);
      OnError?.Invoke(this, ex);
      throw;
    }
  }

  /// <summary>
  /// KeepAlive設定の取得・設定
  /// </summary>
  public KeepAliveConfig? KeepAlive
  {
    get
    {
      lock (_configLock)
      {
        return _config.KeepAlive == null ? null :
            new KeepAliveConfig
            {
              Enabled = _config.KeepAlive.Enabled,
              IntervalSeconds = _config.KeepAlive.IntervalSeconds,
              Message = _config.KeepAlive.Message
            };
      }
    }
    set
    {
      lock (_configLock)
      {
        _config.KeepAlive = value == null ? null :
            new KeepAliveConfig
            {
              Enabled = value.Enabled,
              IntervalSeconds = value.IntervalSeconds,
              Message = value.Message
            };

        if (IsConnected)
        {
          StopKeepAlive();
          if (_config.KeepAlive?.Enabled == true)
          {
            StartKeepAlive();
          }
        }

        _logger?.LogInformation("TCP Client '{Name}' KeepAlive設定を更新しました: Enabled={Enabled}, Interval={Interval}s, Message={Message}",
            Name, _config.KeepAlive?.Enabled ?? false, _config.KeepAlive?.IntervalSeconds ?? 0, _config.KeepAlive?.Message ?? "");
      }
    }
  }

  /// <summary>
  /// タイムアウト設定の取得・設定（ミリ秒）
  /// </summary>
  public int TimeoutMilliseconds
  {
    get
    {
      lock (_configLock)
      {
        return _config.TimeoutMilliseconds;
      }
    }
    set
    {
      if (value <= 0)
      {
        throw new ArgumentException("Timeout must be greater than 0", nameof(value));
      }

      lock (_configLock)
      {
        _config.TimeoutMilliseconds = value;
        _logger?.LogInformation("TCP Client '{Name}' タイムアウト設定を更新しました: {Timeout}ms", Name, value);
      }
    }
  }

  /// <summary>
  /// リトライポリシーの取得・設定
  /// </summary>
  public RetryPolicy? RetryPolicy
  {
    get
    {
      lock (_configLock)
      {
        return _config.RetryPolicy == null ? null :
            new RetryPolicy
            {
              MaxRetryCount = _config.RetryPolicy.MaxRetryCount,
              RetryDelayStrategy = _config.RetryPolicy.RetryDelayStrategy,
              InitialDelayMs = _config.RetryPolicy.InitialDelayMs,
              MaxDelayMs = _config.RetryPolicy.MaxDelayMs,
              FailOnTimeout = _config.RetryPolicy.FailOnTimeout,
              FailOnErrorResponse = _config.RetryPolicy.FailOnErrorResponse
            };
      }
    }
    set
    {
      lock (_configLock)
      {
        _config.RetryPolicy = value == null ? null :
            new RetryPolicy
            {
              MaxRetryCount = value.MaxRetryCount,
              RetryDelayStrategy = value.RetryDelayStrategy,
              InitialDelayMs = value.InitialDelayMs,
              MaxDelayMs = value.MaxDelayMs,
              FailOnTimeout = value.FailOnTimeout,
              FailOnErrorResponse = value.FailOnErrorResponse
            };

        if (_config.RetryPolicy != null)
        {
          _logger?.LogInformation("TCP Client '{Name}' リトライポリシーを更新しました: MaxRetryCount={MaxRetryCount}, Strategy={Strategy}",
              Name, _config.RetryPolicy.MaxRetryCount, _config.RetryPolicy.RetryDelayStrategy);
        }
        else
        {
          _logger?.LogInformation("TCP Client '{Name}' リトライポリシーを無効化しました", Name);
        }
      }
    }
  }

  /// <summary>
  /// 接続リトライポリシーの取得・設定
  /// </summary>
  public RetryPolicy? ConnectionRetryPolicy
  {
    get
    {
      lock (_configLock)
      {
        return _config.ConnectionRetryPolicy == null ? null :
            new RetryPolicy
            {
              MaxRetryCount = _config.ConnectionRetryPolicy.MaxRetryCount,
              RetryDelayStrategy = _config.ConnectionRetryPolicy.RetryDelayStrategy,
              InitialDelayMs = _config.ConnectionRetryPolicy.InitialDelayMs,
              MaxDelayMs = _config.ConnectionRetryPolicy.MaxDelayMs,
              FailOnTimeout = _config.ConnectionRetryPolicy.FailOnTimeout,
              FailOnErrorResponse = _config.ConnectionRetryPolicy.FailOnErrorResponse
            };
      }
    }
    set
    {
      lock (_configLock)
      {
        _config.ConnectionRetryPolicy = value == null ? null :
            new RetryPolicy
            {
              MaxRetryCount = value.MaxRetryCount,
              RetryDelayStrategy = value.RetryDelayStrategy,
              InitialDelayMs = value.InitialDelayMs,
              MaxDelayMs = value.MaxDelayMs,
              FailOnTimeout = value.FailOnTimeout,
              FailOnErrorResponse = value.FailOnErrorResponse
            };

        if (_config.ConnectionRetryPolicy != null)
        {
          _logger?.LogInformation("TCP Client '{Name}' 接続リトライポリシーを更新しました: MaxRetryCount={MaxRetryCount}, Strategy={Strategy}",
              Name, _config.ConnectionRetryPolicy.MaxRetryCount, _config.ConnectionRetryPolicy.RetryDelayStrategy);
        }
        else
        {
          _logger?.LogInformation("TCP Client '{Name}' 接続リトライポリシーを無効化しました", Name);
        }
      }
    }
  }

  /// <summary>
  /// 接続状態情報の取得
  /// </summary>
  public ClientConnectionInfo ConnectionInfo
  {
    get
    {
      // 各ロックを独立して取得（ネスト禁止→デッドロックリスク排除）
      bool isReconnecting;
      lock (_reconnectLock)
      {
        isReconnecting = _reconnectTask != null && !_reconnectTask.IsCompleted;
      }

      int pendingRequestsCount;
      lock (_pendingResponseRequestsLock)
      {
        pendingRequestsCount = _pendingResponseRequests.Count;
      }

      DateTime? connectedAt, lastMessageReceivedAt, lastKeepAliveSentAt,
                lastKeepAliveResponseReceivedAt, lastErrorAt, lastRetryAttemptAt;
      string? lastError;
      lock (_statsLock)
      {
        connectedAt                       = _stats.ConnectedAt;
        lastMessageReceivedAt             = _stats.LastMessageReceivedAt;
        lastKeepAliveSentAt               = _stats.LastKeepAliveSentAt;
        lastKeepAliveResponseReceivedAt   = _stats.LastKeepAliveResponseReceivedAt;
        lastError                         = _stats.LastError;
        lastErrorAt                       = _stats.LastErrorAt;
        lastRetryAttemptAt                = _stats.LastRetryAttemptAt;
      }

      // ロック解放後に組み立て
      var connectionDuration = connectedAt.HasValue
        ? DateTime.UtcNow - connectedAt.Value
        : (TimeSpan?)null;

      return new ClientConnectionInfo
      {
        IsConnected = IsConnected,
        ConnectedAt = connectedAt,
        LastMessageReceivedAt = lastMessageReceivedAt,
        RemoteHost = _config.RemoteHost,
        RemotePort = _config.RemotePort,
        IsReconnecting = isReconnecting,
        ConnectionDuration = connectionDuration,
        MessagesSent = Interlocked.Read(ref _stats.MessagesSent),
        MessagesReceived = Interlocked.Read(ref _stats.MessagesReceived),
        PendingRequests = pendingRequestsCount,
        LastKeepAliveSentAt = lastKeepAliveSentAt,
        LastKeepAliveResponseReceivedAt = lastKeepAliveResponseReceivedAt,
        KeepAliveTimeoutCount = Interlocked.CompareExchange(ref _stats.KeepAliveTimeoutCount, 0, 0),
        ErrorCount = Interlocked.CompareExchange(ref _stats.ErrorCount, 0, 0),
        LastError = lastError,
        LastErrorAt = lastErrorAt,
        ConnectionRetryAttempts = Interlocked.CompareExchange(ref _stats.ConnectionRetryAttempts, 0, 0),
        LastRetryAttemptAt = lastRetryAttemptAt
      };
    }
  }

  /// <summary>
  /// リソースを非同期に解放
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }
    await DisconnectAsync().ConfigureAwait(false);
    _externalCancellationTokenRegistration?.Dispose();
    _cancellationTokenSource.Dispose();
    _messageReceivedSubject.Dispose();
    StopKeepAlive();
    _disposed = true;
  }

  /// <summary>
  /// リソースを解放（互換性維持のため残存。可能であれば DisposeAsync を使用してください）
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    // ConfigureAwait(false)を使用してデッドロックを回避
    DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    _externalCancellationTokenRegistration?.Dispose();
    _cancellationTokenSource.Dispose();
    _messageReceivedSubject.Dispose();
    StopKeepAlive();
    _disposed = true;
  }

  private class SendRequest
  {
    public Message Message { get; set; } = null!;
    public TaskCompletionSource<Message>? ResponseTcs { get; set; } // nullの場合は応答不要
    public Func<Message, bool>? ResponsePredicate { get; set; }
    public TimeSpan Timeout { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public CancellationToken CancellationToken { get; set; }
  }

}

