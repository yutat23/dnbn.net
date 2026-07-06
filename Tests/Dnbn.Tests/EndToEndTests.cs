using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using Encoding = System.Text.Encoding;

namespace Dnbn.Tests;

/// <summary>
/// 実際の TcpClient（TcpTransport）と TcpServer を接続するエンドツーエンドの機能テスト
/// </summary>
public class EndToEndTests
{
  // 他のテストクラス（15000-, 16000-）と衝突しないレンジを使用
  private static int _portCounter = 17000;
  private static int NextPort() => Interlocked.Increment(ref _portCounter);

  private static TcpServer CreateServer(int port)
  {
    return new TcpServer(new ServerConfig
    {
      Name = "E2EServer",
      ListenPort = port,
      Encoding = "UTF-8",
      MessageTerminator = "\n"
    });
  }

  private static TcpClient CreateClient(int port, int timeoutMs = 5000)
  {
    var config = new ClientConfig
    {
      Name = "E2EClient",
      RemoteHost = "127.0.0.1",
      RemotePort = port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = timeoutMs
    };
    return new TcpClient(config, new TcpTransport("127.0.0.1", port));
  }

  /// <summary>受信メッセージを "echo:" 付きで送り返すエコーサーバーを構成する</summary>
  private static void SetupEchoServer(TcpServer server)
  {
    server.OnMessageReceived += async (_, e) =>
    {
      try
      {
        await server.SendAsync(e.sessionInfo.SessionId, $"echo:{e.message.Text?.Trim()}");
      }
      catch (InvalidOperationException)
      {
        // セッションが既に切断された場合は無視
      }
    };
  }

  [Fact]
  public async Task RequestResponse_RoundTrip_Works()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    SetupEchoServer(server);
    await server.StartAsync();

    await using var client = CreateClient(port);
    await client.ConnectAsync();

    var response = await client.SendAsync("hello");

    Assert.Equal("echo:hello", response.Text?.Trim());

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  [Fact]
  public async Task MultipleSequentialRequests_AllReceiveCorrectResponses()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    SetupEchoServer(server);
    await server.StartAsync();

    await using var client = CreateClient(port);
    await client.ConnectAsync();

    for (int i = 0; i < 10; i++)
    {
      var response = await client.SendAsync($"msg{i}");
      Assert.Equal($"echo:msg{i}", response.Text?.Trim());
    }

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  [Fact]
  public async Task ServerPush_ClientReceivesUnsolicitedMessage()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    await using var client = CreateClient(port);
    var pushTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, msg) => pushTcs.TrySetResult(msg.Text?.Trim() ?? "");

    var sessionTcs = new TaskCompletionSource<SessionInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
    server.OnClientConnected += (_, s) => sessionTcs.TrySetResult(s);

    await client.ConnectAsync();
    var session = await sessionTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

    // サーバーからの一方的なプッシュがクライアントの通常受信イベントに届くこと
    await server.SendAsync(session.SessionId, "push_from_server");

    Assert.Equal("push_from_server", await pushTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  [Fact]
  public async Task ClientReconnect_AgainstRealServer_Works()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    SetupEchoServer(server);
    await server.StartAsync();

    await using var client = CreateClient(port);

    // 接続 → 通信 → 切断 → 再接続 → 通信のサイクルが機能すること
    await client.ConnectAsync();
    var r1 = await client.SendAsync("first");
    Assert.Equal("echo:first", r1.Text?.Trim());

    await client.DisconnectAsync();
    Assert.False(client.IsConnected);

    // DisconnectAsync は受信ループの完了を待つため、整定待ちなしで即時再接続できること
    await client.ConnectAsync();
    var r2 = await client.SendAsync("second");
    Assert.Equal("echo:second", r2.Text?.Trim());

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  [Fact]
  public async Task JapaneseText_Utf8RoundTrip_PreservesContent()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    SetupEchoServer(server);
    await server.StartAsync();

    await using var client = CreateClient(port);
    await client.ConnectAsync();

    var response = await client.SendAsync("こんにちは世界");

    Assert.Equal("echo:こんにちは世界", response.Text?.Trim());

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  [Fact]
  public async Task Broadcast_ReachesMultipleRealClients()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    await using var client1 = CreateClient(port);
    await using var client2 = CreateClient(port);

    var tcs1 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var tcs2 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client1.OnMessageReceived += (_, msg) => tcs1.TrySetResult(msg.Text?.Trim() ?? "");
    client2.OnMessageReceived += (_, msg) => tcs2.TrySetResult(msg.Text?.Trim() ?? "");

    await client1.ConnectAsync();
    await client2.ConnectAsync();
    await TestWait.UntilAsync(() => server.GetAllSessions().Count() == 2);

    await server.BroadcastAsync("to_everyone");

    Assert.Equal("to_everyone", await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.Equal("to_everyone", await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(3)));

    await client1.DisconnectAsync();
    await client2.DisconnectAsync();
    await server.StopAsync();
  }

  [Fact]
  public async Task ServerRestart_ClientCanReconnectAndCommunicate()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    SetupEchoServer(server);
    await server.StartAsync();

    await using var client = CreateClient(port);
    await client.ConnectAsync();
    var r1 = await client.SendAsync("before_restart");
    Assert.Equal("echo:before_restart", r1.Text?.Trim());

    // サーバーを再起動
    await client.DisconnectAsync();
    await server.StopAsync();
    await server.StartAsync();

    // クライアントが再接続して通信できること
    await client.ConnectAsync();
    var r2 = await client.SendAsync("after_restart");
    Assert.Equal("echo:after_restart", r2.Text?.Trim());

    await client.DisconnectAsync();
    await server.StopAsync();
  }
}
