using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.WebUI;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
      => CreateService(port, out client, out _, null);

  private static WebUIService CreateService(
      int port,
      out ITcpClient client,
      out MockTransport transport,
      Action<WebUIConfig>? configure)
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
    transport = new MockTransport();
    client = new TcpClient(config, transport);

    var webUIConfig = new WebUIConfig
    {
      Enabled = true,
      Port = port,
      BindAddress = "localhost",
      UpdateIntervalSeconds = 1,
      EnableLogging = false,
    };
    configure?.Invoke(webUIConfig);

    return new WebUIService(
        Array.Empty<ITcpServer>(),
        new[] { client },
        webUIConfig);
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
    using var clientScope = client;
    using (service)
    {
      await service.StartAsync();
      using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

      var health = await http.GetAsync("/api/health");
      Assert.Equal(HttpStatusCode.OK, health.StatusCode);

      var status = await http.GetStringAsync("/api/status");
      Assert.Contains("WebUITestClient", status);

      var html = await http.GetStringAsync("/");
      Assert.Contains("timelineSourceFilter", html);
      Assert.Contains("messageSourceFilter", html);

      var javascript = await http.GetStringAsync("/js/app.js");
      Assert.Contains("buildModalMonitoringHtml", javascript);
      Assert.Contains("loadActiveModalMonitoring", javascript);

      await service.StopAsync();
    }
  }

  [Fact]
  public async Task EmbeddedWebUI_DoesNotInstallConsoleLifetime()
  {
    var port = GetFreePort();
    var service = CreateService(port, out var client);
    using var clientScope = client;
    using (service)
    {
      await service.StartAsync();

      var appField = typeof(WebUIService).GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic);
      var app = Assert.IsType<WebApplication>(appField?.GetValue(service));
      var hostLifetime = app.Services.GetRequiredService<IHostLifetime>();

      Assert.Equal("EmbeddedWebUIHostLifetime", hostLifetime.GetType().Name);
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
    using var clientScope = client;
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

  [Fact]
  public async Task Timeline_RecordsConnectionLifecycle_WithinCapacity()
  {
    var port = GetFreePort();
    var service = CreateService(port, out var client, out _,
        config => config.EventTimelineCapacity = 2);
    using var clientScope = client;
    using (service)
    {
      await service.StartAsync();
      await client.ConnectAsync();
      await client.DisconnectAsync();

      using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
      using var json = JsonDocument.Parse(await http.GetStringAsync("/api/timeline"));
      var events = json.RootElement.GetProperty("events").EnumerateArray().ToArray();

      Assert.Equal(2, events.Length);
      Assert.Contains(events, entry => entry.GetProperty("type").GetString() == "Disconnected");

      await service.StopAsync();
    }
  }

  [Fact]
  public async Task Timeline_SeedsClientConnectedBeforeWebUIStart_AndFiltersByTypeAndName()
  {
    var port = GetFreePort();
    var service = CreateService(port, out var client);
    using var clientScope = client;
    using (service)
    {
      // 実運用とサンプル同様、クライアント接続後にWebUIを起動する。
      await client.ConnectAsync();
      var connectedAt = client.ConnectionInfo.ConnectedAt;
      await service.StartAsync();

      using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
      using var clientJson = JsonDocument.Parse(await http.GetStringAsync(
          "/api/timeline?source=WebUITestClient&sourceType=Client"));
      var connected = clientJson.RootElement.GetProperty("events").EnumerateArray().Single();

      Assert.Equal("Client", connected.GetProperty("sourceType").GetString());
      Assert.Equal("Connected", connected.GetProperty("type").GetString());
      Assert.Contains("monitoring started", connected.GetProperty("detail").GetString());
      Assert.Equal(connectedAt, connected.GetProperty("timestamp").GetDateTime());

      using var wrongTypeJson = JsonDocument.Parse(await http.GetStringAsync(
          "/api/timeline?source=WebUITestClient&sourceType=Server"));
      Assert.Empty(wrongTypeJson.RootElement.GetProperty("events").EnumerateArray().ToArray());

      await service.StopAsync();
    }
  }

  [Fact]
  public async Task MessageHistoryAndAnalytics_RecordBoundedTruncatedRequestResponse()
  {
    var port = GetFreePort();
    var service = CreateService(port, out var client, out var transport, config =>
    {
      config.EnableMessageHistory = true;
      config.MessageHistoryCapacity = 3;
      config.MessageHistoryMaxPayloadBytes = 4;
    });
    using var clientScope = client;
    using (service)
    {
      await service.StartAsync();
      await client.ConnectAsync();

      var request = client.SendAsync("PING");
      await TestWait.UntilSentAsync(transport, "PING");
      transport.EnqueueReceiveData("PONG");
      await request;
      await client.SendOneWayAsync("TOO-LONG");

      using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
      using var messageJson = JsonDocument.Parse(await http.GetStringAsync("/api/messages"));
      Assert.True(messageJson.RootElement.GetProperty("enabled").GetBoolean());
      var messages = messageJson.RootElement.GetProperty("messages").EnumerateArray().ToArray();
      Assert.Equal(3, messages.Length);
      Assert.All(messages, entry => Assert.True(entry.GetProperty("hex").GetString()!.Length <= 8));
      Assert.All(messages, entry => Assert.Equal("Client", entry.GetProperty("sourceType").GetString()));
      Assert.Contains(messages, entry =>
          entry.GetProperty("kind").GetString() == "OneWay" &&
          entry.GetProperty("sizeBytes").GetInt32() == Encoding.UTF8.GetByteCount("TOO-LONG\n"));

      using var analyticsJson = JsonDocument.Parse(await http.GetStringAsync("/api/analytics"));
      var analytics = analyticsJson.RootElement.GetProperty("clients").EnumerateArray().Single();
      Assert.Equal("WebUITestClient", analytics.GetProperty("name").GetString());
      Assert.Equal(1, analytics.GetProperty("responseCount").GetInt32());
      Assert.True(analytics.GetProperty("p95Ms").GetDouble() >= 0);

      using var filteredJson = JsonDocument.Parse(await http.GetStringAsync(
          "/api/messages?source=WebUITestClient&sourceType=Server"));
      Assert.Empty(filteredJson.RootElement.GetProperty("messages").EnumerateArray().ToArray());

      await service.StopAsync();
    }
  }

  [Fact]
  public async Task SendEndpoint_IsDisabledByDefault()
  {
    var port = GetFreePort();
    var service = CreateService(port, out var client);
    using var clientScope = client;
    using (service)
    {
      await service.StartAsync();
      using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

      var response = await http.PostAsJsonAsync("/api/send", new
      {
        client = "WebUITestClient",
        text = "NOTICE",
        oneWay = true,
      });

      Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
      await service.StopAsync();
    }
  }

  [Fact]
  public async Task SendEndpoint_RequiresToken_AndUsesClientSendQueue()
  {
    var port = GetFreePort();
    var service = CreateService(port, out var client, out var transport, config =>
    {
      config.AllowSendFromUI = true;
      config.SendAuthToken = "test-secret";
    });
    using var clientScope = client;
    using (service)
    {
      await service.StartAsync();
      await client.ConnectAsync();
      using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
      var request = new { client = "WebUITestClient", text = "NOTICE", oneWay = true };

      var unauthorized = await http.PostAsJsonAsync("/api/send", request);
      Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

      using var authorizedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/send")
      {
        Content = JsonContent.Create(request),
      };
      authorizedRequest.Headers.Add("X-Dnbn-Send-Token", "test-secret");
      var authorized = await http.SendAsync(authorizedRequest);

      Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
      Assert.Contains(transport.SentData,
          data => Encoding.UTF8.GetString(data) == "NOTICE\n");

      await service.StopAsync();
    }
  }
}
