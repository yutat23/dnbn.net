using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Filters;
using Dnbn.Models;

namespace Dnbn.Tests;

/// <summary>
/// TcpClient のメッセージング機能テスト
/// （イベント発火・応答マッチング・キャンセル・フィルター・終端文字）
/// </summary>
public class TcpClientMessagingTests
{
  private static ClientConfig CreateConfig(int timeoutMs = 3000)
  {
    return new ClientConfig
    {
      Name = "MessagingTestClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 9999,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = timeoutMs
    };
  }

  // ---------------------------------------------------------------------------
  // 非請求メッセージ（サーバープッシュ）の配信
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task UnsolicitedMessage_FiresOnMessageReceivedAndObservable()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    var eventTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var observableTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, msg) => eventTcs.TrySetResult(msg.Text?.Trim() ?? "");
    using var subscription = client.MessageReceived.Subscribe(msg => observableTcs.TrySetResult(msg.Text?.Trim() ?? ""));

    await client.ConnectAsync();

    // 応答待ちリクエストがない状態での受信メッセージは両方に配信されること
    transport.EnqueueReceiveData("server_push");

    Assert.Equal("server_push", await eventTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.Equal("server_push", await observableTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  [Fact]
  public async Task ResponseMessage_DoesNotFireOnMessageReceived()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    int unsolicitedCount = 0;
    client.OnMessageReceived += (_, _) => Interlocked.Increment(ref unsolicitedCount);

    await client.ConnectAsync();

    var sendTask = client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromSeconds(3));
    await TestWait.UntilSentAsync(transport, "ping");
    transport.EnqueueReceiveData("pong");

    var response = await sendTask;
    Assert.Equal("pong", response.Text?.Trim());

    // 応答として消費されたメッセージは OnMessageReceived を発火しないこと
    await Task.Delay(100);
    Assert.Equal(0, unsolicitedCount);
  }

  // ---------------------------------------------------------------------------
  // FIFO 応答マッチング（パイプライン）
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task PipelinedRequests_MatchedInFifoOrder()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    // 3つのリクエストを応答を待たずに順番に発行する（送信確認後に次を発行して順序を確定させる）
    var task1 = client.SendAsync(Message.FromString("req1", Encoding.UTF8), TimeSpan.FromSeconds(5));
    await TestWait.UntilSentAsync(transport, "req1");
    var task2 = client.SendAsync(Message.FromString("req2", Encoding.UTF8), TimeSpan.FromSeconds(5));
    await TestWait.UntilSentAsync(transport, "req2");
    var task3 = client.SendAsync(Message.FromString("req3", Encoding.UTF8), TimeSpan.FromSeconds(5));
    await TestWait.UntilSentAsync(transport, "req3");

    // 応答を順番に返すと、FIFO順でマッチングされること
    transport.EnqueueReceiveData("res1");
    transport.EnqueueReceiveData("res2");
    transport.EnqueueReceiveData("res3");

    var r1 = await task1.WaitAsync(TimeSpan.FromSeconds(3));
    var r2 = await task2.WaitAsync(TimeSpan.FromSeconds(3));
    var r3 = await task3.WaitAsync(TimeSpan.FromSeconds(3));

    Assert.Equal("res1", r1.Text?.Trim());
    Assert.Equal("res2", r2.Text?.Trim());
    Assert.Equal("res3", r3.Text?.Trim());
  }

  [Fact]
  public async Task LateResponse_AfterTimeout_DeliveredAsUnsolicitedMessage()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    var unsolicitedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, msg) => unsolicitedTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();

    // タイムアウトさせる
    await Assert.ThrowsAsync<TimeoutException>(
        () => client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromMilliseconds(100)));

    // タイムアウト後に届いた応答は、保留中リクエストにマッチせず通常メッセージとして配信されること
    transport.EnqueueReceiveData("late_response");
    Assert.Equal("late_response", await unsolicitedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  // ---------------------------------------------------------------------------
  // 呼び出し元キャンセル
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task SendAsync_CancelledByCaller_ThrowsPromptly()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(timeoutMs: 10000), transport);

    await client.ConnectAsync();

    using var cts = new CancellationTokenSource();
    var sendTask = client.SendAsync(
        Message.FromString("ping", Encoding.UTF8), TimeSpan.FromSeconds(10), cts.Token);
    await TestWait.UntilSentAsync(transport, "ping");

    cts.Cancel();

    // タイムアウト（10秒）を待たずにキャンセルが反映されること
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => sendTask.WaitAsync(TimeSpan.FromSeconds(2)));
  }

  // ---------------------------------------------------------------------------
  // 文字列オーバーロード（後方互換含む）
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task SendAsync_StringOverload_Works()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    var sendTask = client.SendAsync("hello_string");
    await TestWait.UntilSentAsync(transport, "hello_string");
    transport.EnqueueReceiveData("string_response");

    var response = await sendTask;
    Assert.Equal("string_response", response.Text?.Trim());
  }

  [Fact]
  public async Task SendAsync_StringWithCancellationTokenOverload_Works()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    // 後方互換オーバーロード SendAsync(string, CancellationToken)
    var sendTask = client.SendAsync("compat_overload", CancellationToken.None);
    await TestWait.UntilSentAsync(transport, "compat_overload");
    transport.EnqueueReceiveData("compat_response");

    var response = await sendTask;
    Assert.Equal("compat_response", response.Text?.Trim());
  }

  // ---------------------------------------------------------------------------
  // SendAndWaitAsync の述語マッチング
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task SendAndWaitAsync_NonMatchingMessage_GoesToOnMessageReceived()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    var unsolicitedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, msg) => unsolicitedTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();

    var sendTask = client.SendAndWaitAsync(
        "ping",
        msg => msg.Text?.Contains("ACK") == true,
        TimeSpan.FromSeconds(5));
    await TestWait.UntilSentAsync(transport, "ping");

    // 述語にマッチしないメッセージは通常配信され、マッチするものが応答になること
    transport.EnqueueReceiveData("NOISE");
    transport.EnqueueReceiveData("ACK_OK");

    var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("ACK_OK", response.Text?.Trim());
    Assert.Equal("NOISE", await unsolicitedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  // ---------------------------------------------------------------------------
  // メッセージフィルター
  // ---------------------------------------------------------------------------

  private sealed class PrefixFilter : IMessageFilter
  {
    public Task<Message> OnSendingAsync(Message msg, IMessageContext ctx)
        => Task.FromResult(Message.FromString("OUT:" + msg.Text, Encoding.UTF8));

    public Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx)
        => Task.FromResult(Message.FromString("IN:" + msg.Text?.Trim(), Encoding.UTF8));
  }

  [Fact]
  public async Task MessageFilter_AppliedOnSendAndReceive()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport, filters: new[] { new PrefixFilter() });

    await client.ConnectAsync();

    var sendTask = client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromSeconds(3));

    // 送信フィルターが適用されていること
    await TestWait.UntilSentAsync(transport, "OUT:ping");

    transport.EnqueueReceiveData("pong");
    var response = await sendTask;

    // 受信フィルターが適用されていること
    Assert.Equal("IN:pong", response.Text?.Trim());
  }

  // ---------------------------------------------------------------------------
  // メッセージ終端文字の自動付与
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task SendAsync_AppendsTerminator_WhenMissing()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    _ = client.SendAsync(Message.FromString("no_term", Encoding.UTF8), TimeSpan.FromMilliseconds(200))
        .ContinueWith(_ => { }); // 応答は返さない（送信内容のみ検証）

    await TestWait.UntilSentAsync(transport, "no_term");
    var sent = Encoding.UTF8.GetString(transport.SentData.Single(d => Encoding.UTF8.GetString(d).Contains("no_term")));
    Assert.Equal("no_term\n", sent);
  }

  [Fact]
  public async Task SendAsync_DoesNotDuplicateTerminator_WhenAlreadyPresent()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    _ = client.SendAsync(Message.FromString("has_term\n", Encoding.UTF8), TimeSpan.FromMilliseconds(200))
        .ContinueWith(_ => { });

    await TestWait.UntilSentAsync(transport, "has_term");
    var sent = Encoding.UTF8.GetString(transport.SentData.Single(d => Encoding.UTF8.GetString(d).Contains("has_term")));
    Assert.Equal("has_term\n", sent);
  }
}
