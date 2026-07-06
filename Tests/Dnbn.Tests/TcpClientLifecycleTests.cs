using System.Net.Sockets;
using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Tests;

/// <summary>
/// TcpClient のライフサイクル（接続・切断・再接続・自動再接続）の機能テスト
/// </summary>
public class TcpClientLifecycleTests
{
  private static ClientConfig CreateConfig(int timeoutMs = 3000, RetryPolicy? connectionRetryPolicy = null)
  {
    return new ClientConfig
    {
      Name = "LifecycleTestClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 9999,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = timeoutMs,
      ConnectionRetryPolicy = connectionRetryPolicy
    };
  }

  /// <summary>
  /// 受信ループが ReceiveAsync に到達したことを検知できるトランスポートラッパー。
  /// 「受信待ち中のNW障害」のタイミングを決定的に制御するのに使用する
  /// （受信ループ起動前の障害は AutoReconnect_WhenConnectionDropsBeforeReceiveLoopStarts が対象）。
  /// </summary>
  private sealed class ReceiveTrackingTransport : ITransport
  {
    private readonly MockTransport _inner;
    private int _receiveCalls;

    public ReceiveTrackingTransport(MockTransport inner) => _inner = inner;

    public int ReceiveCalls => Volatile.Read(ref _receiveCalls);
    public bool IsConnected => _inner.IsConnected;
    public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _inner.DisconnectAsync(ct);
    public Task SendAsync(byte[] data, CancellationToken ct = default) => _inner.SendAsync(data, ct);

