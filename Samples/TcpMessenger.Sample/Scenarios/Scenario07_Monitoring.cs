using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Filters;
using Dnbn.Models;
using Dnbn.WebUI;
using Microsoft.Extensions.Logging;
using TcpClient = Dnbn.Core.TcpClient;

namespace TcpMessenger.Sample.Scenarios;

/// <summary>
/// シナリオ7: 運用監視
/// 実運用で必要になる仕組みを実演する。
///   - IMessageFilter によるチェックサムの自動付与・検証（送受信パイプラインへの割り込み）
///   - ConnectionInfo による統計情報の取得
///   - Web UI によるブラウザからのリアルタイム監視
/// </summary>
internal static class Scenario07_Monitoring
{
  private const int Port = 15207;
  private const int WebUIPort = 8085;

  public static async Task RunAsync(ILoggerFactory loggerFactory)
  {
    // --- チェックサムを検証するサーバーを起動 ---
    SampleConsole.Step("チェックサムを検証するサーバーを起動します");
    SampleConsole.Note("計測器プロトコルでよくある「ペイロード*XX」形式（XX = XORチェックサムの16進2桁）");

    var serverConfig = new ServerConfig
    {
      Name = "ChecksumServer",
      ListenPort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
    };
    await using var server = new TcpServer(serverConfig, loggerFactory.CreateLogger<TcpServer>());

    server.OnMessageReceivedAsync += async (message, sessionInfo, _) =>
    {
      try
      {
        var received = message.Text?.Trim() ?? "";
        if (XorChecksumFilter.TryParse(received, out var payload))
        {
          SampleConsole.Result($"サーバー: チェックサムOK '{received}' → ペイロード '{payload}'");
          await server.SendAsync(sessionInfo.SessionId, XorChecksumFilter.Append($"ACK:{payload}"));
        }
        else
        {
          SampleConsole.Result($"サーバー: チェックサム不正 '{received}'");
          await server.SendAsync(sessionInfo.SessionId, XorChecksumFilter.Append("NAK"));
        }
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };
    await server.StartAsync();

    // --- チェックサムフィルター付きクライアントを接続 ---
    SampleConsole.Step("チェックサムフィルターを組み込んだクライアントを接続します");
    SampleConsole.Note("アプリのコードは素のペイロードを扱うだけで、付与・検証はフィルターが自動で行う");

    var clientConfig = new ClientConfig
    {
      Name = "MonitoredClient",
      RemoteHost = "127.0.0.1",
      RemotePort = Port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = 3000,
    };
    await using var client = new TcpClient(
        clientConfig,
        new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort),
        loggerFactory.CreateLogger<TcpClient>(),
        filters: new[] { new XorChecksumFilter() });

    await client.ConnectAsync();

    // フィルターを通した送受信（アプリ側のコードにチェックサムは現れない）
    var response = await client.SendAsync("MEAS:TEMP");
    SampleConsole.Result($"クライアント: 応答ペイロード '{response.Text?.Trim()}'" +
        $"（チェックサム検証: {(Equals(response.Metadata.GetValueOrDefault("ChecksumValid"), true) ? "OK" : "未検証")}）");

    // --- 統計情報 ---
    SampleConsole.Step("ConnectionInfo で統計情報を取得します");

    // 統計を増やすために何往復か通信
    for (int i = 0; i < 3; i++)
    {
      await client.SendAsync($"DATA:{i}");
    }

    var clientInfo = client.ConnectionInfo;
    var serverInfo = server.ConnectionInfo;
    SampleConsole.Result($"クライアント: 送信={clientInfo.MessagesSent}件, 受信={clientInfo.MessagesReceived}件, " +
        $"接続時間={clientInfo.ConnectionDuration?.TotalSeconds:F1}秒, エラー={clientInfo.ErrorCount}件");
    SampleConsole.Result($"サーバー: 現在の接続数={serverInfo.ConnectionCount}, 累計接続数={serverInfo.TotalConnections}, " +
        $"送信={serverInfo.MessagesSent}件, 受信={serverInfo.MessagesReceived}件");

    // --- Web UI ---
    SampleConsole.Step($"Web UI を起動します（http://localhost:{WebUIPort}/）");

    WebUIService? webUI = null;
    try
    {
      webUI = new WebUIService(
          new ITcpServer[] { server },
          new ITcpClient[] { client },
          new WebUIConfig
          {
            Enabled = true,
            Port = WebUIPort,
            BindAddress = "localhost",
            UpdateIntervalSeconds = 1,
            EnableMessageHistory = true,
            MessageHistoryCapacity = 100,
            MessageHistoryMaxPayloadBytes = 256,
            AllowSendFromUI = true,
            SendAuthToken = "sample-token",
          },
          loggerFactory.CreateLogger<WebUIService>());
      await webUI.StartAsync();

      SampleConsole.Note("ブラウザで開くと接続状態・統計がリアルタイム表示される（SSE）");
      SampleConsole.Note("Web UIから送信する場合のトークン: sample-token");
      Console.WriteLine();
      Console.WriteLine("  EnterキーまたはCtrl-CでWeb UIを停止してシナリオを終了します...");

      // 確認しやすいよう、待っている間も定期的に通信を発生させる
      using var trafficCts = new CancellationTokenSource();
      var trafficTask = GenerateTrafficAsync(client, trafficCts.Token);

      using var exitCts = new CancellationTokenSource();
      ConsoleCancelEventHandler cancelHandler = (_, e) =>
      {
        // プロセスを即時終了させず、finallyでWebUI・TCP接続を正常停止する。
        e.Cancel = true;
        exitCts.Cancel();
      };
      Console.CancelKeyPress += cancelHandler;
      try
      {
        if (Console.IsInputRedirected)
        {
          await Console.In.ReadLineAsync();
        }
        else
        {
          while (!exitCts.IsCancellationRequested)
          {
            if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
            {
              break;
            }
            await Task.Delay(50, exitCts.Token);
          }
        }
      }
      catch (OperationCanceledException) when (exitCts.IsCancellationRequested)
      {
        // Ctrl-Cによる正常終了
      }
      finally
      {
        Console.CancelKeyPress -= cancelHandler;
      }
      trafficCts.Cancel();
      await trafficTask;
    }
    catch (Exception ex)
    {
      SampleConsole.Error($"Web UI の起動に失敗しました（ポート使用中の可能性）: {ex.Message}");
    }
    finally
    {
      if (webUI != null)
      {
        await webUI.StopAsync(CancellationToken.None);
        webUI.Dispose();
      }
    }

    // --- 後片付け ---
    SampleConsole.Step("切断してサーバーを停止します");
    await client.DisconnectAsync();
    await server.StopAsync();
  }

