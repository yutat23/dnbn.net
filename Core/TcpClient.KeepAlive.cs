using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

partial class TcpClient
{
  private void StartKeepAlive()
  {
    lock (_configLock)
    {
      if (_config.KeepAlive == null || !_config.KeepAlive.Enabled)
      {
        return;
      }

      if (_config.KeepAlive.ResponsePredicate == null)
      {
        // 述語未設定時はKeepAlive待機中に届いた任意の受信メッセージを応答として扱うため、
        // 通常のリクエスト応答を横取りする可能性がある（互換性のため挙動は維持）
        _logger?.LogWarning(
            "TCP Client '{Name}' KeepAlive is enabled without ResponsePredicate. " +
            "While a keep-alive is pending, any received message will be consumed as the keep-alive response, " +
            "which may steal responses to in-flight SendAsync requests. " +
            "Set KeepAliveConfig.ResponsePredicate to distinguish keep-alive responses.", Name);
      }

      // 既存のループを停止
      StopKeepAlive();

      var interval = TimeSpan.FromSeconds(_config.KeepAlive.IntervalSeconds);
      _keepAliveTimerCts = new CancellationTokenSource();
      var token = _keepAliveTimerCts.Token;

      // async void を避けるために専用 Task で実行
      _ = Task.Run(() => KeepAliveLoopAsync(interval, token), token);
    }
  }

  private async Task KeepAliveLoopAsync(TimeSpan interval, CancellationToken ct)
  {
    using var timer = new PeriodicTimer(interval);
    try
    {
      while (await timer.WaitForNextTickAsync(ct))
      {
        if (!IsConnected || _disposed)
        {
          break;
        }

        try
        {
          string messageText;
          string encodingName;
          lock (_configLock)
          {
            if (_config.KeepAlive == null || !_config.KeepAlive.Enabled)
            {
              break;
            }

            messageText = _config.KeepAlive.Message;
            encodingName = _config.Encoding;
          }

          var keepAliveMessage = Message.FromString(
              messageText,
              TcpMessageUtils.GetEncoding(encodingName));
          await SendKeepAliveAsync(keepAliveMessage, interval);
        }
        catch (OperationCanceledException)
        {
          // キャンセルによる正常終了
          break;
        }
        catch (Exception ex)
        {
          _logger?.LogError(ex, "Keep-alive failed for client {Name}", Name);
        }
      }
    }
    catch (OperationCanceledException)
    {
      // WaitForNextTickAsync のキャンセルによる正常終了
    }
  }

  private void StopKeepAlive()
  {
    lock (_configLock)
    {
      _keepAliveTimerCts?.Cancel();
      _keepAliveTimerCts?.Dispose();
      _keepAliveTimerCts = null;
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
    var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
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
        _stats.LastKeepAliveSentAt = DateTime.UtcNow;
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
        await tcs.Task;
        // 応答はReceiveLoopAsyncでOnKeepAliveResponseReceivedイベントが発行される
      }
      catch (TaskCanceledException)
      {
        // タイムアウトは無視（キープアライブは継続）
        Interlocked.Increment(ref _stats.KeepAliveTimeoutCount);
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
}
