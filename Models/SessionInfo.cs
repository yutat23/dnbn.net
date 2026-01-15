using System.Net;

namespace Dnbn.Models;

/// <summary>
/// TCPセッション情報を表すクラス
/// </summary>
public class SessionInfo
{
  /// <summary>
  /// セッションID（サーバー側で割り当て）
  /// </summary>
  public string SessionId { get; set; } = string.Empty;

  /// <summary>
  /// 送信元エンドポイント（IP+Port）
  /// </summary>
  public IPEndPoint SourceEndpoint { get; set; } = null!;

  /// <summary>
  /// リモートエンドポイント（接続先）
  /// </summary>
  public IPEndPoint? RemoteEndpoint { get; set; }

  /// <summary>
  /// 接続開始時刻
  /// </summary>
  public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

  /// <summary>
  /// 最後のメッセージ受信時刻
  /// </summary>
  public DateTime? LastMessageReceivedAt { get; set; }

  /// <summary>
  /// 追加のセッションメタデータ
  /// </summary>
  public Dictionary<string, object> Metadata { get; set; } = new();

  /// <summary>
  /// セッションが有効かどうか
  /// </summary>
  public bool IsActive { get; set; } = true;
}



