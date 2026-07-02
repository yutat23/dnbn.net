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
  /// キープアライブ応答かどうかを判定する述語。未設定の場合は従来どおり最初の受信メッセージを応答として扱う。
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore]
  [System.Xml.Serialization.XmlIgnore]
  public Func<Dnbn.Models.Message, bool>? ResponsePredicate { get; set; }

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
      ResponsePredicate = ResponsePredicate
    };
  }
}
