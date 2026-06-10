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
      ResponsePredicate = predicate
    });
    await using var client = new TcpClient(config, transport);

    var copy = client.KeepAlive;

    Assert.NotNull(copy);
    Assert.True(copy.Enabled);
    Assert.Equal(30, copy.IntervalSeconds);
    Assert.Equal("ka", copy.Message);
    Assert.Same(predicate, copy.ResponsePredicate);
  }
}
