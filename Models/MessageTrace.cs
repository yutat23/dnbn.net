namespace Dnbn.Models;

/// <summary>
/// メッセージトレースの方向
/// </summary>
public enum MessageTraceDirection
{
  /// <summary>送信</summary>
  Sent,

  /// <summary>受信</summary>
  Received,
}

/// <summary>
/// メッセージトレースの種別
/// </summary>
public enum MessageTraceKind
{
  /// <summary>応答を待つ送信（SendAsync / SendAndWaitAsync）</summary>
  Request,

  /// <summary>応答を待たない送信（SendOneWayAsync）</summary>
  OneWay,

  /// <summary>KeepAlive電文の送信</summary>
  KeepAlive,

  /// <summary>Requestに対する応答の受信</summary>
  Response,

  /// <summary>通知電文の受信（OnMessageReceivedに配信されるもの）</summary>
  Notification,

  /// <summary>KeepAlive応答の受信</summary>
  KeepAliveResponse,
}

/// <summary>
/// メッセージトレースイベント。
/// OnMessageReceived と異なり、SendAsync の応答や KeepAlive を含む全送受信を観測できる（診断用）。
/// </summary>
public sealed class MessageTraceEvent
{
  /// <summary>発生時刻（UTC）</summary>
  public DateTime Timestamp { get; init; } = DateTime.UtcNow;

  /// <summary>方向（送信/受信）</summary>
  public MessageTraceDirection Direction { get; init; }

  /// <summary>種別</summary>
  public MessageTraceKind Kind { get; init; }

  /// <summary>
  /// 対象メッセージのスナップショット。送受信処理で使用中の Message とは別インスタンス。
  /// 送信時の RawData / Text は終端文字を含む実送信内容。
  /// </summary>
  public Message Message { get; init; } = null!;

  /// <summary>
  /// Response の場合の、要求の送信キュー投入から応答受信までの経過ミリ秒。
  /// それ以外の種別では null
  /// </summary>
  public double? ElapsedMilliseconds { get; init; }
}
