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
  private readonly byte[][]? _terminatorBytes;
  private readonly int? _fixedHeaderLength;
  private readonly int? _fixedBodyLength;
  private readonly int? _lengthFieldOffset;
  private readonly int? _lengthFieldLength;
  private readonly int? _maxReceiveBufferBytes;

  private readonly List<byte> _buffer = new();

  /// <summary>
  /// コンストラクタ（後方互換）
  /// </summary>
  /// <param name="encoding">文字エンコーディング</param>
  /// <param name="messageTerminators">メッセージ終端文字の配列（オプション、複数の候補をサポート）</param>
  /// <param name="fixedHeaderLength">固定長ヘッダーの長さ（オプション）</param>
  /// <param name="fixedBodyLength">固定長ボディの長さ（オプション）</param>
  /// <param name="lengthFieldOffset">長さフィールドのオフセット（オプション）</param>
  /// <param name="lengthFieldLength">長さフィールドの長さ（オプション）</param>
  public MessageParser(
      Encoding encoding,
      string[]? messageTerminators = null,
      int? fixedHeaderLength = null,
      int? fixedBodyLength = null,
      int? lengthFieldOffset = null,
      int? lengthFieldLength = null)
      : this(encoding, messageTerminators, fixedHeaderLength, fixedBodyLength, lengthFieldOffset, lengthFieldLength, null)
  {
  }

  /// <summary>
  /// コンストラクタ（受信バッファ上限付き）
  /// </summary>
  /// <param name="encoding">文字エンコーディング</param>
  /// <param name="messageTerminators">メッセージ終端文字の配列（オプション、複数の候補をサポート）</param>
  /// <param name="fixedHeaderLength">固定長ヘッダーの長さ（オプション）</param>
  /// <param name="fixedBodyLength">固定長ボディの長さ（オプション）</param>
  /// <param name="lengthFieldOffset">長さフィールドのオフセット（オプション）</param>
  /// <param name="lengthFieldLength">長さフィールドの長さ（オプション）</param>
  /// <param name="maxReceiveBufferBytes">受信バッファの最大バイト数（未設定は無制限）</param>
  public MessageParser(
      Encoding encoding,
      string[]? messageTerminators,
      int? fixedHeaderLength,
      int? fixedBodyLength,
      int? lengthFieldOffset,
      int? lengthFieldLength,
      int? maxReceiveBufferBytes)
  {
    _encoding = encoding;
    // 終端文字のバイト列はパースのたびにエンコードせず事前計算しておく
    _terminatorBytes = messageTerminators?
        .Where(t => !string.IsNullOrEmpty(t))
        .Select(encoding.GetBytes)
        .ToArray();
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
    if (_terminatorBytes != null && _terminatorBytes.Length > 0)
    {
      int earliestIndex = int.MaxValue;
      byte[]? matchedTerminatorBytes = null;

      // すべての終端文字候補をチェックし、最も早く見つかったものを使用
      foreach (var terminatorBytes in _terminatorBytes)
      {
        var terminatorIndex = FindSequence(_buffer, terminatorBytes);

        // 同じ位置で複数候補が一致する場合は最長一致を優先する。
        // 例: "\r" と "\r\n" が設定され、入力がCRLFなら一電文として扱う。
        if (terminatorIndex >= 0 &&
            (terminatorIndex < earliestIndex ||
             (terminatorIndex == earliestIndex &&
              (matchedTerminatorBytes == null || terminatorBytes.Length > matchedTerminatorBytes.Length))))
        {
          earliestIndex = terminatorIndex;
          matchedTerminatorBytes = terminatorBytes;
        }
      }

      if (matchedTerminatorBytes != null && earliestIndex < int.MaxValue)
      {
        // 短い終端文字が長い候補のprefixで、かつ現在のバッファ末尾にある場合は、
        // 次のTCPチャンクで長い候補が完成する可能性があるため確定を保留する。
        // 例: CR/CRLFを許可し、現在のチャンクがCRで終わっている場合。
        if (CouldCompleteLongerTerminator(earliestIndex, matchedTerminatorBytes))
        {
          return null;
        }

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
        if (bodyLength > int.MaxValue - headerLen)
        {
          throw new InvalidOperationException(
            $"Declared body length {bodyLength} is too large (header {headerLen} bytes). The stream may be corrupted or misconfigured.");
        }
        var totalLength = headerLen + bodyLength;
        ThrowIfExceedsMaxBuffer(totalLength);

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

        // 宣言された全長が長さフィールド領域より小さい場合、0バイト抽出の無限ループや
        // ストリーム破損の連鎖につながるため、プロトコルエラーとして扱う
        if (totalLength < minLen)
        {
          throw new InvalidOperationException(
            $"Declared message length {totalLength} is smaller than the length field region ({minLen} bytes). The stream may be corrupted or misconfigured.");
        }
        ThrowIfExceedsMaxBuffer(totalLength);

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

  private bool CouldCompleteLongerTerminator(int terminatorIndex, byte[] matchedTerminator)
  {
    if (_terminatorBytes == null || terminatorIndex + matchedTerminator.Length != _buffer.Count)
    {
      return false;
    }

    return _terminatorBytes.Any(candidate =>
        candidate.Length > matchedTerminator.Length &&
        candidate.AsSpan(0, matchedTerminator.Length).SequenceEqual(matchedTerminator));
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
      return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }
    if (bytes.Length == 4)
    {
      var value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
      if (value > int.MaxValue)
      {
        // intへのキャストで負数になり、以降のバッファ操作が破綻するため明示的に拒否する
        throw new InvalidOperationException(
          $"Length field value {value} exceeds the supported maximum ({int.MaxValue}). The stream may be corrupted or misconfigured.");
      }
      return (int)value;
    }
    throw new ArgumentException($"Unsupported length field size: {bytes.Length}");
  }

  private void ThrowIfExceedsMaxBuffer(int totalLength)
  {
    if (_maxReceiveBufferBytes.HasValue && _maxReceiveBufferBytes.Value > 0 && totalLength > _maxReceiveBufferBytes.Value)
    {
      throw new InvalidOperationException(
        $"Declared message length {totalLength} exceeds maximum of {_maxReceiveBufferBytes.Value} bytes. " +
        "Increase MaxReceiveBufferBytes or verify the protocol configuration.");
    }
  }

  /// <summary>
  /// バッファをクリア
  /// </summary>
  public void Clear()
  {
    _buffer.Clear();
  }
}
