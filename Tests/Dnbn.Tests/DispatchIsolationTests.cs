using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Tests;

public class DispatchIsolationTests
{
  private static TcpClient CreateClient(MockTransport transport) => new(new ClientConfig
  {
    Name = "DispatchClient",
    RemoteHost = "127.0.0.1",
    RemotePort = 9999,
    Encoding = "UTF-8",
    MessageTerminator = "\n"
  }, transport);

  [Fact]
  public async Task MessageEvent_SubscriptionFailure_DoesNotSkipLaterSubscriberOrDisconnect()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);
    var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, _) => throw new InvalidOperationException("subscriber failure");
    client.OnMessageReceived += (_, message) => observed.TrySetResult(message.Text?.Trim() ?? "");
    await client.ConnectAsync();

    transport.EnqueueReceiveData("notification");

    Assert.Equal("notification", await observed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    Assert.True(client.IsConnected);
  }

  [Fact]
  public async Task Observable_SubscriptionFailure_DoesNotSkipLaterObserverOrDisconnect()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);
    var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var failing = client.MessageReceived.Subscribe(_ => throw new InvalidOperationException("observer failure"));
    using var succeeding = client.MessageReceived.Subscribe(message => observed.TrySetResult(message.Text?.Trim() ?? ""));
    await client.ConnectAsync();

    transport.EnqueueReceiveData("notification");

    Assert.Equal("notification", await observed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    Assert.True(client.IsConnected);
  }
}
