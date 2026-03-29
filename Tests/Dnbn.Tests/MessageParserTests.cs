using System.Text;
using Dnbn.Core;
using Dnbn.Models;
using Xunit;

namespace Dnbn.Tests;

public class MessageParserTests
{
  private static readonly Encoding Utf8 = Encoding.UTF8;

  [Fact]
  public void Parse_TerminatorMode_SingleMessage()
  {
    var parser = new MessageParser(Utf8, new[] { "\r" });
    var data = Utf8.GetBytes("HELLO\r");
    var messages = parser.Parse(data);

    Assert.Single(messages);
    Assert.Equal("HELLO\r", messages[0].Text);
  }

  [Fact]
  public void Parse_TerminatorMode_MultipleMessagesInOneChunk()
  {
    var parser = new MessageParser(Utf8, new[] { "\r" });
    var data = Utf8.GetBytes("A\rB\rC\r");
    var messages = parser.Parse(data);

    Assert.Equal(3, messages.Count);
    Assert.Equal("A\r", messages[0].Text);
    Assert.Equal("B\r", messages[1].Text);
    Assert.Equal("C\r", messages[2].Text);
  }

  [Fact]
  public void Parse_TerminatorMode_PartialMessageKeptInBuffer()
  {
    var parser = new MessageParser(Utf8, new[] { "\r" });
    var data = Utf8.GetBytes("INCOMPLETE");
    var messages = parser.Parse(data);

    Assert.Empty(messages);

    var more = Utf8.GetBytes("\r");
    var messages2 = parser.Parse(more);
    Assert.Single(messages2);
    Assert.Equal("INCOMPLETE\r", messages2[0].Text);
  }

  [Fact]
  public void Parse_TerminatorMode_MultipleTerminators_UsesEarliest()
  {
    var parser = new MessageParser(Utf8, new[] { "\r\n", "\r" });
    var data = Utf8.GetBytes("LINE\r");
    var messages = parser.Parse(data);

    Assert.Single(messages);
    Assert.Equal("LINE\r", messages[0].Text);
  }

  [Fact]
  public void Parse_TerminatorMode_CRLFBeforeCR()
  {
    var parser = new MessageParser(Utf8, new[] { "\r\n", "\r" });
    var data = Utf8.GetBytes("LINE\r\n");
    var messages = parser.Parse(data);

    Assert.Single(messages);
    Assert.Equal("LINE\r\n", messages[0].Text);
  }

  [Fact]
  public void Parse_FixedLengthMode_SingleMessage()
  {
    var parser = new MessageParser(Utf8, fixedHeaderLength: 0, fixedBodyLength: 5);
    var data = Utf8.GetBytes("12345");
    var messages = parser.Parse(data);

    Assert.Single(messages);
    Assert.Equal("12345", messages[0].Text);
  }

  [Fact]
  public void Parse_FixedLengthMode_HeaderAndBody()
  {
    var parser = new MessageParser(Utf8, fixedHeaderLength: 2, fixedBodyLength: 3);
    var data = Utf8.GetBytes("AB123");
    var messages = parser.Parse(data);

    Assert.Single(messages);
    Assert.Equal("AB123", messages[0].Text);
  }

  [Fact]
  public void Parse_VariableLength_LengthFieldInHeader()
  {
    // Header 4 bytes, length at offset 0, 2 bytes (big-endian). Body length = 3 -> total 4+3=7
    var parser = new MessageParser(Utf8,
      fixedHeaderLength: 4,
      lengthFieldOffset: 0,
      lengthFieldLength: 2);
    var header = new byte[] { 0x00, 0x03, 0x00, 0x00 }; // body length 3
    var body = Utf8.GetBytes("XYZ");
    var data = new byte[header.Length + body.Length];
    Buffer.BlockCopy(header, 0, data, 0, header.Length);
    Buffer.BlockCopy(body, 0, data, header.Length, body.Length);

    var messages = parser.Parse(data);
    Assert.Single(messages);
    Assert.Equal(7, messages[0].RawData.Length);
    Assert.True(data.AsSpan().SequenceEqual(messages[0].RawData));
  }

  [Fact]
  public void Parse_VariableLength_LengthFieldAtStart()
  {
    // Length at start: 2 bytes big-endian = 5 (total message size including length field)
    var parser = new MessageParser(Utf8,
      lengthFieldOffset: 0,
      lengthFieldLength: 2);
    var lengthBytes = new byte[] { 0x00, 0x05 };
    var body = Utf8.GetBytes("HEL"); // 3 bytes -> total 5
    var data = new byte[lengthBytes.Length + body.Length];
    Buffer.BlockCopy(lengthBytes, 0, data, 0, lengthBytes.Length);
    Buffer.BlockCopy(body, 0, data, lengthBytes.Length, body.Length);

    var messages = parser.Parse(data);
    Assert.Single(messages);
    Assert.Equal(5, messages[0].RawData.Length);
    Assert.True(messages[0].RawData.AsSpan(2).SequenceEqual(body));
  }

  [Fact]
  public void Clear_ResetsBuffer()
  {
    var parser = new MessageParser(Utf8, new[] { "\r" });
    parser.Parse(Utf8.GetBytes("PARTIAL"));
    parser.Clear();

    var messages = parser.Parse(Utf8.GetBytes("\r"));
    Assert.Single(messages);
    Assert.Equal("\r", messages[0].Text);
  }

  [Fact]
  public void Parse_TerminatorMode_MultiByteTerminator()
  {
    var parser = new MessageParser(Utf8, new[] { "\r\n" });
    var data = Utf8.GetBytes("MSG\r\n");
    var messages = parser.Parse(data);

    Assert.Single(messages);
    Assert.Equal("MSG\r\n", messages[0].Text);
  }

  [Fact]
  public void Parse_MaxReceiveBufferBytes_ThrowsWhenExceeded()
  {
    var parser = new MessageParser(Utf8, null, null, null, null, null, 5);
    parser.Parse(Utf8.GetBytes("ABCD")); // 4 bytes, OK

    var ex = Assert.Throws<InvalidOperationException>(() => parser.Parse(Utf8.GetBytes("EF")));
    Assert.Contains("5", ex.Message);
    Assert.Contains("MaxReceiveBufferBytes", ex.Message);
  }

  [Fact]
  public void Parse_MaxReceiveBufferBytes_UnlimitedWhenNull()
  {
    var parser = new MessageParser(Utf8, null, null, null, null, null, null);
    var data = Utf8.GetBytes(new string('X', 10000));
    var messages = parser.Parse(data);
    Assert.Empty(messages);
  }
}
