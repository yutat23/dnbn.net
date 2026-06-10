using System.Text;
using Dnbn.Core;

namespace Dnbn.Tests;

/// <summary>
/// MessageParser の境界ケーステスト
/// （チャンク分割・長さフィールドのバリエーション・マルチバイト文字）
/// </summary>
public class MessageParserEdgeCaseTests
{
  private static readonly Encoding Utf8 = Encoding.UTF8;

  // ---------------------------------------------------------------------------
  // チャンク分割（TCPストリームの分断を模擬）
  // ---------------------------------------------------------------------------

  [Fact]
  public void Parse_TerminatorSplitAcrossChunks_ParsesCorrectly()
  {
    var parser = new MessageParser(Utf8, new[] { "\r\n" });

    // 終端文字 "\r\n" が2つのチャンクに分断されるケース
    var first = parser.Parse(Utf8.GetBytes("MESSAGE\r"));
    Assert.Empty(first);

    var second = parser.Parse(Utf8.GetBytes("\n"));
    Assert.Single(second);
    Assert.Equal("MESSAGE\r\n", second[0].Text);
  }

  [Fact]
  public void Parse_ByteByByteFeed_ParsesCorrectly()
  {
    var parser = new MessageParser(Utf8, new[] { "\n" });
    var data = Utf8.GetBytes("AB\nCD\n");

    var allMessages = new List<Dnbn.Models.Message>();
    foreach (var b in data)
    {
      allMessages.AddRange(parser.Parse(new[] { b }));
    }

    Assert.Equal(2, allMessages.Count);
    Assert.Equal("AB\n", allMessages[0].Text);
    Assert.Equal("CD\n", allMessages[1].Text);
  }

  [Fact]
  public void Parse_MultiByteUtf8SplitAcrossChunks_ParsesCorrectly()
  {
    var parser = new MessageParser(Utf8, new[] { "\n" });
    var full = Utf8.GetBytes("こんにちは\n"); // 各文字3バイト

    // マルチバイト文字の途中で分断
    var first = parser.Parse(full.Take(4).ToArray());
    Assert.Empty(first);

    var second = parser.Parse(full.Skip(4).ToArray());
    Assert.Single(second);
    Assert.Equal("こんにちは\n", second[0].Text);
  }

  [Fact]
  public void Parse_FixedLengthSplitAcrossChunks_ParsesCorrectly()
  {
    var parser = new MessageParser(Utf8, fixedHeaderLength: 2, fixedBodyLength: 3);

    var first = parser.Parse(Utf8.GetBytes("AB1"));
    Assert.Empty(first);

    var second = parser.Parse(Utf8.GetBytes("23"));
    Assert.Single(second);
    Assert.Equal("AB123", second[0].Text);
  }

  [Fact]
  public void Parse_LengthFieldSplitAcrossChunks_ParsesCorrectly()
  {
    // 長さフィールド先頭2バイト（メッセージ全体の長さ）
    var parser = new MessageParser(Utf8, lengthFieldOffset: 0, lengthFieldLength: 2);

    // 長さフィールド自体が分断されるケース
    var first = parser.Parse(new byte[] { 0x00 });
    Assert.Empty(first);

    var second = parser.Parse(new byte[] { 0x05 }); // 全長5バイト
    Assert.Empty(second);

    var third = parser.Parse(Utf8.GetBytes("ABC"));
    Assert.Single(third);
    Assert.Equal(5, third[0].RawData.Length);
  }

  // ---------------------------------------------------------------------------
  // 長さフィールドのサイズバリエーション
  // ---------------------------------------------------------------------------

  [Fact]
  public void Parse_OneByteLengthField_ParsesCorrectly()
  {
    var parser = new MessageParser(Utf8, lengthFieldOffset: 0, lengthFieldLength: 1);

    // 全長4バイト = 長さフィールド1バイト + ボディ3バイト
    var data = new byte[] { 0x04 }.Concat(Utf8.GetBytes("XYZ")).ToArray();
    var messages = parser.Parse(data);

    Assert.Single(messages);
    Assert.Equal(4, messages[0].RawData.Length);
  }

