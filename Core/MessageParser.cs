using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>
/// メッセージパーサー（固定長/可変長/終端文字対応）
/// </summary>
public class MessageParser
{
  private readonly Encoding _encoding;
  private readonly string[]? _messageTerminators;
  private readonly int? _fixedHeaderLength;
  private readonly int? _fixedBodyLength;
  private readonly int? _lengthFieldOffset;
  private readonly int? _lengthFieldLength;
  private readonly int? _maxReceiveBufferBytes;

  private readonly List<byte> _buffer = new();

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="encoding">文字エンコーディング</param>
  /// <param name="messageTerminators">メッセージ終端文字の配列（オプション、複数の候補をサポート）</param>
  /// <param name="fixedHeaderLength">固定長ヘッダーの長さ（オプション）</param>
  /// <param name="fixedBodyLength">固定長ボディの長さ（オプション）</param>
  /// <param name="lengthFieldOffset">長さフィールドのオフセット（オプション）</param>
  /// <param name="lengthFieldLength">長さフィールドの長さ（オプション）</param>
  /// <param name="maxReceiveBufferBytes">受信バッファの最大バイト数（オプション、未設定は無制限）</param>
  public MessageParser(
      Encoding encoding,
      string[]? messageTerminators = null,
      int? fixedHeaderLength = null,
      int? fixedBodyLength = null,
      int? lengthFieldOffset = null,
      int? lengthFieldLength = null,
      int? maxReceiveBufferBytes = null)
  {
    _encoding = encoding;
    _messageTerminators = messageTerminators;
    _fixedHeaderLength = fixedHeaderLength;
    _fixedBodyLength = fixedBodyLength;
    _lengthFieldOffset = lengthFieldOffset;
    _lengthFieldLength = lengthFieldLength;
    _maxReceiveBufferBytes = maxReceiveBufferBytes;
  }

  /// <summary>
  /// 受信データをパースしてメッセージリストを返す
  /// </summary>
  public List<Message> Parse(byte[] data)
  {
    _buffer.AddRange(data);
    if (_maxReceiveBufferBytes.HasValue && _maxReceiveBufferBytes.Value > 0 && _buffer.Count > _maxReceiveBufferBytes.Value)
    {
      throw new InvalidOperationException(
        $"Receive buffer exceeded maximum of {_maxReceiveBufferBytes.Value} bytes. " +
        "Configure MessageTerminator or length-based protocol, or increase MaxReceiveBufferBytes.");
    }
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

    // 終端文字方式（複数の終端文字候補をサポート）
    if (_messageTerminators != null && _messageTerminators.Length > 0)
    {
      int earliestIndex = int.MaxValue;
      byte[]? matchedTerminatorBytes = null;

      // すべての終端文字候補をチェックし、最も早く見つかったものを使用
      foreach (var terminator in _messageTerminators)
      {
        if (string.IsNullOrEmpty(terminator))
        {
          continue;
        }

        var terminatorBytes = _encoding.GetBytes(terminator);
        var terminatorIndex = FindSequence(_buffer, terminatorBytes);

        if (terminatorIndex >= 0 && terminatorIndex < earliestIndex)
        {
          earliestIndex = terminatorIndex;
          matchedTerminatorBytes = terminatorBytes;
        }
      }

      if (matchedTerminatorBytes != null && earliestIndex < int.MaxValue)
      {
        var messageLength = earliestIndex + matchedTerminatorBytes.Length;
        messageData = ExtractAndRemoveFromBuffer(messageLength);
      }
    }
    // 固定長ヘッダ + 固定長ボディ
    else if (_fixedHeaderLength.HasValue && _fixedBodyLength.HasValue)
    {
      var totalLength = _fixedHeaderLength.Value + _fixedBodyLength.Value;
      if (_buffer.Count >= totalLength)
      {
        messageData = ExtractAndRemoveFromBuffer(totalLength);
      }
    }
    // 固定長ヘッダ + 可変長ボディ
    else if (_fixedHeaderLength.HasValue && _lengthFieldOffset.HasValue && _lengthFieldLength.HasValue)
    {
      var headerLen = _fixedHeaderLength.Value;
      if (_buffer.Count >= headerLen)
      {
        var bodyLength = ExtractLengthFromSpan(CollectionsMarshal.AsSpan(_buffer).Slice(0, headerLen), _lengthFieldOffset.Value, _lengthFieldLength.Value);
        var totalLength = headerLen + bodyLength;

        if (_buffer.Count >= totalLength)
        {
          messageData = ExtractAndRemoveFromBuffer(totalLength);
        }
      }
    }
    // 可変長ヘッダ + 可変長ボディ（長さフィールドが先頭にある場合）
    else if (_lengthFieldOffset.HasValue && _lengthFieldLength.HasValue)
    {
      var minLen = _lengthFieldOffset.Value + _lengthFieldLength.Value;
      if (_buffer.Count >= minLen)
      {
        var span = CollectionsMarshal.AsSpan(_buffer).Slice(_lengthFieldOffset.Value, _lengthFieldLength.Value);
        var totalLength = ExtractLengthFromSpan(span);

        if (_buffer.Count >= totalLength)
        {
          messageData = ExtractAndRemoveFromBuffer(totalLength);
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

  private byte[] ExtractAndRemoveFromBuffer(int count)
  {
    var result = new byte[count];
    _buffer.CopyTo(0, result, 0, count);
    _buffer.RemoveRange(0, count);
    return result;
  }

  private static int ExtractLengthFromSpan(ReadOnlySpan<byte> bytes, int offset, int length)
  {
    var slice = bytes.Slice(offset, length);
    return ExtractLengthFromSpan(slice);
  }

  private static int ExtractLengthFromSpan(ReadOnlySpan<byte> bytes)
  {
    if (bytes.Length == 1)
    {
      return bytes[0];
    }

    if (bytes.Length == 2)
    {
      return BinaryPrimitives.ReadInt16BigEndian(bytes);
    }
    if (bytes.Length == 4)
    {
      return BinaryPrimitives.ReadInt32BigEndian(bytes);
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

