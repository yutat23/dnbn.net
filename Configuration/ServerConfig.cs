namespace Dnbn.Configuration;

/// <summary>
/// クライアント識別方式
/// </summary>
public enum ClientIdentification
{
  /// <summary>
  /// 送信元エンドポイント（IP+Port）で識別
  /// </summary>
  SourceEndpoint,

  /// <summary>
  /// 伝聞ヘッダに含まれる識別情報で識別
  /// </summary>
  HeaderBased
}

/// <summary>
/// サーバー設定
/// </summary>
public class ServerConfig
{
  /// <summary>
  /// サーバー名（設定識別用）
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// リッスンポート
  /// </summary>
  public int ListenPort { get; set; }

  /// <summary>
  /// 文字エンコーディング（UTF-8, Shift-JIS等）
  /// </summary>
  public string Encoding { get; set; } = "UTF-8";

  /// <summary>
  /// メッセージ終端文字（\r, \r\n, \n等）
  /// </summary>
  public string? MessageTerminator { get; set; }

  /// <summary>
  /// クライアント識別方式
  /// </summary>
  public ClientIdentification ClientIdentification { get; set; } = ClientIdentification.SourceEndpoint;

  /// <summary>
  /// 固定長ヘッダサイズ（バイト）
  /// </summary>
  public int? FixedHeaderLength { get; set; }

  /// <summary>
  /// 固定長ボディサイズ（バイト）
  /// </summary>
  public int? FixedBodyLength { get; set; }

  /// <summary>
  /// 可変長ボディの場合のヘッダ内長さフィールドの位置（バイト）
  /// </summary>
  public int? LengthFieldOffset { get; set; }

  /// <summary>
  /// 可変長ボディの場合のヘッダ内長さフィールドのサイズ（バイト）
  /// </summary>
  public int? LengthFieldLength { get; set; }

  /// <summary>
  /// メッセージ送受信時のログ出力を有効にするかどうか
  /// </summary>
  public bool EnableMessageLogging { get; set; } = false;
}



