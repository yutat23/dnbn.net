using Dnbn.Configuration;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

partial class TcpClient
{
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
      Interlocked.Increment(ref _stats.ConnectionRetryAttempts);
      lock (_statsLock)
      {
        _stats.LastRetryAttemptAt = DateTime.UtcNow;
      }

      // 再接続用のCancellationTokenSourceを作成
      // 元の_cancellationTokenSourceはDisconnectAsyncでキャンセルされているため、
      // 再接続処理では新しいトークンを使用する。
      // フィールドに保持することで、意図的な切断やDisposeで再接続を中断できる
      _reconnectCts?.Dispose();
      _reconnectCts = new CancellationTokenSource();
      var reconnectCts = _reconnectCts;

      _reconnectTask = Task.Run(async () =>
      {
        try
        {
          _logger?.LogInformation("TCP Client '{Name}' attempting automatic reconnection to {Host}:{Port}...", Name, _config.RemoteHost, _config.RemotePort);

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
                      await _transport.ConnectAsync(reconnectCts.Token).ConfigureAwait(false);

                      ResetConnectionStateForConnect();

                      OnTransportConnected(isReconnect: true);
                    },
                    _config.ConnectionRetryPolicy,
                    reconnectCts.Token,
                    _logger,
                    onDelayStarting: cts => { lock (_delayInterruptLock) { _delayInterruptCts = cts; } },
                    targetDescription: RetryLogTarget).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          _logger?.LogInformation("TCP Client '{Name}' reconnection to {Host}:{Port} cancelled", Name, _config.RemoteHost, _config.RemotePort);
        }
        catch (Exception ex)
        {
          // エラー統計を更新
          Interlocked.Increment(ref _stats.ErrorCount);
          lock (_statsLock)
          {
            _stats.LastError = ex.Message;
            _stats.LastErrorAt = DateTime.UtcNow;
          }
          _logger?.LogError(ex, "TCP Client '{Name}' automatic reconnection to {Host}:{Port} failed", Name, _config.RemoteHost, _config.RemotePort);
          OnError?.Invoke(this, ex);
        }
      });
    }
  }
}
