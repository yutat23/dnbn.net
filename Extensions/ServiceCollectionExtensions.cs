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
  /// Dnbn.Netサービスを登録
  /// </summary>
  public static IServiceCollection AddDnbnNet(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    // 設定を登録（dnbn.net を優先、TcpMessenger をフォールバック）
    var dnbnSection = configuration.GetSection("dnbn.net");
    var tcpMessengerSection = configuration.GetSection("TcpMessenger");

    IConfigurationSection configSection;
    if (dnbnSection.Exists())
    {
      configSection = dnbnSection;
    }
    else if (tcpMessengerSection.Exists())
    {
      configSection = tcpMessengerSection;
    }
    else
    {
      throw new InvalidOperationException("設定セクション 'dnbn.net' または 'TcpMessenger' が見つかりません。appsettings.json に設定を追加してください。");
    }

    services.Configure<TcpMessengerConfig>(configSection.Bind);

    // ファクトリーを登録
    services.AddSingleton<ITcpMessengerFactory, TcpMessengerFactory>();

    return services;
  }

  /// <summary>
  /// Dnbn.Netサービスを登録（設定オブジェクトを直接指定）
  /// </summary>
  public static IServiceCollection AddDnbnNet(
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
  /// TCP Messengerサービスを登録
  /// </summary>
  /// <remarks>
  /// このメソッドは後方互換性のために残されていますが、<strong>非推奨</strong>です。
  /// 代わりに<see cref="AddDnbnNet(IServiceCollection, IConfiguration)"/>を使用してください。
  /// </remarks>
  [Obsolete("このメソッドは非推奨です。代わりに AddDnbnNet を使用してください。")]
  public static IServiceCollection AddTcpMessenger(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    return AddDnbnNet(services, configuration);
  }

  /// <summary>
  /// TCP Messengerサービスを登録（設定オブジェクトを直接指定）
  /// </summary>
  /// <remarks>
  /// このメソッドは後方互換性のために残されていますが、<strong>非推奨</strong>です。
  /// 代わりに<see cref="AddDnbnNet(IServiceCollection, TcpMessengerConfig)"/>を使用してください。
  /// </remarks>
  [Obsolete("このメソッドは非推奨です。代わりに AddDnbnNet を使用してください。")]
  public static IServiceCollection AddTcpMessenger(
      this IServiceCollection services,
      TcpMessengerConfig config)
  {
    return AddDnbnNet(services, config);
  }

  /// <summary>
  /// TCP Messengerサービスをlog4netと共に登録
  /// </summary>
  /// <remarks>
  /// <para>
  /// このメソッドは互換性のために残されていますが、<strong>非推奨</strong>です。
  /// </para>
  /// <para>
  /// <strong>推奨される方法</strong>：アプリ側で<see href="https://www.nuget.org/packages/Microsoft.Extensions.Logging.Log4Net.AspNetCore">Microsoft.Extensions.Logging.Log4Net.AspNetCore</see>パッケージを使用してください。
  /// </para>
  /// <para>
  /// 使用例：
  /// <code>
  /// services.AddLogging(builder => builder.AddLog4Net());
  /// services.AddDnbnNet(configuration);
  /// </code>
  /// </para>
  /// </remarks>
  /// <param name="services">サービスコレクション</param>
  /// <param name="configuration">設定</param>
  /// <returns>サービスコレクション</returns>
  [Obsolete("このメソッドは非推奨です。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreを使用してください。services.AddLogging(builder => builder.AddLog4Net())を呼び出してから、services.AddDnbnNet(configuration)を使用してください。")]
  public static IServiceCollection AddTcpMessengerWithLog4net(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    throw new NotSupportedException(
      "このメソッドは非推奨です。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreパッケージをインストールし、" +
      "services.AddLogging(builder => builder.AddLog4Net())を呼び出してから、services.AddDnbnNet(configuration)を使用してください。");
  }

  /// <summary>
  /// TCP Messengerサービスをlog4netと共に登録（設定オブジェクトを直接指定）
  /// </summary>
  /// <remarks>
  /// <para>
  /// このメソッドは互換性のために残されていますが、<strong>非推奨</strong>です。
  /// </para>
  /// <para>
  /// <strong>推奨される方法</strong>：アプリ側で<see href="https://www.nuget.org/packages/Microsoft.Extensions.Logging.Log4Net.AspNetCore">Microsoft.Extensions.Logging.Log4Net.AspNetCore</see>パッケージを使用してください。
  /// </para>
  /// <para>
  /// 使用例：
  /// <code>
  /// services.AddLogging(builder => builder.AddLog4Net());
  /// services.AddDnbnNet(config);
  /// </code>
  /// </para>
  /// </remarks>
  /// <param name="services">サービスコレクション</param>
  /// <param name="config">設定オブジェクト</param>
  /// <returns>サービスコレクション</returns>
  [Obsolete("このメソッドは非推奨です。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreを使用してください。services.AddLogging(builder => builder.AddLog4Net())を呼び出してから、services.AddDnbnNet(config)を使用してください。")]
  public static IServiceCollection AddTcpMessengerWithLog4net(
      this IServiceCollection services,
      TcpMessengerConfig config)
  {
    throw new NotSupportedException(
      "このメソッドは非推奨です。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreパッケージをインストールし、" +
      "services.AddLogging(builder => builder.AddLog4Net())を呼び出してから、services.AddDnbnNet(config)を使用してください。");
  }
}

