using Dnbn.Core;
using Dnbn.Extensions;
using Dnbn.Filters;
using Dnbn.Logging;
using Dnbn.Models;
using log4net.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reactive.Linq;

namespace TcpMessenger.Sample;

class Program
{
  static async Task Main(string[] args)
  {
    Console.WriteLine("=== TCP Messenger Sample ===");
    Console.WriteLine();

    // 設定を読み込む
    // 実行ファイルと同じディレクトリからappsettings.jsonを読み込む
    var appDirectory = AppContext.BaseDirectory;
    var configuration = new ConfigurationBuilder()
        .SetBasePath(appDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .Build();

    // log4net設定を読み込む
    var log4netConfigFile = new FileInfo(Path.Combine(appDirectory, "log4net.config"));
    if (log4netConfigFile.Exists)
    {
      XmlConfigurator.Configure(log4netConfigFile);
      Console.WriteLine("log4net設定を読み込みました: " + log4netConfigFile.FullName);
    }
    else
    {
      Console.WriteLine("警告: log4net.configが見つかりません。デフォルト設定を使用します。");
      XmlConfigurator.Configure();
    }

    // サービスを登録
    var services = new ServiceCollection();

    // log4netアダプターを使用してTCP Messengerサービスを登録
    services.AddTcpMessengerWithLog4net(configuration);

    // ログフィルターを登録（オプション）
    services.AddSingleton<IMessageFilter, LoggingFilter>();

    var serviceProvider = services.BuildServiceProvider();
    var factory = serviceProvider.GetRequiredService<ITcpMessengerFactory>();
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

    // モード選択
    Console.WriteLine("モードを選択してください:");
    Console.WriteLine("1. サーバーモード");
    Console.WriteLine("2. クライアントモード");
    Console.WriteLine("3. サーバー + クライアント（統合モード）");
    Console.Write("選択 (1-3): ");

    var choice = Console.ReadLine();

    try
    {
      switch (choice)
      {
        case "1":
          await RunServerMode(factory, logger);
          break;
        case "2":
          await RunClientMode(factory, logger);
          break;
        case "3":
          await RunIntegratedMode(factory, logger);
          break;
        default:
          Console.WriteLine("無効な選択です。");
          break;
      }
    }
    catch (Exception ex)
    {
      logger?.LogError(ex, "エラーが発生しました");
      Console.WriteLine($"エラーが発生しました: {ex.Message}");
    }
    finally
    {
      Console.WriteLine("\n終了するには何かキーを押してください...");
      Console.ReadKey();
    }
  }

  /// <summary>
  /// サーバーモード
  /// </summary>
  static async Task RunServerMode(ITcpMessengerFactory factory, ILogger logger)
  {
    Console.WriteLine("\n=== サーバーモード ===");
    var server = factory.CreateServer("EchoServer");

    // イベントハンドラを設定
    server.OnClientConnected += (sender, sessionInfo) =>
    {
      logger.LogInformation("クライアント接続: {SessionId} from {Endpoint}",
              sessionInfo.SessionId, sessionInfo.SourceEndpoint);
    };

    server.OnClientDisconnected += (sender, sessionInfo) =>
    {
      logger.LogInformation("クライアント切断: {SessionId}", sessionInfo.SessionId);
    };

    server.OnMessageReceived += async (sender, args) =>
    {
      var (message, sessionInfo) = args;
      logger.LogInformation("受信 [{SessionId}]: {Message}",
              sessionInfo.SessionId, message.Text?.Trim());

      // エコー応答を送信
      var response = Message.FromString($"ECHO: {message.Text}", System.Text.Encoding.UTF8);
      await server.SendAsync(sessionInfo.SessionId, response);
      logger.LogInformation("送信 [{SessionId}]: {Message}",
              sessionInfo.SessionId, response.Text?.Trim());
    };

    server.OnError += (sender, args) =>
    {
      var (exception, sessionInfo) = args;
      logger.LogError(exception, "エラー発生 [SessionId: {SessionId}]",
              sessionInfo?.SessionId ?? "Unknown");
    };

    // Observableパターンの使用例
    server.MessageReceived
        .Where(args => args.message.Text?.Contains("ALERT") == true)
        .Subscribe(args =>
        {
          var (message, sessionInfo) = args;
          logger.LogWarning("アラート受信 [{SessionId}]: {Message}",
                  sessionInfo.SessionId, message.Text);
        });

    await server.StartAsync();
    logger.LogInformation("サーバーがポート 5000 で起動しました。");

    Console.WriteLine("\nサーバーを停止するには 'q' を入力してください。");
    while (true)
    {
      var input = Console.ReadLine();
      if (input?.ToLower() == "q")
      {
        await server.StopAsync();
        break;
      }
    }
  }

  /// <summary>
  /// クライアントモード
  /// </summary>
  static async Task RunClientMode(ITcpMessengerFactory factory, ILogger logger)
  {
    Console.WriteLine("\n=== クライアントモード ===");
    var client = factory.CreateClient("EchoClient");

    // イベントハンドラを設定
    client.OnConnected += (sender, args) =>
    {
      logger.LogInformation("サーバーに接続しました");
    };

    client.OnDisconnected += (sender, args) =>
    {
      logger.LogInformation("サーバーから切断されました");
    };

    client.OnMessageReceived += (sender, message) =>
    {
      logger.LogInformation("受信: {Message}", message.Text?.Trim());
    };

    client.OnError += (sender, exception) =>
    {
      logger.LogError(exception, "エラー発生");
    };

    // キープアライブ応答イベントの処理
    client.OnKeepAliveResponseReceived += (sender, message) =>
    {
      logger.LogInformation("[KeepAlive] 応答受信: {Message}", message.Text?.Trim());
      // 状態取得コマンドの応答を使用して処理を行う例
      // 例えば、応答内容に基づいて状態を更新するなど
    };

    // Observableパターンの使用例
    client.MessageReceived
        .Where(msg => msg.Text?.StartsWith("ECHO:") == true)
        .Subscribe(msg =>
        {
          logger.LogInformation("[Observable] エコー応答: {Message}", msg.Text);
        });

    await client.ConnectAsync();

    Console.WriteLine("\nメッセージを入力してください（終了するには 'quit' を入力）:");
    while (true)
    {
      var input = Console.ReadLine();
      if (string.IsNullOrWhiteSpace(input))
      {
        continue;
      }

      if (input.ToLower() == "quit")
      {
        break;
      }

      try
      {
        var message = Message.FromString($"{input}".Replace(@"\r", "\r"), System.Text.Encoding.UTF8);
        var response = await client.SendAsync(message, TimeSpan.FromSeconds(5));
        logger.LogInformation("送信: {Message}", input);
        logger.LogInformation("応答: {Response}", response.Text?.Trim());
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "送信エラー");
      }
    }

    await client.DisconnectAsync(true);
  }

