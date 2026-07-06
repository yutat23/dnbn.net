using System.Net;
using System.Net.Sockets;
using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.WebUI;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Tests;

/// <summary>
/// WebUIService の機能テスト（実際に Kestrel を起動して HTTP/SSE 経由で検証する）
/// </summary>
public class WebUIServiceTests
{
  /// <summary>OSに空きポートを割り当てさせて取得する</summary>
  private static int GetFreePort()
  {
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
  }

  private static WebUIService CreateService(int port, out ITcpClient client)
  {
    // 監視対象として最低1クライアントを登録（状態JSONに内容が含まれるように）
    var config = new ClientConfig
    {
      Name = "WebUITestClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 9999,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
    };
    client = new TcpClient(config, new MockTransport());

    return new WebUIService(
        Array.Empty<ITcpServer>(),
        new[] { client },
        new WebUIConfig
        {
          Enabled = true,
          Port = port,
          BindAddress = "localhost",
          UpdateIntervalSeconds = 1,
          EnableLogging = false,
        });
  }

  /// <summary>SSEストリームから次のイベント（"data: ..." 行）を1件読み取る</summary>
  private static async Task<string> ReadNextSseEventAsync(StreamReader reader, TimeSpan timeout)
  {
    using var cts = new CancellationTokenSource(timeout);
    while (true)
    {
      var line = await reader.ReadLineAsync(cts.Token) ??
          throw new IOException("SSEストリームが閉じられました");
      if (line.StartsWith("data: ", StringComparison.Ordinal))
      {
        return line;
      }
    }
  }

  [Fact]
  public async Task HealthAndStatusEndpoints_ReturnOk()
  {
    var port = GetFreePort();
    var service = CreateService(port, out var client);
    using var _ = client;
    using (service)
    {
      await service.StartAsync();
      using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

      var health = await http.GetAsync("/api/health");
      Assert.Equal(HttpStatusCode.OK, health.StatusCode);

      var status = await http.GetStringAsync("/api/status");
      Assert.Contains("WebUITestClient", status);

      await service.StopAsync();
    }
  }

  [Fact]
  public async Task SseConnection_KeepsReceivingUpdates_AfterAnotherConnectionCloses()
  {
    // 回帰テスト: SSE接続の管理にConcurrentBag.TryTake（任意の1件を除去）を使っていたため、
    // ある接続の切断時に生きている別の接続が管理外になり、更新が届かなくなることがあった。
    // 接続Aを切断しても接続Bが更新を受け続けることを検証する。
    var port = GetFreePort();
    var service = CreateService(port, out var client);
    using var _ = client;
    using (service)
    {
      await service.StartAsync();
      using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

      // 接続A・Bを確立し、両方が初期イベントを受信できることを確認
      using var ctsA = new CancellationTokenSource();
      var responseA = await http.GetAsync("/api/status/stream", HttpCompletionOption.ResponseHeadersRead, ctsA.Token);
      using var readerA = new StreamReader(await responseA.Content.ReadAsStreamAsync(), Encoding.UTF8);
      await ReadNextSseEventAsync(readerA, TimeSpan.FromSeconds(5));

      var responseB = await http.GetAsync("/api/status/stream", HttpCompletionOption.ResponseHeadersRead);
      using var readerB = new StreamReader(await responseB.Content.ReadAsStreamAsync(), Encoding.UTF8);
      await ReadNextSseEventAsync(readerB, TimeSpan.FromSeconds(5));

      // 接続Aを切断（クライアント側から中断）
      ctsA.Cancel();
      responseA.Dispose();

      // サーバー側がAの切断を処理する時間を与える（定期更新タイマーが1秒間隔で発火し、
      // 死んだ接続の整理もそこで行われる）
      await Task.Delay(1500);

      // 接続Bは切断後も更新イベントを受信し続けること
      for (int i = 0; i < 3; i++)
      {
        var data = await ReadNextSseEventAsync(readerB, TimeSpan.FromSeconds(5));
        Assert.StartsWith("data: ", data);
      }

      await service.StopAsync();
    }
  }
}
