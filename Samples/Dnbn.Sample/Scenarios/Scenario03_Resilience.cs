using Dnbn.Configuration;
using Dnbn.Core;
using Microsoft.Extensions.Logging;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Sample.Scenarios;

/// <summary>
/// シナリオ3: 障害と自動再接続
/// 通信中にサーバーが停止しても、クライアントが自動でリトライし続け、
/// サーバー復旧後に勝手に復帰することを実演する。
///   - ConnectionRetryPolicy（MaxRetryCount = -1 で無限リトライ）
///   - ConnectionInfo.IsReconnecting による再接続状態の監視
///   - InterruptReconnectDelay によるバックオフ待機のスキップ
///   - WaitForConnectionAsync による接続復帰待ち
/// </summary>
internal static class Scenario03_Resilience
{
  private const int Port = 15203;

  public static async Task RunAsync(ILoggerFactory loggerFactory)
  {
    // --- サーバーとクライアントを準備 ---
    SampleConsole.Step("エコーサーバーを起動し、無限リトライ設定のクライアントを接続します");

    await using var server = CreateEchoServer(loggerFactory);
    await server.StartAsync();

    var clientConfig = new ClientConfig
    {
      Name = "ResilientClient",
      RemoteHost = "127.0.0.1",
      RemotePort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = 3000,
      // NW障害時に自動再接続するためのポリシー（-1 = 無限リトライ）
      ConnectionRetryPolicy = new RetryPolicy
      {
        MaxRetryCount = -1,
        RetryDelayStrategy = RetryDelayStrategy.Fixed,
        InitialDelayMs = 2000,
      },
    };
    await using var client = new TcpClient(
        clientConfig,
        new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort),
        loggerFactory.CreateLogger<TcpClient>());

    client.OnConnected += (_, _) => SampleConsole.Result("【イベント】接続しました");
    client.OnDisconnected += (_, _) => SampleConsole.Result("【イベント】切断されました");

    await client.ConnectAsync();

    var response = await client.SendAsync("障害発生前のメッセージ");
    SampleConsole.Result($"応答: {response.Text?.Trim()}");

    // --- サーバーを停止して障害をシミュレート ---
    SampleConsole.Step("サーバーを停止します（ネットワーク障害をシミュレート）");
    await server.StopAsync();

    // クライアントが切断を検知して再接続リトライに入るのを観察
    SampleConsole.Note("クライアントは2秒間隔で接続をリトライし続ける");
    for (int i = 0; i < 3; i++)
    {
      await Task.Delay(1500);
      var info = client.ConnectionInfo;
      SampleConsole.Result(
          $"接続状態: IsConnected={info.IsConnected}, IsReconnecting={info.IsReconnecting}, " +
          $"リトライ回数={info.ConnectionRetryAttempts}");
    }

    // --- サーバーを復旧 ---
    SampleConsole.Step("サーバーを再起動します（障害復旧）");
    await server.StartAsync();

    // バックオフ待機をスキップして即座に再接続を試行させる
    SampleConsole.Note("InterruptReconnectDelay で次のリトライ待ち時間をスキップできる");
    client.InterruptReconnectDelay();

    // 接続が復帰するまで待つ
    await client.WaitForConnectionAsync(TimeSpan.FromSeconds(15));
    SampleConsole.Result("接続が復帰しました");

    // --- 復帰後の通信確認 ---
    SampleConsole.Step("復帰後に通信できることを確認します");
    var afterResponse = await client.SendAsync("障害復旧後のメッセージ");
    SampleConsole.Result($"応答: {afterResponse.Text?.Trim()}");

    // --- 後片付け ---
    SampleConsole.Step("切断してサーバーを停止します");
    await client.DisconnectAsync();
    await server.StopAsync();
  }

  private static TcpServer CreateEchoServer(ILoggerFactory loggerFactory)
  {
    var config = new ServerConfig
    {
      Name = "EchoServer",
      ListenPort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
    };
    var server = new TcpServer(config, loggerFactory.CreateLogger<TcpServer>());
    server.OnMessageReceivedAsync += async (message, sessionInfo, _) =>
    {
      try
      {
        await server.SendAsync(sessionInfo.SessionId, $"ECHO: {message.Text?.Trim()}");
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };
    return server;
  }
}
