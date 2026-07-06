using System.Text;
using Dnbn.Configuration;
using Dnbn.Models;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Tests;

public class TcpClientDiagnosticsTests
{
  private static ClientConfig CreateConfig(bool waitForConnection = false, int waitTimeoutMs = 1000)
      => new()
      {
        Name = "DiagnosticsClient",
        RemoteHost = "127.0.0.1",
        RemotePort = 9999,
        Encoding = "UTF-8",
        MessageTerminator = "\n",
        TimeoutMilliseconds = 1000,
        WaitForConnectionOnSend = waitForConnection,
        WaitForConnectionTimeoutMilliseconds = waitTimeoutMs,
      };

  [Fact]
  public async Task MessageTrace_ObservesRequestResponseAndOneWay_WithWirePayload()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);
    var traces = new List<MessageTraceEvent>();
    var sync = new object();
    client.OnMessageTrace += (_, trace) =>
    {
      lock (sync) traces.Add(trace);
    };

    await client.ConnectAsync();

    var request = client.SendAsync("PING");
    await TestWait.UntilSentAsync(transport, "PING");
    transport.EnqueueReceiveData("PONG");
    Assert.Equal("PONG", (await request).Text?.Trim());

    await client.SendOneWayAsync("NOTICE");

    MessageTraceEvent[] snapshot;
    lock (sync) snapshot = traces.ToArray();
    Assert.Collection(snapshot,
        sent =>
        {
          Assert.Equal(MessageTraceDirection.Sent, sent.Direction);
          Assert.Equal(MessageTraceKind.Request, sent.Kind);
          Assert.Equal("PING\n", Encoding.UTF8.GetString(sent.Message.RawData));
        },
        received =>
        {
          Assert.Equal(MessageTraceDirection.Received, received.Direction);
          Assert.Equal(MessageTraceKind.Response, received.Kind);
          Assert.NotNull(received.ElapsedMilliseconds);
          Assert.True(received.ElapsedMilliseconds >= 0);
        },
        sent =>
        {
          Assert.Equal(MessageTraceDirection.Sent, sent.Direction);
          Assert.Equal(MessageTraceKind.OneWay, sent.Kind);
          Assert.Equal("NOTICE\n", Encoding.UTF8.GetString(sent.Message.RawData));
        });
  }

  [Fact]
  public async Task MessageTrace_HandlerFailureAndMutation_DoNotAffectTransportOrOtherHandlers()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);
    var observed = new TaskCompletionSource<MessageTraceEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

    client.OnMessageTrace += (_, trace) =>
    {
      trace.Message.Text = "MUTATED";
      throw new InvalidOperationException("diagnostic failure");
    };
    client.OnMessageTrace += (_, trace) => observed.TrySetResult(trace);

    await client.ConnectAsync();
    await client.SendOneWayAsync("ORIGINAL");

    var trace = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("ORIGINAL\n", trace.Message.Text);
    Assert.Equal("ORIGINAL\n", Encoding.UTF8.GetString(transport.SentData.Single()));
  }

  [Fact]
  public async Task SendAsync_WhenConnectionWaitingDisabled_PreservesImmediateFailure()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("PING"));
    Assert.Equal("Not connected", ex.Message);
  }

  [Fact]
  public async Task SendAsync_WhenConnectionWaitingEnabled_WaitsThenSends()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(waitForConnection: true), transport);

    var sendTask = client.SendAsync("PING");
    await Task.Delay(50);
    Assert.Empty(transport.SentData);

    await client.ConnectAsync();
    await TestWait.UntilSentAsync(transport, "PING");
    transport.EnqueueReceiveData("PONG");

    Assert.Equal("PONG", (await sendTask).Text?.Trim());
  }

  [Fact]
  public async Task SendOneWayAsync_WhenConnectionWaitingEnabled_TimesOut()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(waitForConnection: true, waitTimeoutMs: 50), transport);

    await Assert.ThrowsAsync<TimeoutException>(() => client.SendOneWayAsync("NOTICE"));
    Assert.Empty(transport.SentData);
  }

  [Fact]
  public async Task SendAsync_WhenConnectionWaitTimeoutIsInvalid_ThrowsConfigurationError()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(waitForConnection: true, waitTimeoutMs: 0), transport);

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("PING"));
    Assert.Contains(nameof(ClientConfig.WaitForConnectionTimeoutMilliseconds), ex.Message);
  }
}