  /// <summary>
  /// 統合モード（サーバー + クライアント）
  /// </summary>
  static async Task RunIntegratedMode(ITcpMessengerFactory factory, ILogger logger)
  {
    Console.WriteLine("\n=== 統合モード（サーバー + クライアント） ===");

    // サーバーを起動
    var server = factory.CreateServer("EchoServer");
    server.OnMessageReceived += async (sender, args) =>
    {
      var (message, sessionInfo) = args;
      logger.LogInformation("[Server] 受信: {Message}", message.Text?.Trim());

      // 応答を送信
      var response = Message.FromString($"OK: {message.Text}", System.Text.Encoding.UTF8);
      await server.SendAsync(sessionInfo.SessionId, response);
    };

    await server.StartAsync();
    logger.LogInformation("サーバーが起動しました。");

    // 少し待ってからクライアントを接続
    await Task.Delay(500);

    // クライアントを作成して接続
    var client = factory.CreateClient("EchoClient");
    client.OnMessageReceived += (sender, message) =>
    {
      logger.LogInformation("[Client] 受信: {Message}", message.Text?.Trim());
    };

    // キープアライブ応答イベントの処理
    client.OnKeepAliveResponseReceived += (sender, message) =>
    {
      logger.LogInformation("[Client] [キープアライブ] 応答受信: {Message}", message.Text?.Trim());
      // 状態取得コマンドの応答を使用して処理を行う例
      // 例えば、応答内容に基づいて状態を更新するなど
    };

    await client.ConnectAsync();
    logger.LogInformation("クライアントが接続しました。");

    // キューイング方式のSendAsyncの動作確認
    Console.WriteLine("\n=== キューイング方式のSendAsyncの動作確認 ===");
    Console.WriteLine("複数のメッセージを順次送信し、応答が来るまで次のメッセージが待機することを確認します。");
    Console.WriteLine("OnMessageReceivedイベントは発行されません（応答は戻り値で取得）。\n");

    try
    {
      // イベントハンドラが呼ばれないことを確認するためのカウンター
      int eventReceivedCount = 0;
      client.OnMessageReceived += (sender, message) =>
      {
        eventReceivedCount++;
        logger.LogWarning("[イベント] これは表示されないはず: {Message}", message.Text);
      };

      // 複数のメッセージを順次送信
      var messages = new[] { "MSG1", "MSG2", "MSG3" };
      var startTime = DateTime.UtcNow;

      foreach (var msgText in messages)
      {
        var msg = Message.FromString($"{msgText}\r\n", System.Text.Encoding.UTF8);
        var sendStart = DateTime.UtcNow;
        var response = await client.SendAsync(msg, TimeSpan.FromSeconds(5));
        var sendEnd = DateTime.UtcNow;

        logger.LogInformation("[送信] {Message} -> [応答] {Response} (所要時間: {Elapsed}ms)",
            msgText, response.Text?.Trim(), (sendEnd - sendStart).TotalMilliseconds);
      }

      var totalTime = DateTime.UtcNow - startTime;
      logger.LogInformation("\n合計所要時間: {TotalTime}ms", totalTime.TotalMilliseconds);
      logger.LogInformation("OnMessageReceivedイベントが発行された回数: {Count} (0であるべき)", eventReceivedCount);

      if (eventReceivedCount == 0)
      {
        logger.LogInformation("✓ キューイング方式が正常に動作しています（イベントは発行されません）");
      }
      else
      {
        logger.LogWarning("✗ イベントが発行されています（キューイング方式の応答はイベントを発行しません）");
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "キューイング方式のテストでエラーが発生しました");
    }

    // Promise的チェーン処理の例（SendAsyncを使用）
    Console.WriteLine("\n=== SendAsyncを使ったチェーン処理の例 ===");
    try
    {
      var initMessage = Message.FromString("INIT\r\n", System.Text.Encoding.UTF8);

      // SendAsyncで送信して応答を待つ
      var firstResponse = await client.SendAsync(initMessage, TimeSpan.FromSeconds(3));
      logger.LogInformation("初期化応答: {Message}", firstResponse.Text);

      // 次のリクエストを送信
      var nextMessage = Message.FromString($"NEXT: {firstResponse.Text}\r\n", System.Text.Encoding.UTF8);
      var finalResponse = await client.SendAsync(nextMessage, TimeSpan.FromSeconds(3));

      logger.LogInformation("最終応答: {Message}", finalResponse.Text);
      logger.LogInformation("チェーン処理が完了しました。");
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "チェーン処理でエラーが発生しました");
    }

