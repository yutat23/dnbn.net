using System.Net;
using System.Net.Sockets;
using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using Encoding = System.Text.Encoding;

namespace Dnbn.Tests;

/// <summary>
/// TcpServer のセッション機能テスト（実ポート使用）
/// 送信の直列化・セッション宛送信・停止時の切断・統計情報を検証する
/// </summary>
public class TcpServerSessionTests
{
  // TcpServerTests (15000-) と衝突しないレンジを使用
  private static int _portCounter = 16000;
  private static int NextPort() => Interlocked.Increment(ref _portCounter);

  private static TcpServer CreateServer(int port)
  {
    var config = new ServerConfig
    {
      Name = "SessionTestServer",
      ListenPort = port,
      Encoding = "UTF-8",
      MessageTerminator = "\n"
    };
    return new TcpServer(config);
  }

  private static async Task<(Socket socket, SessionInfo session)> ConnectClientAsync(TcpServer server, int port)
  {
    var connectedTcs = new TaskCompletionSource<SessionInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
    void Handler(object? sender, SessionInfo s) => connectedTcs.TrySetResult(s);
    server.OnClientConnected += Handler;
    try
    {
      var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
      await socket.ConnectAsync(IPAddress.Loopback, port);
      var session = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
      return (socket, session);
    }
    finally
    {
      server.OnClientConnected -= Handler;
    }
  }

  /// <summary>ソケットから指定数の改行終端メッセージを読み取る</summary>
  private static async Task<List<string>> ReadMessagesAsync(Socket socket, int expectedCount, TimeSpan timeout)
  {
    var received = new StringBuilder();
    var buffer = new byte[8192];
    using var cts = new CancellationTokenSource(timeout);

    while (received.ToString().Count(c => c == '\n') < expectedCount)
    {
      var read = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
      if (read == 0)
      {
        break;
      }
      received.Append(Encoding.UTF8.GetString(buffer, 0, read));
    }

    return received.ToString()
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .ToList();
  }

  // ---------------------------------------------------------------------------
  // セッション宛送信
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task SendAsync_ToSpecificSession_ClientReceivesMessage()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    var (socket, session) = await ConnectClientAsync(server, port);
    using (socket)
    {
      await server.SendAsync(session.SessionId, Message.FromString("hello_session", Encoding.UTF8));

      var messages = await ReadMessagesAsync(socket, 1, TimeSpan.FromSeconds(3));
      Assert.Single(messages);
      Assert.Equal("hello_session", messages[0]);
    }

    await server.StopAsync();
  }

  [Fact]
  public async Task SendAsync_StringOverload_ClientReceivesMessage()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    var (socket, session) = await ConnectClientAsync(server, port);
    using (socket)
    {
      await server.SendAsync(session.SessionId, "text_overload");

      var messages = await ReadMessagesAsync(socket, 1, TimeSpan.FromSeconds(3));
      Assert.Single(messages);
      Assert.Equal("text_overload", messages[0]);
    }

    await server.StopAsync();
  }

  // ---------------------------------------------------------------------------
  // 並行送信の直列化（バイト列が交錯しないこと）
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task ConcurrentSends_ToSameSession_DoNotInterleaveMessages()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    const int messageCount = 50;
    var (socket, session) = await ConnectClientAsync(server, port);
    using (socket)
    {
      // 同一セッションへ並行送信してもメッセージ単位の境界が保たれること
      var sendTasks = Enumerable.Range(0, messageCount)
          .Select(i => server.SendAsync(session.SessionId, $"MSG{i:000}_{new string('x', 200)}"))
          .ToArray();
      await Task.WhenAll(sendTasks);

      var messages = await ReadMessagesAsync(socket, messageCount, TimeSpan.FromSeconds(5));

      Assert.Equal(messageCount, messages.Count);
      foreach (var msg in messages)
      {
        // 各メッセージが完全な形（プレフィックス + 200文字のパディング）であること
        Assert.Matches(@"^MSG\d{3}_x{200}$", msg);
      }
      // 全メッセージが欠落なく届いていること
      var indices = messages.Select(m => int.Parse(m.Substring(3, 3))).OrderBy(i => i).ToList();
      Assert.Equal(Enumerable.Range(0, messageCount), indices);
    }

    await server.StopAsync();
  }

  // ---------------------------------------------------------------------------
  // 停止時の挙動
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task StopAsync_DisconnectsConnectedClients()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    var (socket, _) = await ConnectClientAsync(server, port);
    using (socket)
    {
      await server.StopAsync();

      // クライアント側では接続クローズ（0バイト受信）が観測されること
      var buffer = new byte[16];
      var read = await socket.ReceiveAsync(buffer, SocketFlags.None)
          .WaitAsync(TimeSpan.FromSeconds(3));
      Assert.Equal(0, read);
    }
  }

  [Fact]
  public async Task SendAsync_AfterSessionDisconnected_ThrowsInvalidOperationException()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    server.OnClientDisconnected += (_, _) => disconnectedTcs.TrySetResult();

    var (socket, session) = await ConnectClientAsync(server, port);
    socket.Shutdown(SocketShutdown.Both);
    socket.Close();

    await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

    // 切断済みセッションへの送信はセッション未存在エラーになること
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => server.SendAsync(session.SessionId, Message.FromString("too_late", Encoding.UTF8)));

    await server.StopAsync();
  }

  // ---------------------------------------------------------------------------
  // 統計情報
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task ConnectionInfo_TracksConnectionsAndMessages()
  {
    int port = NextPort();
    await using var server = CreateServer(port);
    await server.StartAsync();

    var (socket1, _) = await ConnectClientAsync(server, port);
    var (socket2, _) = await ConnectClientAsync(server, port);
    using (socket1)
    using (socket2)
    {
      await socket1.SendAsync(Encoding.UTF8.GetBytes("from_client\n"), SocketFlags.None);

      await TestWait.UntilAsync(() => server.ConnectionInfo.MessagesReceived >= 1);

      var info = server.ConnectionInfo;
      Assert.True(info.IsRunning);
      Assert.Equal(2, info.ConnectionCount);
      Assert.Equal(2, info.TotalConnections);
      Assert.Equal(1, info.MessagesReceived);
      Assert.NotNull(info.StartedAt);
      Assert.NotNull(info.LastClientConnectedAt);
    }

    await server.StopAsync();
  }

  [Fact]
  public async Task ServerObservable_MessageReceived_FiresWithSessionInfo()
  {
    int port = NextPort();
    await using var server = CreateServer(port);

    var observableTcs = new TaskCompletionSource<(string text, string sessionId)>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var subscription = server.MessageReceived.Subscribe(
        x => observableTcs.TrySetResult((x.message.Text?.Trim() ?? "", x.sessionInfo.SessionId)));

    await server.StartAsync();

    var (socket, session) = await ConnectClientAsync(server, port);
    using (socket)
    {
      await socket.SendAsync(Encoding.UTF8.GetBytes("via_observable\n"), SocketFlags.None);

      var (text, sessionId) = await observableTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
      Assert.Equal("via_observable", text);
      Assert.Equal(session.SessionId, sessionId);
    }

    await server.StopAsync();
  }
}
