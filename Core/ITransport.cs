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
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task ConnectAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// 切断する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task DisconnectAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// データを送信する
  /// </summary>
  /// <param name="data">送信するデータ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task SendAsync(byte[] data, CancellationToken cancellationToken = default);

  /// <summary>
  /// データを受信する（ストリームから読み取る）
  /// </summary>
  /// <param name="buffer">受信バッファ</param>
  /// <param name="offset">オフセット</param>
  /// <param name="count">受信する最大バイト数</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default);
}



