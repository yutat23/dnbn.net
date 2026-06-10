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

  /// <inheritdoc />
  public Task ConnectAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
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
  public Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    lock (_sentLock)
    {
      _sentData.Add(data);
    }
    return Task.CompletedTask;
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
