using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

partial class TcpClient
{
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
          // 応答待ちのリクエストの場合は、リストに追加（タイムアウト済みはスキップ）
          if (request.ResponseTcs != null && !request.ResponseTcs.Task.IsCompleted)
          {
            lock (_pendingResponseRequestsLock)
            {
              _pendingResponseRequests.AddLast(request);
            }
          }

          // フィルターパイプラインを適用
          var filteredMessage = request.Message;
          foreach (var filter in _filters)
          {
            var ctx = new MessageContext(null, false);
            filteredMessage = await filter.OnSendingAsync(filteredMessage, ctx);
          }

          // メッセージログ出力
          _logger?.LogDebug("TCP Client '{Name}' sending message: {MessageText}", Name, filteredMessage.Text?.Trim());

          // MessageTerminatorを自動的に追加
          var dataToSend = TcpMessageUtils.AppendMessageTerminatorIfNeeded(filteredMessage, _config.MessageTerminator, _config.Encoding);
          await _transport.SendAsync(dataToSend, request.CancellationToken);

          // 統計情報を更新
          Interlocked.Increment(ref _stats.MessagesSent);

          // 送信完了を通知（通知電文の送信元へ）
          request.SendCompletedTcs?.TrySetResult();
        }
        catch (OperationCanceledException)
        {
          // キャンセルされた場合は、応答待ちのリクエストをキャンセル
          if (request.ResponseTcs != null)
          {
            lock (_pendingResponseRequestsLock)
            {
              _pendingResponseRequests.Remove(request);
            }
            request.ResponseTcs.TrySetCanceled();
          }
          request.SendCompletedTcs?.TrySetCanceled();
        }
        catch (Exception ex)
        {
          // エラーハンドリング
          if (request.ResponseTcs != null)
          {
            lock (_pendingResponseRequestsLock)
            {
              _pendingResponseRequests.Remove(request);
            }
            request.ResponseTcs.TrySetException(ex);
          }
          request.SendCompletedTcs?.TrySetException(ex);
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
}
