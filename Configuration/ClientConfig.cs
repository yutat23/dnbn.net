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
  /// キープアライブ設定
  /// </summary>
  public KeepAliveConfig? KeepAlive { get; set; }

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
}



