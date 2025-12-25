using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>
/// トランスポート層の抽象化インターフェイス
/// </summary>
public interface ITransport
{
    /// <summary>
    /// 接続状態
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 接続する
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// 切断する
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// データを送信する
    /// </summary>
    Task SendAsync(byte[] data);

    /// <summary>
    /// データを受信する（ストリームから読み取る）
    /// </summary>
    Task<int> ReceiveAsync(byte[] buffer, int offset, int count);
}



