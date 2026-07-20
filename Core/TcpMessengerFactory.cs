using Dnbn.Configuration;
using Dnbn.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dnbn.Core;

/// <summary>
/// TCP Messengerファクトリー実装
/// </summary>
public class TcpMessengerFactory : ITypedTcpMessengerFactory
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
    TcpMessengerConfigValidator.Validate(config.Value);
    _config = config.Value.Clone();
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

    return CreateServer(serverConfig);
  }

  /// <inheritdoc />
  public ITcpServer CreateServer(ServerConfig config)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    TcpMessengerConfigValidator.ValidateServer(config);
    var logger = _loggerFactory?.CreateLogger<TcpServer>();
    return new TcpServer(config.Clone(), logger, _filters);
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

    return CreateClient(clientConfig);
  }

  /// <inheritdoc />
  public ITcpClient CreateClient(ClientConfig config)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    TcpMessengerConfigValidator.ValidateClient(config);
    var clientConfig = config.Clone();
    var transport = new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort, clientConfig.TcpKeepAlive);
    var logger = _loggerFactory?.CreateLogger<TcpClient>();
    return new TcpClient(clientConfig, transport, logger, _filters);
  }
}
