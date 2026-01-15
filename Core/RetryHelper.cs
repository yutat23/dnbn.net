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
  public static async Task<T> ExecuteWithRetryAsync<T>(
      Func<Task<T>> operation,
      RetryPolicy? policy,
      Func<T, bool>? successPredicate = null,
      CancellationToken cancellationToken = default,
      ILogger? logger = null)
  {
    if (policy == null)
    {
      return await operation();
    }

    int retryCount = 0;
    Exception? lastException = null;

    while (retryCount <= policy.MaxRetryCount)
    {
      try
      {
        if (retryCount > 0)
        {
          logger?.LogWarning("Operation retry attempt {RetryCount}/{MaxRetryCount}",
              retryCount, policy.MaxRetryCount);
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
            logger?.LogInformation("Operation succeeded after {RetryCount} retry attempts", retryCount);
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
      logger?.LogWarning("Operation failed ({ExceptionType}: {Message}), retrying in {DelayMs}ms (attempt {RetryCount}/{MaxRetryCount})",
          lastException?.GetType().Name ?? "Exception", lastException?.Message ?? "Unknown error", delayMs, retryCount, policy.MaxRetryCount);
      await Task.Delay(delayMs, cancellationToken);
    }

    if (lastException != null)
    {
      logger?.LogError(lastException, "Operation failed after {RetryCount} retries", retryCount);
    }
    throw lastException ?? new InvalidOperationException("Retry failed");
  }

  /// <summary>
  /// 接続リトライポリシーに基づいて接続処理を実行（無限リトライ対応）
  /// </summary>
  public static async Task ExecuteConnectionRetryAsync(
      Func<Task> operation,
      RetryPolicy? policy,
      CancellationToken cancellationToken = default,
      ILogger? logger = null)
  {
    if (policy == null)
    {
      await operation();
      return;
    }

    int retryCount = 0;
    bool isInfiniteRetry = policy.MaxRetryCount < 0;

    while (isInfiniteRetry || retryCount <= policy.MaxRetryCount)
    {
      try
      {
        if (retryCount > 0)
        {
          logger?.LogWarning("Connection retry attempt {RetryCount}/{MaxRetryCount}",
              retryCount, isInfiniteRetry ? -1 : policy.MaxRetryCount);
        }
        await operation();
        if (retryCount > 0)
        {
          logger?.LogInformation("Connection succeeded after {RetryCount} retry attempts", retryCount);
        }
        return; // 接続成功
      }
      catch (SocketException ex)
      {
        // 接続関連の例外はリトライ対象
        if (cancellationToken.IsCancellationRequested)
        {
          throw new OperationCanceledException("Connection retry was cancelled", ex);
        }

        // 無限リトライでない場合、最大回数に達したら例外をスロー
        if (!isInfiniteRetry && retryCount >= policy.MaxRetryCount)
        {
          logger?.LogError(ex, "Connection failed after {RetryCount} retries", retryCount);
          throw new InvalidOperationException($"Connection failed after {retryCount} retries", ex);
        }

        retryCount++;
        var delayMs = policy.GetDelayMs(retryCount - 1);
        logger?.LogWarning("Connection failed (SocketException: {Message}), retrying in {DelayMs}ms (attempt {RetryCount}/{MaxRetryCount})",
            ex.Message, delayMs, retryCount, isInfiniteRetry ? -1 : policy.MaxRetryCount);
        await Task.Delay(delayMs, cancellationToken);
      }
      catch (IOException ex)
      {
        // IO例外も接続関連の可能性があるためリトライ対象
        if (cancellationToken.IsCancellationRequested)
        {
          throw new OperationCanceledException("Connection retry was cancelled", ex);
        }

        if (!isInfiniteRetry && retryCount >= policy.MaxRetryCount)
        {
          logger?.LogError(ex, "Connection failed after {RetryCount} retries", retryCount);
          throw new InvalidOperationException($"Connection failed after {retryCount} retries", ex);
        }

        retryCount++;
        var delayMs = policy.GetDelayMs(retryCount - 1);
        logger?.LogWarning("Connection failed (IOException: {Message}), retrying in {DelayMs}ms (attempt {RetryCount}/{MaxRetryCount})",
            ex.Message, delayMs, retryCount, isInfiniteRetry ? -1 : policy.MaxRetryCount);
        await Task.Delay(delayMs, cancellationToken);
      }
      catch (Exception)
      {
        // その他の例外は即座にスロー
        throw;
      }
    }
  }
}



