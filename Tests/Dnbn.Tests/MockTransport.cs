using System.Threading.Channels;
using Dnbn.Core;

namespace Dnbn.Tests;

/// <summary>
/// テスト用 ITransport モック実装
/// </summary>
internal class MockTransport : ITransport
{
  private bool _connected;
  private Channel<byte[]> _receiveChannel = Channel.CreateUnbounded<byte[]>();
  private readonly List<byte[]> _sentData = new();
  private readonly object _sentLock = new();
  private Exception? _connectException;
  private byte[]? _responseOnNextSend;
  private bool _dropAfterNextConnect;
  private int _connectCalls;

  /// <summary>ConnectAsync が呼ばれた回数</summary>
  public int ConnectCalls => Volatile.Read(ref _connectCalls);

  /// <summary>
  /// 次の ConnectAsync 成功直後に IsConnected を false にする（1回だけ）。
  /// 「接続完了直後〜受信ループが最初の ReceiveAsync に入る前のNW障害」を
  /// 決定的に再現するために使用する。受信チャンネルは閉じない
  /// （閉じると 0 バイト受信の通常NW障害パスになってしまうため）。
  /// </summary>
  public void DropConnectionAfterNextConnect() => _dropAfterNextConnect = true;

  /// <inheritdoc />
  public bool IsConnected => _connected;

  /// <summary>送信されたデータの一覧</summary>
  public IReadOnlyList<byte[]> SentData
  {
    get
    {
      lock (_sentLock) return _sentData.ToList();
    }
  }

  /// <summary>接続時に発生させる例外を設定</summary>
  public void SetConnectException(Exception ex) => _connectException = ex;

  /// <summary>次の送信処理が戻る前に受信データを到着させる。</summary>
  public void RespondOnNextSend(string text, string terminator = "\n")
  {
    lock (_sentLock)
    {
      _responseOnNextSend = System.Text.Encoding.UTF8.GetBytes(text + terminator);
    }
  }

  /// <inheritdoc />
  public Task ConnectAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    Interlocked.Increment(ref _connectCalls);
    if (_connectException != null)
    {
      var ex = _connectException;
      _connectException = null;
      throw ex;
    }
    if (_receiveChannel.Reader.Completion.IsCompleted)
    {
      _receiveChannel = Channel.CreateUnbounded<byte[]>();
    }
    _connected = true;
    if (_dropAfterNextConnect)
    {
      // 接続成功の直後に切断された状態を再現（デッドウィンドウ再現用）
      _dropAfterNextConnect = false;
      _connected = false;
    }
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    _connected = false;
    // チャンネルを完了させて ReceiveAsync を 0 バイトで終了させる
    _receiveChannel.Writer.TryComplete();
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    byte[]? response;
    lock (_sentLock)
    {
      _sentData.Add(data);
      response = _responseOnNextSend;
      _responseOnNextSend = null;
    }
    if (response != null)
    {
      _receiveChannel.Writer.TryWrite(response);
      // 受信ループが送信処理より先に進む競合を決定的に再現する。
      await Task.Yield();
    }
  }

  /// <inheritdoc />
  public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
  {
    try
    {
      var data = await _receiveChannel.Reader.ReadAsync(cancellationToken);
      var bytesToCopy = Math.Min(data.Length, count);
      Array.Copy(data, 0, buffer, offset, bytesToCopy);
      return bytesToCopy;
    }
    catch (ChannelClosedException)
    {
      return 0; // 切断を示す
    }
  }

  /// <summary>受信データをキューに追加</summary>
  public void EnqueueReceiveData(byte[] data)
  {
    _receiveChannel.Writer.TryWrite(data);
  }

  /// <summary>受信データ（文字列）をキューに追加</summary>
  public void EnqueueReceiveData(string text, string terminator = "\n")
  {
    var bytes = System.Text.Encoding.UTF8.GetBytes(text + terminator);
    EnqueueReceiveData(bytes);
  }

  /// <summary>切断をシミュレート（0バイト受信で ReceiveLoop に通知）</summary>
  public void SimulateDisconnect()
  {
    _connected = false;
    _receiveChannel.Writer.TryComplete();
  }
}
