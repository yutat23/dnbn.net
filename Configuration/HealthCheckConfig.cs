namespace Dnbn.Configuration;

/// <summary>
/// ヘルスチェック設定
/// </summary>
public class HealthCheckConfig
{
    /// <summary>
    /// ヘルスチェックを有効にするか
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// ヘルスチェック間隔（秒）
    /// </summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>
    /// ヘルスチェックメッセージ
    /// </summary>
    public string Message { get; set; } = string.Empty;
}



