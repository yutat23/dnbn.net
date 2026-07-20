using System.Buffers.Binary;
using System.Text;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using Microsoft.Extensions.Logging;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Sample.Scenarios;

/// <summary>
/// シナリオ5: レガシープロトコル
/// 古い計測器・制御機器との通信で使われる3種類のフレーミングを実演する。
///   (1) 終端文字方式 + Shift-JIS … CR区切りのテキストプロトコル
///   (2) 固定長方式             … ヘッダ4バイト + ボディ6バイトの固定フレーム
///   (3) 長さフィールド方式      … 先頭2バイトに全長を持つバイナリフレーム
/// </summary>
internal static class Scenario05_LegacyProtocols
{
  private const int SjisPort = 15205;
  private const int FixedPort = 15215;
  private const int LengthFieldPort = 15225;

  public static async Task RunAsync(ILoggerFactory loggerFactory)
  {
    // Shift-JIS を使うために CodePages プロバイダーを登録（アプリ起動時に1回必要）
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    await RunShiftJisTerminatorDemoAsync(loggerFactory);
    await RunFixedLengthDemoAsync(loggerFactory);
    await RunLengthFieldDemoAsync(loggerFactory);
  }

  // ---------------------------------------------------------------------------
  // (1) 終端文字方式 + Shift-JIS
  // ---------------------------------------------------------------------------

