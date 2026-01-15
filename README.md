# dnbn.net - TCP独自電文ライブラリ

.NET 8+ 対応の柔軟なTCP電文ライブラリです。

**リポジトリ**: https://github.com/yutat23/dnbn.net

## 主な機能

- 固定長/可変長/終端文字など柔軟な電文プロトコル対応
- サーバーとクライアントを同一アプリ内で併用可能
- 長期接続を前提とした安定したTCP通信
- メッセージ受信イベント（IObservable/イベントデリゲート対応）
- 送信後の応答待ち（Promise的制御）
- リトライポリシー、フィルターパイプライン対応
- 接続リトライ機能（接続失敗時およびNW障害時の自動再接続、無限リトライ対応）
- キープアライブ機能（定期的なメッセージ送信で接続維持）
- 複数クライアントを同一ポートで受信可能
- appsettings.json統合設定

## インストール

```bash
dotnet add package dnbn.net
```

## クイックスタート

### 1. appsettings.json に設定を追加

```json
{
  "TcpMessenger": {
    "Servers": [
      {
        "Name": "MainServer",
        "ListenPort": 5000,
        "Encoding": "UTF-8",
        "MessageTerminator": "\r",
        "ClientIdentification": "SourceEndpoint"
      }
    ],
    "Clients": [
      {
        "Name": "ControllerA",
        "RemoteHost": "192.168.1.10",
        "RemotePort": 7000,
        "RetryPolicy": {
          "MaxRetryCount": 3,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 500,
          "MaxDelayMs": 30000,
          "FailOnTimeout": true,
          "FailOnErrorResponse": true
        },
        "ConnectionRetryPolicy": {
          "MaxRetryCount": -1,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 1000,
          "MaxDelayMs": 60000
        },
        "TimeoutMilliseconds": 5000,
        "KeepAlive": {
          "Enabled": true,
          "IntervalSeconds": 30,
          "Message": "PING\r"
        }
      }
    ]
  }
}
```

### 2. サービスを登録

```csharp
using Dnbn.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddTcpMessenger(configuration);

var serviceProvider = services.BuildServiceProvider();
```

### 3. サーバーを使用

```csharp
using Dnbn.Core;
using Dnbn.Models;

var factory = serviceProvider.GetRequiredService<ITcpMessengerFactory>();

// サーバーを作成して起動
var server = factory.CreateServer("MainServer");
server.OnMessageReceived += (sender, args) =>
{
    var (message, sessionInfo) = args;
    Console.WriteLine($"Received: {message.Text} from {sessionInfo.SessionId}");
    
    // 応答を送信
    var response = Message.FromString("OK\r", System.Text.Encoding.UTF8);
    server.SendAsync(sessionInfo.SessionId, response).Wait();
};
server.OnClientConnected += (sender, sessionInfo) =>
{
    Console.WriteLine($"Client connected: {sessionInfo.SessionId}");
};
server.OnClientDisconnected += (sender, sessionInfo) =>
{
    Console.WriteLine($"Client disconnected: {sessionInfo.SessionId}");
};

await server.StartAsync();
```

### 4. クライアントを使用

```csharp
// クライアントを作成して接続
var client = factory.CreateClient("ControllerA");
client.OnMessageReceived += (sender, message) =>
{
    Console.WriteLine($"Received: {message.Text}");
};
client.OnConnected += (sender, args) =>
{
    Console.WriteLine("Connected to server");
};
client.OnDisconnected += (sender, args) =>
{
    Console.WriteLine("Disconnected from server");
};

// キープアライブ応答イベントの処理
client.OnKeepAliveResponseReceived += (sender, message) =>
{
    Console.WriteLine($"Keep-alive response: {message.Text}");
    // 状態取得コマンドの応答を使用して処理を行う例
};

await client.ConnectAsync();

// メッセージを送信
var msg = Message.FromString("HELLO\r", System.Text.Encoding.UTF8);
await client.SendAsync(msg);

// 応答を待つ
var response = await client.SendAndWaitAsync(
    msg,
    m => m.Code == "OK" || m.Text?.StartsWith("OK") == true,
    TimeSpan.FromSeconds(3)
);

Console.WriteLine($"Response: {response.Text}");
```

