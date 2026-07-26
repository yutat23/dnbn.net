using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

partial class TcpClient
{
  /// <summary>
  /// 受信ループ本体。切断・キャンセルで終了し、NW障害による終了かどうかを返す。
  /// 後始末（切断処理・自動再接続）は行わない（HandleReceiveLoopCompletionAsyncが担当）。
  /// DisconnectAsyncがこのタスクの完了を待機するため、このメソッド内から
  /// DisconnectAsyncを呼び出してはならない（デッドロックするため）。
  /// </summary>
  /// <param name="token">この接続専用のキャンセレーショントークン（再接続でフィールドが差し替わっても影響を受けないよう起動時にキャプチャ）</param>
  private async Task<bool> ReceiveLoopCoreAsync(CancellationToken token)
  {
    var buffer = new byte[4096];
    bool wasNetworkError = false;

    while (!token.IsCancellationRequested && IsConnected)
    {
      try
      {
        var bytesRead = await _transport.ReceiveAsync(buffer, 0, buffer.Length, token);
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

          // メッセージログ出力（EnableMessageLogging有効時はInformationレベル）
          _logger?.Log(MessageLogLevel, "TCP Client '{Name}' received message from {Host}:{Port}: {MessageText}",
              Name, _config.RemoteHost, _config.RemotePort, filteredMessage.Text?.Trim());

          // 統計情報を更新
          Interlocked.Increment(ref _stats.MessagesReceived);
          lock (_statsLock)
          {
            _stats.LastMessageReceivedAt = DateTime.UtcNow;
          }

          bool handled = false;

          // 通知電文をチェック（応答マッチングをスキップして通常配信へ）
          bool isNotification = IsNotificationMessage(filteredMessage);

          // 待機中のリクエストをFIFO順序でチェック
          if (!handled && !isNotification)
          {
            SendRequest? matchedRequest = null;
            lock (_pendingResponseRequestsLock)
            {
              // 完了済み・タイムアウトしたリクエストを削除（O(1) ノード削除）。
              // KeepAliveのタイムアウトはSendKeepAliveAsyncが実送信完了を基準に管理するため対象外
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
                else if (req.ResponseTcs != null && !req.IsKeepAlive && now - req.EnqueuedAt >= req.Timeout)
                {
                  _pendingResponseRequests.Remove(node);
                  if (req.ResponseTcs.TrySetException(new TimeoutException($"Request timed out after {req.Timeout.TotalSeconds} seconds")))
                  {
                    // タイマーコールバックより先に受信ループが期限切れを検出した場合も、
                    // 同じ受信電文を後続要求へ誤相関させない。
                    SuspendResponseMatchingIfRecoveryRequired(req);
                  }
                }
                node = next;
              }

              // FIFO順序で応答をマッチング（O(1) ノード削除）。
              // 並行するタイムアウトとの競合を防ぐため、TCSの完了（TrySetResult）まで
              // このロック内で行い、成功した場合のみマッチとして扱う。
              // KeepAliveタイムアウト検出後（切断進行中）はFIFO相関を信頼できないため
              // マッチングを行わない（遅延KeepAlive応答が後続要求の応答として誤配されるのを防ぐ）
              if (!_responseMatchingSuspended)
              {
                node = _pendingResponseRequests.First;
                while (node != null)
                {
                  var next = node.Next;
                  var req = node.Value;
                  if (req.ResponseTcs != null &&
                      Volatile.Read(ref req.WireWriteStarted) != 0 &&
                      (req.ResponsePredicate == null || req.ResponsePredicate(filteredMessage)))
                  {
                    _pendingResponseRequests.Remove(node);
                    matchedRequest = req;
                    // 送信完了処理と並行して応答が届いた場合は、この電文を対象要求へ予約する。
                    // 後続要求や通知として誤配しないため、TCS完了の勝敗はロック外で確認する。
                    handled = true;
                    break;
                  }
                  node = next;
                }
              }
            }

            // 応答は実送信トレースより先に到着し得る。送信完了通知を待ってから
            // 応答TCSと受信トレースを完了し、診断イベントの因果順序を維持する。
            if (matchedRequest != null)
            {
              var sendCompleted = true;
              if (matchedRequest.SendCompletedTcs != null)
              {
                try
                {
                  await matchedRequest.SendCompletedTcs.Task.ConfigureAwait(false);
                }
                catch
                {
                  sendCompleted = false;
                }
              }

              var responseAccepted = false;
              if (sendCompleted)
              {
                lock (_pendingResponseRequestsLock)
                {
                  responseAccepted = matchedRequest.ResponseTcs?.TrySetResult(filteredMessage) == true;
                }
              }

              if (responseAccepted && matchedRequest.IsKeepAlive)
              {
                // KeepAlive応答（KeepAliveは通常要求と同じFIFOキューで応答と相関させる）
                lock (_statsLock)
                {
                  _stats.LastKeepAliveResponseReceivedAt = DateTime.UtcNow;
                }
                RaiseMessageTrace(MessageTraceDirection.Received, MessageTraceKind.KeepAliveResponse, filteredMessage);
                RaiseKeepAliveResponseReceived(filteredMessage);
              }
              else if (responseAccepted)
              {
                RaiseMessageTrace(
                    MessageTraceDirection.Received,
                    MessageTraceKind.Response,
                    filteredMessage,
                    (DateTime.UtcNow - matchedRequest.EnqueuedAt).TotalMilliseconds);
              }
            }
          }

          if (!handled)
          {
            RaiseMessageTrace(MessageTraceDirection.Received, MessageTraceKind.Notification, filteredMessage);
            SafeEventDispatcher.Invoke(OnMessageReceived, this, filteredMessage,
                ex => _logger?.LogError(ex, "OnMessageReceived handler threw an exception in client {Name}", Name));
            _messageReceivedSubject.Publish(filteredMessage,
                ex => _logger?.LogError(ex, "MessageReceived observer threw an exception in client {Name}", Name));
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
        if (!token.IsCancellationRequested)
        {
          // エラー統計を更新
          Interlocked.Increment(ref _stats.ErrorCount);
          lock (_statsLock)
          {
            _stats.LastError = ex.Message;
            _stats.LastErrorAt = DateTime.UtcNow;
          }
          _logger?.LogError(ex, "Error receiving data in client {Name}", Name);
          SafeEventDispatcher.Invoke(OnError, this, ex,
              handlerEx => _logger?.LogError(handlerEx, "OnError handler threw an exception in client {Name}", Name));
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

    // ループ本体に一度も入らず終了した場合（接続完了直後〜最初の受信前にNW障害が
    // 発生し IsConnected=false になったケース）も、キャンセルでも意図的な切断でも
    // なければNW障害として扱う。これがないと自動再接続が発動しないデッドウィンドウになる。
    if (!wasNetworkError && !token.IsCancellationRequested && !_isIntentionalDisconnect)
    {
      wasNetworkError = true;
    }

    return wasNetworkError;
  }

  /// <summary>
  /// 受信ループ終了後の後始末（切断処理と自動再接続）。
  /// この処理は受信ループ本体とは別タスクで実行され、接続世代（epoch）ガードにより
  /// 「旧接続の後始末が、その後に確立された新しい接続を巻き添えで切断する」ことを防ぐ。
  /// </summary>
  /// <param name="receiveLoopTask">受信ループ本体のタスク</param>
  /// <param name="epoch">受信ループ起動時点の接続世代</param>
  private async Task HandleReceiveLoopCompletionAsync(Task<bool> receiveLoopTask, int epoch)
  {
    bool wasNetworkError;
    try
    {
      wasNetworkError = await receiveLoopTask.ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      // ReceiveLoopCoreAsyncは例外を内部処理するため通常ここには来ないが、安全側でNW障害扱い
      _logger?.LogError(ex, "Receive loop terminated unexpectedly in client {Name}", Name);
      wasNetworkError = !_isIntentionalDisconnect;
    }

    // 既に新しい接続が確立されている場合は何もしない
    if (Volatile.Read(ref _connectionEpoch) != epoch)
    {
      return;
    }

    if (IsConnected || wasNetworkError)
    {
      // NW障害による切断として扱う（epochが変わっていたら中で何もしない）
      await DisconnectIfCurrentEpochAsync(epoch, isIntentional: !wasNetworkError).ConfigureAwait(false);
    }

    // NW障害による切断の場合、自動再接続を試行
    if (wasNetworkError && _config.ConnectionRetryPolicy != null &&
        Volatile.Read(ref _connectionEpoch) == epoch)
    {
      _logger?.LogInformation("TCP Client '{Name}' will attempt automatic reconnection to {Host}:{Port}...", Name, _config.RemoteHost, _config.RemotePort);
      StartAutoReconnect();
    }
  }

  /// <summary>
  /// 接続世代が変わっていない場合のみ切断処理を実行する。
  /// 世代チェックは_disconnectLock内で行うため、新しい接続の確立処理と競合しない。
  /// </summary>
  /// <returns>切断処理を実行した場合は true、世代が変わっていて何もしなかった場合は false</returns>
  private async Task<bool> DisconnectIfCurrentEpochAsync(
      int epoch,
      bool isIntentional,
      CancellationToken? expectedConnectionToken = null)
  {
    await _disconnectLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
    try
    {
      if (Volatile.Read(ref _connectionEpoch) != epoch)
      {
        return false;
      }

      if (Volatile.Read(ref _lastDisconnectedEpoch) == epoch)
      {
        return false;
      }

      // KeepAliveタイムアウトからの障害切断では、同じepochでも手動切断が
      // 先に始まっている、または対象接続のトークンが失効している場合がある。
      // その場合は現在のライフサイクル処理に任せ、二重切断・意図しない再接続を避ける。
      if (expectedConnectionToken.HasValue &&
          (_isIntentionalDisconnect ||
           expectedConnectionToken.Value.IsCancellationRequested ||
           expectedConnectionToken.Value != _cancellationTokenSource.Token))
      {
        return false;
      }

      await DisconnectCoreAsync(isIntentional).ConfigureAwait(false);
      return true;
    }
    finally
    {
      _disconnectLock.Release();
    }
  }

  /// <summary>
  /// OnKeepAliveResponseReceivedを購読者単位の例外隔離付きで発火する
  /// （ある購読者の例外で受信ループや他の購読者を止めない）
  /// </summary>
  private void RaiseKeepAliveResponseReceived(Message message)
  {
    var handler = OnKeepAliveResponseReceived;
    if (handler == null)
    {
      return;
    }

    foreach (EventHandler<Message> subscriber in handler.GetInvocationList())
    {
      try
      {
        subscriber.Invoke(this, message);
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "OnKeepAliveResponseReceived handler threw an exception in client {Name}", Name);
      }
    }
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
