namespace Dnbn.Configuration;

/// <summary>
/// キープアライブ設定
/// </summary>
public class KeepAliveConfig
{
  /// <summary>
  /// キープアライブを有効にするか
  /// </summary>
  public bool Enabled { get; set; } = false;

  /// <summary>
  /// キープアライブ間隔（秒）
  /// </summary>
  public int IntervalSeconds { get; set; } = 30;

  /// <summary>
  /// キープアライブメッセージ
  /// </summary>
  public string Message { get; set; } = string.Empty;

  /// <summary>
  /// キープアライブ応答かどうかを判定する述語。
  /// 未設定の場合、KeepAlive応答は他の要求と同じFIFO順で「次に届いた応答」として相関される。
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore]
  [System.Xml.Serialization.XmlIgnore]
  public Func<Dnbn.Models.Message, bool>? ResponsePredicate { get; set; }

  /// <summary>
  /// KeepAlive応答タイムアウト時に接続を切断するか。既定値: true。
  /// trueの場合、タイムアウトをNW障害として扱って切断し、ConnectionRetryPolicy設定時は自動再接続する。
  /// falseの場合は接続を維持するが、遅れて届いたKeepAlive応答が後続の通常要求の応答として
  /// 誤って相関される可能性がある（応答の内容で区別できないプロトコルでは true を推奨）。
  /// </summary>
  public bool DisconnectOnTimeout { get; set; } = true;

  /// <summary>
  /// この設定の複製を作成
  /// </summary>
  public KeepAliveConfig Clone()
  {
    return new KeepAliveConfig
    {
      Enabled = Enabled,
      IntervalSeconds = IntervalSeconds,
      Message = Message,
      ResponsePredicate = ResponsePredicate,
      DisconnectOnTimeout = DisconnectOnTimeout
    };
  }
}
