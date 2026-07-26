using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Dnbn.Core;

namespace Dnbn.Tests;

public class TcpTransportResourceTests
{
  [Fact]
  public async Task ConnectAsync_WithRetainedDisconnectedClient_DisposesPreviousSocket()
  {
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
      var port = ((IPEndPoint)listener.LocalEndpoint).Port;
      await using var transport = new TcpTransport("127.0.0.1", port);

      // Connected=falseでも未DisposeのTcpClientが残っている状態を決定的に作る。
      // 接続失敗後にクリーンアップが抜けた場合と同じ前提になる。
      var firstClient = new System.Net.Sockets.TcpClient();
      var firstSocket = firstClient.Client;
      SetPrivateField(transport, "_tcpClient", firstClient);
      Assert.False(transport.IsConnected);
      Assert.False(firstSocket.SafeHandle.IsClosed);

      var accept = listener.AcceptTcpClientAsync();
      await transport.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
      using var serverClient = await accept.WaitAsync(TimeSpan.FromSeconds(3));

      Assert.True(transport.IsConnected);
      Assert.True(firstSocket.SafeHandle.IsClosed);
    }
    finally
    {
      listener.Stop();
    }
  }

  [Fact]
  public async Task ConnectAsync_WhenConnectionFails_DoesNotRetainSocket()
  {
    // ポートを予約したままListenしないことで、他プロセスとのポート取得競合なしに
    // ConnectionRefusedを決定的に発生させる。
    using var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    blocker.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    var port = ((IPEndPoint)blocker.LocalEndPoint!).Port;

    await using var transport = new TcpTransport("127.0.0.1", port);

    await Assert.ThrowsAsync<SocketException>(() => transport.ConnectAsync());

    Assert.Null(GetPrivateFieldValue(transport, "_tcpClient"));
    Assert.Null(GetPrivateFieldValue(transport, "_stream"));
  }

  private static object? GetPrivateFieldValue(object instance, string name)
      => instance.GetType()
          .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
          .GetValue(instance);

  private static void SetPrivateField(object instance, string name, object value)
      => instance.GetType()
          .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
          .SetValue(instance, value);
}
