namespace Dnbn.Configuration;

/// <summary>
/// TCPレベル（ソケットオプション SO_KEEPALIVE）のキープアライブ設定。
/// アプリケーションレベルの電文送信によるキープアライブは <see cref="KeepAliveConfig"/> を使用する。
/// 未設定（null）または Enabled=false の場合は従来どおりOSの既定動作となる。
/// </summary>
public class TcpKeepAliveConfig
{
  /// <summary>
  /// TCPキープアライブを有効にするか
  /// </summary>
  public bool Enabled { get; set; } = false;

  /// <summary>
  /// 無通信状態が続いてから最初のキープアライブプローブを送信するまでの時間（秒）
  /// </summary>
  public int TimeSeconds { get; set; } = 60;

  /// <summary>
  /// キープアライブプローブの再送間隔（秒）
  /// </summary>
  public int IntervalSeconds { get; set; } = 10;

  /// <summary>
  /// 応答がない場合に接続断と判定するまでのプローブ再送回数
  /// </summary>
  public int RetryCount { get; set; } = 5;

  /// <summary>
  /// この設定の複製を作成
  /// </summary>
  public TcpKeepAliveConfig Clone()
  {
    return new TcpKeepAliveConfig
    {
      Enabled = Enabled,
      TimeSeconds = TimeSeconds,
      IntervalSeconds = IntervalSeconds,
      RetryCount = RetryCount
    };
  }
}
