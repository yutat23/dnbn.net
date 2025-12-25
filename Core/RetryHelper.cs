using Dnbn.Configuration;
using Dnbn.Models;

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
        CancellationToken cancellationToken = default)
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
            await Task.Delay(delayMs, cancellationToken);
        }

        throw lastException ?? new InvalidOperationException("Retry failed");
    }
}



