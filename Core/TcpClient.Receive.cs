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
        if (!_cancellationTokenSource.Token.IsCancellationRequested)
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
}
