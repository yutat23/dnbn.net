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
  /// リトライ回数に応じた遅延時間を計算
  /// </summary>
  public int GetDelayMs(int retryCount)
  {
    var delay = RetryDelayStrategy switch
    {
      RetryDelayStrategy.Fixed => InitialDelayMs,
      RetryDelayStrategy.Exponential => InitialDelayMs * (int)Math.Pow(2, retryCount),
      _ => InitialDelayMs
    };

    // MaxDelayMsを上限として適用
    return Math.Min(delay, MaxDelayMs);
  }
}



