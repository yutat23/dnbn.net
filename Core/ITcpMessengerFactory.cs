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

/// <summary>
/// 名前付き設定に加え、起動時に組み立てた型付き設定からendpointを生成できるfactory。
/// 既存の <see cref="ITcpMessengerFactory"/> 実装とのバイナリ互換性を保つため分離している。
/// </summary>
public interface ITypedTcpMessengerFactory : ITcpMessengerFactory
{
  /// <summary>型付き設定からサーバーを作成する。</summary>
  ITcpServer CreateServer(Dnbn.Configuration.ServerConfig config);

  /// <summary>型付き設定からクライアントを作成する。</summary>
  ITcpClient CreateClient(Dnbn.Configuration.ClientConfig config);
}
