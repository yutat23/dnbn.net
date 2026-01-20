namespace Dnbn.Configuration;

/// <summary>
/// Web UI設定
/// </summary>
public class WebUIConfig
{
  /// <summary>
  /// Web UIの有効化/無効化（デフォルト: false）
  /// </summary>
  public bool Enabled { get; set; } = false;

  /// <summary>
  /// Web UIのポート番号（デフォルト: 8080）
  /// </summary>
  public int Port { get; set; } = 8080;

  /// <summary>
  /// SSEストリームの送信間隔（秒、デフォルト: 1）
  /// </summary>
  public int UpdateIntervalSeconds { get; set; } = 1;

  /// <summary>
  /// バインドアドレス（デフォルト: "localhost"、"*"で全アドレス）
  /// </summary>
  public string BindAddress { get; set; } = "localhost";

  /// <summary>
  /// Web UI関連のログ出力の有効化/無効化（デフォルト: true）
  /// </summary>
  public bool EnableLogging { get; set; } = true;
}
