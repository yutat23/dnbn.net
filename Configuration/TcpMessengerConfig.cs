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

  /// <summary>
  /// Web UI設定
  /// </summary>
  public WebUIConfig? WebUI { get; set; }

  /// <summary>この設定の複製を作成する。</summary>
  public TcpMessengerConfig Clone()
  {
    return new TcpMessengerConfig
    {
      Servers = Servers.Select(server => server.Clone()).ToList(),
      Clients = Clients.Select(client => client.Clone()).ToList(),
      WebUI = WebUI?.Clone()
    };
  }
}

