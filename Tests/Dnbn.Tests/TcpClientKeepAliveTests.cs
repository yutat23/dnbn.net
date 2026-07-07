using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;

namespace Dnbn.Tests;

/// <summary>
/// TcpClient のキープアライブ機能テスト
/// </summary>
public class TcpClientKeepAliveTests
{
  private static ClientConfig CreateConfig(KeepAliveConfig keepAlive, int timeoutMs = 3000)
  {
    return new ClientConfig
    {
      Name = "KeepAliveTestClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 9999,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = timeoutMs,
      KeepAlive = keepAlive
    };
  }

  [Fact]
  public async Task KeepAlive_SendsMessagePeriodically()
  {
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 1,
      Message = "ka_ping"
    });
    await using var client = new TcpClient(config, transport);

    await client.ConnectAsync();

    // 間隔経過後にキープアライブメッセージが送信されること
    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 3000);
  }

  [Fact]
  public async Task KeepAlive_Disabled_DoesNotSend()
  {
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = false,
      IntervalSeconds = 1,
      Message = "ka_ping"
    });
    await using var client = new TcpClient(config, transport);

    await client.ConnectAsync();
    await Task.Delay(1500);

    Assert.DoesNotContain(transport.SentData,
        d => Encoding.UTF8.GetString(d).Contains("ka_ping"));
  }

  [Fact]
  public async Task KeepAlive_DefaultBehavior_FirstMessageConsumedAsResponse()
  {
    // 後方互換: ResponsePredicate 未設定の場合、キープアライブ待機中の最初の受信メッセージが応答として扱われる
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 2, // タイムアウト（=間隔）に余裕を持たせる
      Message = "ka_ping"
    });
    await using var client = new TcpClient(config, transport);

    var kaResponseTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    int unsolicitedCount = 0;
    client.OnKeepAliveResponseReceived += (_, msg) => kaResponseTcs.TrySetResult(msg.Text?.Trim() ?? "");
    client.OnMessageReceived += (_, _) => Interlocked.Increment(ref unsolicitedCount);

    await client.ConnectAsync();

    // キープアライブが送信されるのを待ってから応答を返す
    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 5000);
    transport.EnqueueReceiveData("any_message");

    Assert.Equal("any_message", await kaResponseTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));

    // キープアライブ応答として消費され、通常メッセージとしては配信されないこと
    await Task.Delay(100);
    Assert.Equal(0, unsolicitedCount);
  }

  [Fact]
  public async Task KeepAlive_WithPredicate_MatchingMessageConsumedAsResponse()
  {
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 2,
      Message = "ka_ping",
      ResponsePredicate = msg => msg.Text?.Trim() == "ka_ack"
    });
    await using var client = new TcpClient(config, transport);

    var kaResponseTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var unsolicitedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnKeepAliveResponseReceived += (_, msg) => kaResponseTcs.TrySetResult(msg.Text?.Trim() ?? "");
    client.OnMessageReceived += (_, msg) => unsolicitedTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();

    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 5000);

    // 述語にマッチしないメッセージ → 通常配信、マッチするメッセージ → キープアライブ応答
    transport.EnqueueReceiveData("server_push");
    transport.EnqueueReceiveData("ka_ack");

    Assert.Equal("server_push", await unsolicitedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.Equal("ka_ack", await kaResponseTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  [Fact]
  public async Task KeepAlive_DeferredWhilePendingRequest_ResponseNotStolen()
  {
    // 通常要求が応答待ちの間はKeepAliveが延期され、応答が横取りされないこと
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 1,
      Message = "ka_ping"
    }, timeoutMs: 5000);
    await using var client = new TcpClient(config, transport);

    await client.ConnectAsync();

    // 応答待ちの通常要求を作る
    var sendTask = client.SendAsync("request1");
    await TestWait.UntilSentAsync(transport, "request1", timeoutMs: 3000);

    // KeepAlive間隔を跨いで待っても、KeepAliveは送信されない（延期される）
    await Task.Delay(1600);
    Assert.DoesNotContain(transport.SentData,
        d => Encoding.UTF8.GetString(d).Contains("ka_ping"));

    // 応答はKeepAliveに横取りされず SendAsync に配送される
    transport.EnqueueReceiveData("response1");
    var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("response1", response.Text?.Trim());

    // 要求が捌けた後はKeepAliveが再開すること
    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 3000);
  }

  [Fact]
  public async Task KeepAlive_PendingKeepAliveThenSendAsync_ResponsesCorrelateInFifoOrder()
  {
    // KeepAlive応答待ち中に通常要求を送っても、FIFO順で正しく相関されること
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 2,
      Message = "ka_ping"
    }, timeoutMs: 5000);
    await using var client = new TcpClient(config, transport);

    var kaResponseTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnKeepAliveResponseReceived += (_, msg) => kaResponseTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();

    // KeepAliveが送信され応答待ちになるのを待つ
    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 5000);

    // KeepAlive応答待ちのまま通常要求を送信
    var sendTask = client.SendAsync("request1");
    await TestWait.UntilSentAsync(transport, "request1", timeoutMs: 3000);

    // 応答はFIFO順（KeepAlive応答 → 通常応答）で相関される
    transport.EnqueueReceiveData("ka_pong");
    transport.EnqueueReceiveData("response1");

    Assert.Equal("ka_pong", await kaResponseTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("response1", response.Text?.Trim());
  }

  [Fact]
  public async Task KeepAlive_Timeout_DefaultBehavior_Disconnects()
  {
    // 応答が来ないKeepAliveはタイムアウトで切断される。
    // 遅延して届いたKeepAlive応答が後続の通常要求の応答として誤配されるのを防ぐため
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 1,
      Message = "ka_ping"
    });
    await using var client = new TcpClient(config, transport);
    var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnDisconnected += (_, _) => disconnectedTcs.TrySetResult();

    await client.ConnectAsync();
    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 3000);

    // 応答を返さない → タイムアウト（=間隔）で切断されること
    await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.False(client.IsConnected);
    Assert.Equal(ConnectionState.Disconnected, client.State);
    Assert.True(client.ConnectionInfo.KeepAliveTimeoutCount >= 1);
  }

  [Fact]
  public async Task KeepAlive_Timeout_WithDisconnectOnTimeoutFalse_KeepsConnection()
  {
    // 明示的にfalseを指定した場合は接続を維持してKeepAliveを継続する
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 1,
      Message = "ka_ping",
      DisconnectOnTimeout = false
    });
    await using var client = new TcpClient(config, transport);

    await client.ConnectAsync();

    // 1回目のタイムアウトを跨いでも2回目のKeepAliveが送信されること
    await TestWait.UntilAsync(
        () => transport.SentData.Count(d => Encoding.UTF8.GetString(d).Contains("ka_ping")) >= 2,
        timeoutMs: 8000);
    Assert.True(client.IsConnected);
    Assert.True(client.ConnectionInfo.KeepAliveTimeoutCount >= 1);
  }

  [Fact]
  public async Task KeepAlive_Timeout_WithDisconnectOnTimeoutTrue_AutoReconnectsAndWorks()
  {
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 1,
      Message = "ka_ping",
      DisconnectOnTimeout = true
    }, timeoutMs: 5000);
    config.ConnectionRetryPolicy = new RetryPolicy
    {
      MaxRetryCount = 3,
      InitialDelayMs = 10,
      MaxDelayMs = 100
    };
    await using var client = new TcpClient(config, transport);

    var reconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int connectedCount = 0;
    client.OnConnected += (_, _) =>
    {
      if (Interlocked.Increment(ref connectedCount) >= 2)
      {
        reconnectedTcs.TrySetResult();
      }
    };

    await client.ConnectAsync();
    await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(8));
    Assert.True(client.IsConnected);

    // 再接続後に通常の要求・応答が機能することまで確認する
    var sendTask = client.SendAsync("after_reconnect");
    await TestWait.UntilSentAsync(transport, "after_reconnect", timeoutMs: 3000);
    transport.EnqueueReceiveData("ok");
    var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("ok", response.Text?.Trim());
  }

  [Fact]
  public async Task KeepAliveResponse_HandlerFailure_DoesNotPreventOtherSubscribers()
  {
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 2,
      Message = "ka_ping"
    });
    await using var client = new TcpClient(config, transport);

    var laterSubscriberTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnKeepAliveResponseReceived += (_, _) => throw new InvalidOperationException("handler failure");
    client.OnKeepAliveResponseReceived += (_, message) =>
        laterSubscriberTcs.TrySetResult(message.Text?.Trim() ?? string.Empty);

    await client.ConnectAsync();
    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 5000);
    transport.EnqueueReceiveData("ka_pong");

    Assert.Equal("ka_pong", await laterSubscriberTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.True(client.IsConnected);
  }

  [Fact]
  public async Task NotificationPredicate_TakesPrecedenceOverKeepAliveResponse()
  {
    // 通知判定はKeepAlive応答マッチングより先に行われる（通知電文は応答として消費されない）
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 2,
      Message = "ka_ping"
    });
    config.NotificationPredicate = msg => msg.Text?.StartsWith("push") == true;
    await using var client = new TcpClient(config, transport);

    var kaResponseTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var notificationTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnKeepAliveResponseReceived += (_, msg) => kaResponseTcs.TrySetResult(msg.Text?.Trim() ?? "");
    client.OnMessageReceived += (_, msg) => notificationTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();
    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 5000);

    // KeepAlive応答待ち中でも、通知電文はKeepAlive応答として消費されず通知として配信されること
    transport.EnqueueReceiveData("push1");
    Assert.Equal("push1", await notificationTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.False(kaResponseTcs.Task.IsCompleted);

    // その後の応答はKeepAlive応答として相関されること
    transport.EnqueueReceiveData("ka_pong");
    Assert.Equal("ka_pong", await kaResponseTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  [Fact]
  public async Task KeepAliveProperty_SetToNull_StopsSending()
  {
    var transport = new MockTransport();
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 1,
      Message = "ka_ping"
    });
    await using var client = new TcpClient(config, transport);

    await client.ConnectAsync();
    await TestWait.UntilSentAsync(transport, "ka_ping", timeoutMs: 3000);

    // 実行中に無効化 → 以降は送信されないこと
    client.KeepAlive = null;
    var countAfterDisable = transport.SentData.Count;

    await Task.Delay(1500);
    Assert.Equal(countAfterDisable, transport.SentData.Count);
  }

  [Fact]
  public async Task KeepAliveProperty_Getter_ReturnsCopyIncludingPredicate()
  {
    var transport = new MockTransport();
    Func<Message, bool> predicate = msg => msg.Text == "ack";
    var config = CreateConfig(new KeepAliveConfig
    {
      Enabled = true,
      IntervalSeconds = 30,
      Message = "ka",
      ResponsePredicate = predicate,
      DisconnectOnTimeout = true
    });
    await using var client = new TcpClient(config, transport);

    var copy = client.KeepAlive;

    Assert.NotNull(copy);
    Assert.True(copy.Enabled);
    Assert.Equal(30, copy.IntervalSeconds);
    Assert.Equal("ka", copy.Message);
    Assert.Same(predicate, copy.ResponsePredicate);
    Assert.True(copy.DisconnectOnTimeout);
  }
}
