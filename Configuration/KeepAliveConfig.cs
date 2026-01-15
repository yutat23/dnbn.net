namespace Dnbn.Configuration;

/// <summary>
/// キープアライブ設定
/// </summary>
public class KeepAliveConfig
{
  /// <summary>
  /// キープアライブを有効にするか
  /// </summary>
  public bool Enabled { get; set; } = false;

  /// <summary>
  /// キープアライブ間隔（秒）
  /// </summary>
  public int IntervalSeconds { get; set; } = 30;

  /// <summary>
  /// キープアライブメッセージ
  /// </summary>
  public string Message { get; set; } = string.Empty;
}
