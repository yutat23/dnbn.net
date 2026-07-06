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

  /// <summary>
  /// 接続/切断/エラーのイベントタイムラインの保持件数（リングバッファ、デフォルト: 200）
  /// </summary>
  public int EventTimelineCapacity { get; set; } = 200;

  /// <summary>
  /// メッセージログ（送受信履歴）の有効化/無効化（デフォルト: false）。
  /// 有効時のメモリ使用量は MessageHistoryCapacity と MessageHistoryMaxPayloadBytes に比例する固定上限となる
  /// </summary>
  public bool EnableMessageHistory { get; set; } = false;

  /// <summary>
  /// メッセージログの保持件数（リングバッファ、デフォルト: 200）
  /// </summary>
  public int MessageHistoryCapacity { get; set; } = 200;

  /// <summary>
  /// メッセージログ1件あたりに保持するペイロードの最大バイト数（デフォルト: 512）。
  /// 超過分は切り詰めて保持する（元の電文サイズは別途記録される）
  /// </summary>
  public int MessageHistoryMaxPayloadBytes { get; set; } = 512;

  /// <summary>
  /// Web UIからのメッセージ送信の有効化/無効化（デフォルト: false）。
  /// 稼働中のアプリと同じ接続を共有するため、有効化する場合は SendAuthToken の設定を推奨
  /// </summary>
  public bool AllowSendFromUI { get; set; } = false;

  /// <summary>
  /// Web UIからの送信に必要な認証トークン（デフォルト: null = トークン不要）。
  /// 設定した場合、送信リクエストは X-Dnbn-Send-Token ヘッダーに同じ値を要求する
  /// </summary>
  public string? SendAuthToken { get; set; }
}