    // 対話的なメッセージ送信（キューイング方式）
    Console.WriteLine("\n=== 対話的なメッセージ送信（キューイング方式） ===");
    Console.WriteLine("メッセージを入力してください（終了するには 'quit' を入力）:");
    Console.WriteLine("複数のメッセージを連続で送信すると、順次処理されます。");
    Console.WriteLine("応答は戻り値で取得でき、OnMessageReceivedイベントは発行されません。\n");

    while (true)
    {
      var input = Console.ReadLine();
      if (string.IsNullOrWhiteSpace(input))
      {
        continue;
      }

      if (input.ToLower() == "quit")
      {
        break;
      }

      try
      {
        var sendStart = DateTime.UtcNow;
        var message = Message.FromString($"{input}".Replace(@"\r", "\r"), System.Text.Encoding.UTF8);
        var response = await client.SendAsync(message, TimeSpan.FromSeconds(5));
        var sendEnd = DateTime.UtcNow;

        logger.LogInformation("[送信] {Message}", input);
        logger.LogInformation("[応答] {Response} (所要時間: {Elapsed}ms)",
            response.Text?.Trim(), (sendEnd - sendStart).TotalMilliseconds);
      }
      catch (TimeoutException ex)
      {
        logger.LogError(ex, "タイムアウト: {Message}", ex.Message);
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "送信エラー");
      }
    }

    await client.DisconnectAsync();
    await server.StopAsync();
  }
}

