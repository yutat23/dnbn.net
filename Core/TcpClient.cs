using System.Collections.Concurrent;
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
  private readonly ClientConfig _config;
  private readonly ILogger<TcpClient>? _logger;
  private readonly List<IMessageFilter> _filters;
  private readonly ITransport _transport;
  private readonly MessageParser _parser;
  private readonly Subject<Message> _messageReceivedSubject = new();
  private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Message>> _pendingRequests = new();
  private System.Timers.Timer? _keepAliveTimer;
  private CancellationTokenSource _cancellationTokenSource = new();
  private bool _disposed = false;
  private TaskCompletionSource<Message>? _keepAliveResponseTcs;
  private bool _isIntentionalDisconnect = false;
  private Task? _reconnectTask;
  private readonly object _reconnectLock = new();

  public string Name => _config.Name;
  public bool IsConnected => _transport.IsConnected;

  public event EventHandler<Message>? OnMessageReceived;
  public event EventHandler? OnConnected;
  public event EventHandler? OnDisconnected;
  public event EventHandler<Exception>? OnError;
  public event EventHandler<Message>? OnKeepAliveResponseReceived;

  public IObservable<Message> MessageReceived => _messageReceivedSubject;

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
    _parser = new MessageParser(
        encoding,
        config.MessageTerminator,
        config.FixedHeaderLength,
        config.FixedBodyLength,
        config.LengthFieldOffset,
        config.LengthFieldLength);
  }

  /// <summary>
  /// サーバーに接続
  /// </summary>
  public async Task ConnectAsync()
  {
    if (IsConnected)
    {
      return;
    }

    // 接続リトライポリシーが設定されている場合は、リトライを実行
    if (_config.ConnectionRetryPolicy != null)
    {
      await RetryHelper.ExecuteConnectionRetryAsync(
          async () =>
          {
            await _transport.ConnectAsync();
            _logger?.LogInformation("TCP Client '{Name}' connected to {Host}:{Port}", Name, _config.RemoteHost, _config.RemotePort);

            OnConnected?.Invoke(this, EventArgs.Empty);

            // 受信ループを開始
            _ = Task.Run(ReceiveLoopAsync, _cancellationTokenSource.Token);

            // キープアライブを開始
            if (_config.KeepAlive?.Enabled == true)
            {
              StartKeepAlive();
            }
          },
          _config.ConnectionRetryPolicy,
          _cancellationTokenSource.Token,
          _logger);
    }
    else
    {
      // リトライポリシーが設定されていない場合は、従来通り1回だけ試行
      await _transport.ConnectAsync();
      _logger?.LogInformation("TCP Client '{Name}' connected to {Host}:{Port}", Name, _config.RemoteHost, _config.RemotePort);

      OnConnected?.Invoke(this, EventArgs.Empty);

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
  public async Task DisconnectAsync(bool isIntentional = true)
  {
    if (!IsConnected)
    {
      return;
    }

    _isIntentionalDisconnect = isIntentional;

    _keepAliveTimer?.Stop();
    _keepAliveTimer?.Dispose();
    _keepAliveTimer = null;

    _cancellationTokenSource.Cancel();
    await _transport.DisconnectAsync();

    // 待機中のリクエストをキャンセル
    foreach (var pending in _pendingRequests.Values)
    {
      pending.TrySetCanceled();
    }
    _pendingRequests.Clear();

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
        var bytesRead = await _transport.ReceiveAsync(buffer, 0, buffer.Length);
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

          // キープアライブ応答をチェック（優先的に処理）
          bool handled = false;
          var keepAliveTcs = Interlocked.Exchange(ref _keepAliveResponseTcs, null);
          if (keepAliveTcs != null)
          {
            keepAliveTcs.TrySetResult(filteredMessage);
            OnKeepAliveResponseReceived?.Invoke(this, filteredMessage);
            handled = true;
          }

          // 待機中のリクエストをチェック
          if (!handled)
          {
            foreach (var kvp in _pendingRequests.ToList())
            {
              // 最初の待機中のリクエストにメッセージを渡す
              // 応答条件のチェックはSendAndWaitAsync側で行う
              if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
              {
                tcs.TrySetResult(filteredMessage);
                handled = true;
                break;
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
                      await _transport.ConnectAsync();
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
  public async Task SendAsync(Message message)
  {
    if (!IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    // フィルターパイプラインを適用
    var filteredMessage = message;
    foreach (var filter in _filters)
    {
      var ctx = new MessageContext(null, false);
      filteredMessage = await filter.OnSendingAsync(filteredMessage, ctx);
    }

    await _transport.SendAsync(filteredMessage.RawData);
  }

  /// <summary>
  /// メッセージを送信して応答を待つ
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="timeout">タイムアウト時間</param>
  /// <returns>受信した応答メッセージ</returns>
  public async Task<Message> SendAsync(Message message, TimeSpan timeout)
  {
    return await SendAndWaitAsync(message, _ => true, timeout);
  }

  public async Task<Message> SendAndWaitAsync(
      Message requestMessage,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout)
  {
    if (!IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    // リトライポリシーが設定されている場合は、それを使用
    if (_config.RetryPolicy != null)
    {
      return await RetryHelper.ExecuteWithRetryAsync(
          async () => await SendAndWaitSingleAsync(requestMessage, responsePredicate, timeout),
          _config.RetryPolicy,
          responsePredicate,
          _cancellationTokenSource.Token,
          _logger);
    }

    return await SendAndWaitSingleAsync(requestMessage, responsePredicate, timeout);
  }

  private async Task<Message> SendAndWaitSingleAsync(
      Message requestMessage,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout)
  {
    var requestId = Guid.NewGuid();
    var tcs = new TaskCompletionSource<Message>();
    var responseQueue = new ConcurrentQueue<Message>();
    var responseReceived = new SemaphoreSlim(0);

    // 応答メッセージを受信するイベントハンドラ
    void OnResponseReceived(object? sender, Message msg)
    {
      responseQueue.Enqueue(msg);
      responseReceived.Release();
    }

    OnMessageReceived += OnResponseReceived;

    try
    {
      // 送信
      await SendAsync(requestMessage);

      // タイムアウト用のキャンセレーショントークン
      using var cts = new CancellationTokenSource(timeout);
      var startTime = DateTime.UtcNow;

      // 応答条件を満たすメッセージが来るまで待つ
      while (true)
      {
        var elapsed = DateTime.UtcNow - startTime;
        if (elapsed >= timeout)
        {
          throw new TimeoutException($"Request timed out after {timeout.TotalSeconds} seconds");
        }

        var remainingTimeout = timeout - elapsed;
        var waitTask = responseReceived.WaitAsync(remainingTimeout, cts.Token);

        try
        {
          await waitTask;
        }
        catch (OperationCanceledException)
        {
          throw new TimeoutException($"Request timed out after {timeout.TotalSeconds} seconds");
        }

        // キューからメッセージを取得
        while (responseQueue.TryDequeue(out var response))
        {
          if (responsePredicate(response))
          {
            return response;
          }
          // 条件を満たさない場合は、次のメッセージを待つ
        }
      }
    }
    finally
    {
      OnMessageReceived -= OnResponseReceived;
      responseReceived.Dispose();
    }
  }

  private void StartKeepAlive()
  {
    if (_config.KeepAlive == null)
    {
      return;
    }

    _keepAliveTimer = new System.Timers.Timer(_config.KeepAlive.IntervalSeconds * 1000);
    _keepAliveTimer.Elapsed += async (sender, e) =>
    {
      if (IsConnected && !_disposed)
      {
        try
        {
          var keepAliveMessage = Message.FromString(_config.KeepAlive.Message, GetEncoding(_config.Encoding));
          await SendKeepAliveAsync(keepAliveMessage, TimeSpan.FromSeconds(_config.KeepAlive.IntervalSeconds));
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

    // フィルターパイプラインを適用
    var filteredMessage = keepAliveMessage;
    foreach (var filter in _filters)
    {
      var ctx = new MessageContext(null, false);
      filteredMessage = await filter.OnSendingAsync(filteredMessage, ctx);
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
      // 送信
      await _transport.SendAsync(filteredMessage.RawData);

      // タイムアウト用のキャンセレーショントークン
      using var cts = new CancellationTokenSource(timeout);
      cts.Token.Register(() =>
      {
        if (Interlocked.CompareExchange(ref _keepAliveResponseTcs, null, tcs) == tcs)
        {
          tcs.TrySetCanceled();
        }
      });

      // 応答を待つ（タイムアウトは無視して続行）
      try
      {
        var response = await tcs.Task;
        // 応答はReceiveLoopAsyncでOnKeepAliveResponseReceivedイベントが発行される
      }
      catch (TaskCanceledException)
      {
        // タイムアウトは無視（キープアライブは継続）
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
    _keepAliveTimer?.Dispose();
    _disposed = true;
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

