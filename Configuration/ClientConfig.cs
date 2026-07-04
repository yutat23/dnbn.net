namespace Dnbn.Configuration;

/// <summary>
/// クライアント設定
/// </summary>
public class ClientConfig
{
  /// <summary>
  /// クライアント名（設定識別用）
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// リモートホスト（IPアドレスまたはホスト名）
  /// </summary>
  public string RemoteHost { get; set; } = string.Empty;

  /// <summary>
  /// リモートポート
  /// </summary>
  public int RemotePort { get; set; }

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
  /// リトライポリシー（メッセージ送信用）
  /// </summary>
  public RetryPolicy? RetryPolicy { get; set; }

  /// <summary>
  /// 接続リトライポリシー（接続失敗時およびNW障害時の自動再接続用）
  /// </summary>
  public RetryPolicy? ConnectionRetryPolicy { get; set; }

  /// <summary>
  /// タイムアウト（ミリ秒）
  /// </summary>
  public int TimeoutMilliseconds { get; set; } = 5000;

  /// <summary>
  /// キープアライブ設定（アプリケーションレベル：電文送信による死活監視）
  /// </summary>
  public KeepAliveConfig? KeepAlive { get; set; }

  /// <summary>
  /// TCPレベルのキープアライブ設定（ソケットオプション SO_KEEPALIVE）。
  /// 未設定（null）の場合は従来どおりOSの既定動作。
  /// </summary>
  public TcpKeepAliveConfig? TcpKeepAlive { get; set; }

  /// <summary>
  /// 通知電文の判定述語。マッチした受信メッセージは応答マッチングをスキップして
  /// OnMessageReceived / MessageReceived(Rx) に直接配信される。
  /// 未設定（null）の場合は従来どおりの動作。
  /// 注意: キープアライブ応答の判定が先に行われるため、KeepAliveと併用する場合は
  /// KeepAliveConfig.ResponsePredicate を設定して通知電文と区別すること。
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore]
  [System.Xml.Serialization.XmlIgnore]
  public Func<Dnbn.Models.Message, bool>? NotificationPredicate { get; set; }

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
  /// 送信キューの最大サイズ。キューが満杯の場合、送信呼び出しは空きが出るまで待機する。
  /// 既定値: 1000
  /// </summary>
  public int SendQueueCapacity { get; set; } = 1000;

  /// <summary>
  /// 受信バッファの最大バイト数（未設定または0以下は無制限）。
  /// 終端文字・長さフィールドが未設定のプロトコルではバッファが無制限に伸びるリスクがあるため、
  /// 任意で上限を設定することでメモリ枯渇を防げる。
  /// </summary>
  public int? MaxReceiveBufferBytes { get; set; }
}


