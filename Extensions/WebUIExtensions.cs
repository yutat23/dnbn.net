using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.WebUI;
using Microsoft.Extensions.Logging;

namespace Dnbn.Extensions;

/// <summary>
/// Web UI拡張メソッド
/// </summary>
public static class WebUIExtensions
{
  /// <summary>
  /// TCPサーバーに対してWeb UIを起動
  /// </summary>
  public static async Task<WebUIService?> StartWebUIAsync(
      this ITcpServer server,
      WebUIConfig? config,
      ILogger? logger = null,
      CancellationToken cancellationToken = default)
  {
    if (config?.Enabled != true)
    {
      return null;
    }

    var service = new WebUIService(new[] { server }, Array.Empty<ITcpClient>(), config, logger);
    await service.StartAsync(cancellationToken);
    return service;
  }

  /// <summary>
  /// TCPクライアントに対してWeb UIを起動
  /// </summary>
  public static async Task<WebUIService?> StartWebUIAsync(
      this ITcpClient client,
      WebUIConfig? config,
      ILogger? logger = null,
      CancellationToken cancellationToken = default)
  {
    if (config?.Enabled != true)
    {
      return null;
    }

    var service = new WebUIService(Array.Empty<ITcpServer>(), new[] { client }, config, logger);
    await service.StartAsync(cancellationToken);
    return service;
  }

  /// <summary>
  /// 複数のTCPサーバーとクライアントに対してWeb UIを起動（統合モード）
  /// </summary>
  public static async Task<WebUIService?> StartWebUIAsync(
      this IEnumerable<ITcpServer> servers,
      IEnumerable<ITcpClient> clients,
      WebUIConfig? config,
      ILogger? logger = null,
      CancellationToken cancellationToken = default)
  {
    if (config?.Enabled != true)
    {
      return null;
    }

    var service = new WebUIService(servers, clients, config, logger);
    await service.StartAsync(cancellationToken);
    return service;
  }
}
