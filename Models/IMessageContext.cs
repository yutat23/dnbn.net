namespace Dnbn.Models;

/// <summary>
/// メッセージ処理のコンテキストを表すインターフェイス
/// </summary>
public interface IMessageContext
{
    /// <summary>
    /// セッション情報
    /// </summary>
    SessionInfo? SessionInfo { get; }

    /// <summary>
    /// 送信元がサーバーかクライアントか
    /// </summary>
    bool IsServerSide { get; }

    /// <summary>
    /// 追加のコンテキストデータ
    /// </summary>
    Dictionary<string, object> Properties { get; }
}



