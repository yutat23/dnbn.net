using Dnbn.Configuration;
using Dnbn.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dnbn.Core;

/// <summary>
/// TCP Messengerファクトリー実装
/// </summary>
public class TcpMessengerFactory : ITcpMessengerFactory
{
  private readonly TcpMessengerConfig _config;
  private readonly ILoggerFactory? _loggerFactory;
  private readonly IEnumerable<IMessageFilter> _filters;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="config">TCP Messenger設定</param>
  /// <param name="loggerFactory">ロガーファクトリー（オプション）</param>
  /// <param name="filters">メッセージフィルター（オプション）</param>
  public TcpMessengerFactory(
      IOptions<TcpMessengerConfig> config,
      ILoggerFactory? loggerFactory = null,
      IEnumerable<IMessageFilter>? filters = null)
  {
    _config = config.Value;
    _loggerFactory = loggerFactory;
    _filters = filters ?? Enumerable.Empty<IMessageFilter>();
  }

  /// <summary>
  /// サーバーインスタンスを作成
  /// </summary>
  /// <param name="name">サーバー設定名</param>
  /// <returns>サーバーインスタンス</returns>
  public ITcpServer CreateServer(string name)
  {
    var serverConfig = _config.Servers.FirstOrDefault(s => s.Name == name)
        ?? throw new ArgumentException($"Server configuration '{name}' not found", nameof(name));

    var logger = _loggerFactory?.CreateLogger<TcpServer>();
    return new TcpServer(serverConfig, logger, _filters);
  }

  /// <summary>
  /// クライアントインスタンスを作成
  /// </summary>
  /// <param name="name">クライアント設定名</param>
  /// <returns>クライアントインスタンス</returns>
  public ITcpClient CreateClient(string name)
  {
    var clientConfig = _config.Clients.FirstOrDefault(c => c.Name == name)
        ?? throw new ArgumentException($"Client configuration '{name}' not found", nameof(name));

    var transport = new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort, clientConfig.TcpKeepAlive);
    var logger = _loggerFactory?.CreateLogger<TcpClient>();
    return new TcpClient(clientConfig, transport, logger, _filters);
  }
}



