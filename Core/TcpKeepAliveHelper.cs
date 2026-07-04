using System.Net.Sockets;
using Dnbn.Configuration;

namespace Dnbn.Core;

/// <summary>
/// TCPレベルのキープアライブをソケットへ適用するヘルパー
/// </summary>
internal static class TcpKeepAliveHelper
{
  /// <summary>
  /// 設定に従いソケットへ SO_KEEPALIVE と関連パラメータを適用する。
  /// 設定が null または無効の場合は何もしない（従来動作）。
  /// </summary>
  /// <param name="socket">対象のソケット</param>
  /// <param name="config">TCPキープアライブ設定</param>
  public static void Apply(Socket socket, TcpKeepAliveConfig? config)
  {
    if (config == null || !config.Enabled)
    {
      return;
    }

    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

    // 詳細パラメータはOS/バージョンにより未対応の場合があるため、
    // 失敗しても基本のキープアライブ有効化だけで継続する
    TrySetTcpOption(socket, SocketOptionName.TcpKeepAliveTime, config.TimeSeconds);
    TrySetTcpOption(socket, SocketOptionName.TcpKeepAliveInterval, config.IntervalSeconds);
    TrySetTcpOption(socket, SocketOptionName.TcpKeepAliveRetryCount, config.RetryCount);
  }

  private static void TrySetTcpOption(Socket socket, SocketOptionName name, int value)
  {
    if (value <= 0)
    {
      return;
    }

    try
    {
      socket.SetSocketOption(SocketOptionLevel.Tcp, name, value);
    }
    catch (SocketException)
    {
      // このプラットフォームでは未対応
    }
    catch (PlatformNotSupportedException)
    {
      // このプラットフォームでは未対応
    }
  }
}
