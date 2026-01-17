using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Extensions;
using Dnbn.Filters;
using Dnbn.Logging;
using Dnbn.Models;
using log4net;
using log4net.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reactive.Linq;
using System.Text.Json;

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
    ILog _log = LogManager.GetLogger(typeof(Program));
    _log.Info("log4netが初期化されました。");

    // サービスを登録
    var services = new ServiceCollection();

    // log4netアダプターを使用してTCP Messengerサービスを登録
    services.AddTcpMessengerWithLog4net(configuration);

    // ログフィルターを登録（オプション）
    services.AddSingleton<IMessageFilter, SampleLoggingFilter>();

    var serviceProvider = services.BuildServiceProvider();
    var factory = serviceProvider.GetRequiredService<ITcpMessengerFactory>();

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
          await RunServerMode(factory, _log);
          break;
        case "2":
          await RunClientMode(factory, _log);
          break;
        case "3":
          await RunIntegratedMode(factory, _log);
          break;
        default:
          Console.WriteLine("無効な選択です。");
          break;
      }
    }
    catch (Exception ex)
    {
      _log.Error("エラーが発生しました", ex);
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
  static async Task RunServerMode(ITcpMessengerFactory factory, ILog _log)
  {
    Console.WriteLine("\n=== サーバーモード ===");
    var server = factory.CreateServer("EchoServer");

    // イベントハンドラを設定
    server.OnClientConnected += (sender, sessionInfo) =>
    {
      _log.Info($"クライアント接続: {sessionInfo.SessionId} from {sessionInfo.SourceEndpoint}");
    };

    server.OnClientDisconnected += (sender, sessionInfo) =>
    {
      _log.Info($"クライアント切断: {sessionInfo.SessionId}");
    };

    server.OnMessageReceived += async (sender, args) =>
    {
      var (message, sessionInfo) = args;
      _log.Info($"受信 [{sessionInfo.SessionId}]: {message.Text?.Trim()}");

      // エコー応答を送信
      var response = Message.FromString($"ECHO: {message.Text}", System.Text.Encoding.UTF8);
      await server.SendAsync(sessionInfo.SessionId, response);
      _log.Info($"送信 [{sessionInfo.SessionId}]: {response.Text?.Trim()}");
    };

    server.OnError += (sender, args) =>
    {
      var (exception, sessionInfo) = args;
      _log.Error($"エラー発生 [SessionId: {sessionInfo?.SessionId ?? "Unknown"}]", exception);
    };

    // Observableパターンの使用例
    server.MessageReceived
        .Where(args => args.message.Text?.Contains("ALERT") == true)
        .Subscribe(args =>
        {
          var (message, sessionInfo) = args;
          _log.Warn($"アラート受信 [{sessionInfo.SessionId}]: {message.Text}");
        });

    await server.StartAsync();
    _log.Info("サーバーがポート 5000 で起動しました。");

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
  static async Task RunClientMode(ITcpMessengerFactory factory, ILog _log)
  {
    Console.WriteLine("\n=== クライアントモード ===");
    var client = factory.CreateClient("EchoClient");

    // イベントハンドラを設定
    client.OnConnected += (sender, args) =>
    {
      _log.Info("サーバーに接続しました");
    };

    client.OnDisconnected += (sender, args) =>
    {
      _log.Info("サーバーから切断されました");
    };

    client.OnMessageReceived += (sender, message) =>
    {
      _log.Info($"受信: {message.Text?.Trim()}");
    };

    client.OnError += (sender, exception) =>
    {
      _log.Error("エラー発生", exception);
    };

    // キープアライブ応答イベントの処理
    client.OnKeepAliveResponseReceived += (sender, message) =>
    {
      _log.Info($"[KeepAlive] 応答受信: {message.Text?.Trim()}");
      // 状態取得コマンドの応答を使用して処理を行う例
      // 例えば、応答内容に基づいて状態を更新するなど
    };

    // Observableパターンの使用例
    client.MessageReceived
        .Where(msg => msg.Text?.StartsWith("ECHO:") == true)
        .Subscribe(msg =>
        {
          _log.Info($"[Observable] エコー応答: {msg.Text}");
        });

    await client.ConnectAsync();

    Console.WriteLine("\nメッセージを入力してください（終了するには 'quit' を入力）:");
    Console.WriteLine("設定変更: 'config' と入力");
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

      if (input.ToLower().StartsWith("config"))
      {
        await HandleConfigCommand(client, input, _log);
        continue;
      }

      try
      {
        var message = Message.FromString($"{input}".Replace(@"\r", "\r"), System.Text.Encoding.UTF8);
        var response = await client.SendAsync(message, TimeSpan.FromSeconds(5));
        _log.Info($"送信: {input}");
        _log.Info($"応答: {response.Text?.Trim()}");
      }
      catch (Exception ex)
      {
        _log.Error("送信エラー", ex);
      }
    }

    await client.DisconnectAsync(true);
  }

  /// <summary>
  /// 統合モード（サーバー + クライアント）
  /// </summary>
  static async Task RunIntegratedMode(ITcpMessengerFactory factory, ILog _log)
  {
    Console.WriteLine("\n=== 統合モード（サーバー + クライアント） ===");

    // サーバーを起動
    var server = factory.CreateServer("EchoServer");
    server.OnMessageReceived += async (sender, args) =>
    {
      var (message, sessionInfo) = args;
      _log.Info($"[Server] 受信: {message.Text?.Trim()}");

      // 応答を送信
      var response = Message.FromString($"OK: {message.Text}", System.Text.Encoding.UTF8);
      await server.SendAsync(sessionInfo.SessionId, response);
    };

    await server.StartAsync();
    _log.Info("サーバーが起動しました。");

    // 少し待ってからクライアントを接続
    await Task.Delay(500);

    // クライアントを作成して接続
    var client = factory.CreateClient("EchoClient");
    client.OnMessageReceived += (sender, message) =>
    {
      _log.Info($"[Client] 受信: {message.Text?.Trim()}");
    };

    // キープアライブ応答イベントの処理
    client.OnKeepAliveResponseReceived += (sender, message) =>
    {
      _log.Info($"[Client] [キープアライブ] 応答受信: {message.Text?.Trim()}");
      // 状態取得コマンドの応答を使用して処理を行う例
      // 例えば、応答内容に基づいて状態を更新するなど
    };

    await client.ConnectAsync();
    _log.Info("クライアントが接続しました。");

    // HTTPエンドポイントを起動（接続状態情報をJSONで取得可能）
    _ = Task.Run(async () => await StartHttpServer(server, client, _log));
    _log.Info("HTTPエンドポイントが起動しました: http://localhost:8080");
    _log.Info("  - GET /api/status -> 全接続状態情報を取得");
    _log.Info("  - GET /api/status/client -> クライアント接続状態情報を取得");
    _log.Info("  - GET /api/status/server -> サーバー接続状態情報を取得");

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
        _log.Warn($"[イベント] これは表示されないはず: {message.Text}");
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

        _log.Info($"[送信] {msgText} -> [応答] {response.Text?.Trim()} (所要時間: {(sendEnd - sendStart).TotalMilliseconds}ms)");
      }

      var totalTime = DateTime.UtcNow - startTime;
      _log.Info($"\n合計所要時間: {totalTime.TotalMilliseconds}ms");
      _log.Info($"OnMessageReceivedイベントが発行された回数: {eventReceivedCount} (0であるべき)");

      if (eventReceivedCount == 0)
      {
        _log.Info("✓ キューイング方式が正常に動作しています（イベントは発行されません）");
      }
      else
      {
        _log.Warn("✗ イベントが発行されています（キューイング方式の応答はイベントを発行しません）");
      }
    }
    catch (Exception ex)
    {
      _log.Error("キューイング方式のテストでエラーが発生しました", ex);
    }

    // Promise的チェーン処理の例（SendAsyncを使用）
    Console.WriteLine("\n=== SendAsyncを使ったチェーン処理の例 ===");
    try
    {
      var initMessage = Message.FromString("INIT\r\n", System.Text.Encoding.UTF8);

      // SendAsyncで送信して応答を待つ
      var firstResponse = await client.SendAsync(initMessage, TimeSpan.FromSeconds(3));
      _log.Info($"初期化応答: {firstResponse.Text}");

      // 次のリクエストを送信
      var nextMessage = Message.FromString($"NEXT: {firstResponse.Text}\r\n", System.Text.Encoding.UTF8);
      var finalResponse = await client.SendAsync(nextMessage, TimeSpan.FromSeconds(3));

      _log.Info($"最終応答: {finalResponse.Text}");
      _log.Info("チェーン処理が完了しました。");
    }
    catch (Exception ex)
    {
      _log.Error("チェーン処理でエラーが発生しました", ex);
    }

    // 対話的なメッセージ送信（キューイング方式）
    Console.WriteLine("\n=== 対話的なメッセージ送信（キューイング方式） ===");
    Console.WriteLine("メッセージを入力してください（終了するには 'quit' を入力）:");
    Console.WriteLine("複数のメッセージを連続で送信すると、順次処理されます。");
    Console.WriteLine("応答は戻り値で取得でき、OnMessageReceivedイベントは発行されません。");
    Console.WriteLine("設定変更: 'config' と入力\n");

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

      if (input.ToLower().StartsWith("config"))
      {
        await HandleConfigCommand(client, input, _log);
        continue;
      }

      try
      {
        var sendStart = DateTime.UtcNow;
        var message = Message.FromString($"{input}".Replace(@"\r", "\r"), System.Text.Encoding.UTF8);
        var response = await client.SendAsync(message, TimeSpan.FromSeconds(5));
        var sendEnd = DateTime.UtcNow;

        _log.Info($"[送信] {input}");
        _log.Info($"[応答] {response.Text?.Trim()} (所要時間: {(sendEnd - sendStart).TotalMilliseconds}ms)");
      }
      catch (TimeoutException ex)
      {
        _log.Error($"タイムアウト: {ex.Message}", ex);
      }
      catch (Exception ex)
      {
        _log.Error("送信エラー", ex);
      }
    }

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  /// <summary>
  /// 設定変更コマンドのハンドラー
  /// </summary>
  static async Task HandleConfigCommand(ITcpClient client, string command, ILog log)
  {
    var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 2)
    {
      ShowConfigMenu(log);
      return;
    }

    try
    {
      switch (parts[1].ToLower())
      {
        case "show":
          ShowCurrentConfig(client, log);
          break;
        case "keepalive":
          await HandleKeepAliveConfig(client, parts, log);
          break;
        case "timeout":
          await HandleTimeoutConfig(client, parts, log);
          break;
        case "retry":
          await HandleRetryPolicyConfig(client, parts, log);
          break;
        case "connectionretry":
          await HandleConnectionRetryPolicyConfig(client, parts, log);
          break;
        default:
          Console.WriteLine("無効なコマンドです。'config' と入力してメニューを表示してください。");
          break;
      }
    }
    catch (Exception ex)
    {
      log.Error("設定変更中にエラーが発生しました", ex);
      Console.WriteLine($"エラー: {ex.Message}");
    }
  }

  /// <summary>
  /// 設定メニューを表示
  /// </summary>
  static void ShowConfigMenu(ILog log)
  {
    Console.WriteLine("\n=== 設定変更メニュー ===");
    Console.WriteLine("config show              - 現在の設定を表示");
    Console.WriteLine("config keepalive         - KeepAlive設定を変更");
    Console.WriteLine("config timeout <ms>      - タイムアウト設定を変更");
    Console.WriteLine("config retry             - リトライポリシーを変更");
    Console.WriteLine("config connectionretry   - 接続リトライポリシーを変更");
    Console.WriteLine();
  }

  /// <summary>
  /// 現在の設定を表示
  /// </summary>
  static void ShowCurrentConfig(ITcpClient client, ILog log)
  {
    Console.WriteLine("\n=== 現在の設定 ===");
    Console.WriteLine($"クライアント名: {client.Name}");
    Console.WriteLine($"接続状態: {(client.IsConnected ? "接続中" : "切断中")}");

    var keepAlive = client.KeepAlive;
    if (keepAlive != null)
    {
      Console.WriteLine($"KeepAlive: Enabled={keepAlive.Enabled}, Interval={keepAlive.IntervalSeconds}s, Message={keepAlive.Message}");
    }
    else
    {
      Console.WriteLine("KeepAlive: 未設定");
    }

    Console.WriteLine($"タイムアウト: {client.TimeoutMilliseconds}ms");

    var retryPolicy = client.RetryPolicy;
    if (retryPolicy != null)
    {
      Console.WriteLine($"リトライポリシー: MaxRetryCount={retryPolicy.MaxRetryCount}, Strategy={retryPolicy.RetryDelayStrategy}");
    }
    else
    {
      Console.WriteLine("リトライポリシー: 未設定");
    }

    var connectionRetryPolicy = client.ConnectionRetryPolicy;
    if (connectionRetryPolicy != null)
    {
      Console.WriteLine($"接続リトライポリシー: MaxRetryCount={connectionRetryPolicy.MaxRetryCount}, Strategy={connectionRetryPolicy.RetryDelayStrategy}");
    }
    else
    {
      Console.WriteLine("接続リトライポリシー: 未設定");
    }
    Console.WriteLine();
  }

  /// <summary>
  /// KeepAlive設定の変更
  /// </summary>
  static async Task HandleKeepAliveConfig(ITcpClient client, string[] parts, ILog log)
  {
    if (parts.Length < 3)
    {
      Console.WriteLine("使用方法: config keepalive <enable|disable> [interval] [message]");
      Console.WriteLine("例: config keepalive enable 10 \"w\\r\"");
      return;
    }

    bool enabled = parts[2].ToLower() == "enable" || parts[2].ToLower() == "enabled";
    
    if (!enabled)
    {
      client.KeepAlive = new KeepAliveConfig { Enabled = false };
      Console.WriteLine("KeepAliveを無効化しました。");
      return;
    }

    int intervalSeconds = 30;
    string message = "w\r";

    if (parts.Length >= 4 && int.TryParse(parts[3], out int interval))
    {
      intervalSeconds = interval;
    }

    if (parts.Length >= 5)
    {
      message = parts[4].Replace("\\r", "\r").Replace("\\n", "\n");
    }

    client.KeepAlive = new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = intervalSeconds,
      Message = message
    };

    Console.WriteLine($"KeepAlive設定を更新しました: Enabled=true, Interval={intervalSeconds}s, Message={message}");
  }

  /// <summary>
  /// タイムアウト設定の変更
  /// </summary>
  static async Task HandleTimeoutConfig(ITcpClient client, string[] parts, ILog log)
  {
    if (parts.Length < 3)
    {
      Console.WriteLine("使用方法: config timeout <milliseconds>");
      Console.WriteLine("例: config timeout 10000");
      return;
    }

    if (!int.TryParse(parts[2], out int timeoutMs) || timeoutMs <= 0)
    {
      Console.WriteLine("無効な値です。正の整数を指定してください。");
      return;
    }

    client.TimeoutMilliseconds = timeoutMs;
    Console.WriteLine($"タイムアウト設定を更新しました: {timeoutMs}ms");
  }

  /// <summary>
  /// リトライポリシー設定の変更
  /// </summary>
  static async Task HandleRetryPolicyConfig(ITcpClient client, string[] parts, ILog log)
  {
    if (parts.Length < 3)
    {
      Console.WriteLine("使用方法: config retry <disable|enable> [maxRetryCount] [strategy] [initialDelayMs] [maxDelayMs]");
      Console.WriteLine("例: config retry enable 3 exponential 500 30000");
      return;
    }

    if (parts[2].ToLower() == "disable")
    {
      client.RetryPolicy = null;
      Console.WriteLine("リトライポリシーを無効化しました。");
      return;
    }

    int maxRetryCount = 3;
    RetryDelayStrategy strategy = RetryDelayStrategy.Exponential;
    int initialDelayMs = 500;
    int maxDelayMs = 30000;

    if (parts.Length >= 4 && int.TryParse(parts[3], out int maxRetry))
    {
      maxRetryCount = maxRetry;
    }

    if (parts.Length >= 5)
    {
      strategy = parts[4].ToLower() == "fixed" ? RetryDelayStrategy.Fixed : RetryDelayStrategy.Exponential;
    }

    if (parts.Length >= 6 && int.TryParse(parts[5], out int initialDelay))
    {
      initialDelayMs = initialDelay;
    }

    if (parts.Length >= 7 && int.TryParse(parts[6], out int maxDelay))
    {
      maxDelayMs = maxDelay;
    }

    client.RetryPolicy = new RetryPolicy
    {
      MaxRetryCount = maxRetryCount,
      RetryDelayStrategy = strategy,
      InitialDelayMs = initialDelayMs,
      MaxDelayMs = maxDelayMs
    };

    Console.WriteLine($"リトライポリシーを更新しました: MaxRetryCount={maxRetryCount}, Strategy={strategy}");
  }

  /// <summary>
  /// 接続リトライポリシー設定の変更
  /// </summary>
  static async Task HandleConnectionRetryPolicyConfig(ITcpClient client, string[] parts, ILog log)
  {
    if (parts.Length < 3)
    {
      Console.WriteLine("使用方法: config connectionretry <disable|enable> [maxRetryCount] [strategy] [initialDelayMs] [maxDelayMs]");
      Console.WriteLine("例: config connectionretry enable -1 exponential 1000 60000");
      return;
    }

    if (parts[2].ToLower() == "disable")
    {
      client.ConnectionRetryPolicy = null;
      Console.WriteLine("接続リトライポリシーを無効化しました。");
      return;
    }

    int maxRetryCount = -1;
    RetryDelayStrategy strategy = RetryDelayStrategy.Exponential;
    int initialDelayMs = 1000;
    int maxDelayMs = 60000;

    if (parts.Length >= 4 && int.TryParse(parts[3], out int maxRetry))
    {
      maxRetryCount = maxRetry;
    }

    if (parts.Length >= 5)
    {
      strategy = parts[4].ToLower() == "fixed" ? RetryDelayStrategy.Fixed : RetryDelayStrategy.Exponential;
    }

    if (parts.Length >= 6 && int.TryParse(parts[5], out int initialDelay))
    {
      initialDelayMs = initialDelay;
    }

    if (parts.Length >= 7 && int.TryParse(parts[6], out int maxDelay))
    {
      maxDelayMs = maxDelay;
    }

    client.ConnectionRetryPolicy = new RetryPolicy
    {
      MaxRetryCount = maxRetryCount,
      RetryDelayStrategy = strategy,
      InitialDelayMs = initialDelayMs,
      MaxDelayMs = maxDelayMs
    };

    Console.WriteLine($"接続リトライポリシーを更新しました: MaxRetryCount={maxRetryCount}, Strategy={strategy}");
  }

  /// <summary>
  /// HTTPサーバーを起動して接続状態情報をJSONで提供
  /// </summary>
  static async Task StartHttpServer(ITcpServer server, ITcpClient client, ILog log)
  {
    var builder = WebApplication.CreateBuilder();
    
    // CORSサービスを追加
    builder.Services.AddCors(options =>
    {
      options.AddDefaultPolicy(policy =>
      {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
      });
    });
    
    var app = builder.Build();

    // CORSミドルウェアを使用
    app.UseCors();

    // JSONシリアライザーオプション
    var jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // 全接続状態情報を取得
    app.MapGet("/api/status", () =>
    {
      var clientInfo = client.ConnectionInfo;
      var serverInfo = server.ConnectionInfo;
      var result = new
      {
        Client = new
        {
          Name = client.Name,
          IsConnected = clientInfo.IsConnected,
          ConnectedAt = clientInfo.ConnectedAt,
          ConnectionDuration = clientInfo.ConnectionDuration?.ToString(@"dd\.hh\:mm\:ss"),
          RemoteHost = clientInfo.RemoteHost,
          RemotePort = clientInfo.RemotePort,
          IsReconnecting = clientInfo.IsReconnecting,
          MessagesSent = clientInfo.MessagesSent,
          MessagesReceived = clientInfo.MessagesReceived,
          PendingRequests = clientInfo.PendingRequests,
          LastMessageReceivedAt = clientInfo.LastMessageReceivedAt,
          KeepAlive = new
          {
            LastSentAt = clientInfo.LastKeepAliveSentAt,
            LastResponseReceivedAt = clientInfo.LastKeepAliveResponseReceivedAt,
            TimeoutCount = clientInfo.KeepAliveTimeoutCount
          },
          Error = new
          {
            Count = clientInfo.ErrorCount,
            LastError = clientInfo.LastError,
            LastErrorAt = clientInfo.LastErrorAt
          },
          ConnectionRetry = new
          {
            Attempts = clientInfo.ConnectionRetryAttempts,
            LastAttemptAt = clientInfo.LastRetryAttemptAt
          }
        },
        Server = new
        {
          Name = server.Name,
          IsRunning = serverInfo.IsRunning,
          ListenPort = serverInfo.ListenPort,
          StartedAt = serverInfo.StartedAt,
          Uptime = serverInfo.Uptime?.ToString(@"dd\.hh\:mm\:ss"),
          ConnectionCount = serverInfo.ConnectionCount,
          TotalConnections = serverInfo.TotalConnections,
          LastClientConnectedAt = serverInfo.LastClientConnectedAt,
          LastClientDisconnectedAt = serverInfo.LastClientDisconnectedAt,
          MessagesSent = serverInfo.MessagesSent,
          MessagesReceived = serverInfo.MessagesReceived,
          Sessions = server.GetAllSessions().Select(s => new
          {
            SessionId = s.SessionId,
            SourceEndpoint = s.SourceEndpoint.ToString(),
            ConnectedAt = s.ConnectedAt,
            LastMessageReceivedAt = s.LastMessageReceivedAt,
            IsActive = s.IsActive
          }).ToArray()
        }
      };
      return Results.Json(result, jsonOptions);
    });

    // クライアント接続状態情報を取得
    app.MapGet("/api/status/client", () =>
    {
      var info = client.ConnectionInfo;
      var result = new
      {
        Name = client.Name,
        IsConnected = info.IsConnected,
        ConnectedAt = info.ConnectedAt,
        ConnectionDuration = info.ConnectionDuration?.ToString(@"dd\.hh\:mm\:ss"),
        RemoteHost = info.RemoteHost,
        RemotePort = info.RemotePort,
        IsReconnecting = info.IsReconnecting,
        MessagesSent = info.MessagesSent,
        MessagesReceived = info.MessagesReceived,
        PendingRequests = info.PendingRequests,
        LastMessageReceivedAt = info.LastMessageReceivedAt,
        KeepAlive = new
        {
          LastSentAt = info.LastKeepAliveSentAt,
          LastResponseReceivedAt = info.LastKeepAliveResponseReceivedAt,
          TimeoutCount = info.KeepAliveTimeoutCount
        },
        Error = new
        {
          Count = info.ErrorCount,
          LastError = info.LastError,
          LastErrorAt = info.LastErrorAt
        },
        ConnectionRetry = new
        {
          Attempts = info.ConnectionRetryAttempts,
          LastAttemptAt = info.LastRetryAttemptAt
        }
      };
      return Results.Json(result, jsonOptions);
    });

    // サーバー接続状態情報を取得
    app.MapGet("/api/status/server", () =>
    {
      var info = server.ConnectionInfo;
      var result = new
      {
        Name = server.Name,
        IsRunning = info.IsRunning,
        ListenPort = info.ListenPort,
        StartedAt = info.StartedAt,
        Uptime = info.Uptime?.ToString(@"dd\.hh\:mm\:ss"),
        ConnectionCount = info.ConnectionCount,
        TotalConnections = info.TotalConnections,
        LastClientConnectedAt = info.LastClientConnectedAt,
        LastClientDisconnectedAt = info.LastClientDisconnectedAt,
        MessagesSent = info.MessagesSent,
        MessagesReceived = info.MessagesReceived,
        Sessions = server.GetAllSessions().Select(s => new
        {
          SessionId = s.SessionId,
          SourceEndpoint = s.SourceEndpoint.ToString(),
          ConnectedAt = s.ConnectedAt,
          LastMessageReceivedAt = s.LastMessageReceivedAt,
          IsActive = s.IsActive
        }).ToArray()
      };
      return Results.Json(result, jsonOptions);
    });

    // ヘルスチェックエンドポイント
    app.MapGet("/api/health", () =>
    {
      return Results.Json(new
      {
        Status = "Healthy",
        Timestamp = DateTime.UtcNow
      }, jsonOptions);
    });

    try
    {
      await app.RunAsync("http://localhost:8080");
    }
    catch (Exception ex)
    {
      log.Error("HTTPサーバーの起動に失敗しました", ex);
    }
  }
}

/// <summary>
/// ログフィルターの実装例
/// </summary>
internal class SampleLoggingFilter : IMessageFilter
{
  private static readonly ILog _log = LogManager.GetLogger(typeof(SampleLoggingFilter));

  /// <summary>
  /// コンストラクタ
  /// </summary>
  public SampleLoggingFilter()
  {
  }

  /// <summary>
  /// 送信前のメッセージを処理
  /// </summary>
  /// <param name="msg">送信するメッセージ</param>
  /// <param name="ctx">メッセージコンテキスト</param>
  /// <returns>処理後のメッセージ</returns>
  public Task<Message> OnSendingAsync(Message msg, IMessageContext ctx)
  {
    _log.Debug($"[Filter] 送信前: {msg.Text?.Trim()}");
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
    _log.Debug($"[Filter] 受信後: {msg.Text?.Trim()}");
    return Task.FromResult(msg);
  }
}

