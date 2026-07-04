using System.Net;
using System.Net.Sockets;
using Dnbn.Configuration;
using Dnbn.Core;

namespace Dnbn.Tests;

/// <summary>
/// TCPレベル（SO_KEEPALIVE）キープアライブのテスト
/// </summary>
public class TcpLevelKeepAliveTests
{
  // 他のテストクラス（15000-, 16000-, 17000-）と衝突しないレンジを使用
  private static int _portCounter = 18000;
  private static int NextPort() => Interlocked.Increment(ref _portCounter);

  private static Socket CreateSocket()
  {
    return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
  }

  private static bool IsKeepAliveEnabled(Socket socket)
  {
    return (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)! != 0;
  }

  [Fact]
  public void Apply_Enabled_SetsKeepAliveOptions()
  {
    using var socket = CreateSocket();
    var config = new TcpKeepAliveConfig
    {
      Enabled = true,
      TimeSeconds = 30,
      IntervalSeconds = 5,
      RetryCount = 3
    };

    TcpKeepAliveHelper.Apply(socket, config);

    Assert.True(IsKeepAliveEnabled(socket));
    Assert.Equal(30, (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime)!);
    Assert.Equal(5, (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval)!);
    Assert.Equal(3, (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount)!);
  }

  [Fact]
  public void Apply_Disabled_DoesNotEnableKeepAlive()
  {
    using var socket = CreateSocket();
    var config = new TcpKeepAliveConfig { Enabled = false };

    TcpKeepAliveHelper.Apply(socket, config);

    Assert.False(IsKeepAliveEnabled(socket));
  }

  [Fact]
  public void Apply_NullConfig_DoesNotEnableKeepAlive()
  {
    using var socket = CreateSocket();

    TcpKeepAliveHelper.Apply(socket, null);

    Assert.False(IsKeepAliveEnabled(socket));
  }

  [Fact]
  public void Apply_NonPositiveValues_SkipsDetailOptions()
  {
    using var socket = CreateSocket();
    var config = new TcpKeepAliveConfig
    {
      Enabled = true,
      TimeSeconds = 0,
      IntervalSeconds = -1,
      RetryCount = 0
    };

    // 詳細パラメータが不正でも基本のキープアライブ有効化は行われること
    TcpKeepAliveHelper.Apply(socket, config);

    Assert.True(IsKeepAliveEnabled(socket));
  }

  [Fact]
  public void Clone_CopiesAllProperties()
  {
    var config = new TcpKeepAliveConfig
    {
      Enabled = true,
      TimeSeconds = 120,
      IntervalSeconds = 15,
      RetryCount = 7
    };

    var clone = config.Clone();

    Assert.NotSame(config, clone);
    Assert.Equal(config.Enabled, clone.Enabled);
    Assert.Equal(config.TimeSeconds, clone.TimeSeconds);
    Assert.Equal(config.IntervalSeconds, clone.IntervalSeconds);
    Assert.Equal(config.RetryCount, clone.RetryCount);
  }

  [Fact]
  public async Task TcpTransport_WithTcpKeepAlive_ConnectsAndCommunicates()
  {
    int port = NextPort();
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();
    try
    {
      var acceptTask = listener.AcceptTcpClientAsync();

      var keepAlive = new TcpKeepAliveConfig
      {
        Enabled = true,
        TimeSeconds = 30,
        IntervalSeconds = 5,
        RetryCount = 3
      };
      await using var transport = new TcpTransport("127.0.0.1", port, keepAlive);
      await transport.ConnectAsync();

      using var accepted = await acceptTask;

      // キープアライブ設定込みでも通常の送受信が従来どおり動作すること
      var payload = new byte[] { 1, 2, 3 };
      await transport.SendAsync(payload);
      var buffer = new byte[16];
      var read = await accepted.GetStream().ReadAsync(buffer, 0, buffer.Length);
      Assert.Equal(payload, buffer.Take(read).ToArray());
    }
    finally
    {
      listener.Stop();
    }
  }

  [Fact]
  public async Task TcpServer_WithTcpKeepAlive_AcceptsAndCommunicates()
  {
    int port = NextPort();
    await using var server = new TcpServer(new ServerConfig
    {
      Name = "TcpKeepAliveServer",
      ListenPort = port,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TcpKeepAlive = new TcpKeepAliveConfig
      {
        Enabled = true,
        TimeSeconds = 30,
        IntervalSeconds = 5,
        RetryCount = 3
      }
    });

    var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    server.OnMessageReceived += (_, e) => received.TrySetResult(e.message.Text?.Trim());

    await server.StartAsync();

    await using var client = new Dnbn.Core.TcpClient(
        new ClientConfig
        {
          Name = "TcpKeepAliveClient",
          RemoteHost = "127.0.0.1",
          RemotePort = port,
          Encoding = "UTF-8",
          MessageTerminator = "\n"
        },
        new TcpTransport("127.0.0.1", port));
    await client.ConnectAsync();

    await client.SendOneWayAsync("hello");

    var text = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal("hello", text);

    await client.DisconnectAsync();
    await server.StopAsync();
  }
}
