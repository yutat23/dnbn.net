using System.Text;
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
}