    public Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
      Interlocked.Increment(ref _receiveCalls);
      return _inner.ReceiveAsync(buffer, offset, count, ct);
    }
  }

  // ---------------------------------------------------------------------------
  // 手動再接続（Connect → Disconnect → Connect）
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task Reconnect_AfterDisconnect_SendAndReceiveWork()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    // DisconnectAsync は受信ループの完了を待つため、整定待ちなしで即時再接続できること
    await client.ConnectAsync();
    await client.DisconnectAsync();
    await client.ConnectAsync();

    // 再接続後に送信ループ・受信ループが機能していること（IsConnected だけでは不十分）
    var sendTask = client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromSeconds(3));
    await TestWait.UntilSentAsync(transport, "ping");
    transport.EnqueueReceiveData("pong");

    var response = await sendTask;
    Assert.Equal("pong", response.Text?.Trim());
  }

  [Fact]
  public async Task Reconnect_MultipleCycles_RemainsFunctional()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    for (int i = 0; i < 3; i++)
    {
      await client.ConnectAsync();
      Assert.True(client.IsConnected);

      var sendTask = client.SendAsync(Message.FromString($"req{i}", Encoding.UTF8), TimeSpan.FromSeconds(3));
      await TestWait.UntilSentAsync(transport, $"req{i}");
      transport.EnqueueReceiveData($"res{i}");
      var response = await sendTask;
      Assert.Equal($"res{i}", response.Text?.Trim());

      await client.DisconnectAsync();
      Assert.False(client.IsConnected);
    }
  }

  // ---------------------------------------------------------------------------
  // 接続リトライ
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task ConnectAsync_WithRetryPolicy_RetriesAfterSocketException()
  {
    var transport = new MockTransport();
    transport.SetConnectException(new SocketException((int)SocketError.ConnectionRefused));

    var policy = new RetryPolicy { MaxRetryCount = 3, InitialDelayMs = 10 };
    await using var client = new TcpClient(CreateConfig(connectionRetryPolicy: policy), transport);

    // 1回目は失敗するが、リトライで接続成功すること
    await client.ConnectAsync();

    Assert.True(client.IsConnected);
  }

  [Fact]
  public async Task ConnectAsync_WithoutRetryPolicy_ThrowsOnConnectFailure()
  {
    var transport = new MockTransport();
    transport.SetConnectException(new SocketException((int)SocketError.ConnectionRefused));

    await using var client = new TcpClient(CreateConfig(), transport);

    await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync());
    Assert.False(client.IsConnected);
  }

  // ---------------------------------------------------------------------------
  // 自動再接続（NW障害時）
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task AutoReconnect_OnNetworkError_ReconnectsAndWorks()
  {
    var mock = new MockTransport();
    var transport = new ReceiveTrackingTransport(mock);
    var policy = new RetryPolicy { MaxRetryCount = 5, InitialDelayMs = 10 };
    await using var client = new TcpClient(CreateConfig(connectionRetryPolicy: policy), transport);

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

    // 受信ループが受信待ちに入ったのを確認してからNW障害をシミュレート → 自動再接続されること
    await TestWait.UntilAsync(() => transport.ReceiveCalls >= 1);
    mock.SimulateDisconnect();
    await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.True(client.IsConnected);

    // 再接続後に送受信が機能すること
    var sendTask = client.SendAsync(Message.FromString("after_reconnect", Encoding.UTF8), TimeSpan.FromSeconds(3));
    await TestWait.UntilSentAsync(mock, "after_reconnect");
    mock.EnqueueReceiveData("ok");
    var response = await sendTask;
    Assert.Equal("ok", response.Text?.Trim());
  }

  [Fact]
  public async Task AutoReconnect_WhenConnectionDropsBeforeReceiveLoopStarts()
  {
    // デッドウィンドウの回帰テスト:
    // 接続完了直後（受信ループが最初の ReceiveAsync に入る前）にNW障害が起きた場合でも
    // 自動再接続が発動すること
    var transport = new MockTransport();
    transport.DropConnectionAfterNextConnect();

    var policy = new RetryPolicy { MaxRetryCount = 5, InitialDelayMs = 10 };
    await using var client = new TcpClient(CreateConfig(connectionRetryPolicy: policy), transport);

    var reconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int connectedCount = 0;
    client.OnConnected += (_, _) =>
    {
      if (Interlocked.Increment(ref connectedCount) >= 2)
      {
        reconnectedTcs.TrySetResult();
      }
    };

    // 1回目の接続は成功直後に切断される（transportの設定による）
    await client.ConnectAsync();

    // 自動再接続により復帰すること
    await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.True(client.IsConnected);

    // 復帰後に送受信が機能すること
    var sendTask = client.SendAsync(Message.FromString("recovered", Encoding.UTF8), TimeSpan.FromSeconds(3));
    await TestWait.UntilSentAsync(transport, "recovered");
    transport.EnqueueReceiveData("ok");
    var response = await sendTask;
    Assert.Equal("ok", response.Text?.Trim());
  }

  [Fact]
  public async Task AutoReconnect_NotTriggered_OnIntentionalDisconnect()
  {
    var transport = new MockTransport();
    var policy = new RetryPolicy { MaxRetryCount = 5, InitialDelayMs = 10 };
    await using var client = new TcpClient(CreateConfig(connectionRetryPolicy: policy), transport);

    int connectedCount = 0;
    client.OnConnected += (_, _) => Interlocked.Increment(ref connectedCount);

    await client.ConnectAsync();
    await client.DisconnectAsync(); // 意図的な切断

    // 自動再接続が走らないこと（リトライ間隔より十分長く待つ）
    await Task.Delay(300);

    Assert.False(client.IsConnected);
    Assert.Equal(1, connectedCount);
  }

  [Fact]
  public async Task NetworkError_PendingRequestFails()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await client.ConnectAsync();

    // 応答待ちの状態でNW障害が発生 → リクエストは例外で終了すること（ハングしない）
    var sendTask = client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromMilliseconds(500));
    await TestWait.UntilSentAsync(transport, "ping");

    transport.SimulateDisconnect();

    await Assert.ThrowsAnyAsync<Exception>(() => sendTask.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  // ---------------------------------------------------------------------------
  // WaitForConnectionAsync
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task WaitForConnectionAsync_ReturnsImmediately_WhenAlreadyConnected()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);
    await client.ConnectAsync();

    // 既に接続済みなら即座に完了すること
    await client.WaitForConnectionAsync(TimeSpan.FromMilliseconds(100));
  }

  [Fact]
  public async Task WaitForConnectionAsync_Completes_WhenConnectedLater()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    var waitTask = client.WaitForConnectionAsync(TimeSpan.FromSeconds(3));
    await Task.Delay(50);
    await client.ConnectAsync();

    await waitTask.WaitAsync(TimeSpan.FromSeconds(3));
  }

  [Fact]
  public async Task WaitForConnectionAsync_ThrowsTimeoutException_WhenNotConnected()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    await Assert.ThrowsAsync<TimeoutException>(
        () => client.WaitForConnectionAsync(TimeSpan.FromMilliseconds(100)));
  }

  [Fact]
  public async Task WaitForConnectionAsync_ThrowsOperationCanceled_WhenCallerCancels()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);
    using var cts = new CancellationTokenSource(50);

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => client.WaitForConnectionAsync(TimeSpan.FromSeconds(10), cts.Token));
  }

  // ---------------------------------------------------------------------------
  // Dispose と保留中リクエスト
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task DisposeAsync_CancelsPendingRequest()
  {
    var transport = new MockTransport();
    var client = new TcpClient(CreateConfig(), transport);
    await client.ConnectAsync();

    var sendTask = client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromSeconds(10));
    await TestWait.UntilSentAsync(transport, "ping");

    await client.DisposeAsync();

    // 保留中のリクエストはタイムアウトを待たずにキャンセルされること
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => sendTask.WaitAsync(TimeSpan.FromSeconds(3)));
  }

  [Fact]
  public async Task DisconnectAsync_WhenNeverConnected_DoesNotThrow()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    // 未接続状態での切断は例外を出さず正常終了すること
    await client.DisconnectAsync();

    Assert.False(client.IsConnected);
  }
}
