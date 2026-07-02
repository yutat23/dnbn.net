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
  /// リモートエンドポイント。
  /// 注意: 歴史的経緯により、サーバー側セッションではローカル（サーバー自身）の
  /// エンドポイントが格納されている。互換性のため値は変更していない。
  /// 接続相手のエンドポイントは <see cref="SourceEndpoint"/>、
  /// サーバー自身のエンドポイントは <see cref="LocalEndpoint"/> を使用すること。
  /// </summary>
  public IPEndPoint? RemoteEndpoint { get; set; }

  /// <summary>
  /// ローカル（自分側）のエンドポイント
  /// </summary>
  public IPEndPoint? LocalEndpoint { get; set; }

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



