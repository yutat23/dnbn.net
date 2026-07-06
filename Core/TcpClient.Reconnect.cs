using Dnbn.Configuration;
using Dnbn.Models;
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

      SetConnectionState(ConnectionState.Reconnecting);

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
                      // ConnectAsyncとの並行実行を直列化する（二重接続の防止）
                      await _connectLock.WaitAsync(reconnectCts.Token).ConfigureAwait(false);
                      try
                      {
                        // 既に接続されている場合は何もしない
                        if (IsConnected)
                        {
                          return;
                        }

                        // トランスポートを再接続
                        await _transport.ConnectAsync(reconnectCts.Token).ConfigureAwait(false);

                        // 状態リセットとループ起動は切断処理・旧ループの後始末と直列化する
                        await _disconnectLock.WaitAsync(reconnectCts.Token).ConfigureAwait(false);
                        try
                        {
                          ResetConnectionStateForConnect();

                          OnTransportConnected(isReconnect: true);
                        }
                        finally
                        {
                          _disconnectLock.Release();
                        }
                      }
                      finally
                      {
                        _connectLock.Release();
                      }
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
          if (!IsConnected)
          {
            SetConnectionState(ConnectionState.Disconnected);
          }
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
          if (!IsConnected)
          {
            SetConnectionState(ConnectionState.Disconnected);
          }
        }
      });
    }
  }
}
