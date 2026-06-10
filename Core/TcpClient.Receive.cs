using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

partial class TcpClient
{
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

          // メッセージログ出力
          _logger?.LogDebug("TCP Client '{Name}' received message: {MessageText}", Name, filteredMessage.Text?.Trim());

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
        if (!_cancellationTokenSource.Token.IsCancellationRequested)
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
