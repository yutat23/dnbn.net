using System.Net.Sockets;

namespace Dnbn.Core;

/// <summary>
/// TCPトランスポート実装
/// </summary>
public class TcpTransport : ITransport, IDisposable
{
  private System.Net.Sockets.TcpClient? _tcpClient;
  private NetworkStream? _stream;
  private readonly string _host;
  private readonly int _port;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="host">接続先ホスト名</param>
  /// <param name="port">接続先ポート番号</param>
  public TcpTransport(string host, int port)
  {
    _host = host;
    _port = port;
  }

  /// <summary>
  /// 接続状態
  /// </summary>
  public bool IsConnected => _tcpClient?.Connected ?? false;

  /// <summary>
  /// サーバーに接続
  /// </summary>
  public async Task ConnectAsync()
  {
    if (IsConnected)
    {
      return;
    }

    _tcpClient = new System.Net.Sockets.TcpClient();
    await _tcpClient.ConnectAsync(_host, _port);
    _stream = _tcpClient.GetStream();
  }

  /// <summary>
  /// サーバーから切断
  /// </summary>
  public async Task DisconnectAsync()
  {
    if (_stream != null)
    {
      await _stream.DisposeAsync();
      _stream = null;
    }

    _tcpClient?.Dispose();
    _tcpClient = null;
  }

  /// <summary>
  /// データを送信
  /// </summary>
  /// <param name="data">送信するデータ</param>
  public async Task SendAsync(byte[] data)
  {
    if (_stream == null || !IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    await _stream.WriteAsync(data);
    await _stream.FlushAsync();
  }

  /// <summary>
  /// データを受信
  /// </summary>
  /// <param name="buffer">受信バッファ</param>
  /// <param name="offset">オフセット</param>
  /// <param name="count">受信する最大バイト数</param>
  /// <returns>実際に受信したバイト数</returns>
  public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
  {
    if (_stream == null || !IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    return await _stream.ReadAsync(buffer, offset, count);
  }

  /// <summary>
  /// リソースを解放
  /// </summary>
  public void Dispose()
  {
    DisconnectAsync().GetAwaiter().GetResult();
  }
}

