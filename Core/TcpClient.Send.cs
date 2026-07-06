using System.Threading.Channels;
using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

partial class TcpClient
{
  /// <summary>
  /// 送信ループ（順次処理）
  /// </summary>
  /// <param name="sendQueueReader">この接続専用の送信キューリーダー（再接続でフィールドが差し替わっても影響を受けないよう起動時にキャプチャ）</param>
  /// <param name="token">この接続専用のキャンセレーショントークン</param>
  private async Task SendLoopAsync(ChannelReader<SendRequest> sendQueueReader, CancellationToken token)
  {
    try
    {
      await foreach (var request in sendQueueReader.ReadAllAsync(token))
      {
        try
        {
          // キュー滞留中にキャンセルされたリクエストは送信しない
          if (request.CancellationToken.IsCancellationRequested)
          {
            request.ResponseTcs?.TrySetCanceled(request.CancellationToken);
            request.SendCompletedTcs?.TrySetCanceled(request.CancellationToken);
            continue;
          }

          // キュー滞留中にタイムアウト等で完了済みになったリクエストは送信しない
          // （呼び出し側は既にTimeoutExceptionを受け取っており、電文だけが
          // 後から届くと応答の対応関係が崩れるため）
          if (request.ResponseTcs != null && request.ResponseTcs.Task.IsCompleted)
          {
            continue;
          }

          // 応答待ちのリクエストの場合は、リストに追加
          if (request.ResponseTcs != null)
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

          // メッセージログ出力（EnableMessageLogging有効時はInformationレベル）
          _logger?.Log(MessageLogLevel, "TCP Client '{Name}' sending message to {Host}:{Port}: {MessageText}",
              Name, _config.RemoteHost, _config.RemotePort, filteredMessage.Text?.Trim());

          // MessageTerminatorを自動的に追加
          var dataToSend = TcpMessageUtils.AppendMessageTerminatorIfNeeded(filteredMessage, _config.MessageTerminator, _config.Encoding);
          await _transport.SendAsync(dataToSend, request.CancellationToken);

          // 統計情報を更新
          Interlocked.Increment(ref _stats.MessagesSent);

          // メッセージトレース（種別はリクエストの構成から判定）
          var traceKind = request.ResponseTcs != null ? MessageTraceKind.Request
              : request.SendCompletedTcs != null ? MessageTraceKind.OneWay
              : MessageTraceKind.KeepAlive;
          // 終端文字を含め、実際にトランスポートへ渡したバイト列を記録する。
          var traceMessage = new Message
          {
            RawData = dataToSend,
            Text = TcpMessageUtils.GetEncoding(_config.Encoding).GetString(dataToSend),
            Code = filteredMessage.Code,
            Timestamp = filteredMessage.Timestamp,
            Metadata = filteredMessage.Metadata,
          };
          RaiseMessageTrace(MessageTraceDirection.Sent, traceKind, traceMessage);

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
