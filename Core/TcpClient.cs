using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Reactive.Subjects;
using System.Text;
using System.Timers;
using Dnbn.Configuration;
using Dnbn.Filters;
using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

/// <summary>
/// TCPクライアント実装
/// </summary>
public class TcpClient : ITcpClient
{
  private ClientConfig _config;
  private readonly ILogger<TcpClient>? _logger;
  private readonly List<IMessageFilter> _filters;
  private readonly ITransport _transport;
  private readonly MessageParser _parser;
  private readonly Subject<Message> _messageReceivedSubject = new();
  private readonly Channel<SendRequest> _sendQueue;
  private readonly ChannelWriter<SendRequest> _sendQueueWriter;
  private readonly ChannelReader<SendRequest> _sendQueueReader;
  private Task? _sendLoopTask;
  private readonly Queue<SendRequest> _pendingResponseRequests = new();
  private readonly object _pendingResponseRequestsLock = new();
  private System.Timers.Timer? _keepAliveTimer;
  private CancellationTokenSource _cancellationTokenSource = new();
  private bool _disposed = false;
  private TaskCompletionSource<Message>? _keepAliveResponseTcs;
  private bool _isIntentionalDisconnect = false;
  private Task? _reconnectTask;
  private readonly object _reconnectLock = new();
  private readonly object _configLock = new();

  // 統計情報追跡用フィールド
  private DateTime? _connectedAt;
  private DateTime? _lastMessageReceivedAt;
  private long _messagesSent = 0;
  private long _messagesReceived = 0;
  private DateTime? _lastKeepAliveSentAt;
  private DateTime? _lastKeepAliveResponseReceivedAt;
  private int _keepAliveTimeoutCount = 0;
  private int _errorCount = 0;
  private string? _lastError;
  private DateTime? _lastErrorAt;
  private int _connectionRetryAttempts = 0;
  private DateTime? _lastRetryAttemptAt;
  private readonly object _statsLock = new();

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

    var encoding = GetEncoding(config.Encoding);
    // 受信時の終端文字を決定：ReceiveMessageTerminatorが設定されている場合はそれを使用、未設定の場合はMessageTerminatorを使用
    string[]? receiveTerminators = config.ReceiveMessageTerminator ?? 
        (config.MessageTerminator != null ? new[] { config.MessageTerminator } : null);
    _parser = new MessageParser(
        encoding,
        receiveTerminators,
        config.FixedHeaderLength,
        config.FixedBodyLength,
        config.LengthFieldOffset,
        config.LengthFieldLength);

