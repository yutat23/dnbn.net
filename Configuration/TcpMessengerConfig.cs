namespace Dnbn.Configuration;

/// <summary>
/// TCP Messenger設定のルートクラス（appsettings.json用）
/// </summary>
public class TcpMessengerConfig
{
  /// <summary>
  /// サーバー設定リスト
  /// </summary>
  public List<ServerConfig> Servers { get; set; } = new();

  /// <summary>
  /// クライアント設定リスト
  /// </summary>
  public List<ClientConfig> Clients { get; set; } = new();
}



