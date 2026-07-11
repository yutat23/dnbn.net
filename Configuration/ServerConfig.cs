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
  /// 待ち受けIPアドレス。既定値は全IPv4インターフェイス（0.0.0.0）。
  /// </summary>
  public string BindAddress { get; set; } = "0.0.0.0";

  /// <summary>
  /// 文字エンコーディング（UTF-8, Shift-JIS等）
  /// </summary>
  public string Encoding { get; set; } = "UTF-8";

  /// <summary>
  /// メッセージ終端文字（\r, \r\n, \n等）
  /// </summary>
  public string? MessageTerminator { get; set; }

  /// <summary>
  /// 受信時のメッセージ終端文字の配列（複数の候補をサポート）。未設定の場合はMessageTerminatorを使用
  /// </summary>
  public string[]? ReceiveMessageTerminator { get; set; }

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

  /// <summary>
  /// 受信バッファの最大バイト数（未設定または0以下は無制限）。
  /// 終端文字・長さフィールドが未設定のプロトコルではバッファが無制限に伸びるリスクがあるため、
  /// 任意で上限を設定することでメモリ枯渇を防げる。
  /// </summary>
  public int? MaxReceiveBufferBytes { get; set; }

  /// <summary>
  /// TCPレベルのキープアライブ設定（ソケットオプション SO_KEEPALIVE）。
  /// 接続を受け付けたクライアントソケットに適用される。
  /// 未設定（null）の場合は従来どおりOSの既定動作。
  /// </summary>
  public TcpKeepAliveConfig? TcpKeepAlive { get; set; }

  /// <summary>この設定の複製を作成する。</summary>
  public ServerConfig Clone()
  {
    return new ServerConfig
    {
      Name = Name,
      ListenPort = ListenPort,
      BindAddress = BindAddress,
      Encoding = Encoding,
      MessageTerminator = MessageTerminator,
      ReceiveMessageTerminator = ReceiveMessageTerminator?.ToArray(),
      ClientIdentification = ClientIdentification,
      FixedHeaderLength = FixedHeaderLength,
      FixedBodyLength = FixedBodyLength,
      LengthFieldOffset = LengthFieldOffset,
      LengthFieldLength = LengthFieldLength,
      EnableMessageLogging = EnableMessageLogging,
      MaxReceiveBufferBytes = MaxReceiveBufferBytes,
      TcpKeepAlive = TcpKeepAlive?.Clone()
    };
  }
}

