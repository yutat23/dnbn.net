namespace Dnbn.Models;

/// <summary>
/// クライアントの接続状態
/// </summary>
public enum ConnectionState
{
  /// <summary>
  /// 未接続（初期状態、意図的な切断後、または再接続を断念した状態）
  /// </summary>
  Disconnected,

  /// <summary>
  /// 接続処理中（ConnectAsyncによる接続試行中。リトライ待機中を含む）
  /// </summary>
  Connecting,

  /// <summary>
  /// 接続済み
  /// </summary>
  Connected,

  /// <summary>
  /// NW障害後の自動再接続中（リトライ待機中を含む）
  /// </summary>
  Reconnecting,
}
