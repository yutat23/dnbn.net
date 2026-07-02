using System.Net.Sockets;
using Dnbn.Configuration;
using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

/// <summary>
/// リトライ処理ヘルパー
/// </summary>
public static class RetryHelper
{
  /// <summary>
  /// リトライポリシーに基づいて処理を実行
  /// </summary>
  public static Task<T> ExecuteWithRetryAsync<T>(
      Func<Task<T>> operation,
      RetryPolicy? policy,
      Func<T, bool>? successPredicate = null,
      CancellationToken cancellationToken = default,
      ILogger? logger = null)
  {
    return ExecuteWithRetryAsync(operation, policy, successPredicate, cancellationToken, logger, targetDescription: null);
  }

  /// <summary>
  /// リトライポリシーに基づいて処理を実行（ログ用の相手先識別名付き）
  /// </summary>
  /// <param name="operation">実行する処理</param>
  /// <param name="policy">リトライポリシー</param>
  /// <param name="successPredicate">成功条件の判定関数</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <param name="logger">ロガー</param>
  /// <param name="targetDescription">ログに出力する相手先の識別名（例: "host:port"）</param>
  public static async Task<T> ExecuteWithRetryAsync<T>(
      Func<Task<T>> operation,
      RetryPolicy? policy,
      Func<T, bool>? successPredicate,
      CancellationToken cancellationToken,
      ILogger? logger,
      string? targetDescription)
  {
    if (policy == null)
    {
      return await operation().ConfigureAwait(false);
    }

    // 識別名が未指定の場合は従来と同じレンダリング結果になる
    var target = string.IsNullOrEmpty(targetDescription) ? "" : $" to {targetDescription}";
    int retryCount = 0;
    Exception? lastException = null;

    while (retryCount <= policy.MaxRetryCount)
    {
      try
      {
        if (retryCount > 0)
        {
          logger?.LogWarning("Operation{Target} retry attempt {RetryCount}/{MaxRetryCount}",
              target, retryCount, policy.MaxRetryCount);
        }
        var result = await operation();

        // 成功条件をチェック
        if (successPredicate != null && !successPredicate(result))
        {
          if (policy.FailOnErrorResponse)
          {
            throw new InvalidOperationException("Operation returned error response");
          }
          // エラー応答でもリトライしない場合は、結果を返す
          if (retryCount >= policy.MaxRetryCount)
          {
            return result;
          }
        }
        else
        {
          if (retryCount > 0)
          {
            logger?.LogInformation("Operation{Target} succeeded after {RetryCount} retry attempts", target, retryCount);
          }
          return result;
        }
      }
      catch (TimeoutException) when (policy.FailOnTimeout && retryCount >= policy.MaxRetryCount)
      {
        throw;
      }
      catch (Exception ex)
      {
        lastException = ex;
      }

      if (retryCount >= policy.MaxRetryCount)
      {
        break;
      }

      retryCount++;
      var delayMs = policy.GetDelayMs(retryCount - 1);
      logger?.LogWarning("Operation{Target} failed ({ExceptionType}: {Message}), retrying in {DelayMs}ms (attempt {RetryCount}/{MaxRetryCount})",
          target, lastException?.GetType().Name ?? "Exception", lastException?.Message ?? "Unknown error", delayMs, retryCount, policy.MaxRetryCount);
      await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
    }

    if (lastException != null)
    {
      logger?.LogError(lastException, "Operation{Target} failed after {RetryCount} retries", target, retryCount);
    }
    throw lastException ?? new InvalidOperationException("Retry failed");
  }

