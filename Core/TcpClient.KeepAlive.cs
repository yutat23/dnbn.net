using Dnbn.Models;
using Microsoft.Extensions.Logging;
#if NETSTANDARD2_0
using TaskCompletionSource = Dnbn.Core.TaskCompletionSourceCompat;
#endif

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
        // 述語未設定時、KeepAlive応答はFIFO順で「次に届いた応答」として相関される。
        // 要求・応答が順序どおりに返るプロトコルでは正しく動作するが、
        // KeepAlive応答待ち中に届いたサーバープッシュ通知はKeepAlive応答として消費される
        _logger?.LogWarning(
            "TCP Client '{Name}' KeepAlive is enabled without ResponsePredicate. " +
            "While a keep-alive is awaiting its response, an unsolicited push message may be consumed as the keep-alive response. " +
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

          // 通常要求が応答待ちの間はKeepAliveを延期する。
          // 電文が流れている間は死活確認が不要であり、FIFO相関の取り違え
          // （KeepAlive応答と通常応答の混線）も避けられる
          bool hasPendingUserRequests;
          lock (_pendingResponseRequestsLock)
          {
            hasPendingUserRequests = _pendingResponseRequests.Any(r => !r.IsKeepAlive);
          }
          if (hasPendingUserRequests)
          {
            continue;
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
  /// KeepAlive電文を送信し、応答を待つ。
  /// KeepAliveは通常要求と同じ送信キュー・FIFO応答キューに載せ、
  /// 応答タイムアウトは実送信の完了時点から計測する。これにより
  /// 先行する通常要求の応答の横取りや、送信前タイムアウトによる孤児応答が発生しない。
  /// 応答受信時の処理（統計更新・OnKeepAliveResponseReceived発火）はReceiveLoopCoreAsyncが行う。
  /// </summary>
  private async Task SendKeepAliveAsync(Message keepAliveMessage, TimeSpan timeout)
  {
    if (!IsConnected)
    {
      return;
    }

    Func<Message, bool>? responsePredicate;
    bool disconnectOnTimeout;
    lock (_configLock)
    {
      responsePredicate = _config.KeepAlive?.ResponsePredicate;
      disconnectOnTimeout = _config.KeepAlive?.DisconnectOnTimeout ?? true;
    }

    var connectionToken = _cancellationTokenSource.Token;
    var epoch = Volatile.Read(ref _connectionEpoch);
    var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
    var sendCompletedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var request = new SendRequest
    {
      Message = keepAliveMessage,
      ResponseTcs = tcs,
      // 未設定（null）は任意の応答にマッチ（従来の既定挙動を維持）
      ResponsePredicate = responsePredicate,
      Timeout = timeout,
      IsKeepAlive = true,
      SendCompletedTcs = sendCompletedTcs,
      EnqueuedAt = DateTime.UtcNow,
      CancellationToken = connectionToken
    };

    try
    {
      await _sendQueueWriter.WriteAsync(request, connectionToken);

      // 実送信の完了を待つ（送信失敗・キャンセルはここで例外になる）。
      // 送信キューやフィルターの遅延で送信前にタイムアウトが走ると、
      // 後から実送信された電文への応答が孤児になるため、計測は送信完了後に開始する
      await sendCompletedTcs.Task.WaitAsync(connectionToken);

      // 応答タイムアウト（受信が一切ない場合にも待機を打ち切れるよう、自前のタイマーで管理する。
      // 受信ループのスイープはKeepAliveを対象にしない）
      using var timeoutCts = new CancellationTokenSource(timeout);
      timeoutCts.Token.Register(() =>
      {
        // 受信ループのマッチングと競合しないよう、TCSの完了とpending除去をロック内で行う
        lock (_pendingResponseRequestsLock)
        {
          if (tcs.TrySetException(new TimeoutException($"Keep-alive response timed out after {timeout.TotalSeconds} seconds")))
          {
            _pendingResponseRequests.Remove(request);
            if (disconnectOnTimeout)
            {
              // タイムアウト検出と同時に応答マッチングを停止する（切断完了時に解除）。
              // 切断処理が走り出すまでの間に遅延KeepAlive応答が届いても、
              // 後続要求の応答として誤配されない
              _responseMatchingSuspended = true;
            }
          }
        }
      });

      await tcs.Task;
    }
    catch (TimeoutException)
    {
      Interlocked.Increment(ref _stats.KeepAliveTimeoutCount);

      if (disconnectOnTimeout)
      {
        // 応答が来ない接続は相手が死んでいるか、FIFO相関がもう信頼できない
        // （遅延応答が後続要求の応答として誤配される）ため、NW障害として切断する。
        // 接続世代の確認は_disconnectLock内で行われるため、
        // 既に再接続済みの新しい接続を巻き添えにしない
        _logger?.LogWarning("Keep-alive response timeout for client {Name}; disconnecting to prevent response correlation corruption (DisconnectOnTimeout=true)", Name);
        var disconnected = await DisconnectIfCurrentEpochAsync(
            epoch,
            isIntentional: false,
            expectedConnectionToken: connectionToken).ConfigureAwait(false);

        // NW障害切断と同様に自動再接続へ繋ぐ（この間に手動再接続されていれば何もしない）
        if (disconnected && !_isIntentionalDisconnect && _config.ConnectionRetryPolicy != null &&
            Volatile.Read(ref _connectionEpoch) == epoch)
        {
          _logger?.LogInformation("TCP Client '{Name}' will attempt automatic reconnection to {Host}:{Port}...", Name, _config.RemoteHost, _config.RemotePort);
          StartAutoReconnect();
        }
      }
      else
      {
        _logger?.LogWarning("Keep-alive response timeout for client {Name}", Name);
      }
    }
    catch (OperationCanceledException)
    {
      // 切断・再接続によるキャンセル（正常系）
    }
  }
}
