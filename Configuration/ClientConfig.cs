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
    /// リトライポリシー
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// タイムアウト（ミリ秒）
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 5000;

    /// <summary>
    /// ヘルスチェック設定
    /// </summary>
    public HealthCheckConfig? HealthCheck { get; set; }

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
}



