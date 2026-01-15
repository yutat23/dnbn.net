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

  public TcpTransport(string host, int port)
  {
    _host = host;
    _port = port;
  }

  public bool IsConnected => _tcpClient?.Connected ?? false;

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

  public async Task SendAsync(byte[] data)
  {
    if (_stream == null || !IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    await _stream.WriteAsync(data);
    await _stream.FlushAsync();
  }

  public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
  {
    if (_stream == null || !IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    return await _stream.ReadAsync(buffer, offset, count);
  }

  public void Dispose()
  {
    DisconnectAsync().GetAwaiter().GetResult();
  }
}

