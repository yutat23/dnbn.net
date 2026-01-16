using Dnbn.Models;

namespace Dnbn.Filters;

/// <summary>
/// メッセージフィルターインターフェイス
/// </summary>
public interface IMessageFilter
{
  /// <summary>
  /// 送信前のメッセージを処理
  /// </summary>
  Task<Message> OnSendingAsync(Message msg, IMessageContext ctx);

  /// <summary>
  /// 受信後のメッセージを処理
  /// </summary>
  Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx);
}



