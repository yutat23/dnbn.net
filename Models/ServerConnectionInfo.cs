namespace Dnbn.Models;

/// <summary>
/// TCPサーバーの接続状態情報を表すクラス
/// </summary>
public class ServerConnectionInfo
{
  /// <summary>
  /// サーバーが起動中かどうか
  /// </summary>
  public bool IsRunning { get; set; }

  /// <summary>
  /// 起動時刻
  /// </summary>
  public DateTime? StartedAt { get; set; }

  /// <summary>
  /// 稼働時間（起動時刻から現在までの経過時間）
  /// </summary>
  public TimeSpan? Uptime { get; set; }

  /// <summary>
  /// リッスンポート
  /// </summary>
  public int ListenPort { get; set; }

  /// <summary>
  /// 現在の接続数
  /// </summary>
  public int ConnectionCount { get; set; }

  /// <summary>
  /// 累計接続数
  /// </summary>
  public long TotalConnections { get; set; }

  /// <summary>
  /// 最後のクライアント接続時刻
  /// </summary>
  public DateTime? LastClientConnectedAt { get; set; }

  /// <summary>
  /// 最後のクライアント切断時刻
  /// </summary>
  public DateTime? LastClientDisconnectedAt { get; set; }

  /// <summary>
  /// 送信メッセージ数（全セッション合計）
  /// </summary>
  public long MessagesSent { get; set; }

  /// <summary>
  /// 受信メッセージ数（全セッション合計）
  /// </summary>
  public long MessagesReceived { get; set; }
}
