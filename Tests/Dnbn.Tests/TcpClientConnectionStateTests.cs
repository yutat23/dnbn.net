using System.Net.Sockets;
using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Tests;

/// <summary>
/// ConnectionState と OnConnectionStateChanged の状態遷移テスト
/// </summary>
public class TcpClientConnectionStateTests
{
  private static ClientConfig CreateConfig(RetryPolicy? connectionRetryPolicy = null)
  {
    return new ClientConfig
    {
      Name = "StateTestClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 9999,
      Encoding = "UTF-8",
      MessageTerminator = "\n",
      TimeoutMilliseconds = 3000,
      ConnectionRetryPolicy = connectionRetryPolicy
    };
  }

  /// <summary>状態遷移をスレッドセーフに記録するヘルパー</summary>
  private sealed class TransitionRecorder
  {
    private readonly List<(ConnectionState previous, ConnectionState current)> _transitions = new();
    private readonly object _lock = new();

    public void Attach(ITcpClient client)
    {
      client.OnConnectionStateChanged += (_, e) =>
      {
        lock (_lock)
        {
          _transitions.Add(e);
        }
      };
    }

    public IReadOnlyList<(ConnectionState previous, ConnectionState current)> Transitions
    {
      get
      {
        lock (_lock)
        {
          return _transitions.ToList();
        }
      }
    }
  }

  [Fact]
  public async Task InitialState_IsDisconnected()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    Assert.Equal(ConnectionState.Disconnected, client.State);
  }

  [Fact]
  public async Task StateTransitions_OnConnectAndDisconnect()
  {
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);
    var recorder = new TransitionRecorder();
    recorder.Attach(client);

    await client.ConnectAsync();
    Assert.Equal(ConnectionState.Connected, client.State);
    Assert.Equal(
        new[]
        {
          (ConnectionState.Disconnected, ConnectionState.Connecting),
          (ConnectionState.Connecting, ConnectionState.Connected),
        },
        recorder.Transitions);

    await client.DisconnectAsync();
    Assert.Equal(ConnectionState.Disconnected, client.State);
    Assert.Equal((ConnectionState.Connected, ConnectionState.Disconnected), recorder.Transitions[^1]);
  }

  [Fact]
  public async Task StateTransitions_OnAutoReconnect()
  {
    var transport = new MockTransport();
    var policy = new RetryPolicy { MaxRetryCount = 5, InitialDelayMs = 10 };
    await using var client = new TcpClient(CreateConfig(policy), transport);
    var recorder = new TransitionRecorder();
    recorder.Attach(client);

    await client.ConnectAsync();

    // NW障害 → 自動再接続で復帰するまで待つ
    transport.SimulateDisconnect();
    await TestWait.UntilAsync(() =>
        client.State == ConnectionState.Connected &&
        recorder.Transitions.Any(t => t.current == ConnectionState.Reconnecting),
        timeoutMs: 5000);

    // 切断 → 再接続中 → 接続済み の遷移が観測できること
    Assert.Equal(
        new[]
        {
          (ConnectionState.Disconnected, ConnectionState.Connecting),
          (ConnectionState.Connecting, ConnectionState.Connected),
          (ConnectionState.Connected, ConnectionState.Disconnected),
          (ConnectionState.Disconnected, ConnectionState.Reconnecting),
          (ConnectionState.Reconnecting, ConnectionState.Connected),
        },
        recorder.Transitions);
  }

  [Fact]
  public async Task ConnectFailure_WithoutRetry_EndsDisconnected()
  {
    var transport = new MockTransport();
    transport.SetConnectException(new SocketException((int)SocketError.ConnectionRefused));
    await using var client = new TcpClient(CreateConfig(), transport);
    var recorder = new TransitionRecorder();
    recorder.Attach(client);

    await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync());

    Assert.Equal(ConnectionState.Disconnected, client.State);
    Assert.Equal(
        new[]
        {
          (ConnectionState.Disconnected, ConnectionState.Connecting),
          (ConnectionState.Connecting, ConnectionState.Disconnected),
        },
        recorder.Transitions);
  }

  [Fact]
  public async Task IntentionalDisconnect_DoesNotEnterReconnecting()
  {
    var transport = new MockTransport();
    var policy = new RetryPolicy { MaxRetryCount = 5, InitialDelayMs = 10 };
    await using var client = new TcpClient(CreateConfig(policy), transport);
    var recorder = new TransitionRecorder();
    recorder.Attach(client);

    await client.ConnectAsync();
    await client.DisconnectAsync();

    // 意図的な切断では Reconnecting に遷移しないこと
    await Task.Delay(200);
    Assert.Equal(ConnectionState.Disconnected, client.State);
    Assert.DoesNotContain(recorder.Transitions, t => t.current == ConnectionState.Reconnecting);
  }

  [Fact]
  public async Task ConcurrentConnectAsync_ConnectsOnlyOnce()
  {
    // 回帰テスト: ConnectAsync に並行呼び出しガードがなく、
    // 同時に複数回呼ぶと二重に接続試行・ループ起動が起きることがあった
    var transport = new MockTransport();
    await using var client = new TcpClient(CreateConfig(), transport);

    int connectedEvents = 0;
    client.OnConnected += (_, _) => Interlocked.Increment(ref connectedEvents);

    var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() => client.ConnectAsync())).ToArray();
    await Task.WhenAll(tasks);

    Assert.True(client.IsConnected);
    Assert.Equal(1, transport.ConnectCalls);
    Assert.Equal(1, connectedEvents);

    // 接続後に送受信が正常に機能すること（ループが二重起動していれば応答処理が壊れる）
    var sendTask = client.SendAsync(Message.FromString("ping", Encoding.UTF8), TimeSpan.FromSeconds(3));
    await TestWait.UntilSentAsync(transport, "ping");
    transport.EnqueueReceiveData("pong");
    var response = await sendTask;
    Assert.Equal("pong", response.Text?.Trim());
  }
}
