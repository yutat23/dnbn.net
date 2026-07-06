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
  private Task<bool>? _receiveLoopTask;
  // 接続世代。新しい接続が確立されるたびに増加し、旧接続の受信ループ後始末が
  // 新しい接続を巻き添えで切断しないためのガードとして使用する
  private int _connectionEpoch;
  private readonly LinkedList<SendRequest> _pendingResponseRequests = new();
  private readonly object _pendingResponseRequestsLock = new();
  private CancellationTokenSource? _keepAliveTimerCts;
  private CancellationTokenSource _cancellationTokenSource = new();
  private CancellationTokenRegistration? _externalCancellationTokenRegistration;
  private bool _disposed = false;
  private TaskCompletionSource<Message>? _keepAliveResponseTcs;
  private bool _isIntentionalDisconnect = false;
  private Task? _reconnectTask;
  private CancellationTokenSource? _reconnectCts;
  private readonly object _reconnectLock = new();
  private readonly object _configLock = new();
  private CancellationTokenSource? _delayInterruptCts;
  private readonly object _delayInterruptLock = new();
  private readonly SemaphoreSlim _disconnectLock = new(1, 1);

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
    var channelOptions = new BoundedChannelOptions(Math.Max(1, _config.SendQueueCapacity))
    {
      FullMode = BoundedChannelFullMode.Wait
    };
    var channel = Channel.CreateBounded<SendRequest>(channelOptions);
    _sendQueue = channel;
    _sendQueueWriter = channel.Writer;
    _sendQueueReader = channel.Reader;
  }

  /// <summary>
  /// 新しい接続のために状態をリセットする。
  /// 呼び出し側は_disconnectLockを保持し、旧接続の送受信ループが完了していることを保証すること。
  /// </summary>
  private void ResetConnectionStateForConnect()
  {
    // 接続世代を進める。これ以降、旧世代の受信ループ後始末（HandleReceiveLoopCompletionAsync）は
    // 何もせずに終了するため、新しい接続が巻き添えで切断されることはない
    Interlocked.Increment(ref _connectionEpoch);

    // 旧接続のループが万一まだ動いていても確実に停止するようキャンセルしてから差し替える。
    // 旧CTSは旧ループが参照している可能性があるためDisposeしない（登録もタイマーも持たないためGCに任せる）
    var oldCts = _cancellationTokenSource;
    if (!oldCts.IsCancellationRequested)
    {
      oldCts.Cancel();
    }
    _cancellationTokenSource = new CancellationTokenSource();

    _sendQueueWriter.TryComplete();
    InitializeSendQueue();
    // 前回接続の中途半端な受信データが残っていると、再接続後の
    // メッセージ境界がずれるためクリアする
    _parser.Clear();
    _isIntentionalDisconnect = false;
    Interlocked.Exchange(ref _keepAliveResponseTcs, null)?.TrySetCanceled();
    _sendLoopTask = null;
    _receiveLoopTask = null;
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

    // 切断処理・旧ループの後始末と直列化して状態をリセットする
    await _disconnectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (IsConnected)
      {
        return;
      }

      // 旧接続のループが残っている場合（NW障害直後の再接続など）は、
      // 確実に停止・完了させてから状態をリセットする
      _cancellationTokenSource.Cancel();
      _sendQueueWriter.TryComplete();
      StopKeepAlive();
      await WaitForLoopTasksAsync().ConfigureAwait(false);

      ResetConnectionStateForConnect();

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
    }
    finally
    {
      _disconnectLock.Release();
    }

    // 外部CancellationTokenと内部CancellationTokenSourceを統合
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);

    // 接続リトライポリシーが設定されている場合は、リトライを実行
    if (_config.ConnectionRetryPolicy != null)
    {
      await RetryHelper.ExecuteConnectionRetryAsync(
          async () =>
          {
            await _transport.ConnectAsync(linkedCts.Token).ConfigureAwait(false);
            OnTransportConnected(isReconnect: false);
          },
          _config.ConnectionRetryPolicy,
          linkedCts.Token,
          _logger,
          onDelayStarting: cts => { lock (_delayInterruptLock) { _delayInterruptCts = cts; } },
          targetDescription: RetryLogTarget).ConfigureAwait(false);
    }
    else
    {
      // リトライポリシーが設定されていない場合は、従来通り1回だけ試行
      await _transport.ConnectAsync(linkedCts.Token).ConfigureAwait(false);
      OnTransportConnected(isReconnect: false);
    }
  }

  /// <summary>
  /// 相手先の識別名（ログ出力用）
  /// </summary>
  private string RemoteTarget => $"{_config.RemoteHost}:{_config.RemotePort}";

  /// <summary>
  /// RetryHelperのログに出力する識別名（相手先＋クライアント名）
  /// </summary>
  private string RetryLogTarget => $"{RemoteTarget} (client '{Name}')";

  /// <summary>
  /// 電文内容ログの出力レベル。
  /// EnableMessageLogging有効時はInformation、無効時は従来どおりDebugで出力する
  /// </summary>
  private LogLevel MessageLogLevel => _config.EnableMessageLogging ? LogLevel.Information : LogLevel.Debug;

  /// <summary>
  /// トランスポート接続成功後の共通処理（統計更新・イベント発火・送受信ループ開始・KeepAlive開始）
  /// </summary>
  private void OnTransportConnected(bool isReconnect)
  {
    lock (_statsLock)
    {
      _stats.ConnectedAt = DateTime.UtcNow;
      _stats.ConnectionRetryAttempts = 0; // 接続成功時にリセット
    }
    if (isReconnect)
    {
      _logger?.LogInformation("TCP Client '{Name}' reconnected to {Host}:{Port}", Name, _config.RemoteHost, _config.RemotePort);
    }
    else
    {
      _logger?.LogInformation("TCP Client '{Name}' connected to {Host}:{Port}", Name, _config.RemoteHost, _config.RemotePort);
    }

    OnConnected?.Invoke(this, EventArgs.Empty);

    // この接続専用のトークン・キューリーダーをキャプチャする。
    // 再接続でフィールドが新しいインスタンスに差し替わっても、
    // この接続のループが新しい接続の状態に触れないようにするため
    var token = _cancellationTokenSource.Token;
    var sendQueueReader = _sendQueueReader;
    var epoch = Volatile.Read(ref _connectionEpoch);

    // 送信ループを開始
    _sendLoopTask = Task.Run(() => SendLoopAsync(sendQueueReader, token), CancellationToken.None);

    // 受信ループを開始（本体タスクはDisconnectAsyncが完了を待機する。
    // 後始末はepochガード付きの別タスクに分離し、awaitの循環によるデッドロックを防ぐ）
    var receiveLoopTask = Task.Run(() => ReceiveLoopCoreAsync(token), CancellationToken.None);
    _receiveLoopTask = receiveLoopTask;
    _ = HandleReceiveLoopCompletionAsync(receiveLoopTask, epoch);

    // キープアライブを開始
    if (_config.KeepAlive?.Enabled == true)
    {
      StartKeepAlive();
    }
  }

  /// <summary>
  /// 送受信ループの完了を待つ。トークンのキャンセル後に呼ぶこと。
  /// 受信ループ本体はDisconnectAsyncを呼ばないため、ここで待機してもデッドロックしない。
  /// </summary>
  private async Task WaitForLoopTasksAsync()
  {
    if (_sendLoopTask != null)
    {
      try
      {
        await _sendLoopTask.ConfigureAwait(false);
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

    if (_receiveLoopTask != null)
    {
      try
      {
        await _receiveLoopTask.ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "Error waiting for receive loop to complete in client {Name}", Name);
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
    cancellationToken.ThrowIfCancellationRequested();

    // 意図的な切断の場合は、進行中の自動再接続を中断する
    // （NW障害起因の内部呼び出しでは再接続を止めない）
    if (isIntentional)
    {
      Task? reconnectTask = null;
      lock (_reconnectLock)
      {
        if (_reconnectTask != null && !_reconnectTask.IsCompleted)
        {
          try
          {
            _reconnectCts?.Cancel();
          }
          catch (ObjectDisposedException)
          {
          }
          reconnectTask = _reconnectTask;
        }
      }
      if (reconnectTask != null)
      {
        // 再接続タスク自身がOperationCanceledExceptionを処理するため、ここでは例外は伝播しない
        await reconnectTask.ConfigureAwait(false);
      }
    }

    // 受信ループ起因の切断とユーザーからの切断が並行実行されても
    // OnDisconnectedの二重発火やtransportの二重切断が起きないよう直列化する
    await _disconnectLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
    try
    {
      await DisconnectCoreAsync(isIntentional, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _disconnectLock.Release();
    }
  }

  private async Task DisconnectCoreAsync(bool isIntentional, CancellationToken cancellationToken)
  {
    var wasConnected = IsConnected;
    _isIntentionalDisconnect = isIntentional;

    // 外部CancellationTokenの登録を破棄
    _externalCancellationTokenRegistration?.Dispose();
    _externalCancellationTokenRegistration = null;

    // CancellationTokenSourceを先にキャンセル（これによりキープアライブタイマーのElapsedイベント内のチェックが機能する）
    _cancellationTokenSource.Cancel();

    // キープアライブを停止（タイマーを確実に停止）
    StopKeepAlive();

    // 送信キューを閉じる（既に閉じている場合は無視）
    _sendQueueWriter.TryComplete();

    // 送受信ループの完了を待つ。これにより切断完了後に旧受信ループが
    // 残存して新しい接続に干渉することがなくなる
    await WaitForLoopTasksAsync().ConfigureAwait(false);

    if (wasConnected)
    {
      await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
    }

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
        request.ResponseTcs.TrySetCanceled();
      }
      _pendingResponseRequests.Clear();
    }

    Interlocked.Exchange(ref _keepAliveResponseTcs, null)?.TrySetCanceled();

    if (!wasConnected)
    {
      return;
    }

    if (isIntentional)
    {
      _logger?.LogInformation("TCP Client '{Name}' disconnected from {Host}:{Port}", Name, _config.RemoteHost, _config.RemotePort);
    }
    else
    {
      _logger?.LogError("TCP Client '{Name}' disconnected unexpectedly from {Host}:{Port} (network error)", Name, _config.RemoteHost, _config.RemotePort);
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
          _logger,
          RetryLogTarget);
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

  /// <summary>
  /// メッセージを送信する（応答を待たない通知電文用）。
  /// 送信キューを経由するため、SendAsync との送信順序は保証される。
  /// 戻りのTaskはソケットへの書き込み完了時に完了する（応答の有無は関知しない）。
  /// リトライポリシーは適用されない。
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task SendOneWayAsync(Message message, CancellationToken cancellationToken = default)
  {
    if (!IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    cancellationToken.ThrowIfCancellationRequested();

    // 外部CancellationTokenと内部CancellationTokenSourceを統合
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);

    var sendCompletedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var request = new SendRequest
    {
      Message = message,
      ResponseTcs = null, // 応答を待たない
      SendCompletedTcs = sendCompletedTcs,
      EnqueuedAt = DateTime.UtcNow,
      CancellationToken = linkedCts.Token
    };

    await _sendQueueWriter.WriteAsync(request, linkedCts.Token);

    // ソケットへの書き込み完了（または送信失敗）を待つ
    await sendCompletedTcs.Task.WaitAsync(linkedCts.Token);
  }

  /// <summary>
  /// 文字列を送信する（応答を待たない通知電文用、設定のEncodingを使用）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public Task SendOneWayAsync(string text, CancellationToken cancellationToken = default)
  {
    var encoding = TcpMessageUtils.GetEncoding(_config.Encoding);
    return SendOneWayAsync(Message.FromString(text, encoding), cancellationToken);
  }

  private async Task<Message> SendAndWaitSingleAsync(
      Message requestMessage,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout,
      CancellationToken cancellationToken = default)
  {
    var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
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
      return await tcs.Task.WaitAsync(cancellationToken);
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
  /// 通知電文の判定述語の取得・設定。
  /// マッチした受信メッセージは応答マッチングをスキップして OnMessageReceived に直接配信される。
  /// </summary>
  public Func<Message, bool>? NotificationPredicate
  {
    get
    {
      lock (_configLock)
      {
        return _config.NotificationPredicate;
      }
    }
    set
    {
      lock (_configLock)
      {
        _config.NotificationPredicate = value;
      }
      _logger?.LogInformation("TCP Client '{Name}' 通知電文の判定述語を{State}しました", Name, value != null ? "設定" : "解除");
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
        return _config.KeepAlive?.Clone();
      }
    }
    set
    {
      lock (_configLock)
      {
        _config.KeepAlive = value?.Clone();

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
        return _config.RetryPolicy?.Clone();
      }
    }
    set
    {
      lock (_configLock)
      {
        _config.RetryPolicy = value?.Clone();

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
        return _config.ConnectionRetryPolicy?.Clone();
      }
    }
    set
    {
      lock (_configLock)
      {
        _config.ConnectionRetryPolicy = value?.Clone();

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

  /// <inheritdoc />
  public void InterruptReconnectDelay()
  {
    lock (_delayInterruptLock)
    {
      try { _delayInterruptCts?.Cancel(); }
      catch (ObjectDisposedException) { }
    }
  }

  /// <inheritdoc />
  public async Task WaitForConnectionAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
  {
    if (IsConnected) return;

    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    void handler(object? sender, EventArgs e) => tcs.TrySetResult();
    OnConnected += handler;
    try
    {
      if (IsConnected) return;
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      cts.CancelAfter(timeout);
      await tcs.Task.WaitAsync(cts.Token);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      throw new TimeoutException($"Connection was not established within {timeout.TotalSeconds} seconds");
    }
    finally
    {
      OnConnected -= handler;
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
    lock (_reconnectLock)
    {
      _reconnectCts?.Dispose();
      _reconnectCts = null;
    }
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
    lock (_reconnectLock)
    {
      _reconnectCts?.Dispose();
      _reconnectCts = null;
    }
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
    public TaskCompletionSource? SendCompletedTcs { get; set; } // 送信完了通知（通知電文用）
  }

}
