using System.Net;
using System.Net.Sockets;
using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using Encoding = System.Text.Encoding;

namespace Dnbn.Tests;

/// <summary>
/// TcpServer のテスト（実際のポートを使用）
/// </summary>
public class TcpServerTests
{
  private static int _portCounter = 15000;

  /// <summary>テストごとに異なるポートを割り当て</summary>
  private static int NextPort() => Interlocked.Increment(ref _portCounter);

  private static TcpServer CreateServer(int port, string terminator = "\n")
  {
    var config = new ServerConfig
    {
      Name = "TestServer",
      ListenPort = port,
      Encoding = "UTF-8",
      MessageTerminator = terminator
    };
    return new TcpServer(config);
  }

  // ---------------------------------------------------------------------------
  // 起動 / 停止テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task StartAsync_SetsIsRunning_True()
  {
    int port = NextPort();
    await using var server = CreateServer(port);

    await server.StartAsync();

    Assert.True(server.IsRunning);
    await server.StopAsync();
  }

  [Fact]
  public async Task StartAsync_AlreadyRunning_NoOp()
  {
    int port = NextPort();
    await using var server = CreateServer(port);

    await server.StartAsync();
    await server.StartAsync(); // 2回目は何もしない

    Assert.True(server.IsRunning);
    await server.StopAsync();
  }

  [Fact]
  public async Task StopAsync_SetsIsRunning_False()
  {
    int port = NextPort();
    await using var server = CreateServer(port);

    await server.StartAsync();
    await server.StopAsync();

    Assert.False(server.IsRunning);
  }

  [Fact]
  public async Task StartAsync_AfterStop_AcceptsClients()
  {
    int port = NextPort();
    await using var server = CreateServer(port);

    await server.StartAsync();
    await server.StopAsync();

    var connectedTcs = new TaskCompletionSource<SessionInfo>();
    server.OnClientConnected += (_, session) => connectedTcs.TrySetResult(session);

    await server.StartAsync();

    using var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
    await client.ConnectAsync(IPAddress.Loopback, port);

    var session = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

    Assert.NotNull(session);
    Assert.Single(server.GetAllSessions());

    await server.StopAsync();
  }

  [Fact]
  public async Task DisposeAsync_StopsServer()
  {
    int port = NextPort();
    var server = CreateServer(port);

    await server.StartAsync();
    await server.DisposeAsync();

    Assert.False(server.IsRunning);
  }

  // ---------------------------------------------------------------------------
  // セッション管理テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task HandleClient_AddsSession_OnConnect()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    var connectedTcs = new TaskCompletionSource<SessionInfo>();
    server.OnClientConnected += (_, session) => connectedTcs.TrySetResult(session);

    await server.StartAsync();

    // クライアントを接続
    using var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
    await client.ConnectAsync(IPAddress.Loopback, port);

    var session = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

    Assert.NotNull(session);
    Assert.NotEmpty(session.SessionId);
    Assert.Single(server.GetAllSessions());

    await server.StopAsync();
  }

  [Fact]
  public async Task HandleClient_RemovesSession_OnDisconnect()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    var connectedTcs = new TaskCompletionSource<bool>();
    var disconnectedTcs = new TaskCompletionSource<bool>();
    server.OnClientConnected += (_, _) => connectedTcs.TrySetResult(true);
    server.OnClientDisconnected += (_, _) => disconnectedTcs.TrySetResult(true);

    await server.StartAsync();

    using var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
    await client.ConnectAsync(IPAddress.Loopback, port);
    await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

    // クライアントを切断
    client.Shutdown(SocketShutdown.Both);
    client.Close();

    await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

    // セッションが削除されること
    await Task.Delay(50); // セッション削除が完了するまで少し待つ
    Assert.Empty(server.GetAllSessions());

    await server.StopAsync();
  }

  [Fact]
  public async Task SendAsync_InvalidSession_ThrowsInvalidOperationException()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => server.SendAsync("nonexistent-session", Message.FromString("hello", Encoding.UTF8)));

    await server.StopAsync();
  }

  [Fact]
  public async Task BroadcastAsync_SendsToAllSessions()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    server.OnClientConnected += (_, _) => { };

    await server.StartAsync();

    // 2つのクライアントを接続
    using var client1 = new Socket(SocketType.Stream, ProtocolType.Tcp);
    using var client2 = new Socket(SocketType.Stream, ProtocolType.Tcp);
    await client1.ConnectAsync(IPAddress.Loopback, port);
    await client2.ConnectAsync(IPAddress.Loopback, port);

    // セッション確立を確認（固定待ちではなく条件で待機）
    await TestWait.UntilAsync(() => server.GetAllSessions().Count() == 2);
    Assert.Equal(2, server.GetAllSessions().Count());

    await server.BroadcastAsync("broadcast_msg");

    // 各クライアントがデータを受信できること
    var buf1 = new byte[256];
    var buf2 = new byte[256];
    var r1 = await client1.ReceiveAsync(buf1, SocketFlags.None).WaitAsync(TimeSpan.FromSeconds(3));
    var r2 = await client2.ReceiveAsync(buf2, SocketFlags.None).WaitAsync(TimeSpan.FromSeconds(3));

    Assert.True(r1 > 0);
    Assert.True(r2 > 0);
    Assert.Contains("broadcast_msg", Encoding.UTF8.GetString(buf1, 0, r1));
    Assert.Contains("broadcast_msg", Encoding.UTF8.GetString(buf2, 0, r2));

    await server.StopAsync();
  }

  [Fact]
  public async Task OnMessageReceived_Fires_WhenClientSendsData()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    var messageTcs = new TaskCompletionSource<string>();
    server.OnMessageReceived += (_, e) => messageTcs.TrySetResult(e.message.Text ?? "");

    await server.StartAsync();

    using var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
    await client.ConnectAsync(IPAddress.Loopback, port);
    await Task.Delay(50);

    // メッセージを送信（終端文字 \n を含む）
    var msg = Encoding.UTF8.GetBytes("hello\n");
    await client.SendAsync(msg, SocketFlags.None);

    var received = await messageTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("hello", received.Trim());

    await server.StopAsync();
  }

  [Fact]
  public async Task ConnectionInfo_ReflectsRunningState()
  {
    int port = NextPort();
    await using var server = CreateServer(port);

    Assert.False(server.ConnectionInfo.IsRunning);

    await server.StartAsync();
    Assert.True(server.ConnectionInfo.IsRunning);

    await server.StopAsync();
    Assert.False(server.ConnectionInfo.IsRunning);
  }
}
