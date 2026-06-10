using System.Collections.Concurrent;
using Dnbn.Configuration;
using Dnbn.Core;
using Microsoft.Extensions.Logging;
using TcpClient = Dnbn.Core.TcpClient;

namespace TcpMessenger.Sample.Scenarios;

/// <summary>
/// シナリオ6: リクエスト制御
/// 応答が遅い・返ってこない相手とのやり取りを制御する機能を実演する。
///   - SendAsync のタイムアウト
///   - RetryPolicy による自動再送（実行時のポリシー変更も実演）
///   - SendAndWaitAsync の述語マッチング（途中のイベント通知を読み飛ばす）
///   - NotificationPredicate による通知電文の自動振り分け
///   - SendOneWayAsync による応答を待たない通知電文の送信
///   - 複数リクエストのFIFOパイプライン処理
/// </summary>
internal static class Scenario06_RequestControl
{
  private const int Port = 15206;

  public static async Task RunAsync(ILoggerFactory loggerFactory)
  {
    // --- 気まぐれなサーバーを起動 ---
    SampleConsole.Step("挙動を制御できるテストサーバーを起動します");
    SampleConsole.Note("SLOW=応答しない / FLAKY=1回目は無視して2回目で応答 / CALC=イベント通知の後に結果を返す");

    var serverConfig = new ServerConfig
    {
      Name = "MoodyServer",
      ListenPort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
    };
    await using var server = new TcpServer(serverConfig, loggerFactory.CreateLogger<TcpServer>());

    var flakyAttempts = new ConcurrentDictionary<string, int>();
    server.OnMessageReceived += async (_, e) =>
    {
      try
      {
        var command = e.message.Text?.Trim() ?? "";
        var sessionId = e.sessionInfo.SessionId;

        switch (command)
        {
          case "SLOW":
            // 応答を返さない（クライアント側のタイムアウトを発生させる）
            break;

          case "FLAKY":
            // 1回目は無視、2回目以降は応答（リトライで成功するケースを再現）
            var attempt = flakyAttempts.AddOrUpdate(sessionId, 1, (_, n) => n + 1);
            if (attempt >= 2)
            {
              await server.SendAsync(sessionId, $"OK: FLAKY ({attempt}回目の要求で応答)");
            }
            break;

          case "CALC":
            // 結果の前に途中経過のイベント通知を送る（述語マッチングのデモ用）
            await server.SendAsync(sessionId, "EVENT: 計算を開始しました");
            await server.SendAsync(sessionId, "EVENT: 処理中...");
            await server.SendAsync(sessionId, "RESULT: 42");
            break;

          case var log when log.StartsWith("LOG:"):
            // 通知電文（応答を返さない）
            SampleConsole.Result($"サーバー: 通知電文を受信（応答なし）: {log}");
            break;

          default:
            await server.SendAsync(sessionId, $"OK: {command}");
            break;
        }
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };

    await server.StartAsync();

    // --- クライアントを接続 ---
    var clientConfig = new ClientConfig
    {
      Name = "ControlClient",
      RemoteHost = "127.0.0.1",
      RemotePort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = 2000,
    };
    await using var client = new TcpClient(
        clientConfig,
        new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort),
        loggerFactory.CreateLogger<TcpClient>());

    // 応答にマッチしなかった受信メッセージ（イベント通知など）はここに届く
    client.OnMessageReceived += (_, msg) =>
        SampleConsole.Result($"【プッシュ受信】{msg.Text?.Trim()}");

    await client.ConnectAsync();

    // --- タイムアウト ---
    SampleConsole.Step("応答が返らないコマンドを送り、タイムアウトを発生させます（タイムアウト1秒）");
    try
    {
      await client.SendAsync("SLOW", TimeSpan.FromSeconds(1));
    }
    catch (TimeoutException ex)
    {
      SampleConsole.Result($"TimeoutException を捕捉: {ex.Message}");
    }

    // --- リトライによる自動再送 ---
    SampleConsole.Step("リトライポリシーを実行時に設定し、1回目失敗→再送で成功するケースを実演します");
    SampleConsole.Note("RetryPolicy プロパティは接続中でも変更できる");

    client.RetryPolicy = new RetryPolicy
    {
      MaxRetryCount = 2,
      RetryDelayStrategy = RetryDelayStrategy.Fixed,
      InitialDelayMs = 200,
      FailOnTimeout = true,
    };

    var flakyResponse = await client.SendAsync("FLAKY", TimeSpan.FromSeconds(1));
    SampleConsole.Result($"応答: {flakyResponse.Text?.Trim()}");

    client.RetryPolicy = null; // 以降のデモに影響しないよう解除

    // --- 述語マッチング ---
    SampleConsole.Step("SendAndWaitAsync で「RESULT: で始まる応答」だけを待ちます");
    SampleConsole.Note("途中の EVENT 通知は応答とみなされず、OnMessageReceived に流れる");

    var result = await client.SendAndWaitAsync(
        "CALC",
        msg => msg.Text?.StartsWith("RESULT:") == true,
        TimeSpan.FromSeconds(5));
    SampleConsole.Result($"述語にマッチした応答: {result.Text?.Trim()}");

    // --- 通知電文（NotificationPredicate + SendOneWayAsync） ---
    SampleConsole.Step("通知電文: 受信は NotificationPredicate で自動振り分け、送信は SendOneWayAsync");
    SampleConsole.Note("EVENT: で始まる受信を通知と判定させると、素の SendAsync でも応答と取り違えない");

    client.NotificationPredicate = msg => msg.Text?.StartsWith("EVENT:") == true;

    // SendAndWaitAsync の述語なしでも、通知判定があるので RESULT が正しく応答になる
    var calcResult = await client.SendAsync("CALC", TimeSpan.FromSeconds(5));
    SampleConsole.Result($"SendAsync の応答: {calcResult.Text?.Trim()}");

    // 応答を待たない通知電文の送信（戻りのTaskはソケット書き込み完了で完了する）
    await client.SendOneWayAsync("LOG: クライアント側の定期報告");
    SampleConsole.Result("SendOneWayAsync 完了（応答を待たずに戻った）");

    client.NotificationPredicate = null; // 以降のデモに影響しないよう解除

    // --- FIFOパイプライン ---
    SampleConsole.Step("3つのリクエストを応答を待たずに連続発行します（FIFO順で応答が対応付く）");

    var task1 = client.SendAsync("リクエスト1");
    var task2 = client.SendAsync("リクエスト2");
    var task3 = client.SendAsync("リクエスト3");
    var responses = await Task.WhenAll(task1, task2, task3);

    for (int i = 0; i < responses.Length; i++)
    {
      SampleConsole.Result($"リクエスト{i + 1} の応答: {responses[i].Text?.Trim()}");
    }

    // --- 後片付け ---
    SampleConsole.Step("切断してサーバーを停止します");
    await client.DisconnectAsync();
    await server.StopAsync();
  }
}
