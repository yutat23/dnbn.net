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
    TrySetTcpOption(socket, TcpKeepAliveTime, config.TimeSeconds);
    TrySetTcpOption(socket, TcpKeepAliveInterval, config.IntervalSeconds);
    TrySetTcpOption(socket, TcpKeepAliveRetryCount, config.RetryCount);
  }

#if NETSTANDARD2_0
  // これらの列挙値は.NET Core 3.0で追加されたため数値で指定する。
  // Windows(10 1709以降)ではsetsockoptへそのまま渡り機能し、
  // 未対応環境ではSocketException/PlatformNotSupportedExceptionが握りつぶされる
  private const SocketOptionName TcpKeepAliveTime = (SocketOptionName)3;
  private const SocketOptionName TcpKeepAliveInterval = (SocketOptionName)17;
  private const SocketOptionName TcpKeepAliveRetryCount = (SocketOptionName)16;
#else
  private const SocketOptionName TcpKeepAliveTime = SocketOptionName.TcpKeepAliveTime;
  private const SocketOptionName TcpKeepAliveInterval = SocketOptionName.TcpKeepAliveInterval;
  private const SocketOptionName TcpKeepAliveRetryCount = SocketOptionName.TcpKeepAliveRetryCount;
#endif

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