  private static async Task RunShiftJisTerminatorDemoAsync(ILoggerFactory loggerFactory)
  {
    SampleConsole.Step("(1) 終端文字方式 + Shift-JIS: CR区切りで日本語応答を返す計測器を模擬します");

    var serverConfig = new ServerConfig
    {
      Name = "SjisInstrument",
      ListenPort = SjisPort,
      Encoding = "Shift-JIS",
      MessageTerminator = "\r",
    };
    await using var server = new TcpServer(serverConfig, loggerFactory.CreateLogger<TcpServer>());

    server.OnMessageReceivedAsync += async (message, sessionInfo, _) =>
    {
      try
      {
        var command = message.Text?.Trim();
        var reply = command switch
        {
          "*IDN?" => "計測器シミュレータ 型式SJ-100 Ver1.0",
          "MEAS?" => "測定値 23.5℃",
          _ => $"不明なコマンド: {command}",
        };
        await server.SendAsync(sessionInfo.SessionId, reply);
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };
    await server.StartAsync();

    var clientConfig = new ClientConfig
    {
      Name = "SjisClient",
      RemoteHost = "127.0.0.1",
      RemotePort = SjisPort,
      Encoding = "Shift-JIS",
      MessageTerminator = "\r",
      TimeoutMilliseconds = 3000,
    };
    await using var client = new TcpClient(
        clientConfig,
        new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort),
        loggerFactory.CreateLogger<TcpClient>());
    await client.ConnectAsync();

    var idn = await client.SendAsync("*IDN?");
    SampleConsole.Result($"*IDN? → {idn.Text?.Trim()}（{idn.RawData.Length}バイト = Shift-JISエンコード）");

    var meas = await client.SendAsync("MEAS?");
    SampleConsole.Result($"MEAS? → {meas.Text?.Trim()}");

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  // ---------------------------------------------------------------------------
  // (2) 固定長方式（ヘッダ4バイト + ボディ6バイト = 10バイト固定フレーム）
  // ---------------------------------------------------------------------------

  private static async Task RunFixedLengthDemoAsync(ILoggerFactory loggerFactory)
  {
    SampleConsole.Step("(2) 固定長方式: 10バイト固定（ヘッダ4 + ボディ6）のフレームを交換します");
    SampleConsole.Note("終端文字は使わず、受信側はバイト数だけでメッセージを区切る");

    var serverConfig = new ServerConfig
    {
      Name = "FixedLengthServer",
      ListenPort = FixedPort,
      Encoding = "ASCII",
      FixedHeaderLength = 4,
      FixedBodyLength = 6,
    };
    await using var server = new TcpServer(serverConfig, loggerFactory.CreateLogger<TcpServer>());

    server.OnMessageReceivedAsync += async (message, sessionInfo, _) =>
    {
      try
      {
        // 受信フレーム: "CMD0" + "READ  " → 応答フレーム: "RSP0" + "OK 42 "
        var header = Encoding.ASCII.GetString(message.RawData, 0, 4);
        var body = Encoding.ASCII.GetString(message.RawData, 4, 6);
        SampleConsole.Result($"サーバー受信: ヘッダ='{header}' ボディ='{body}'");

        var reply = "RSP0" + "OK 42 ";
        await server.SendAsync(sessionInfo.SessionId, Message.FromString(reply, Encoding.ASCII));
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };
    await server.StartAsync();

    var clientConfig = new ClientConfig
    {
      Name = "FixedLengthClient",
      RemoteHost = "127.0.0.1",
      RemotePort = FixedPort,
      Encoding = "ASCII",
      FixedHeaderLength = 4,
      FixedBodyLength = 6,
      TimeoutMilliseconds = 3000,
    };
    await using var client = new TcpClient(
        clientConfig,
        new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort),
        loggerFactory.CreateLogger<TcpClient>());
    await client.ConnectAsync();

    var request = "CMD0" + "READ  "; // ちょうど10バイトになるよう空白でパディング
    var response = await client.SendAsync(Message.FromString(request, Encoding.ASCII));
    SampleConsole.Result($"クライアント受信: '{response.Text}'（{response.RawData.Length}バイト固定）");

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  // ---------------------------------------------------------------------------
  // (3) 長さフィールド方式（先頭2バイト = フレーム全長のビッグエンディアン）
  // ---------------------------------------------------------------------------

  private static async Task RunLengthFieldDemoAsync(ILoggerFactory loggerFactory)
  {
    SampleConsole.Step("(3) 長さフィールド方式: 先頭2バイトに全長を持つバイナリフレームを交換します");
    SampleConsole.Note("ペイロード長が可変のバイナリプロトコルでよく使われる形式");

    var serverConfig = new ServerConfig
    {
      Name = "BinaryServer",
      ListenPort = LengthFieldPort,
      Encoding = "ASCII",
      LengthFieldOffset = 0,
      LengthFieldLength = 2,
    };
    await using var server = new TcpServer(serverConfig, loggerFactory.CreateLogger<TcpServer>());

    server.OnMessageReceivedAsync += async (message, sessionInfo, _) =>
    {
      try
      {
        var payload = Encoding.ASCII.GetString(message.RawData, 2, message.RawData.Length - 2);
        SampleConsole.Result($"サーバー受信: ペイロード='{payload}'（フレーム全長 {message.RawData.Length} バイト）");

        var replyPayload = payload == "TEMP?" ? "TEMP=23.5" : "ERR";
        await server.SendAsync(sessionInfo.SessionId, BuildLengthPrefixedFrame(replyPayload));
      }
      catch (Exception ex)
      {
        SampleConsole.Error($"応答送信に失敗: {ex.Message}");
      }
    };
    await server.StartAsync();

    var clientConfig = new ClientConfig
    {
      Name = "BinaryClient",
      RemoteHost = "127.0.0.1",
      RemotePort = LengthFieldPort,
      Encoding = "ASCII",
      LengthFieldOffset = 0,
      LengthFieldLength = 2,
      TimeoutMilliseconds = 3000,
    };
    await using var client = new TcpClient(
        clientConfig,
        new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort),
        loggerFactory.CreateLogger<TcpClient>());
    await client.ConnectAsync();

    var response = await client.SendAsync(BuildLengthPrefixedFrame("TEMP?"));
    var responsePayload = Encoding.ASCII.GetString(response.RawData, 2, response.RawData.Length - 2);
    SampleConsole.Result($"クライアント受信: ペイロード='{responsePayload}'");

    await client.DisconnectAsync();
    await server.StopAsync();
  }

  /// <summary>先頭2バイト（ビッグエンディアン）に全長を持つフレームを構築する</summary>
  private static Message BuildLengthPrefixedFrame(string payload)
  {
    var payloadBytes = Encoding.ASCII.GetBytes(payload);
    var frame = new byte[2 + payloadBytes.Length];
    BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)frame.Length);
    payloadBytes.CopyTo(frame, 2);
    return new Message { RawData = frame, Text = payload };
  }
}