### 5. Promise的チェーン処理

```csharp
var initMessage = Message.FromString("INIT\r", System.Text.Encoding.UTF8);

await client
    .SendAndWaitAsync(initMessage, m => m.Code == "OK", TimeSpan.FromSeconds(3))
    .Then(async resp =>
    {
        var next = Message.FromString($"NEXT:{resp.Text}\r", System.Text.Encoding.UTF8);
        return await client.SendAndWaitAsync(next, m => m.Code == "OK", TimeSpan.FromSeconds(3));
    })
    .Then(async final =>
    {
        Console.WriteLine($"Final response: {final?.Text}");
        return (Message?)null;
    });
```

### 6. Observableパターン

```csharp
using System.Reactive.Linq;

server.MessageReceived
    .Where(args => args.message.Code == "ALERT")
    .Subscribe(args =>
    {
        var (message, sessionInfo) = args;
        Console.WriteLine($"Alert from {sessionInfo.SessionId}: {message.Text}");
    });
```

### 7. フィルターパイプライン

```csharp
using Dnbn.Filters;
using Dnbn.Models;

public class LoggingFilter : IMessageFilter
{
    public Task<Message> OnSendingAsync(Message msg, IMessageContext ctx)
    {
        Console.WriteLine($"[SEND] {msg.Text}");
        return Task.FromResult(msg);
    }

    public Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx)
    {
        Console.WriteLine($"[RECV] {msg.Text}");
        return Task.FromResult(msg);
    }
}

// フィルターを登録
services.AddSingleton<IMessageFilter, LoggingFilter>();
```

### 8. 接続リトライ機能

接続失敗時やNW障害時に自動的にリトライする機能です。`ConnectionRetryPolicy`を設定することで、接続の確立と維持を自動化できます。

#### 設定例

```json
{
  "TcpMessenger": {
    "Clients": [
      {
        "Name": "ControllerA",
        "RemoteHost": "192.168.1.10",
        "RemotePort": 7000,
        "ConnectionRetryPolicy": {
          "MaxRetryCount": -1,  // -1 で無限リトライ、正の値で指定回数リトライ
          "RetryDelayStrategy": "Exponential",  // "Fixed" または "Exponential"
          "InitialDelayMs": 1000,  // 初期待機時間（ミリ秒）
          "MaxDelayMs": 60000  // 最大待機時間（ミリ秒）。指数バックオフ時の上限
        }
      }
    ]
  }
}
```

#### 動作

- **接続時のリトライ**: `ConnectAsync()`呼び出し時に接続に失敗した場合、`ConnectionRetryPolicy`に基づいて自動的にリトライします
- **NW障害時の自動再接続**: 通信中にNW障害が発生した場合、自動的に再接続を試行します
- **無限リトライ**: `MaxRetryCount = -1`に設定すると、接続成功まで永続的にリトライを続けます
- **指数バックオフ**: `RetryDelayStrategy = "Exponential"`の場合、リトライ間隔が指数関数的に増加します（`MaxDelayMs`で上限が設定されます）

#### リトライ遅延の計算

- **Fixed（固定遅延）**: 常に`InitialDelayMs`の待機時間
- **Exponential（指数バックオフ）**: `InitialDelayMs * 2^retryCount`で計算され、`MaxDelayMs`を上限として適用

**例**（`InitialDelayMs = 1000`, `MaxDelayMs = 60000`）:
- 1回目: 1秒
- 2回目: 2秒
- 3回目: 4秒
- 4回目: 8秒
- 5回目: 16秒
- 6回目: 32秒
- 7回目以降: 60秒（`MaxDelayMs`で上限）

#### メッセージ送信時のリトライ

メッセージ送信時のリトライは`RetryPolicy`で設定します。こちらも`MaxDelayMs`を設定することで、指数バックオフの上限を制御できます。

