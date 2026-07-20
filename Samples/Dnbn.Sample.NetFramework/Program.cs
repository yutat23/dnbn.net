using System;
using System.Threading.Tasks;
using Dnbn.Configuration;
using Dnbn.Core;
using TcpClient = Dnbn.Core.TcpClient;

namespace Dnbn.Sample.NetFramework
{
  /// <summary>
  /// .NET Framework 4.8 から dnbn.net（netstandard2.0 ビルド）を使うサンプル。
  /// エコーサーバーとクライアントを同一プロセスで起動し、以下を順に実演する。
  ///   1. リクエスト／レスポンス（SendAsync）
  ///   2. サーバープッシュ電文の受信（OnMessageReceived）
  ///   3. アプリケーションレベル KeepAlive（PING/応答の自動送受信）
  /// C# 7.3（.NET Framework プロジェクトの既定）で書かれているため、
  /// 既存のレガシープロジェクトにそのまま持ち込める。
  /// </summary>
  internal static class Program
  {
    private const int Port = 15301;

    private static async Task Main()
    {
      Console.OutputEncoding = System.Text.Encoding.UTF8;

      // ---------------------------------------------------------------
      // 1. エコーサーバーを起動
      // ---------------------------------------------------------------
      Console.WriteLine("[1] エコーサーバーを起動します");

      var serverConfig = new ServerConfig
      {
        Name = "EchoServer",
        ListenPort = Port,
        Encoding = "UTF-8",       // Shift-JIS 等も指定可能（例: "Shift_JIS"）
        MessageTerminator = "\n", // 改行を1メッセージの区切りとする
      };

      var server = new TcpServer(serverConfig);

      // 接続してきたクライアントのセッションIDを控えておく（後でプッシュ送信に使う）
      string lastSessionId = null;
      server.OnClientConnected += (sender, session) =>
      {
        lastSessionId = session.SessionId;
        Console.WriteLine($"    サーバー: クライアント接続 (SessionId={session.SessionId})");
      };

      // 受信したメッセージを「ECHO:」付きで送り返す。
      // OnMessageReceivedAsync はセッション内の受信順序を保ったまま await される
      server.OnMessageReceivedAsync += async (message, session, ct) =>
      {
        try
        {
          await server.SendAsync(session.SessionId, "ECHO: " + (message.Text ?? "").Trim());
        }
        catch (Exception ex)
        {
          // イベントハンドラ内の例外は必ず握りつぶす（ライブラリの受信ループを守るため）
          Console.WriteLine("    サーバー: 応答送信に失敗: " + ex.Message);
        }
      };

      var client = default(TcpClient);
      try
      {
        await server.StartAsync();
        Console.WriteLine($"    ポート {Port} で待ち受け開始");

        // ---------------------------------------------------------------
        // 2. クライアントを接続
        // ---------------------------------------------------------------
        Console.WriteLine("[2] クライアントを接続します");

        var clientConfig = new ClientConfig
        {
          Name = "SampleClient",
          RemoteHost = "127.0.0.1",
          RemotePort = Port,
          Encoding = "UTF-8",
          MessageTerminator = "\n",
          TimeoutMilliseconds = 5000,

          // NW障害時・接続失敗時の自動再接続（指数バックオフ）
          ConnectionRetryPolicy = new RetryPolicy
          {
            MaxRetryCount = 3,
            InitialDelayMs = 500,
          },

          // アプリケーションレベル KeepAlive:
          // 2秒ごとに "PING" を送り、応答が返らなければ切断→自動再接続する
          KeepAlive = new KeepAliveConfig
          {
            Enabled = true,
            IntervalSeconds = 2,
            Message = "PING",
            // KeepAlive応答と通常応答を区別する述語（FIFO相関の混線を防ぐため設定を推奨）
            ResponsePredicate = m => m.Text != null && m.Text.StartsWith("ECHO: PING"),
          },
        };

        client = new TcpClient(
            clientConfig,
            new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort));

        // 接続状態遷移の観測（自動再接続の開始も検知できる）
        client.OnConnectionStateChanged += (sender, e) =>
            Console.WriteLine($"    クライアント: 状態遷移 {e.previous} → {e.current}");

        // サーバープッシュ電文（応答待ちしていない受信）はこのイベントに届く
        client.OnMessageReceived += (sender, message) =>
            Console.WriteLine("    クライアント: プッシュ受信: " + (message.Text ?? "").Trim());

        // KeepAlive応答の受信を観測（通常は購読不要。動作確認用）
        client.OnKeepAliveResponseReceived += (sender, message) =>
            Console.WriteLine("    クライアント: KeepAlive応答受信: " + (message.Text ?? "").Trim());

        await client.ConnectAsync();

        // ---------------------------------------------------------------
        // 3. リクエスト／レスポンス
        // ---------------------------------------------------------------
        Console.WriteLine("[3] メッセージを送信して応答を待ちます（SendAsync）");

        var response = await client.SendAsync("こんにちは");
        Console.WriteLine("    送信: こんにちは → 応答: " + (response.Text ?? "").Trim());

        var response2 = await client.SendAsync("dnbn.net from .NET Framework 4.8");
        Console.WriteLine("    送信: dnbn.net from .NET Framework 4.8 → 応答: " + (response2.Text ?? "").Trim());

        // ---------------------------------------------------------------
        // 4. サーバーからのプッシュ電文
        // ---------------------------------------------------------------
        Console.WriteLine("[4] サーバーからプッシュ電文を送ります");

        if (lastSessionId != null)
        {
          await server.SendAsync(lastSessionId, "サーバーからのお知らせ");
        }

        // プッシュとKeepAlive（2秒間隔）の動作が見えるまで少し待つ
        Console.WriteLine("[5] KeepAlive の動作を5秒間観測します（2秒間隔で PING が送られる）");
        await Task.Delay(TimeSpan.FromSeconds(5));

        // ---------------------------------------------------------------
        // 5. 後片付け
        // ---------------------------------------------------------------
        Console.WriteLine("[6] 切断してサーバーを停止します");
        await client.DisconnectAsync();
        await server.StopAsync();
      }
      finally
      {
        // C# 8 の await using が使えないため、明示的に Dispose する
        if (client != null)
        {
          client.Dispose();
        }
        server.Dispose();
      }

      Console.WriteLine("完了。Enterキーで終了します。");
      Console.ReadLine();
    }
  }
}
