using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Filters;
using Dnbn.Models;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Tests;

public class TcpClientResponseSafetyTests
{
  private sealed class BlockingSendingFilter : IMessageFilter
  {
    private readonly TaskCompletionSource _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Message> OnSendingAsync(Message msg, IMessageContext ctx)
    {
      Entered.TrySetResult();
      await _continue.Task;
      Exited.TrySetResult();
      return msg;
    }

    public Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx) => Task.FromResult(msg);

    public void Continue() => _continue.TrySetResult();
  }

  private static ClientConfig CreateConfig(
      int? maxConcurrentResponseWaits = null,
      IncompleteRequestRecovery recovery = IncompleteRequestRecovery.KeepConnection)
      => new()
      {
        Name = "ResponseSafetyClient",
        RemoteHost = "127.0.0.1",
        RemotePort = 9999,
        Encoding = "UTF-8",
        MessageTerminator = "\n",
        TimeoutMilliseconds = 200,
        MaxConcurrentResponseWaits = maxConcurrentResponseWaits,
        IncompleteRequestRecovery = recovery,
        ConnectionRetryPolicy = new RetryPolicy { MaxRetryCount = 1, InitialDelayMs = 1, MaxDelayMs = 10 }
      };

  [Fact]
  public async Task MaxConcurrentResponseWaits_One_DoesNotWriteSecondRequestBeforeFirstResponse()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(maxConcurrentResponseWaits: 1), transport);
    await client.ConnectAsync();

    var first = client.SendAsync("first", TimeSpan.FromSeconds(2));
    await TestWait.UntilSentAsync(transport, "first");
    var second = client.SendAsync("second", TimeSpan.FromSeconds(2));

    await Task.Delay(50);
    Assert.Single(transport.SentData);

    transport.EnqueueReceiveData("first-response");
    Assert.Equal("first-response", (await first).Text?.Trim());
    await TestWait.UntilSentAsync(transport, "second");
    transport.EnqueueReceiveData("second-response");
    Assert.Equal("second-response", (await second).Text?.Trim());
  }

  [Fact]
  public async Task SendOneWayAsync_DoesNotConsumeOrWaitForResponseSlot()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(maxConcurrentResponseWaits: 1), transport);
    await client.ConnectAsync();

    var request = client.SendAsync("request", TimeSpan.FromSeconds(2));
    await TestWait.UntilSentAsync(transport, "request");
    await client.SendOneWayAsync("one-way");
    await TestWait.UntilSentAsync(transport, "one-way");

    Assert.Equal(2, transport.SentData.Count);
    transport.EnqueueReceiveData("response");
    Assert.Equal("response", (await request).Text?.Trim());
  }

  [Fact]
  public async Task TimeoutAfterWireWrite_ReconnectsBeforeLaterRequest()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(recovery: IncompleteRequestRecovery.Reconnect), transport);
    var unsolicited = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, message) => unsolicited.TrySetResult(message.Text?.Trim() ?? "");
    await client.ConnectAsync();

    await Assert.ThrowsAsync<TimeoutException>(() =>
        client.SendAsync(Message.FromString("timed-out", Encoding.UTF8), TimeSpan.FromMilliseconds(50)));
    await TestWait.UntilAsync(() => transport.ConnectCalls >= 2 && client.IsConnected);

    transport.EnqueueReceiveData("late-response");
    Assert.Equal("late-response", await unsolicited.Task.WaitAsync(TimeSpan.FromSeconds(2)));

    var next = client.SendAsync("next", TimeSpan.FromSeconds(2));
    await TestWait.UntilSentAsync(transport, "next");
    transport.EnqueueReceiveData("next-response");
    Assert.Equal("next-response", (await next).Text?.Trim());
  }

  [Fact]
  public async Task CallerCancellationAfterWireWrite_Reconnects()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(recovery: IncompleteRequestRecovery.Reconnect), transport);
    await client.ConnectAsync();
    using var cts = new CancellationTokenSource();

    var request = client.SendAsync("cancel-me", TimeSpan.FromSeconds(5), cts.Token);
    await TestWait.UntilSentAsync(transport, "cancel-me");
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    await TestWait.UntilAsync(() => transport.ConnectCalls >= 2 && client.IsConnected);
  }

  [Fact]
  public async Task TimeoutWhileSendingFilterIsAwaited_DoesNotWriteRequestAfterTimeout()
  {
    var transport = new MockTransport();
    var filter = new BlockingSendingFilter();
    await using var client = new TcpClient(
        CreateConfig(recovery: IncompleteRequestRecovery.Reconnect),
        transport,
        filters: [filter]);
    await client.ConnectAsync();

    var request = client.SendAsync("must-not-be-sent", TimeSpan.FromMilliseconds(500));
    await filter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Assert.ThrowsAsync<TimeoutException>(() => request);

    filter.Continue();
    await filter.Exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(25);

    Assert.Empty(transport.SentData);
    Assert.True(client.IsConnected);
  }
}
