using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TcpMessenger.Sample.Scenarios;

/// <summary>
/// シナリオ8: 対話プレイグラウンド
/// appsettings.json + DIコンテナ（AddDnbnNet）でサーバーとクライアントを構成し、
/// 自由にメッセージを打って動作を確かめられる対話モード。
/// コマンド:
///   /status        現在の接続状態・統計を表示
///   /ka on|off     KeepAlive の有効/無効を切り替え
///   /timeout 3000  応答タイムアウト（ミリ秒）を変更
///   /quit          終了
///   それ以外       そのままサーバーへ送信して応答を表示
/// </summary>
internal static class Scenario08_Playground
{
  public static async Task RunAsync(ILoggerFactory loggerFactory)
  {
    // --- appsettings.json から構成を読み込み、DIコンテナに登録 ---
    SampleConsole.Step("appsettings.json から構成を読み込んで DI コンテナを構築します");

    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .Build();

    var services = new ServiceCollection();
    services.AddSingleton(loggerFactory);
    services.AddLogging();
    services.AddDnbnNet(configuration); // 設定セクション "dnbn.net" を読み込む

    await using var serviceProvider = services.BuildServiceProvider();
    var factory = serviceProvider.GetRequiredService<ITcpMessengerFactory>();

    // --- サーバーとクライアントを起動 ---
    SampleConsole.Step("エコーサーバーとクライアントを起動します");

    var server = factory.CreateServer("PlaygroundServer");
    server.OnMessageReceived += async (_, e) =>
    {
      try
      {
        await server.SendAsync(e.sessionInfo.SessionId, $"ECHO: {e.message.Text?.Trim()}");
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };
    await server.StartAsync();

    var client = factory.CreateClient("PlaygroundClient");
    client.OnConnected += (_, _) => SampleConsole.Result("【イベント】接続しました");
    client.OnDisconnected += (_, _) => SampleConsole.Result("【イベント】切断されました");
    client.OnMessageReceived += (_, msg) => SampleConsole.Result($"【プッシュ受信】{msg.Text?.Trim()}");
    client.OnKeepAliveResponseReceived += (_, msg) => SampleConsole.Result($"【KeepAlive応答】{msg.Text?.Trim()}");

    await client.ConnectAsync();

    // --- 対話ループ ---
    Console.WriteLine();
    Console.WriteLine("メッセージを入力してください。コマンド: /status, /ka on|off, /timeout <ms>, /quit");

    try
    {
      while (true)
      {
        Console.Write("> ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
        {
          continue;
        }

        if (input.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
          break;
        }

        if (input.StartsWith('/'))
        {
          HandleCommand(client, input);
          continue;
        }

        try
        {
          var response = await client.SendAsync(input);
          SampleConsole.Result($"応答: {response.Text?.Trim()}");
        }
        catch (TimeoutException)
        {
          SampleConsole.Error("応答がタイムアウトしました");
        }
        catch (Exception ex)
        {
          SampleConsole.Error($"送信エラー: {ex.Message}");
        }
      }
    }
    finally
    {
      // --- 後片付け ---
      SampleConsole.Step("切断してサーバーを停止します");
      await client.DisconnectAsync();
      await server.StopAsync();
      client.Dispose();
      server.Dispose();
    }
  }

  /// <summary>「/」で始まるコマンドを処理する（実行時の設定変更デモ）</summary>
  private static void HandleCommand(ITcpClient client, string input)
  {
    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    switch (parts[0].ToLowerInvariant())
    {
      case "/status":
        var info = client.ConnectionInfo;
        SampleConsole.Result($"接続: {(info.IsConnected ? "接続中" : "切断")} (再接続中: {info.IsReconnecting})");
        SampleConsole.Result($"送信: {info.MessagesSent}件 / 受信: {info.MessagesReceived}件 / 保留中: {info.PendingRequests}件");
        SampleConsole.Result($"タイムアウト設定: {client.TimeoutMilliseconds}ms, KeepAlive: {(client.KeepAlive?.Enabled == true ? "有効" : "無効")}");
        SampleConsole.Result($"エラー: {info.ErrorCount}件 (最終: {info.LastError ?? "なし"})");
        break;

      case "/ka" when parts.Length >= 2:
        // KeepAlive プロパティは接続中でも変更でき、即座に反映される
        client.KeepAlive = parts[1].Equals("on", StringComparison.OrdinalIgnoreCase)
            ? new KeepAliveConfig { Enabled = true, IntervalSeconds = 5, Message = "PING" }
            : null;
        SampleConsole.Result($"KeepAlive を{(client.KeepAlive != null ? "有効化（5秒間隔, PING）" : "無効化")}しました");
        break;

      case "/timeout" when parts.Length >= 2 && int.TryParse(parts[1], out var ms) && ms > 0:
        client.TimeoutMilliseconds = ms;
        SampleConsole.Result($"タイムアウトを {ms}ms に変更しました");
        break;

      default:
        Console.WriteLine("コマンド: /status, /ka on|off, /timeout <ms>, /quit");
        break;
    }
  }
}
