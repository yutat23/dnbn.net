using System.Linq;
using System.Text;
using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>
/// メッセージパーサー（固定長/可変長/終端文字対応）
/// </summary>
public class MessageParser
{
  private readonly Encoding _encoding;
  private readonly string? _messageTerminator;
  private readonly int? _fixedHeaderLength;
  private readonly int? _fixedBodyLength;
  private readonly int? _lengthFieldOffset;
  private readonly int? _lengthFieldLength;

  private readonly List<byte> _buffer = new();

  public MessageParser(
      Encoding encoding,
      string? messageTerminator = null,
      int? fixedHeaderLength = null,
      int? fixedBodyLength = null,
      int? lengthFieldOffset = null,
      int? lengthFieldLength = null)
  {
    _encoding = encoding;
    _messageTerminator = messageTerminator;
    _fixedHeaderLength = fixedHeaderLength;
    _fixedBodyLength = fixedBodyLength;
    _lengthFieldOffset = lengthFieldOffset;
    _lengthFieldLength = lengthFieldLength;
  }

  /// <summary>
  /// 受信データをパースしてメッセージリストを返す
  /// </summary>
  public List<Message> Parse(byte[] data)
  {
    _buffer.AddRange(data);
    var messages = new List<Message>();

    while (true)
    {
      var message = TryExtractMessage();
      if (message == null)
      {
        break;
      }

      messages.Add(message);
    }

    return messages;
  }

  private Message? TryExtractMessage()
  {
    if (_buffer.Count == 0)
    {
      return null;
    }

    byte[]? messageData = null;

    // 終端文字方式
    if (!string.IsNullOrEmpty(_messageTerminator))
    {
      var terminatorBytes = _encoding.GetBytes(_messageTerminator);
      var terminatorIndex = FindSequence(_buffer, terminatorBytes);

      if (terminatorIndex >= 0)
      {
        var messageLength = terminatorIndex + terminatorBytes.Length;
        messageData = _buffer.Take(messageLength).ToArray();
        _buffer.RemoveRange(0, messageLength);
      }
    }
    // 固定長ヘッダ + 固定長ボディ
    else if (_fixedHeaderLength.HasValue && _fixedBodyLength.HasValue)
    {
      var totalLength = _fixedHeaderLength.Value + _fixedBodyLength.Value;
      if (_buffer.Count >= totalLength)
      {
        messageData = _buffer.Take(totalLength).ToArray();
        _buffer.RemoveRange(0, totalLength);
      }
    }
    // 固定長ヘッダ + 可変長ボディ
    else if (_fixedHeaderLength.HasValue && _lengthFieldOffset.HasValue && _lengthFieldLength.HasValue)
    {
      if (_buffer.Count >= _fixedHeaderLength.Value)
      {
        var header = _buffer.Take(_fixedHeaderLength.Value).ToArray();
        var bodyLength = ExtractLength(header, _lengthFieldOffset.Value, _lengthFieldLength.Value);
        var totalLength = _fixedHeaderLength.Value + bodyLength;

        if (_buffer.Count >= totalLength)
        {
          messageData = _buffer.Take(totalLength).ToArray();
          _buffer.RemoveRange(0, totalLength);
        }
      }
    }
    // 可変長ヘッダ + 可変長ボディ（長さフィールドが先頭にある場合）
    else if (_lengthFieldOffset.HasValue && _lengthFieldLength.HasValue)
    {
      if (_buffer.Count >= _lengthFieldOffset.Value + _lengthFieldLength.Value)
      {
        var lengthBytes = _buffer.Skip(_lengthFieldOffset.Value).Take(_lengthFieldLength.Value).ToArray();
        var totalLength = ExtractLengthFromBytes(lengthBytes);

        if (_buffer.Count >= totalLength)
        {
          messageData = _buffer.Take(totalLength).ToArray();
          _buffer.RemoveRange(0, totalLength);
        }
      }
    }
    // デフォルト：終端文字が見つかるまで待つ（バッファに残す）
    else
    {
      // 終端文字がない場合は、一定量のデータが来たら1メッセージとして扱う
      // または、アプリ側で明示的に終端文字を設定する必要がある
      return null;
    }

    if (messageData == null)
    {
      return null;
    }

    return Message.FromBytes(messageData, _encoding);
  }

  private int FindSequence(List<byte> buffer, byte[] sequence)
  {
    for (int i = 0; i <= buffer.Count - sequence.Length; i++)
    {
      bool found = true;
      for (int j = 0; j < sequence.Length; j++)
      {
        if (buffer[i + j] != sequence[j])
        {
          found = false;
          break;
        }
      }
      if (found)
      {
        return i;
      }
    }
    return -1;
  }

  private int ExtractLength(byte[] header, int offset, int length)
  {
    var lengthBytes = header.Skip(offset).Take(length).ToArray();
    return ExtractLengthFromBytes(lengthBytes);
  }

  private int ExtractLengthFromBytes(byte[] bytes)
  {
    if (bytes.Length == 1)
    {
      return bytes[0];
    }

    if (bytes.Length == 2)
    {
      // Big-endian想定（ネットワークバイトオーダー）
      if (BitConverter.IsLittleEndian)
      {
        // バイト順序を反転
        var reversed = new byte[] { bytes[1], bytes[0] };
        return BitConverter.ToInt16(reversed, 0);
      }
      return BitConverter.ToInt16(bytes, 0);
    }
    if (bytes.Length == 4)
    {
      // Big-endian想定（ネットワークバイトオーダー）
      if (BitConverter.IsLittleEndian)
      {
        // バイト順序を反転
        var reversed = new byte[] { bytes[3], bytes[2], bytes[1], bytes[0] };
        return BitConverter.ToInt32(reversed, 0);
      }
      return BitConverter.ToInt32(bytes, 0);
    }
    throw new ArgumentException($"Unsupported length field size: {bytes.Length}");
  }

  /// <summary>
  /// バッファをクリア
  /// </summary>
  public void Clear()
  {
    _buffer.Clear();
  }
}

