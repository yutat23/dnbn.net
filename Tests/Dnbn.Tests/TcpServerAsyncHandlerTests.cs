using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;

namespace Dnbn.Tests;

public class TcpServerAsyncHandlerTests
{
  private static int _port = 17500;
  private static int NextPort() => Interlocked.Increment(ref _port);

  [Fact]
  public async Task AsyncHandler_IsAwaitedInReceiveOrderPerSession()
  {
    var port = NextPort();
    await using var server = new TcpServer(new ServerConfig
    {
      Name = "AsyncHandlerServer",
      ListenPort = port,
      Encoding = "UTF-8",
      MessageTerminator = "\n"
    });
    var order = new ConcurrentQueue<string>();
    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    server.OnMessageReceivedAsync += async (message, _, cancellationToken) =>
    {
      var value = message.Text?.Trim() ?? "";
      order.Enqueue("start-" + value);
      if (value == "1") await Task.Delay(75, cancellationToken);
      order.Enqueue("end-" + value);
      if (value == "2") completed.TrySetResult();
    };
    await server.StartAsync();
    using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
    await socket.ConnectAsync(IPAddress.Loopback, port);

    await socket.SendAsync(Encoding.UTF8.GetBytes("1\n2\n"), SocketFlags.None);
    await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Assert.Equal(["start-1", "end-1", "start-2", "end-2"], order.ToArray());
  }

  [Fact]
  public async Task ConcurrentStartAndStop_AreIdempotent()
  {
    var port = NextPort();
    await using var server = new TcpServer(new ServerConfig
    {
      Name = "LifecycleServer",
      ListenPort = port,
      MessageTerminator = "\n"
    });

    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => server.StartAsync()));
    Assert.True(server.IsRunning);
    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => server.StopAsync()));
    Assert.False(server.IsRunning);
  }
}
