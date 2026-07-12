using System.Net.Sockets;
using Dnbn.Configuration;
using Dnbn.Core;

namespace Dnbn.Tests;

/// <summary>
/// RetryHelper のユニットテスト
/// </summary>
public class RetryHelperTests
{
  // ---------------------------------------------------------------------------
  // RetryPolicy.GetDelayMs テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public void GetDelayMs_Fixed_ReturnsInitialDelay()
  {
    var policy = new RetryPolicy
    {
      RetryDelayStrategy = RetryDelayStrategy.Fixed,
      InitialDelayMs = 200
    };

    Assert.Equal(200, policy.GetDelayMs(0));
    Assert.Equal(200, policy.GetDelayMs(1));
    Assert.Equal(200, policy.GetDelayMs(5));
  }

  [Fact]
  public void GetDelayMs_Exponential_DoublesEachRetry()
  {
    var policy = new RetryPolicy
    {
      RetryDelayStrategy = RetryDelayStrategy.Exponential,
      InitialDelayMs = 100,
      MaxDelayMs = 100000
    };

    Assert.Equal(100, policy.GetDelayMs(0));  // 100 * 2^0 = 100
    Assert.Equal(200, policy.GetDelayMs(1));  // 100 * 2^1 = 200
    Assert.Equal(400, policy.GetDelayMs(2));  // 100 * 2^2 = 400
    Assert.Equal(800, policy.GetDelayMs(3));  // 100 * 2^3 = 800
  }

  [Fact]
  public void GetDelayMs_Exponential_CapsAtMaxDelay()
  {
    var policy = new RetryPolicy
    {
      RetryDelayStrategy = RetryDelayStrategy.Exponential,
      InitialDelayMs = 1000,
      MaxDelayMs = 5000
    };

    // 1000 * 2^3 = 8000 > MaxDelayMs → 5000
    Assert.Equal(5000, policy.GetDelayMs(3));
    Assert.Equal(5000, policy.GetDelayMs(10));
  }

  [Fact]
  public void GetDelayMs_Exponential_LargeRetryCountStaysCappedWithoutOverflow()
  {
    var policy = new RetryPolicy
    {
      RetryDelayStrategy = RetryDelayStrategy.Exponential,
      InitialDelayMs = 500,
      MaxDelayMs = 60000
    };

    Assert.Equal(60000, policy.GetDelayMs(23));
    Assert.Equal(60000, policy.GetDelayMs(1000));
    Assert.Equal(60000, policy.GetDelayMs(int.MaxValue));
  }

  [Fact]
  public void GetDelayMs_NegativeRetryCount_Throws()
  {
    var policy = new RetryPolicy();

    Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelayMs(-1));
  }

  // ---------------------------------------------------------------------------
  // ExecuteWithRetryAsync テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task ExecuteWithRetryAsync_SucceedsOnFirstAttempt_NeverRetries()
  {
    int callCount = 0;
    var policy = new RetryPolicy { MaxRetryCount = 3, InitialDelayMs = 1 };

    var result = await RetryHelper.ExecuteWithRetryAsync(
        () => { callCount++; return Task.FromResult(42); },
        policy);

    Assert.Equal(42, result);
    Assert.Equal(1, callCount);
  }

  [Fact]
  public async Task ExecuteWithRetryAsync_RetriesUpToMaxCount()
  {
    int callCount = 0;
    var policy = new RetryPolicy { MaxRetryCount = 3, InitialDelayMs = 1, FailOnTimeout = false };

    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    {
      await RetryHelper.ExecuteWithRetryAsync<int>(
          () =>
          {
            callCount++;
            throw new InvalidOperationException("失敗");
          },
          policy);
    });

    // 初回 + 3回リトライ = 4回
    Assert.Equal(4, callCount);
  }

  [Fact]
  public async Task ExecuteWithRetryAsync_RespectsCancellation()
  {
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var policy = new RetryPolicy { MaxRetryCount = 3, InitialDelayMs = 1 };

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
      await RetryHelper.ExecuteWithRetryAsync<int>(
          async () =>
          {
            await Task.Delay(1000, cts.Token); // キャンセルトークンを使用
            return 0;
          },
          policy,
          cancellationToken: cts.Token);
    });
  }

  [Fact]
  public async Task ExecuteWithRetryAsync_NullPolicy_ExecutesOnce()
  {
    int callCount = 0;

    var result = await RetryHelper.ExecuteWithRetryAsync(
        () => { callCount++; return Task.FromResult(99); },
        null);

    Assert.Equal(99, result);
    Assert.Equal(1, callCount);
  }

  [Fact]
  public async Task ExecuteWithRetryAsync_WithSuccessPredicate_RetriesOnFalse()
  {
    int callCount = 0;
    var policy = new RetryPolicy
    {
      MaxRetryCount = 2,
      InitialDelayMs = 1,
      FailOnErrorResponse = false
    };

    // successPredicate が常に false を返す → MaxRetryCount まで試行して最後の結果を返す
    var result = await RetryHelper.ExecuteWithRetryAsync(
        () => { callCount++; return Task.FromResult(-1); },
        policy,
        successPredicate: r => r > 0);

    Assert.Equal(-1, result);
    Assert.Equal(3, callCount); // 初回 + 2回 = 3回
  }

  // ---------------------------------------------------------------------------
  // ExecuteConnectionRetryAsync テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task ExecuteConnectionRetryAsync_SucceedsOnFirstAttempt()
  {
    int callCount = 0;
    var policy = new RetryPolicy { MaxRetryCount = 3, InitialDelayMs = 1 };

    await RetryHelper.ExecuteConnectionRetryAsync(
        () => { callCount++; return Task.CompletedTask; },
        policy);

    Assert.Equal(1, callCount);
  }

  [Fact]
  public async Task ExecuteConnectionRetryAsync_NullPolicy_ExecutesOnce()
  {
    int callCount = 0;

    await RetryHelper.ExecuteConnectionRetryAsync(
        () => { callCount++; return Task.CompletedTask; },
        null);

    Assert.Equal(1, callCount);
  }

  [Fact]
  public async Task ExecuteConnectionRetryAsync_ThrowsOnNonSocketException()
  {
    var policy = new RetryPolicy { MaxRetryCount = 3, InitialDelayMs = 1 };

    // SocketException 以外はリトライせず即スロー
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    {
      await RetryHelper.ExecuteConnectionRetryAsync(
          () => throw new InvalidOperationException("予期しないエラー"),
          policy);
    });
  }

  [Fact]
  public async Task ExecuteConnectionRetryAsync_InfiniteRetry_StopsOnCancellation()
  {
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
    int callCount = 0;

    // MaxRetryCount = -1 は無限リトライ
    var policy = new RetryPolicy { MaxRetryCount = -1, InitialDelayMs = 10 };

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
      await RetryHelper.ExecuteConnectionRetryAsync(
          () =>
          {
            callCount++;
            throw new SocketException(); // 接続失敗をシミュレート
          },
          policy,
          cancellationToken: cts.Token);
    });

    Assert.True(callCount > 1, "キャンセルまでに複数回リトライされること");
  }

  [Fact]
  public async Task ExecuteConnectionRetryAsync_FiniteRetry_FailsAfterMaxRetries()
  {
    int callCount = 0;
    var policy = new RetryPolicy { MaxRetryCount = 2, InitialDelayMs = 1 };

    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    {
      await RetryHelper.ExecuteConnectionRetryAsync(
          () =>
          {
            callCount++;
            throw new SocketException();
          },
          policy);
    });

    // 初回 + 2回リトライ = 3回
    Assert.Equal(3, callCount);
  }
}
