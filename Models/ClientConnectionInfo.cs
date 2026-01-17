namespace Dnbn.Models;

/// <summary>
/// TCPクライアントの接続状態情報を表すクラス
/// </summary>
public class ClientConnectionInfo
{
  /// <summary>
  /// 接続状態
  /// </summary>
  public bool IsConnected { get; set; }

  /// <summary>
  /// 接続開始時刻
  /// </summary>
  public DateTime? ConnectedAt { get; set; }

  /// <summary>
  /// 最後のメッセージ受信時刻
  /// </summary>
  public DateTime? LastMessageReceivedAt { get; set; }

  /// <summary>
  /// リモートホスト（IPアドレスまたはホスト名）
  /// </summary>
  public string RemoteHost { get; set; } = string.Empty;

  /// <summary>
  /// リモートポート
  /// </summary>
  public int RemotePort { get; set; }

  /// <summary>
  /// 再接続試行中かどうか
  /// </summary>
  public bool IsReconnecting { get; set; }

  /// <summary>
  /// 接続継続時間（接続開始時刻から現在までの経過時間）
  /// </summary>
  public TimeSpan? ConnectionDuration { get; set; }

  /// <summary>
  /// 送信メッセージ数
  /// </summary>
  public long MessagesSent { get; set; }

  /// <summary>
  /// 受信メッセージ数
  /// </summary>
  public long MessagesReceived { get; set; }

  /// <summary>
  /// 待機中のリクエスト数
  /// </summary>
  public int PendingRequests { get; set; }

  /// <summary>
  /// 最後のキープアライブ送信時刻
  /// </summary>
  public DateTime? LastKeepAliveSentAt { get; set; }

  /// <summary>
  /// 最後のキープアライブ応答受信時刻
  /// </summary>
  public DateTime? LastKeepAliveResponseReceivedAt { get; set; }

  /// <summary>
  /// キープアライブタイムアウト回数
  /// </summary>
  public int KeepAliveTimeoutCount { get; set; }

  /// <summary>
  /// エラー発生回数
  /// </summary>
  public int ErrorCount { get; set; }

  /// <summary>
  /// 最後のエラーメッセージ
  /// </summary>
  public string? LastError { get; set; }

  /// <summary>
  /// 最後のエラー発生時刻
  /// </summary>
  public DateTime? LastErrorAt { get; set; }

  /// <summary>
  /// 接続リトライ試行回数
  /// </summary>
  public int ConnectionRetryAttempts { get; set; }

  /// <summary>
  /// 最後のリトライ試行時刻
  /// </summary>
  public DateTime? LastRetryAttemptAt { get; set; }
}
