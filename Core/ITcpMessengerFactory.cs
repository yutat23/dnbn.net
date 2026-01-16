namespace Dnbn.Core;

/// <summary>
/// TCP Messengerファクトリーインターフェイス
/// </summary>
public interface ITcpMessengerFactory
{
  /// <summary>
  /// サーバーを作成
  /// </summary>
  ITcpServer CreateServer(string name);

  /// <summary>
  /// クライアントを作成
  /// </summary>
  ITcpClient CreateClient(string name);
}



