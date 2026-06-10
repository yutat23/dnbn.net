using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Tests;

/// <summary>
/// 通知電文機能（SendOneWayAsync / NotificationPredicate）のテスト
/// </summary>
public class TcpClientNotificationTests
{
  private static ClientConfig CreateConfig(
      int timeoutMs = 3000,
      Func<Message, bool>? notificationPredicate = null)
  {
    return new ClientConfig
    {
      Name = "NotificationTestClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 9999,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = timeoutMs,
      NotificationPredicate = notificationPredicate
    };
  }

  // ---------------------------------------------------------------------------
  // SendOneWayAsync（応答を待たない送信）
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task SendOneWayAsync_SendsMessageWithTerminator_AndCompletes()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    // 応答が来なくても完了すること（応答待ちのSendAsyncならタイムアウトする状況）
    await client.SendOneWayAsync("notify1").WaitAsync(TimeSpan.FromSeconds(3));

    var sent = Encoding.UTF8.GetString(
        transport.SentData.Single(d => Encoding.UTF8.GetString(d).Contains("notify1")));
    Assert.Equal("notify1\n", sent);
  }

  [Fact]
  public async Task SendOneWayAsync_MessageOverload_Works()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    await client.SendOneWayAsync(Message.FromString("notify_msg", Encoding.UTF8))
        .WaitAsync(TimeSpan.FromSeconds(3));

    Assert.Contains(transport.SentData,
        d => Encoding.UTF8.GetString(d).Contains("notify_msg"));
  }

  [Fact]
  public async Task SendOneWayAsync_ThrowsInvalidOperationException_WhenNotConnected()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => client.SendOneWayAsync("notify"));
  }

  [Fact]
  public async Task SendOneWayAsync_DoesNotConsumeIncomingMessages()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    var unsolicitedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, msg) => unsolicitedTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();

    // 通知送信は応答待ちリストに入らないため、後続の受信は通常配信されること
    await client.SendOneWayAsync("notify").WaitAsync(TimeSpan.FromSeconds(3));
    transport.EnqueueReceiveData("server_push");

    Assert.Equal("server_push", await unsolicitedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  [Fact]
  public async Task SendOneWayAsync_PreservesFifoOrder_WithSendAsync()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    // 通知電文→リクエストの順で発行すると、ソケットへもその順で書き込まれること
    await client.SendOneWayAsync("first_notify").WaitAsync(TimeSpan.FromSeconds(3));
    var sendTask = client.SendAsync(Message.FromString("second_request", Encoding.UTF8), TimeSpan.FromSeconds(3));
    await TestWait.UntilSentAsync(transport, "second_request");

    var sentTexts = transport.SentData.Select(d => Encoding.UTF8.GetString(d).Trim()).ToList();
    Assert.Equal(new[] { "first_notify", "second_request" }, sentTexts);

    transport.EnqueueReceiveData("response");
    var response = await sendTask;
    Assert.Equal("response", response.Text?.Trim());
  }

  [Fact]
  public async Task SendOneWayAsync_PropagatesSendFailure()
  {
    var transport = new FailingSendTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    // 送信失敗が呼び出し元に伝播すること（fire-and-forgetでもエラーは観測できる）
    await Assert.ThrowsAsync<IOException>(
        () => client.SendOneWayAsync("notify").WaitAsync(TimeSpan.FromSeconds(3)));
  }

  /// <summary>SendAsync が常に失敗するトランスポート（送信失敗の伝播テスト用）</summary>
  private sealed class FailingSendTransport : ITransport
  {
    private readonly MockTransport _inner = new();

    public bool IsConnected => _inner.IsConnected;
    public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _inner.DisconnectAsync(ct);
    public Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
        => _inner.ReceiveAsync(buffer, offset, count, ct);

    public Task SendAsync(byte[] data, CancellationToken ct = default)
        => throw new IOException("simulated send failure");
  }

  // ---------------------------------------------------------------------------
  // NotificationPredicate（通知電文の受信判定）
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task NotificationPredicate_NotificationDoesNotConsumePendingResponse()
  {
    var transport = new MockTransport();
    var config = CreateConfig(
        notificationPredicate: msg => msg.Text?.StartsWith("EVENT:") == true);
    await using var client = new TcpClient(config, transport);

    var notificationTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, msg) => notificationTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();

    // 応答待ち中に通知電文が割り込むケース
    var sendTask = client.SendAsync(Message.FromString("request", Encoding.UTF8), TimeSpan.FromSeconds(5));
    await TestWait.UntilSentAsync(transport, "request");

    transport.EnqueueReceiveData("EVENT: 割り込み通知");
    transport.EnqueueReceiveData("RESULT");

    // 通知は応答として消費されず OnMessageReceived へ、本来の応答はリクエストへ返ること
    var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("RESULT", response.Text?.Trim());
    Assert.Equal("EVENT: 割り込み通知", await notificationTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  [Fact]
  public async Task NotificationPredicate_Null_LegacyBehaviorUnchanged()
  {
    // 後方互換: 述語未設定の場合、応答待ち中の受信メッセージは従来どおり応答として扱われる
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    var sendTask = client.SendAsync(Message.FromString("request", Encoding.UTF8), TimeSpan.FromSeconds(5));
    await TestWait.UntilSentAsync(transport, "request");

    transport.EnqueueReceiveData("EVENT: 通知のつもり");

    var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("EVENT: 通知のつもり", response.Text?.Trim());
  }

  [Fact]
  public async Task NotificationPredicate_CanBeSetAtRuntime()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    var notificationTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, msg) => notificationTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();

    // 実行時にプロパティで設定できること
    client.NotificationPredicate = msg => msg.Text?.StartsWith("EVENT:") == true;
    Assert.NotNull(client.NotificationPredicate);

    var sendTask = client.SendAsync(Message.FromString("request", Encoding.UTF8), TimeSpan.FromSeconds(5));
    await TestWait.UntilSentAsync(transport, "request");

    transport.EnqueueReceiveData("EVENT: 実行時設定");
    transport.EnqueueReceiveData("RESULT");

    var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("RESULT", response.Text?.Trim());
    Assert.Equal("EVENT: 実行時設定", await notificationTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  [Fact]
  public async Task NotificationPredicate_WithoutPendingRequest_DeliversAsUnsolicited()
  {
    var transport = new MockTransport();
    var config = CreateConfig(
        notificationPredicate: msg => msg.Text?.StartsWith("EVENT:") == true);
    await using var client = new TcpClient(config, transport);

    var notificationTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.OnMessageReceived += (_, msg) => notificationTcs.TrySetResult(msg.Text?.Trim() ?? "");

    await client.ConnectAsync();

    transport.EnqueueReceiveData("EVENT: 単独通知");

    Assert.Equal("EVENT: 単独通知", await notificationTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  [Fact]
  public async Task NotificationPredicate_ExceptionInPredicate_DoesNotBreakReceiveLoop()
  {
    var transport = new MockTransport();
    var config = CreateConfig(
        notificationPredicate: _ => throw new InvalidOperationException("predicate failure"));
    await using var client = new TcpClient(config, transport);

    await client.ConnectAsync();

    // 述語が例外を投げても受信ループは止まらず、メッセージは通知ではないものとして処理されること
    var sendTask = client.SendAsync(Message.FromString("request", Encoding.UTF8), TimeSpan.FromSeconds(5));
    await TestWait.UntilSentAsync(transport, "request");

    transport.EnqueueReceiveData("response");

    var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("response", response.Text?.Trim());
    Assert.True(client.IsConnected);
  }
}
