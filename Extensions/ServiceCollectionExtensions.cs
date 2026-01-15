using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

  /// <summary>
  /// TCP Messengerサービスをlog4netと共に登録
  /// アプリ側でlog4netが設定済みの場合、その設定を使用してログ出力します
  /// </summary>
  /// <param name="services">サービスコレクション</param>
  /// <param name="configuration">設定</param>
  /// <returns>サービスコレクション</returns>
  public static IServiceCollection AddTcpMessengerWithLog4net(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    // log4netアダプターを登録
    services.AddSingleton(typeof(ILogger<>), typeof(Log4netLoggerAdapter<>));
    services.AddSingleton<ILoggerFactory, Log4netLoggerFactoryAdapter>();

    // TCP Messengerサービスを登録
    return services.AddTcpMessenger(configuration);
  }

  /// <summary>
  /// TCP Messengerサービスをlog4netと共に登録（設定オブジェクトを直接指定）
  /// アプリ側でlog4netが設定済みの場合、その設定を使用してログ出力します
  /// </summary>
  /// <param name="services">サービスコレクション</param>
  /// <param name="config">設定オブジェクト</param>
  /// <returns>サービスコレクション</returns>
  public static IServiceCollection AddTcpMessengerWithLog4net(
      this IServiceCollection services,
      TcpMessengerConfig config)
  {
    // log4netアダプターを登録
    services.AddSingleton(typeof(ILogger<>), typeof(Log4netLoggerAdapter<>));
    services.AddSingleton<ILoggerFactory, Log4netLoggerFactoryAdapter>();

    // TCP Messengerサービスを登録
    return services.AddTcpMessenger(config);
  }
}

