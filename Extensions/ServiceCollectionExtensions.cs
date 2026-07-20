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

    var boundConfig = configSection.Get<TcpMessengerConfig>() ?? new TcpMessengerConfig();
    TcpMessengerConfigValidator.Validate(boundConfig);
    services.Configure<TcpMessengerConfig>(options => CopyConfig(boundConfig, options));

    // ファクトリーを登録
    AddFactoryServices(services);

    return services;
  }

  /// <summary>
  /// Dnbn.Netサービスを登録（設定オブジェクトを直接指定）
  /// </summary>
  public static IServiceCollection AddDnbnNet(
      this IServiceCollection services,
      TcpMessengerConfig config)
  {
    TcpMessengerConfigValidator.Validate(config);
    services.Configure<TcpMessengerConfig>(options => CopyConfig(config, options));
    AddFactoryServices(services);

    return services;
  }

  /// <summary>
  /// 設定に定義された全クライアントを Hosted Service として登録する。
  /// アプリ起動時に自動接続（バックグラウンド、ConnectionRetryPolicy に従いリトライ）、
  /// シャットダウン時に自動切断する。
  /// あわせて各クライアントを keyed singleton（キー = クライアント名）として登録するため、
  /// <c>GetRequiredKeyedService&lt;ITcpClient&gt;("Name")</c> や
  /// <see cref="IDnbnClientCollection"/> で取得できる。
  /// <see cref="AddDnbnNet(IServiceCollection, IConfiguration)"/> の後に呼び出すこと。
  /// </summary>
  /// <param name="services">サービスコレクション</param>
  /// <param name="configuration">設定（dnbn.net または TcpMessenger セクションを含むもの）</param>
  /// <param name="connectOnHostStart">Host起動時に自動接続し、停止時に自動切断するか</param>
  public static IServiceCollection AddDnbnNetHostedClients(
      this IServiceCollection services,
      IConfiguration configuration,
      bool connectOnHostStart = true)
  {
    // AddDnbnNet と同じ優先順位でセクションを解決する
    var section = configuration.GetSection("dnbn.net");
    if (!section.Exists())
    {
      section = configuration.GetSection("TcpMessenger");
    }

    var clients = section.GetSection("Clients").Get<List<ClientConfig>>() ?? new List<ClientConfig>();
    TcpMessengerConfigValidator.Validate(new TcpMessengerConfig { Clients = clients });
    var names = clients.Select(client => client.Name).ToArray();
    foreach (var name in names)
    {
      var capturedName = name;
      services.AddKeyedSingleton<ITcpClient>(capturedName,
          (sp, _) => sp.GetRequiredService<ITcpMessengerFactory>().CreateClient(capturedName));
    }
    return AddClientRegistryAndHostedService(services, names, connectOnHostStart);
  }

  /// <summary>
  /// 型付き設定で指定したクライアントをkeyed singletonとして登録し、Hostの起動・停止と連動させる。
  /// DB等から起動時に構成したクライアントにも使用できる。
  /// </summary>
  public static IServiceCollection AddDnbnNetHostedClients(
      this IServiceCollection services,
      IEnumerable<ClientConfig> clients,
      bool connectOnHostStart = true)
  {
    if (clients is null) throw new ArgumentNullException(nameof(clients));
    var configs = clients.Select(client => client.Clone()).ToList();
    var root = new TcpMessengerConfig { Clients = configs };
    TcpMessengerConfigValidator.Validate(root);
    var names = configs.Select(client => client.Name).ToArray();

    foreach (var config in configs)
    {
      var capturedConfig = config;
      services.AddKeyedSingleton<ITcpClient>(capturedConfig.Name,
          (sp, _) => sp.GetRequiredService<ITypedTcpMessengerFactory>().CreateClient(capturedConfig));
    }

    return AddClientRegistryAndHostedService(services, names, connectOnHostStart);
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

  private static void CopyConfig(TcpMessengerConfig source, TcpMessengerConfig destination)
  {
    var clone = source.Clone();
    destination.Servers = clone.Servers;
    destination.Clients = clone.Clients;
    destination.WebUI = clone.WebUI;
  }

  private static void AddFactoryServices(IServiceCollection services)
  {
    services.AddSingleton<TcpMessengerFactory>();
    services.AddSingleton<ITypedTcpMessengerFactory>(sp => sp.GetRequiredService<TcpMessengerFactory>());
    services.AddSingleton<ITcpMessengerFactory>(sp => sp.GetRequiredService<TcpMessengerFactory>());
  }

  private static IServiceCollection AddClientRegistryAndHostedService(
      IServiceCollection services,
      IReadOnlyList<string> names,
      bool connectOnHostStart)
  {
    services.AddSingleton<DnbnClientCollection>(sp => new DnbnClientCollection(sp, names));
    services.AddSingleton<IDnbnClientRegistry>(sp => sp.GetRequiredService<DnbnClientCollection>());
    services.AddSingleton<IDnbnClientCollection>(sp => sp.GetRequiredService<DnbnClientCollection>());
    if (connectOnHostStart)
    {
      services.AddHostedService<DnbnClientsHostedService>();
    }
    return services;
  }
}
