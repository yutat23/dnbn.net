using Dnbn.Core;
using Dnbn.Extensions;
using Dnbn.Filters;
using Dnbn.Logging;
using Dnbn.Models;
using log4net;
using log4net.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