```json
{
  "RetryPolicy": {
    "MaxRetryCount": 3,
    "RetryDelayStrategy": "Exponential",
    "InitialDelayMs": 500,
    "MaxDelayMs": 30000,  // メッセージ送信時の最大待機時間
    "FailOnTimeout": true,
    "FailOnErrorResponse": true
  }
}
```

#### 注意事項

- 意図的な切断（`DisconnectAsync(true)`）の場合は自動再接続しません
- 無限リトライ（`MaxRetryCount = -1`）の場合、アプリケーション終了まで接続を試行し続けます
- ログにリトライ試行回数とエラー内容が記録されます

### 9. log4netとの統合

このライブラリはlog4netと統合できます。アプリ側でlog4netを使用している場合、その設定に合わせてログ出力されます。

#### log4netの設定

アプリ側でlog4netを設定します（例：`log4net.config`）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<log4net>
  <appender name="ConsoleAppender" type="log4net.Appender.ConsoleAppender">
    <layout type="log4net.Layout.PatternLayout">
      <conversionPattern value="%date [%thread] %level %logger - %message%newline" />
    </layout>
  </appender>
  <appender name="FileAppender" type="log4net.Appender.FileAppender">
    <file value="logs/dnbn.log" />
    <appendToFile value="true" />
    <layout type="log4net.Layout.PatternLayout">
      <conversionPattern value="%date [%thread] %level %logger - %message%newline" />
    </layout>
  </appender>
  <root>
    <level value="DEBUG" />
    <appender-ref ref="ConsoleAppender" />
    <appender-ref ref="FileAppender" />
  </root>
  <logger name="Dnbn.Core.TcpClient">
    <level value="DEBUG" />
  </logger>
  <logger name="Dnbn.Core.TcpServer">
    <level value="DEBUG" />
  </logger>
</log4net>
```

#### log4netを使用する場合のサービス登録

```csharp
using Dnbn.Extensions;
using log4net.Config;

// log4net設定を読み込む
XmlConfigurator.Configure(new System.IO.FileInfo("log4net.config"));

var services = new ServiceCollection();

// log4netと共にTCP Messengerサービスを登録
services.AddTcpMessengerWithLog4net(configuration);

var serviceProvider = services.BuildServiceProvider();
```

#### ログレベル

ライブラリは以下のログレベルを使用します：

- **DEBUG**: 電文受信（メッセージの詳細）
- **INFO**: 接続（CONNECT）、サーバー起動/停止、意図的な切断
- **WARN**: キープアライブタイムアウトなど警告
- **ERROR**: 意図しない切断（NW障害など）、受信エラー、接続エラー

#### 手動でlog4netアダプターを登録する場合

```csharp
using Dnbn.Logging;
using Microsoft.Extensions.Logging;

// log4net設定を読み込む
XmlConfigurator.Configure(new System.IO.FileInfo("log4net.config"));

var services = new ServiceCollection();

// log4netアダプターを手動で登録
services.AddSingleton(typeof(ILogger<>), typeof(Log4netLoggerAdapter<>));
services.AddSingleton<ILoggerFactory, Log4netLoggerFactoryAdapter>();

// TCP Messengerサービスを登録
services.AddTcpMessenger(configuration);
```

## サンプルプロジェクト

実際の使用例は `Samples/TcpMessenger.Sample` プロジェクトを参照してください。

### サンプルプロジェクトの実行

```bash
cd Samples/TcpMessenger.Sample
dotnet run
```

サンプルプロジェクトには以下の機能が含まれています：

- **サーバーモード**: ポート5000でリッスンし、クライアントからのメッセージをエコー
- **クライアントモード**: localhost:5000に接続し、メッセージを送信
- **統合モード**: サーバーとクライアントを同時に起動し、Promise的チェーン処理の例を実行

詳細は [Samples/TcpMessenger.Sample/README.md](./Samples/TcpMessenger.Sample/README.md) を参照してください。
