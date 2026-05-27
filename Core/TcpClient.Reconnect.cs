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
                        _stats.ConnectedAt = DateTime.UtcNow;
                        _stats.ConnectionRetryAttempts = 0; // 再接続成功時にリセット
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

                      // 送信キューを再初期化（DisconnectAsyncでComplete()が呼ばれているため）
                      InitializeSendQueue();

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
                    _logger,
                    onDelayStarting: cts => { lock (_delayInterruptLock) { _delayInterruptCts = cts; } });
        }
        catch (OperationCanceledException)
        {
          _logger?.LogInformation("TCP Client '{Name}' reconnection cancelled", Name);
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
          _logger?.LogError(ex, "TCP Client '{Name}' automatic reconnection failed", Name);
          OnError?.Invoke(this, ex);
        }
      });
    }
  }
}
