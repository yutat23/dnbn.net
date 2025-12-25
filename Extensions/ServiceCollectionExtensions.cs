using Dnbn.Configuration;
using Dnbn.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dnbn.Extensions;

/// <summary>
/// サービスコレクション拡張メソッド
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// TCP Messengerサービスを登録
    /// </summary>
    public static IServiceCollection AddTcpMessenger(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 設定を登録
        services.Configure<TcpMessengerConfig>(configuration.GetSection("TcpMessenger").Bind);

        // ファクトリーを登録
        services.AddSingleton<ITcpMessengerFactory, TcpMessengerFactory>();

        return services;
    }

    /// <summary>
    /// TCP Messengerサービスを登録（設定オブジェクトを直接指定）
    /// </summary>
    public static IServiceCollection AddTcpMessenger(
        this IServiceCollection services,
        TcpMessengerConfig config)
    {
        services.Configure<TcpMessengerConfig>(options =>
        {
            options.Servers = config.Servers;
            options.Clients = config.Clients;
        });
        services.AddSingleton<ITcpMessengerFactory, TcpMessengerFactory>();

        return services;
    }
}

