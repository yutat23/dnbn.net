using System.Net.Sockets;
using Dnbn.Configuration;

namespace Dnbn.Core;

/// <summary>
/// TCPトランスポート実装
/// </summary>
public class TcpTransport : ITransport, IDisposable, IAsyncDisposable
{
  private System.Net.Sockets.TcpClient? _tcpClient;
  private NetworkStream? _stream;
  private readonly string _host;
  private readonly int _port;
  private readonly TcpKeepAliveConfig? _tcpKeepAlive;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="host">接続先ホスト名</param>
  /// <param name="port">接続先ポート番号</param>
  public TcpTransport(string host, int port)
      : this(host, port, null)
  {
  }

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="host">接続先ホスト名</param>
  /// <param name="port">接続先ポート番号</param>
  /// <param name="tcpKeepAlive">TCPレベルのキープアライブ設定（null=OSの既定動作）</param>
  public TcpTransport(string host, int port, TcpKeepAliveConfig? tcpKeepAlive)
  {
    _host = host;
    _port = port;
    _tcpKeepAlive = tcpKeepAlive;
  }

  /// <summary>
  /// 接続状態。最後のI/O時点のスナップショットであり、現在の接続生存性を保証しない
  /// </summary>
  public bool IsConnected => _tcpClient?.Connected ?? false;

  /// <summary>
  /// サーバーに接続
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <remarks>接続済みと判定された場合は何もしない。強制的に再接続する場合は、先に<see cref="DisconnectAsync"/>を呼ぶこと</remarks>
  public async Task ConnectAsync(CancellationToken cancellationToken = default)
  {
    if (IsConnected)
    {
      return;
    }

    cancellationToken.ThrowIfCancellationRequested();

    // Connected=false は「解放済み」を意味しない。接続失敗やI/Oエラー検出後の
    // ソケット/ストリームが残っている場合があるため、新しい接続で上書きする前に
    // 必ず既存リソースを破棄する。
    await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

    var tcpClient = new System.Net.Sockets.TcpClient();
    try
    {
#if NETSTANDARD2_0
      // CancellationToken付きConnectAsyncは.NET 5以降のみ。
      // キャンセル時はソケットを閉じて接続試行を中断させる
      using (cancellationToken.Register(() => tcpClient.Close()))
      {
        try
        {
          await tcpClient.ConnectAsync(_host, _port).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
          throw new OperationCanceledException(cancellationToken);
        }
      }
#else
      await tcpClient.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
#endif
      TcpKeepAliveHelper.Apply(tcpClient.Client, _tcpKeepAlive);
      var stream = tcpClient.GetStream();

      // 接続と初期化がすべて成功してからフィールドへ公開する。
      _stream = stream;
      _tcpClient = tcpClient;
    }
    catch
    {
      // 接続失敗・キャンセル・KeepAlive設定失敗のいずれでも、
      // 作成したソケットをファイナライザ任せにしない。
      tcpClient.Dispose();
      throw;
    }
  }

  /// <summary>
  /// サーバーから切断
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    if (_stream != null)
    {
#if NETSTANDARD2_0
      _stream.Dispose();
      await Task.CompletedTask;
#else
      await _stream.DisposeAsync();
#endif
      _stream = null;
    }

    _tcpClient?.Dispose();
    _tcpClient = null;
  }

  /// <summary>
  /// データを送信
  /// </summary>
  /// <param name="data">送信するデータ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
  {
    if (_stream == null || !IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    cancellationToken.ThrowIfCancellationRequested();

    await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
    await _stream.FlushAsync(cancellationToken);
  }

  /// <summary>
  /// データを受信
  /// </summary>
  /// <param name="buffer">受信バッファ</param>
  /// <param name="offset">オフセット</param>
  /// <param name="count">受信する最大バイト数</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>実際に受信したバイト数</returns>
  public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
  {
    if (_stream == null || !IsConnected)
    {
      throw new InvalidOperationException("Not connected");
    }

    return await _stream.ReadAsync(buffer, offset, count, cancellationToken);
  }

  /// <summary>
  /// リソースを非同期に解放
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    await DisconnectAsync().ConfigureAwait(false);
  }

  /// <summary>
  /// リソースを解放（互換性維持のため残存。可能であれば DisposeAsync を使用してください）
  /// </summary>
  public void Dispose()
  {
    // ConfigureAwait(false)を使用してデッドロックを回避
    DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
  }
}
