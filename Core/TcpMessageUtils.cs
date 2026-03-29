using System.Text;
using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>
/// TCP メッセージ処理の共通ユーティリティ
/// </summary>
internal static class TcpMessageUtils
{
  /// <summary>
  /// エンコーディング名から Encoding オブジェクトを取得
  /// </summary>
  internal static Encoding GetEncoding(string encodingName)
  {
    return encodingName.ToUpperInvariant() switch
    {
      "UTF-8" => Encoding.UTF8,
      "SHIFT-JIS" or "SHIFTJIS" => Encoding.GetEncoding("shift_jis"),
      "ASCII" => Encoding.ASCII,
      _ => Encoding.UTF8
    };
  }

  /// <summary>
  /// MessageTerminator が設定されている場合、メッセージに自動的に追加する
  /// </summary>
  /// <param name="message">対象メッセージ</param>
  /// <param name="messageTerminator">終端文字列（null または空の場合は追加しない）</param>
  /// <param name="encodingName">エンコーディング名</param>
  /// <returns>終端文字を追加したバイト配列</returns>
  internal static byte[] AppendMessageTerminatorIfNeeded(Message message, string? messageTerminator, string encodingName)
  {
    if (string.IsNullOrEmpty(messageTerminator))
    {
      return message.RawData;
    }

    var encoding = GetEncoding(encodingName);
    var terminatorBytes = encoding.GetBytes(messageTerminator);

    // 既に終端文字が含まれているかチェック（末尾に一致するか）
    if (message.RawData.Length >= terminatorBytes.Length)
    {
      var suffix = new byte[terminatorBytes.Length];
      Array.Copy(message.RawData, message.RawData.Length - terminatorBytes.Length, suffix, 0, terminatorBytes.Length);
      if (suffix.SequenceEqual(terminatorBytes))
      {
        // 既に終端文字が含まれている場合は追加しない
        return message.RawData;
      }
    }

    // 終端文字を追加
    var result = new byte[message.RawData.Length + terminatorBytes.Length];
    Array.Copy(message.RawData, 0, result, 0, message.RawData.Length);
    Array.Copy(terminatorBytes, 0, result, message.RawData.Length, terminatorBytes.Length);
    return result;
  }
}