/// <summary>
/// ログフィルターの実装例
/// </summary>
public class LoggingFilter : IMessageFilter
{
  private readonly ILogger<LoggingFilter>? _logger;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="loggerFactory">ロガーファクトリー（オプション）</param>
  public LoggingFilter(ILoggerFactory? loggerFactory = null)
  {
    _logger = loggerFactory?.CreateLogger<LoggingFilter>();
  }

  /// <summary>
  /// 送信前のメッセージを処理
  /// </summary>
  /// <param name="msg">送信するメッセージ</param>
  /// <param name="ctx">メッセージコンテキスト</param>
  /// <returns>処理後のメッセージ</returns>
  public Task<Message> OnSendingAsync(Message msg, IMessageContext ctx)
  {
    _logger?.LogDebug("[Filter] 送信前: {Message}", msg.Text?.Trim());
    return Task.FromResult(msg);
  }

  /// <summary>
  /// 受信後のメッセージを処理
  /// </summary>
  /// <param name="msg">受信したメッセージ</param>
  /// <param name="ctx">メッセージコンテキスト</param>
  /// <returns>処理後のメッセージ</returns>
  public Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx)
  {
    _logger?.LogDebug("[Filter] 受信後: {Message}", msg.Text?.Trim());
    return Task.FromResult(msg);
  }
}

/// <summary>
/// 簡易的なLoggerFactory実装（フォールバック用）
/// </summary>
internal class SimpleLoggerFactory : ILoggerFactory
{
  public void AddProvider(ILoggerProvider provider)
  {
    // 簡易実装では何もしない
  }

  public ILogger CreateLogger(string categoryName)
  {
    return new SimpleLogger(categoryName);
  }

  public void Dispose()
  {
    // リソースのクリーンアップ
  }
}

/// <summary>
/// 簡易的なLogger実装（フォールバック用）
/// </summary>
internal class SimpleLogger : ILogger
{
  private readonly string _categoryName;

  public SimpleLogger(string categoryName)
  {
    _categoryName = categoryName;
  }

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
  {
    var message = formatter(state, exception);
    Console.WriteLine($"[{logLevel}] [{_categoryName}] {message}");
    if (exception != null)
    {
      Console.WriteLine($"Exception: {exception}");
    }
  }
}

/// <summary>
/// 簡易的なLogger実装（フォールバック用）
/// </summary>
/// <typeparam name="T">ロガーのカテゴリ型</typeparam>
internal class SimpleLogger<T> : ILogger<T>
{
  private readonly SimpleLogger _logger;

  public SimpleLogger(ILoggerFactory loggerFactory)
  {
    _logger = new SimpleLogger(typeof(T).FullName ?? typeof(T).Name);
  }

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
  {
    _logger.Log(logLevel, eventId, state, exception, formatter);
  }
}
