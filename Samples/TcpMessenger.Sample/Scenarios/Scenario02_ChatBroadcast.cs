using System.Reactive.Linq;
using Dnbn.Configuration;
using Dnbn.Core;
using Microsoft.Extensions.Logging;
using TcpClient = Dnbn.Core.TcpClient;

namespace TcpMessenger.Sample.Scenarios;

/// <summary>
/// シナリオ2: チャット／ブロードキャスト
/// 複数のクライアントが接続し、サーバーが発言を全員に配信する。
/// dnbn.net の2つの受信経路の使い分けを示す:
///   - SendAsync の戻り値      … 自分のリクエストに対する応答
///   - OnMessageReceived       … サーバーからのプッシュ（他人の発言など）
///   - MessageReceived (Rx)    … 条件付き購読（ALERTメッセージのみ等）
/// </summary>
internal static class Scenario02_ChatBroadcast
{
  private const int Port = 15202;

  public static async Task RunAsync(ILoggerFactory loggerFactory)
  {
    // --- チャットサーバーを起動 ---
    SampleConsole.Step("チャットサーバーを起動します（受信した発言を全員にブロードキャスト）");

    var serverConfig = new ServerConfig
    {
      Name = "ChatServer",
      ListenPort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
    };
    await using var server = new TcpServer(serverConfig, loggerFactory.CreateLogger<TcpServer>());

    server.OnMessageReceivedAsync += async (message, sessionInfo, _) =>
    {
      try
      {
        // 発言者のセッションIDの先頭部分を名前代わりに付けて全員へ配信
        var shortId = sessionInfo.SessionId.Split('-')[0];
        await server.BroadcastAsync($"[{shortId}] {message.Text?.Trim()}");
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"ブロードキャストに失敗: {ex.Message}");
      }
    };

    await server.StartAsync();

    // --- 3人のクライアントを接続 ---
    SampleConsole.Step("3人のクライアント（Alice / Bob / Carol）を接続します");

    await using var alice = CreateChatClient("Alice", loggerFactory);
    await using var bob = CreateChatClient("Bob", loggerFactory);
    await using var carol = CreateChatClient("Carol", loggerFactory);

    // Bob と Carol はプッシュ受信（OnMessageReceived）で発言を表示
    bob.OnMessageReceived += (_, msg) => SampleConsole.Result($"Bob が受信: {msg.Text?.Trim()}");
    carol.OnMessageReceived += (_, msg) => SampleConsole.Result($"Carol が受信: {msg.Text?.Trim()}");

    // Carol はさらに Rx で「ALERT を含む発言だけ」を購読
    using var alertSubscription = carol.MessageReceived
        .Where(msg => msg.Text?.Contains("ALERT") == true)
        .Subscribe(msg => SampleConsole.Result($"★ Carol のアラート購読がヒット: {msg.Text?.Trim()}"));

    await alice.ConnectAsync();
    await bob.ConnectAsync();
    await carol.ConnectAsync();
    SampleConsole.Result($"接続中のセッション数: {server.GetAllSessions().Count()}");

    // --- 発言 ---
    SampleConsole.Step("Alice が発言します");
    SampleConsole.Note("ブロードキャストは発言者自身にも届くため、Alice の SendAsync には自分の発言が応答として返る");

    var echoed = await alice.SendAsync("おはようございます");
    SampleConsole.Result($"Alice の SendAsync の戻り値: {echoed.Text?.Trim()}");

    await Task.Delay(200); // Bob / Carol への配信を待つ

    SampleConsole.Step("Alice が ALERT メッセージを発言します（Carol の Rx 購読だけが追加で反応）");
    await alice.SendAsync("ALERT: 温度が閾値を超えました");
    await Task.Delay(200);

    // --- サーバー主導のお知らせ配信 ---
    SampleConsole.Step("サーバーから全員へお知らせをプッシュ配信します");
    SampleConsole.Note("クライアント側はリクエストを送っていないので OnMessageReceived で受信する");

    await server.BroadcastAsync("【お知らせ】まもなくメンテナンスを開始します");
    await Task.Delay(200);

    // --- 後片付け ---
    SampleConsole.Step("全員切断してサーバーを停止します");
    await alice.DisconnectAsync();
    await bob.DisconnectAsync();
    await carol.DisconnectAsync();
    await server.StopAsync();
  }

  private static TcpClient CreateChatClient(string name, ILoggerFactory loggerFactory)
  {
    var config = new ClientConfig
    {
      Name = name,
      RemoteHost = "127.0.0.1",
      RemotePort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = 5000,
    };
    return new TcpClient(
        config,
        new TcpTransport(config.RemoteHost, config.RemotePort),
        loggerFactory.CreateLogger<TcpClient>());
  }
}
