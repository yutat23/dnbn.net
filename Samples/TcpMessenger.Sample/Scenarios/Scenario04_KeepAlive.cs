using Dnbn.Configuration;
using Dnbn.Core;
using Microsoft.Extensions.Logging;
using TcpClient = Dnbn.Core.TcpClient;

namespace TcpMessenger.Sample.Scenarios;

/// <summary>
/// シナリオ4: KeepAliveと死活監視
/// 一定間隔で死活確認メッセージを送り、相手の生存を監視する。
///   - KeepAliveConfig（間隔・メッセージ・応答判定）
///   - ResponsePredicate による「KeepAlive応答」と「通常メッセージ」の区別
///   - 無応答時の KeepAliveTimeoutCount による検出
/// </summary>
internal static class Scenario04_KeepAlive
{
  private const int Port = 15204;

  // サーバーがPINGに応答するかどうか（無応答状態をシミュレートするためのスイッチ）
  private static volatile bool _respondToPing = true;

  public static async Task RunAsync(ILoggerFactory loggerFactory)
  {
    // --- 機器シミュレータサーバーを起動 ---
    SampleConsole.Step("機器シミュレータサーバーを起動します（PING には PONG を返す）");

    var serverConfig = new ServerConfig
    {
      Name = "DeviceSimulator",
      ListenPort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
    };
    await using var server = new TcpServer(serverConfig, loggerFactory.CreateLogger<TcpServer>());

    _respondToPing = true;
    server.OnMessageReceived += async (_, e) =>
    {
      try
      {
        var text = e.message.Text?.Trim();
        if (text == "PING")
        {
          if (_respondToPing)
          {
            await server.SendAsync(e.sessionInfo.SessionId, "PONG");
          }
          // 無応答モードのときは PING を無視する（機器ハング状態のシミュレート）
        }
        else
        {
          await server.SendAsync(e.sessionInfo.SessionId, $"STATUS:OK ({text})");
        }
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };

    await server.StartAsync();

    // --- KeepAlive付きクライアントを接続 ---
    SampleConsole.Step("KeepAlive を有効にしたクライアントを接続します（2秒間隔で PING を送信）");
    SampleConsole.Note("ResponsePredicate で「PONG だけが KeepAlive 応答」と判定させる。");
    SampleConsole.Note("これにより KeepAlive と通常のリクエスト応答が混在しても取り違えが起きない。");

    var clientConfig = new ClientConfig
    {
      Name = "MonitoringClient",
      RemoteHost = "127.0.0.1",
      RemotePort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = 3000,
      KeepAlive = new KeepAliveConfig
      {
        Enabled = true,
        IntervalSeconds = 2,
        Message = "PING",
        DisconnectOnTimeout = true,
        ResponsePredicate = msg => msg.Text?.Trim() == "PONG",
      },
    };
    await using var client = new TcpClient(
        clientConfig,
        new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort),
        loggerFactory.CreateLogger<TcpClient>());

    client.OnKeepAliveResponseReceived += (_, msg) =>
        SampleConsole.Result($"KeepAlive応答を受信: {msg.Text?.Trim()}");

    await client.ConnectAsync();

    // --- 正常時のKeepAliveを観察 ---
    SampleConsole.Step("正常時の KeepAlive の様子を5秒間観察します");
    await Task.Delay(5000);

    // --- KeepAlive稼働中に通常リクエストを送る ---
    SampleConsole.Step("KeepAlive 稼働中に通常のリクエストを送信します");
    var response = await client.SendAsync("GET_STATUS");
    SampleConsole.Result($"通常リクエストの応答: {response.Text?.Trim()}");
    SampleConsole.Note("PONG 以外の応答は KeepAlive に消費されず、正しくリクエストに返る");

    // --- 機器の無応答をシミュレート ---
    SampleConsole.Step("機器を無応答状態にします（PING を無視させて7秒間観察）");
    _respondToPing = false;

    await Task.Delay(7000);

    var info = client.ConnectionInfo;
    SampleConsole.Result($"KeepAliveタイムアウト回数: {info.KeepAliveTimeoutCount}");
    SampleConsole.Result($"最後にKeepAlive応答を受信した時刻: {info.LastKeepAliveResponseReceivedAt:HH:mm:ss}");
    SampleConsole.Note("タイムアウト回数の増加を監視すれば、TCP接続が生きたまま相手がハングした状態を検出できる");

    // --- 後片付け ---
    SampleConsole.Step("切断してサーバーを停止します");
    await client.DisconnectAsync();
    await server.StopAsync();
  }
}