  /// <summary>Web UI 観察用に2秒間隔でダミー通信を発生させる</summary>
  private static async Task GenerateTrafficAsync(ITcpClient client, CancellationToken cancellationToken)
  {
    int sequence = 0;
    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        await Task.Delay(2000, cancellationToken);
        await client.SendAsync($"HEARTBEAT:{sequence++}", TimeSpan.FromSeconds(2), cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      // 正常終了
    }
    catch (Exception)
    {
      // デモ用トラフィックの失敗は無視
    }
  }
}

/// <summary>
/// XORチェックサムを送信時に付与し、受信時に検証・除去するメッセージフィルター。
/// 形式: 「ペイロード*XX」（XX = ペイロードのXORチェックサム16進2桁）
/// </summary>
internal sealed class XorChecksumFilter : IMessageFilter
{
  /// <summary>送信前: ペイロードにチェックサムを付与する</summary>
  public Task<Message> OnSendingAsync(Message msg, IMessageContext ctx)
  {
    var payload = msg.Text ?? "";
    return Task.FromResult(Message.FromString(Append(payload), Encoding.UTF8));
  }

  /// <summary>受信後: チェックサムを検証し、除去したペイロードに差し替える</summary>
  public Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx)
  {
    var received = msg.Text?.Trim() ?? "";
    if (TryParse(received, out var payload))
    {
      var stripped = Message.FromString(payload, Encoding.UTF8);
      stripped.Metadata["ChecksumValid"] = true;
      return Task.FromResult(stripped);
    }

    msg.Metadata["ChecksumValid"] = false;
    return Task.FromResult(msg);
  }

  /// <summary>ペイロードに「*XX」形式のチェックサムを付与する</summary>
  public static string Append(string payload)
      => $"{payload}*{Calculate(payload):X2}";

  /// <summary>「ペイロード*XX」を検証し、正しければペイロードを取り出す</summary>
  public static bool TryParse(string text, out string payload)
  {
    payload = "";
    var index = text.LastIndexOf('*');
    if (index < 0 || text.Length - index != 3)
    {
      return false;
    }

    var body = text[..index];
    var checksumPart = text[(index + 1)..];
    if (!byte.TryParse(checksumPart, System.Globalization.NumberStyles.HexNumber, null, out var checksum))
    {
      return false;
    }

    if (Calculate(body) != checksum)
    {
      return false;
    }

    payload = body;
    return true;
  }

  private static byte Calculate(string payload)
  {
    byte checksum = 0;
    foreach (var b in Encoding.UTF8.GetBytes(payload))
    {
      checksum ^= b;
    }
    return checksum;
  }
}
