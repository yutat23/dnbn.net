using Dnbn.Configuration;
using Dnbn.Core;
using Microsoft.Extensions.Logging;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Sample.Scenarios;

/// <summary>
/// シナリオ1: クイックスタート
/// サーバーとクライアントを同一プロセスで起動し、最小構成でメッセージを往復させる。
/// dnbn.net を使い始めるときに最初に読むコード。
/// （DIコンテナを使う構成例はシナリオ8のプレイグラウンドを参照）
/// </summary>
internal static class Scenario01_QuickStart
{
  private const int Port = 15201;

  public static async Task RunAsync(ILoggerFactory loggerFactory)
  {
    // --- サーバーを起動 ---
    SampleConsole.Step("エコーサーバーを起動します");

    var serverConfig = new ServerConfig
    {
      Name = "EchoServer",
      ListenPort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n", // 改行を1メッセージの区切りとする
    };
    await using var server = new TcpServer(serverConfig, loggerFactory.CreateLogger<TcpServer>());

    // 受信したメッセージをそのまま「ECHO:」付きで送り返す
    server.OnMessageReceivedAsync += async (message, sessionInfo, _) =>
    {
      try
      {
        await server.SendAsync(sessionInfo.SessionId, $"ECHO: {message.Text?.Trim()}");
      }
      catch (Exception ex)
      {
        // イベントハンドラ内の例外はライブラリの動作に影響するため必ず握りつぶす
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };

    await server.StartAsync();
    SampleConsole.Result($"ポート {Port} で待ち受け開始");

    // --- クライアントを接続 ---
    SampleConsole.Step("クライアントを接続します");

    var clientConfig = new ClientConfig
    {
      Name = "EchoClient",
      RemoteHost = "127.0.0.1",
      RemotePort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = 5000,
    };
    await using var client = new TcpClient(
        clientConfig,
        new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort),
        loggerFactory.CreateLogger<TcpClient>());

    await client.ConnectAsync();

    // --- リクエスト／レスポンス ---
    SampleConsole.Step("メッセージを送信して応答を待ちます");
    SampleConsole.Note("SendAsync は HTTP クライアントのように「応答が返るまで待つ」呼び出し");

    var response = await client.SendAsync("こんにちは");
    SampleConsole.Result($"送信: こんにちは → 応答: {response.Text?.Trim()}");

    var response2 = await client.SendAsync("dnbn.net");
    SampleConsole.Result($"送信: dnbn.net → 応答: {response2.Text?.Trim()}");

    // --- 後片付け ---
    SampleConsole.Step("切断してサーバーを停止します");
    await client.DisconnectAsync();
    await server.StopAsync();
  }
}
