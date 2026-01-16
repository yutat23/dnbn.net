namespace Dnbn.Models;

/// <summary>
/// TCP伝聞メッセージを表すクラス
/// </summary>
public class Message
{
  /// <summary>
  /// メッセージの生データ（バイト配列）
  /// </summary>
  public byte[] RawData { get; set; } = Array.Empty<byte>();

  /// <summary>
  /// メッセージの文字列表現（エンコーディング変換後）
  /// </summary>
  public string? Text { get; set; }

  /// <summary>
  /// メッセージコード（プロトコルヘッダから抽出可能）
  /// </summary>
  public string? Code { get; set; }

  /// <summary>
  /// メッセージのタイムスタンプ
  /// </summary>
  public DateTime Timestamp { get; set; } = DateTime.UtcNow;

  /// <summary>
  /// 追加のメタデータ
  /// </summary>
  public Dictionary<string, object> Metadata { get; set; } = new();

  /// <summary>
  /// 文字列からメッセージを作成
  /// </summary>
  public static Message FromString(string text, System.Text.Encoding encoding)
  {
    return new Message
    {
      Text = text,
      RawData = encoding.GetBytes(text),
      Timestamp = DateTime.UtcNow
    };
  }

  /// <summary>
  /// バイト配列からメッセージを作成
  /// </summary>
  public static Message FromBytes(byte[] data, System.Text.Encoding encoding)
  {
    return new Message
    {
      RawData = data,
      Text = encoding.GetString(data),
      Timestamp = DateTime.UtcNow
    };
  }
}