  /// <summary>
  /// 接続リトライポリシーに基づいて接続処理を実行（無限リトライ対応）
  /// </summary>
  /// <param name="operation">接続処理</param>
  /// <param name="policy">リトライポリシー</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <param name="logger">ロガー</param>
  /// <param name="onDelayStarting">リトライ待機開始時に呼ばれるコールバック。渡された CancellationTokenSource をキャンセルすると待機を即座にスキップできる</param>
  public static Task ExecuteConnectionRetryAsync(
      Func<Task> operation,
      RetryPolicy? policy,
      CancellationToken cancellationToken = default,
      ILogger? logger = null,
      Action<CancellationTokenSource>? onDelayStarting = null)
  {
    return ExecuteConnectionRetryAsync(operation, policy, cancellationToken, logger, onDelayStarting, targetDescription: null);
  }

  /// <summary>
  /// 接続リトライポリシーに基づいて接続処理を実行（無限リトライ対応、ログ用の相手先識別名付き）
  /// </summary>
  /// <param name="operation">接続処理</param>
  /// <param name="policy">リトライポリシー</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <param name="logger">ロガー</param>
  /// <param name="onDelayStarting">リトライ待機開始時に呼ばれるコールバック。渡された CancellationTokenSource をキャンセルすると待機を即座にスキップできる</param>
  /// <param name="targetDescription">ログに出力する相手先の識別名（例: "host:port"）</param>
  public static async Task ExecuteConnectionRetryAsync(
      Func<Task> operation,
      RetryPolicy? policy,
      CancellationToken cancellationToken,
      ILogger? logger,
      Action<CancellationTokenSource>? onDelayStarting,
      string? targetDescription)
  {
    if (policy == null)
    {
      await operation().ConfigureAwait(false);
      return;
    }

    // 識別名が未指定の場合は従来と同じレンダリング結果になる
    var target = string.IsNullOrEmpty(targetDescription) ? "" : $" to {targetDescription}";
    int retryCount = 0;
    bool isInfiniteRetry = policy.MaxRetryCount < 0;

    while (isInfiniteRetry || retryCount <= policy.MaxRetryCount)
    {
      try
      {
        if (retryCount > 0)
        {
          logger?.LogWarning("Connection{Target} retry attempt {RetryCount}/{MaxRetryCount}",
              target, retryCount, isInfiniteRetry ? -1 : policy.MaxRetryCount);
        }
        await operation().ConfigureAwait(false);
        if (retryCount > 0)
        {
          logger?.LogInformation("Connection{Target} succeeded after {RetryCount} retry attempts", target, retryCount);
        }
        return; // 接続成功
      }
      catch (Exception ex) when (ex is SocketException or IOException)
      {
        // 接続関連の例外（SocketException / IOException）はリトライ対象
        if (cancellationToken.IsCancellationRequested)
        {
          throw new OperationCanceledException("Connection retry was cancelled", ex);
        }

        // 無限リトライでない場合、最大回数に達したら例外をスロー
        if (!isInfiniteRetry && retryCount >= policy.MaxRetryCount)
        {
          logger?.LogError(ex, "Connection{Target} failed after {RetryCount} retries", target, retryCount);
          throw new InvalidOperationException($"Connection failed after {retryCount} retries", ex);
        }

        retryCount++;
        var delayMs = policy.GetDelayMs(retryCount - 1);
        logger?.LogWarning("Connection{Target} failed ({ExceptionType}: {Message}), retrying in {DelayMs}ms (attempt {RetryCount}/{MaxRetryCount})",
            target, ex.GetType().Name, ex.Message, delayMs, retryCount, isInfiniteRetry ? -1 : policy.MaxRetryCount);
        await DelayWithInterruptAsync(delayMs, cancellationToken, onDelayStarting, logger, target).ConfigureAwait(false);
      }
      // その他の例外は即座にスロー
    }
  }

  private static async Task DelayWithInterruptAsync(
      int delayMs,
      CancellationToken cancellationToken,
      Action<CancellationTokenSource>? onDelayStarting,
      ILogger? logger,
      string target)
  {
    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    onDelayStarting?.Invoke(delayCts);
    try
    {
      await Task.Delay(delayMs, delayCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      logger?.LogInformation("Connection{Target} retry delay interrupted, retrying immediately", target);
    }
  }
}



