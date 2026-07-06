using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

partial class TcpClient
{
  /// <summary>
  /// 受信ループ本体。切断・キャンセルで終了し、NW障害による終了かどうかを返す。
  /// 後始末（切断処理・自動再接続）は行わない（HandleReceiveLoopCompletionAsyncが担当）。
  /// DisconnectAsyncがこのタスクの完了を待機するため、このメソッド内から
  /// DisconnectAsyncを呼び出してはならない（デッドロックするため）。
  /// </summary>
  /// <param name="token">この接続専用のキャンセレーショントークン（再接続でフィールドが差し替わっても影響を受けないよう起動時にキャプチャ）</param>
  private async Task<bool> ReceiveLoopCoreAsync(CancellationToken token)
  {
    var buffer = new byte[4096];
    bool wasNetworkError = false;

    while (!token.IsCancellationRequested && IsConnected)
    {
      try
      {
        var bytesRead = await _transport.ReceiveAsync(buffer, 0, buffer.Length, token);
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

          // メッセージログ出力（EnableMessageLogging有効時はInformationレベル）
          _logger?.Log(MessageLogLevel, "TCP Client '{Name}' received message from {Host}:{Port}: {MessageText}",
              Name, _config.RemoteHost, _config.RemotePort, filteredMessage.Text?.Trim());

          // 統計情報を更新
          Interlocked.Increment(ref _stats.MessagesReceived);
          lock (_statsLock)
          {
            _stats.LastMessageReceivedAt = DateTime.UtcNow;
          }

          // キープアライブ応答をチェック（優先的に処理）
          bool handled = false;
          var keepAliveTcs = Volatile.Read(ref _keepAliveResponseTcs);
          if (keepAliveTcs != null && IsKeepAliveResponse(filteredMessage) &&
              Interlocked.CompareExchange(ref _keepAliveResponseTcs, null, keepAliveTcs) == keepAliveTcs)
          {
            keepAliveTcs.TrySetResult(filteredMessage);
            lock (_statsLock)
            {
              _stats.LastKeepAliveResponseReceivedAt = DateTime.UtcNow;
            }
            OnKeepAliveResponseReceived?.Invoke(this, filteredMessage);
            handled = true;
          }

          // 通知電文をチェック（応答マッチングをスキップして通常配信へ）
          bool isNotification = !handled && IsNotificationMessage(filteredMessage);

          // 待機中のリクエストをFIFO順序でチェック
          if (!handled && !isNotification)
          {
            SendRequest? matchedRequest = null;
            List<SendRequest>? timedOutRequests = null;
            lock (_pendingResponseRequestsLock)
            {
              // 完了済み・タイムアウトしたリクエストを削除（O(1) ノード削除）
              var now = DateTime.UtcNow;
              var node = _pendingResponseRequests.First;
              while (node != null)
              {
                var next = node.Next;
                var req = node.Value;
                if (req.ResponseTcs != null && req.ResponseTcs.Task.IsCompleted)
                {
                  // 完了済み（キャンセル等）はそのまま除去
                  _pendingResponseRequests.Remove(node);
                }
                else if (req.ResponseTcs != null && now - req.EnqueuedAt >= req.Timeout)
                {
                  _pendingResponseRequests.Remove(node);
                  (timedOutRequests ??= new List<SendRequest>()).Add(req);
                }
                node = next;
              }

              // FIFO順序で応答をマッチング（O(1) ノード削除）
              node = _pendingResponseRequests.First;
              while (node != null)
              {
                var next = node.Next;
                var req = node.Value;
                if (req.ResponseTcs != null && !req.ResponseTcs.Task.IsCompleted)
                {
                  if (req.ResponsePredicate == null || req.ResponsePredicate(filteredMessage))
                  {
                    _pendingResponseRequests.Remove(node);
                    matchedRequest = req;
                    handled = true;
                    break;
                  }
                }
                node = next;
              }
            }

            if (timedOutRequests != null)
            {
              foreach (var req in timedOutRequests)
              {
                req.ResponseTcs?.TrySetException(new TimeoutException($"Request timed out after {req.Timeout.TotalSeconds} seconds"));
              }
            }

            matchedRequest?.ResponseTcs?.TrySetResult(filteredMessage);
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
        if (!token.IsCancellationRequested)
        {
          // エラー統計を更新
          Interlocked.Increment(ref _stats.ErrorCount);
          lock (_statsLock)
          {
            _stats.LastError = ex.Message;
            _stats.LastErrorAt = DateTime.UtcNow;
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

    // ループ本体に一度も入らず終了した場合（接続完了直後〜最初の受信前にNW障害が
    // 発生し IsConnected=false になったケース）も、キャンセルでも意図的な切断でも
    // なければNW障害として扱う。これがないと自動再接続が発動しないデッドウィンドウになる。
    if (!wasNetworkError && !token.IsCancellationRequested && !_isIntentionalDisconnect)
    {
      wasNetworkError = true;
    }

    return wasNetworkError;
  }

  /// <summary>
  /// 受信ループ終了後の後始末（切断処理と自動再接続）。
  /// この処理は受信ループ本体とは別タスクで実行され、接続世代（epoch）ガードにより
  /// 「旧接続の後始末が、その後に確立された新しい接続を巻き添えで切断する」ことを防ぐ。
  /// </summary>
  /// <param name="receiveLoopTask">受信ループ本体のタスク</param>
  /// <param name="epoch">受信ループ起動時点の接続世代</param>
  private async Task HandleReceiveLoopCompletionAsync(Task<bool> receiveLoopTask, int epoch)
  {
    bool wasNetworkError;
    try
    {
      wasNetworkError = await receiveLoopTask.ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      // ReceiveLoopCoreAsyncは例外を内部処理するため通常ここには来ないが、安全側でNW障害扱い
      _logger?.LogError(ex, "Receive loop terminated unexpectedly in client {Name}", Name);
      wasNetworkError = !_isIntentionalDisconnect;
    }

    // 既に新しい接続が確立されている場合は何もしない
    if (Volatile.Read(ref _connectionEpoch) != epoch)
    {
      return;
    }

    if (IsConnected || wasNetworkError)
    {
      // NW障害による切断として扱う（epochが変わっていたら中で何もしない）
      await DisconnectIfCurrentEpochAsync(epoch, isIntentional: !wasNetworkError).ConfigureAwait(false);
    }

    // NW障害による切断の場合、自動再接続を試行
    if (wasNetworkError && _config.ConnectionRetryPolicy != null &&
        Volatile.Read(ref _connectionEpoch) == epoch)
    {
      _logger?.LogInformation("TCP Client '{Name}' will attempt automatic reconnection to {Host}:{Port}...", Name, _config.RemoteHost, _config.RemotePort);
      StartAutoReconnect();
    }
  }

  /// <summary>
  /// 接続世代が変わっていない場合のみ切断処理を実行する。
  /// 世代チェックは_disconnectLock内で行うため、新しい接続の確立処理と競合しない。
  /// </summary>
  private async Task DisconnectIfCurrentEpochAsync(int epoch, bool isIntentional)
  {
    await _disconnectLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
    try
    {
      if (Volatile.Read(ref _connectionEpoch) != epoch)
      {
        return;
      }
      await DisconnectCoreAsync(isIntentional, CancellationToken.None).ConfigureAwait(false);
    }
    finally
    {
      _disconnectLock.Release();
    }
  }

  private bool IsKeepAliveResponse(Message message)
  {
    Func<Message, bool>? responsePredicate;
    lock (_configLock)
    {
      responsePredicate = _config.KeepAlive?.ResponsePredicate;
    }

    return responsePredicate?.Invoke(message) ?? true;
  }

  private bool IsNotificationMessage(Message message)
  {
    Func<Message, bool>? predicate;
    lock (_configLock)
    {
      predicate = _config.NotificationPredicate;
    }

    if (predicate == null)
    {
      return false;
    }

    try
    {
      return predicate(message);
    }
    catch (Exception ex)
    {
      // ユーザー定義述語の例外で受信ループを止めないよう、通知ではないものとして扱う
      _logger?.LogError(ex, "NotificationPredicate threw an exception in client {Name}", Name);
      return false;
    }
  }
}