    // 送信キューを初期化
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
              _connectedAt = DateTime.UtcNow;
              _connectionRetryAttempts = 0; // 接続成功時にリセット
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
        _connectedAt = DateTime.UtcNow;
        _connectionRetryAttempts = 0; // 接続成功時にリセット
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
      _connectedAt = null;
    }

    // 待機中のリクエストをキャンセル
    lock (_pendingResponseRequestsLock)
    {
      while (_pendingResponseRequests.Count > 0)
      {
        var request = _pendingResponseRequests.Dequeue();
        if (request.ResponseTcs != null && !request.ResponseTcs.Task.IsCompleted)
        {
          request.ResponseTcs.TrySetCanceled();
        }
      }
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

  private async Task ReceiveLoopAsync()
  {
    var buffer = new byte[4096];
    bool wasNetworkError = false;

    while (!_cancellationTokenSource.Token.IsCancellationRequested && IsConnected)
    {
      try
      {
        var bytesRead = await _transport.ReceiveAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token);
        if (bytesRead == 0)
        {
          // 接続が閉じられた（NW障害）
          wasNetworkError = !_isIntentionalDisconnect;
          break;
        }

        var data = new byte[bytesRead];
        Array.Copy(buffer, data, bytesRead);

        var messages = _parser.Parse(data);
        foreach (var message in messages)
        {
          // フィルターパイプラインを適用
          var filteredMessage = message;
          foreach (var filter in _filters)
          {
            var ctx = new MessageContext(null, false);
            filteredMessage = await filter.OnReceivedAsync(filteredMessage, ctx);
          }

          // メッセージログ出力（設定が有効な場合）
          if (_config.EnableMessageLogging)
          {
            _logger?.LogDebug("TCP Client '{Name}' received message: {MessageText}", Name, filteredMessage.Text?.Trim());
          }

          // 統計情報を更新
          Interlocked.Increment(ref _messagesReceived);
          lock (_statsLock)
          {
            _lastMessageReceivedAt = DateTime.UtcNow;
          }

          // キープアライブ応答をチェック（優先的に処理）
          bool handled = false;
          var keepAliveTcs = Interlocked.Exchange(ref _keepAliveResponseTcs, null);
          if (keepAliveTcs != null)
          {
            keepAliveTcs.TrySetResult(filteredMessage);
            lock (_statsLock)
            {
              _lastKeepAliveResponseReceivedAt = DateTime.UtcNow;
            }
            OnKeepAliveResponseReceived?.Invoke(this, filteredMessage);
            handled = true;
          }

          // 待機中のリクエストをFIFO順序でチェック
          if (!handled)
          {
            lock (_pendingResponseRequestsLock)
            {
              // タイムアウトしたリクエストを削除
              var now = DateTime.UtcNow;
              var tempQueue = new Queue<SendRequest>();
              while (_pendingResponseRequests.Count > 0)
              {
                var request = _pendingResponseRequests.Dequeue();
                var elapsed = now - request.EnqueuedAt;
                if (elapsed >= request.Timeout && request.ResponseTcs != null && !request.ResponseTcs.Task.IsCompleted)
                {
                  // タイムアウト
                  request.ResponseTcs.TrySetException(new TimeoutException($"Request timed out after {request.Timeout.TotalSeconds} seconds"));
                  continue;
                }
                tempQueue.Enqueue(request);
              }
              while (tempQueue.Count > 0)
              {
                _pendingResponseRequests.Enqueue(tempQueue.Dequeue());
              }

              // FIFO順序で応答をマッチング
              tempQueue = new Queue<SendRequest>();
              while (_pendingResponseRequests.Count > 0)
              {
                var request = _pendingResponseRequests.Dequeue();
                if (request.ResponseTcs != null && !request.ResponseTcs.Task.IsCompleted)
                {
                  // responsePredicateで応答を判定
                  if (request.ResponsePredicate == null || request.ResponsePredicate(filteredMessage))
                  {
                    request.ResponseTcs.TrySetResult(filteredMessage);
                    handled = true;
                    break;
                  }
                  else
                  {
                    // 条件を満たさない場合はキューに戻す
                    tempQueue.Enqueue(request);
                  }
                }
              }
              // 残りのリクエストをキューに戻す
              while (tempQueue.Count > 0)
              {
                _pendingResponseRequests.Enqueue(tempQueue.Dequeue());
              }
            }
          }

          if (!handled)
          {
            OnMessageReceived?.Invoke(this, filteredMessage);
            _messageReceivedSubject.OnNext(filteredMessage);
          }
        }
      }
      catch (OperationCanceledException)
      {
        // 意図的な切断
        wasNetworkError = false;
        break;
      }
      catch (Exception ex)
      {
        // 意図的な切断の場合はエラーログを出さない
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
          // エラー統計を更新
          Interlocked.Increment(ref _errorCount);
          lock (_statsLock)
          {
            _lastError = ex.Message;
            _lastErrorAt = DateTime.UtcNow;
          }
          _logger?.LogError(ex, "Error receiving data in client {Name}", Name);
          OnError?.Invoke(this, ex);
          // NW障害として扱う
          wasNetworkError = !_isIntentionalDisconnect;
        }
        else
        {
          // 意図的な切断
          wasNetworkError = false;
        }
        break;
      }
    }

    if (IsConnected)
    {
      // NW障害による切断として扱う
      await DisconnectAsync(isIntentional: !wasNetworkError);
    }

    // NW障害による切断の場合、自動再接続を試行
    // 注意: DisconnectAsyncで_cancellationTokenSourceがキャンセルされるため、
    // 再接続時には新しいCancellationTokenSourceが必要
    if (wasNetworkError && _config.ConnectionRetryPolicy != null)
    {
      _logger?.LogInformation("TCP Client '{Name}' will attempt automatic reconnection...", Name);
      StartAutoReconnect();
    }
  }

  /// <summary>
  /// 送信ループ（順次処理）
  /// </summary>
  private async Task SendLoopAsync()
  {
    try
    {
      await foreach (var request in _sendQueueReader.ReadAllAsync(_cancellationTokenSource.Token))
      {
        try
        {
          // 応答待ちのリクエストの場合は、キューに追加
          if (request.ResponseTcs != null)
          {
            lock (_pendingResponseRequestsLock)
            {
              _pendingResponseRequests.Enqueue(request);
            }
          }

          // フィルターパイプラインを適用
          var filteredMessage = request.Message;
          foreach (var filter in _filters)
          {
            var ctx = new MessageContext(null, false);
            filteredMessage = await filter.OnSendingAsync(filteredMessage, ctx);
          }

          // メッセージログ出力（設定が有効な場合）
          if (_config.EnableMessageLogging)
          {
            _logger?.LogDebug("TCP Client '{Name}' sending message: {MessageText}", Name, filteredMessage.Text?.Trim());
          }

          // MessageTerminatorを自動的に追加
          var dataToSend = AppendMessageTerminatorIfNeeded(filteredMessage);
          await _transport.SendAsync(dataToSend, request.CancellationToken);

          // 統計情報を更新
          Interlocked.Increment(ref _messagesSent);
        }
        catch (OperationCanceledException)
        {
          // キャンセルされた場合は、応答待ちのリクエストをキャンセル
          if (request.ResponseTcs != null)
          {
            lock (_pendingResponseRequestsLock)
            {
              // キューから削除
              var tempQueue = new Queue<SendRequest>();
              while (_pendingResponseRequests.Count > 0)
              {
                var item = _pendingResponseRequests.Dequeue();
                if (item != request)
                {
                  tempQueue.Enqueue(item);
                }
              }
              while (tempQueue.Count > 0)
              {
                _pendingResponseRequests.Enqueue(tempQueue.Dequeue());
              }
            }
            request.ResponseTcs.TrySetCanceled();
          }
        }
        catch (Exception ex)
        {
          // エラーハンドリング
          if (request.ResponseTcs != null)
          {
            lock (_pendingResponseRequestsLock)
            {
              // キューから削除
              var tempQueue = new Queue<SendRequest>();
              while (_pendingResponseRequests.Count > 0)
              {
                var item = _pendingResponseRequests.Dequeue();
                if (item != request)
                {
                  tempQueue.Enqueue(item);
                }
              }
              while (tempQueue.Count > 0)
              {
                _pendingResponseRequests.Enqueue(tempQueue.Dequeue());
              }
            }
            request.ResponseTcs.TrySetException(ex);
          }
          _logger?.LogError(ex, "Error sending message in client {Name}", Name);
        }
      }
    }
    catch (OperationCanceledException)
    {
      // 正常な終了
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Send loop error in client {Name}", Name);
    }
  }

  /// <summary>
  /// 自動再接続を開始
  /// </summary>
  private void StartAutoReconnect()
  {
    lock (_reconnectLock)
    {
      // 既に再接続タスクが実行中の場合は、新しいタスクを開始しない
      if (_reconnectTask != null && !_reconnectTask.IsCompleted)
      {
        return;
      }

      // リトライ統計を更新
      Interlocked.Increment(ref _connectionRetryAttempts);
      lock (_statsLock)
      {
        _lastRetryAttemptAt = DateTime.UtcNow;
      }

      _reconnectTask = Task.Run(async () =>
      {
        try
        {
          _logger?.LogInformation("TCP Client '{Name}' attempting automatic reconnection...", Name);

          // 再接続用のCancellationTokenSourceを作成
          // 元の_cancellationTokenSourceはDisconnectAsyncでキャンセルされているため、
          // 再接続処理では新しいトークンを使用する
          using var reconnectCts = new CancellationTokenSource();

          _logger?.LogDebug("TCP Client '{Name}' starting connection retry with policy: MaxRetryCount={MaxRetryCount}",
                    Name, _config.ConnectionRetryPolicy?.MaxRetryCount ?? -1);

          await RetryHelper.ExecuteConnectionRetryAsync(
                    async () =>
                    {
                      // 既に接続されている場合は何もしない
                      if (IsConnected)
                      {
                        return;
                      }

                      // トランスポートを再接続
                      await _transport.ConnectAsync(reconnectCts.Token);
                      lock (_statsLock)
                      {
                        _connectedAt = DateTime.UtcNow;
                        _connectionRetryAttempts = 0; // 再接続成功時にリセット
                      }
                      _logger?.LogInformation("TCP Client '{Name}' reconnected to {Host}:{Port}", Name, _config.RemoteHost, _config.RemotePort);

                      // 新しいCancellationTokenSourceを作成（前のものはキャンセル済み）
                      lock (_reconnectLock)
                      {
                        if (_cancellationTokenSource.IsCancellationRequested)
                        {
                          var oldCts = _cancellationTokenSource;
                          _cancellationTokenSource = new CancellationTokenSource();
                          oldCts.Dispose();
                        }
                      }

                      // 意図的切断フラグをリセット
                      _isIntentionalDisconnect = false;

                      OnConnected?.Invoke(this, EventArgs.Empty);

                      // 送信ループを再開
                      _sendLoopTask = Task.Run(SendLoopAsync, _cancellationTokenSource.Token);

                      // 受信ループを再開
                      _ = Task.Run(ReceiveLoopAsync, _cancellationTokenSource.Token);

                      // キープアライブを再開
                      if (_config.KeepAlive?.Enabled == true)
                      {
                        StartKeepAlive();
                      }
                    },
                    _config.ConnectionRetryPolicy,
                    reconnectCts.Token,
                    _logger);
        }
        catch (OperationCanceledException)
        {
          _logger?.LogInformation("TCP Client '{Name}' reconnection cancelled", Name);
        }
        catch (Exception ex)
        {
          // エラー統計を更新
          Interlocked.Increment(ref _errorCount);
          lock (_statsLock)
          {
            _lastError = ex.Message;
            _lastErrorAt = DateTime.UtcNow;
          }
          _logger?.LogError(ex, "TCP Client '{Name}' automatic reconnection failed", Name);
          OnError?.Invoke(this, ex);
        }
      });
    }
  }

  /// <summary>
  /// メッセージを送信（応答を待たない）
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task SendAsync(Message message, CancellationToken cancellationToken = default)
  {
    if (!IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    cancellationToken.ThrowIfCancellationRequested();

    var request = new SendRequest
    {
      Message = message,
      ResponseTcs = null, // 応答不要
      EnqueuedAt = DateTime.UtcNow,
      CancellationToken = cancellationToken
    };

    await _sendQueueWriter.WriteAsync(request, cancellationToken);
  }

  /// <summary>
  /// メッセージを送信して応答を待つ
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="timeout">タイムアウト時間</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>受信した応答メッセージ</returns>
  public async Task<Message> SendAsync(Message message, TimeSpan timeout, CancellationToken cancellationToken = default)
  {
    return await SendAndWaitAsync(message, _ => true, timeout, cancellationToken);
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
          var tempQueue = new Queue<SendRequest>();
          while (_pendingResponseRequests.Count > 0)
          {
            var item = _pendingResponseRequests.Dequeue();
            if (item != request)
            {
              tempQueue.Enqueue(item);
            }
          }
          while (tempQueue.Count > 0)
          {
            _pendingResponseRequests.Enqueue(tempQueue.Dequeue());
          }
        }
        tcs.TrySetException(new TimeoutException($"Request timed out after {timeout.TotalSeconds} seconds"));
      }
    });

    // キューに追加
    await _sendQueueWriter.WriteAsync(request, cancellationToken);

    // 応答を待つ
    return await tcs.Task;
  }

  private void StartKeepAlive()
  {
    lock (_configLock)
    {
      if (_config.KeepAlive == null || !_config.KeepAlive.Enabled)
      {
        return;
      }

      // 既存のタイマーを停止・破棄
      _keepAliveTimer?.Stop();
      _keepAliveTimer?.Dispose();
      _keepAliveTimer = null;

      _keepAliveTimer = new System.Timers.Timer(_config.KeepAlive.IntervalSeconds * 1000);
      _keepAliveTimer.Elapsed += async (sender, e) =>
      {
        // CancellationTokenがキャンセルされている場合はキープアライブを送信しない
        if (_cancellationTokenSource.Token.IsCancellationRequested)
        {
          _keepAliveTimer?.Stop();
          return;
        }

        if (IsConnected && !_disposed)
        {
          try
          {
            var keepAliveMessage = Message.FromString(_config.KeepAlive.Message, GetEncoding(_config.Encoding));
            await SendKeepAliveAsync(keepAliveMessage, TimeSpan.FromSeconds(_config.KeepAlive.IntervalSeconds));
          }
          catch (OperationCanceledException)
          {
            // キャンセルされた場合はタイマーを停止
            _keepAliveTimer?.Stop();
          }
          catch (Exception ex)
          {
            _logger?.LogError(ex, "Keep-alive failed for client {Name}", Name);
          }
        }
      };
      _keepAliveTimer.AutoReset = true;
      _keepAliveTimer.Start();
    }
  }

  private void StopKeepAlive()
  {
    lock (_configLock)
    {
      if (_keepAliveTimer != null)
      {
        _keepAliveTimer.Stop();
        _keepAliveTimer.Dispose();
        _keepAliveTimer = null;
      }
    }
  }

  /// <summary>
  /// キープアライブ専用の送信・応答待ちメソッド
  /// 通常のリクエスト応答と混在しないように、専用のTaskCompletionSourceを使用
  /// </summary>
  private async Task SendKeepAliveAsync(Message keepAliveMessage, TimeSpan timeout)
  {
    if (!IsConnected)
    {
      return;
    }

    // キープアライブ応答用のTaskCompletionSourceを作成
    var tcs = new TaskCompletionSource<Message>();
    var previousTcs = Interlocked.Exchange(ref _keepAliveResponseTcs, tcs);

    // 前のキープアライブがまだ待機中の場合はキャンセル
    if (previousTcs != null)
    {
      previousTcs.TrySetCanceled();
    }

    try
    {
      // キープアライブ送信時刻を更新
      lock (_statsLock)
      {
        _lastKeepAliveSentAt = DateTime.UtcNow;
      }

      // タイムアウト用のキャンセレーショントークン
      using var cts = new CancellationTokenSource(timeout);
      cts.Token.Register(() =>
      {
        if (Interlocked.CompareExchange(ref _keepAliveResponseTcs, null, tcs) == tcs)
        {
          tcs.TrySetCanceled();
        }
      });

      // 送信キューに追加（応答待ちはしない）
      var request = new SendRequest
      {
        Message = keepAliveMessage,
        ResponseTcs = null, // キープアライブは専用のTCSを使用
        EnqueuedAt = DateTime.UtcNow,
        CancellationToken = _cancellationTokenSource.Token
      };

      await _sendQueueWriter.WriteAsync(request, _cancellationTokenSource.Token);

      // 応答を待つ（タイムアウトは無視して続行）
      try
      {
        var response = await tcs.Task;
        // 応答はReceiveLoopAsyncでOnKeepAliveResponseReceivedイベントが発行される
      }
      catch (TaskCanceledException)
      {
        // タイムアウトは無視（キープアライブは継続）
        Interlocked.Increment(ref _keepAliveTimeoutCount);
        _logger?.LogWarning("Keep-alive response timeout for client {Name}", Name);
      }
    }
    catch (Exception)
    {
      // エラーが発生した場合はTaskCompletionSourceをクリア
      Interlocked.CompareExchange(ref _keepAliveResponseTcs, null, tcs);
      throw;
    }
  }

  /// <summary>
  /// MessageTerminatorが設定されている場合、メッセージに自動的に追加する
  /// </summary>
  private byte[] AppendMessageTerminatorIfNeeded(Message message)
  {
    if (string.IsNullOrEmpty(_config.MessageTerminator))
    {
      return message.RawData;
    }

    var encoding = GetEncoding(_config.Encoding);
    var terminatorBytes = encoding.GetBytes(_config.MessageTerminator);
    
    // 既に終端文字が含まれているかチェック（末尾に一致するか）
    if (message.RawData.Length >= terminatorBytes.Length)
    {
      var suffix = new byte[terminatorBytes.Length];
      Array.Copy(message.RawData, message.RawData.Length - terminatorBytes.Length, suffix, 0, terminatorBytes.Length);
      if (suffix.SequenceEqual(terminatorBytes))
      {
        // 既に終端文字が含まれている場合は追加しない
        return message.RawData;
      }
    }

    // 終端文字を追加
    var result = new byte[message.RawData.Length + terminatorBytes.Length];
    Array.Copy(message.RawData, 0, result, 0, message.RawData.Length);
    Array.Copy(terminatorBytes, 0, result, message.RawData.Length, terminatorBytes.Length);
    return result;
  }

  private Encoding GetEncoding(string encodingName)
  {
    return encodingName.ToUpperInvariant() switch
    {
      "UTF-8" => Encoding.UTF8,
      "SHIFT-JIS" or "SHIFTJIS" => Encoding.GetEncoding("shift_jis"),
      "ASCII" => Encoding.ASCII,
      _ => Encoding.UTF8
    };
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
          _keepAliveTimer?.Stop();
          _keepAliveTimer?.Dispose();
          _keepAliveTimer = null;

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
      lock (_statsLock)
      {
        var isReconnecting = false;
        lock (_reconnectLock)
        {
          isReconnecting = _reconnectTask != null && !_reconnectTask.IsCompleted;
        }

        var connectionDuration = _connectedAt.HasValue
          ? DateTime.UtcNow - _connectedAt.Value
          : (TimeSpan?)null;

        int pendingRequestsCount;
        lock (_pendingResponseRequestsLock)
        {
          pendingRequestsCount = _pendingResponseRequests.Count;
        }

        return new ClientConnectionInfo
        {
          IsConnected = IsConnected,
          ConnectedAt = _connectedAt,
          LastMessageReceivedAt = _lastMessageReceivedAt,
          RemoteHost = _config.RemoteHost,
          RemotePort = _config.RemotePort,
          IsReconnecting = isReconnecting,
          ConnectionDuration = connectionDuration,
          MessagesSent = Interlocked.Read(ref _messagesSent),
          MessagesReceived = Interlocked.Read(ref _messagesReceived),
          PendingRequests = pendingRequestsCount,
          LastKeepAliveSentAt = _lastKeepAliveSentAt,
          LastKeepAliveResponseReceivedAt = _lastKeepAliveResponseReceivedAt,
          KeepAliveTimeoutCount = Interlocked.CompareExchange(ref _keepAliveTimeoutCount, 0, 0),
          ErrorCount = Interlocked.CompareExchange(ref _errorCount, 0, 0),
          LastError = _lastError,
          LastErrorAt = _lastErrorAt,
          ConnectionRetryAttempts = Interlocked.CompareExchange(ref _connectionRetryAttempts, 0, 0),
          LastRetryAttemptAt = _lastRetryAttemptAt
        };
      }
    }
  }

  /// <summary>
  /// リソースを解放
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    DisconnectAsync().GetAwaiter().GetResult();
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

  private class MessageContext : IMessageContext
  {
    public SessionInfo? SessionInfo { get; }
    public bool IsServerSide { get; }
    public Dictionary<string, object> Properties { get; } = new();

    public MessageContext(SessionInfo? sessionInfo, bool isServerSide)
    {
      SessionInfo = sessionInfo;
      IsServerSide = isServerSide;
    }
  }
}