  [Fact]
  public void Parse_FourByteLengthField_ParsesCorrectly()
  {
    var parser = new MessageParser(Utf8, lengthFieldOffset: 0, lengthFieldLength: 4);

    // 全長7バイト = 長さフィールド4バイト + ボディ3バイト（ビッグエンディアン）
    var data = new byte[] { 0x00, 0x00, 0x00, 0x07 }.Concat(Utf8.GetBytes("ABC")).ToArray();
    var messages = parser.Parse(data);

    Assert.Single(messages);
    Assert.Equal(7, messages[0].RawData.Length);
  }

  [Fact]
  public void Parse_HeaderWithLengthField_MultipleMessages()
  {
    // ヘッダ3バイト、長さフィールドはオフセット1から2バイト（ボディ長）
    var parser = new MessageParser(Utf8,
        fixedHeaderLength: 3,
        lengthFieldOffset: 1,
        lengthFieldLength: 2);

    static byte[] BuildMessage(byte type, string body)
    {
      var bodyBytes = Encoding.UTF8.GetBytes(body);
      return new byte[] { type, (byte)(bodyBytes.Length >> 8), (byte)bodyBytes.Length }
          .Concat(bodyBytes).ToArray();
    }

    var data = BuildMessage(0x01, "AAA").Concat(BuildMessage(0x02, "BBBBB")).ToArray();
    var messages = parser.Parse(data);

    Assert.Equal(2, messages.Count);
    Assert.Equal(6, messages[0].RawData.Length);  // 3 + 3
    Assert.Equal(8, messages[1].RawData.Length);  // 3 + 5
  }

  // ---------------------------------------------------------------------------
  // 終端文字モードの境界ケース
  // ---------------------------------------------------------------------------

  [Fact]
  public void Parse_TerminatorOnly_ProducesEmptyBodyMessage()
  {
    var parser = new MessageParser(Utf8, new[] { "\n" });
    var messages = parser.Parse(Utf8.GetBytes("\n"));

    Assert.Single(messages);
    Assert.Equal("\n", messages[0].Text);
  }

  [Fact]
  public void Parse_MultiByteTerminator_NotConfusedByPartialMatch()
  {
    // 本文中に終端文字（"@@"）の一部である "@" が単体で含まれていても誤検出しないこと
    var parser = new MessageParser(Utf8, new[] { "@@" });
    var messages = parser.Parse(Utf8.GetBytes("A@B@@"));

    Assert.Single(messages);
    Assert.Equal("A@B@@", messages[0].Text);
  }

  [Fact]
  public void Parse_JapaneseTextWithTerminator_RoundTrips()
  {
    var parser = new MessageParser(Utf8, new[] { "\r\n" });
    var messages = parser.Parse(Utf8.GetBytes("日本語メッセージ\r\n"));

    Assert.Single(messages);
    Assert.Equal("日本語メッセージ\r\n", messages[0].Text);
  }

  [Fact]
  public void Parse_ShiftJisEncoding_ParsesCorrectly()
  {
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var sjis = Encoding.GetEncoding("shift_jis");
    var parser = new MessageParser(sjis, new[] { "\r\n" });

    var messages = parser.Parse(sjis.GetBytes("テスト\r\n"));

    Assert.Single(messages);
    Assert.Equal("テスト\r\n", messages[0].Text);
  }

  // ---------------------------------------------------------------------------
  // 受信バッファ上限（終端文字モード）
  // ---------------------------------------------------------------------------

  [Fact]
  public void Parse_MaxReceiveBufferBytes_TerminatorMode_ThrowsWhenExceeded()
  {
    var parser = new MessageParser(Utf8, new[] { "\n" }, null, null, null, null, 10);

    // 終端文字が来ないままバッファ上限を超過すると例外になること
    Assert.Throws<InvalidOperationException>(
        () => parser.Parse(Utf8.GetBytes(new string('X', 11))));
  }

  [Fact]
  public void Parse_MaxReceiveBufferBytes_NotExceeded_WhenMessagesConsumed()
  {
    var parser = new MessageParser(Utf8, new[] { "\n" }, null, null, null, null, 10);

    // メッセージが順次消費されればバッファは溢れないこと
    for (int i = 0; i < 5; i++)
    {
      var messages = parser.Parse(Utf8.GetBytes("12345678\n"));
      Assert.Single(messages);
    }
  }
}
