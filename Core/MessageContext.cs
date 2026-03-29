using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>
/// メッセージ処理コンテキストの共通実装
/// </summary>
internal sealed class MessageContext : IMessageContext
{
  /// <inheritdoc />
  public SessionInfo? SessionInfo { get; }

  /// <inheritdoc />
  public bool IsServerSide { get; }

  /// <inheritdoc />
  public Dictionary<string, object> Properties { get; } = new();

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="sessionInfo">セッション情報</param>
  /// <param name="isServerSide">サーバー側かどうか</param>
  public MessageContext(SessionInfo? sessionInfo, bool isServerSide)
  {
    SessionInfo = sessionInfo;
    IsServerSide = isServerSide;
  }
}
