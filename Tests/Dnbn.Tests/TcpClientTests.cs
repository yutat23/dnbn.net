using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;

namespace Dnbn.Tests;

/// <summary>
/// TcpClient のユニットテスト（MockTransport を使用）
/// </summary>
public class TcpClientTests
{
  private static TcpClient CreateClient(MockTransport transport, int timeoutMs = 3000, string terminator = "\n")
  {
    var config = new ClientConfig
    {
      Name = "TestClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 9999,
      Encoding = "UTF-8",
      MessageTerminator = terminator,
      TimeoutMilliseconds = timeoutMs
    };
    return new TcpClient(config, transport);
  }

  private static TcpClient CreateClient(MockTransport transport, ClientConfig config)
      => new(config, transport);

  // ---------------------------------------------------------------------------
  // 接続 / 切断テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task ConnectAsync_SetsIsConnected_True()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    await client.ConnectAsync();

    Assert.True(client.IsConnected);
  }

  [Fact]
  public async Task ConnectAsync_AlreadyConnected_DoesNotReconnect()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    await client.ConnectAsync();
    await client.ConnectAsync(); // 2回目は何もしない

    Assert.True(client.IsConnected);
  }

  [Fact]
  public async Task DisconnectAsync_SetsIsConnected_False()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    await client.ConnectAsync();
    await client.DisconnectAsync();

    Assert.False(client.IsConnected);
  }

  [Fact]
  public async Task ConnectAsync_AfterDisconnect_ReinitializesInternalState()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    await client.ConnectAsync();
    await client.DisconnectAsync();
    await client.ConnectAsync();

    Assert.True(client.IsConnected);
  }

  [Fact]
  public async Task ConnectAsync_WithCancelledToken_ThrowsOperationCanceledException()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => client.ConnectAsync(cts.Token));
  }

  [Fact]
  public async Task DisconnectAsync_CancelsPendingRequests()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport, timeoutMs: 10000);

    await client.ConnectAsync();

    // バックグラウンドで送信（応答は来ない）
    var sendTask = client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromSeconds(10));

    // 少し待ってから切断
    await Task.Delay(50);
    await client.DisconnectAsync();

    // 送信タスクはキャンセルまたはタイムアウト例外で終了すること
    await Assert.ThrowsAnyAsync<Exception>(() => sendTask);
  }

  // ---------------------------------------------------------------------------
  // 送受信テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task SendAsync_ReturnsResponse_WhenResponseArrives()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    await client.ConnectAsync();

    // バックグラウンドで応答を返す
    _ = Task.Run(async () =>
    {
      await Task.Delay(50);
      transport.EnqueueReceiveData("pong");
    });

    var response = await client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromSeconds(3));

    Assert.Equal("pong", response.Text?.Trim());
  }

  [Fact]
  public async Task SendAsync_ResponseIsNotConsumedByKeepAlive_WhenPredicateDoesNotMatch()
  {
    var transport = new MockTransport();
    var config = new ClientConfig
    {
      Name = "TestClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 9999,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = 1000,
      KeepAlive = new KeepAliveConfig
      {
        Enabled = true,
        IntervalSeconds = 1,
        Message = "keepalive",
        ResponsePredicate = message => message.Text?.Trim() == "keepalive_ack"
      }
    };
    await using var client = CreateClient(transport, config);

    await client.ConnectAsync();

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (!transport.SentData.Any(data => Encoding.UTF8.GetString(data).Contains("keepalive")))
    {
      await Task.Delay(20, timeout.Token);
    }

    var responseTask = client.SendAsync(
        Message.FromString("ping", Encoding.UTF8),
        TimeSpan.FromSeconds(1));

    while (!transport.SentData.Any(data => Encoding.UTF8.GetString(data).Contains("ping")))
    {
      await Task.Delay(20, timeout.Token);
    }

    transport.EnqueueReceiveData("pong");

    var response = await responseTask;
    Assert.Equal("pong", response.Text?.Trim());
  }

  [Fact]
  public async Task SendAsync_ThrowsTimeoutException_WhenNoResponse()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    await client.ConnectAsync();

    // 応答を返さない
    await Assert.ThrowsAsync<TimeoutException>(
        () => client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromMilliseconds(100)));
  }

  [Fact]
  public async Task SendAsync_ThrowsInvalidOperationException_WhenNotConnected()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    // 接続せずに送信
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => client.SendAsync(Message.FromString("ping", Encoding.UTF8)));
  }

  [Fact]
  public async Task SendAndWaitAsync_WithPredicate_MatchesCorrectResponse()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    await client.ConnectAsync();

    // "ACK" を含む応答のみマッチするように設定
    _ = Task.Run(async () =>
    {
      await Task.Delay(50);
      transport.EnqueueReceiveData("ACK_response");
    });

    var response = await client.SendAndWaitAsync(
        "ping",
        msg => msg.Text?.Contains("ACK") == true,
        TimeSpan.FromSeconds(3));

    Assert.Contains("ACK", response.Text);
  }

  [Fact]
  public async Task MultipleRequests_ProcessedInFifoOrder()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    await client.ConnectAsync();

    // 3つのリクエストを順番に送信（送信を確認してから応答を返し、タイミング依存を排除）
    for (int i = 1; i <= 3; i++)
    {
      var sendTask = client.SendAsync(Message.FromString($"req{i}", Encoding.UTF8), TimeSpan.FromSeconds(3));
      await TestWait.UntilSentAsync(transport, $"req{i}");
      transport.EnqueueReceiveData($"response{i}");

      var response = await sendTask;
      Assert.Equal($"response{i}", response.Text?.Trim());
    }
  }

  // ---------------------------------------------------------------------------
  // ConnectionInfo テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task ConnectionInfo_IsConnected_ReflectsState()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);

    Assert.False(client.ConnectionInfo.IsConnected);

    await client.ConnectAsync();
    Assert.True(client.ConnectionInfo.IsConnected);

    await client.DisconnectAsync();
    Assert.False(client.ConnectionInfo.IsConnected);
  }

  [Fact]
  public async Task ConnectionInfo_DoesNotDeadlock_WhenCalledConcurrently()
  {
    var transport = new MockTransport();
    await using var client = CreateClient(transport);
    await client.ConnectAsync();

    // ConnectionInfo への並行アクセスでデッドロックが発生しないことを確認
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var tasks = Enumerable.Range(0, 20)
        .Select(_ => Task.Run(() => { var _ = client.ConnectionInfo; }))
        .ToArray();

    await Task.WhenAll(tasks).WaitAsync(timeoutCts.Token);
  }

  // ---------------------------------------------------------------------------
  // IAsyncDisposable テスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task DisposeAsync_CanBeCalledTwice_WithoutException()
  {
    var transport = new MockTransport();
    var client = CreateClient(transport);
    await client.ConnectAsync();

    await client.DisposeAsync();
    await client.DisposeAsync(); // 2回目は何もしない
  }

  [Fact]
  public async Task DisposeAsync_DisconnectsClient()
  {
    var transport = new MockTransport();
    var client = CreateClient(transport);
    await client.ConnectAsync();

    await client.DisposeAsync();

    Assert.False(client.IsConnected);
  }
}
