namespace Dnbn.Configuration;

/// <summary>
/// リトライ遅延戦略
/// </summary>
public enum RetryDelayStrategy
{
  /// <summary>
  /// 固定遅延
  /// </summary>
  Fixed,

  /// <summary>
  /// 指数バックオフ
  /// </summary>
  Exponential
}

/// <summary>
/// リトライポリシー設定
/// </summary>
public class RetryPolicy
{
  /// <summary>
  /// 最大リトライ回数
  /// </summary>
  public int MaxRetryCount { get; set; } = 3;

  /// <summary>
  /// リトライ遅延戦略
  /// </summary>
  public RetryDelayStrategy RetryDelayStrategy { get; set; } = RetryDelayStrategy.Exponential;

  /// <summary>
  /// 初期待機時間（ミリ秒）
  /// </summary>
  public int InitialDelayMs { get; set; } = 500;

  /// <summary>
  /// 最大待機時間（ミリ秒）。指数バックオフ時の上限値。デフォルト: 60000ms（60秒）
  /// </summary>
  public int MaxDelayMs { get; set; } = 60000;

  /// <summary>
  /// タイムアウト時に失敗とするか
  /// </summary>
  public bool FailOnTimeout { get; set; } = true;

  /// <summary>
  /// エラー応答時に失敗とするか
  /// </summary>
  public bool FailOnErrorResponse { get; set; } = true;

  /// <summary>
  /// このポリシーの複製を作成
  /// </summary>
  public RetryPolicy Clone()
  {
    return new RetryPolicy
    {
      MaxRetryCount = MaxRetryCount,
      RetryDelayStrategy = RetryDelayStrategy,
      InitialDelayMs = InitialDelayMs,
      MaxDelayMs = MaxDelayMs,
      FailOnTimeout = FailOnTimeout,
      FailOnErrorResponse = FailOnErrorResponse
    };
  }

  /// <summary>
  /// リトライ回数に応じた遅延時間を計算
  /// </summary>
  public int GetDelayMs(int retryCount)
  {
    if (retryCount < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(retryCount));
    }
    if (InitialDelayMs < 0 || MaxDelayMs < 0 || MaxDelayMs < InitialDelayMs)
    {
      throw new InvalidOperationException("Retry delays must be non-negative and MaxDelayMs must be greater than or equal to InitialDelayMs.");
    }

    if (RetryDelayStrategy == RetryDelayStrategy.Fixed)
    {
      return Math.Min(InitialDelayMs, MaxDelayMs);
    }

    if (RetryDelayStrategy != RetryDelayStrategy.Exponential)
    {
      return Math.Min(InitialDelayMs, MaxDelayMs);
    }

    // intで指数乗算してから上限を適用すると、長時間の無限リトライで
    // オーバーフローして負数になり、Task.Delayが失敗する。
    // 計算途中からMaxDelayMsへ飽和させることで、任意のretryCountを安全に扱う。
    if (InitialDelayMs == 0 || MaxDelayMs == 0)
    {
      return 0;
    }

    long delay = InitialDelayMs;
    for (var i = 0; i < retryCount && delay < MaxDelayMs; i++)
    {
      delay = Math.Min(delay * 2L, MaxDelayMs);
    }

    return (int)Math.Min(delay, MaxDelayMs);
  }
}

