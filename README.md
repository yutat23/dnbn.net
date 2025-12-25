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
          "FailOnTimeout": true,
          "FailOnErrorResponse": true
        },
        "TimeoutMilliseconds": 5000,
        "HealthCheck": {
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

## 他のプロジェクトで使用する方法

このライブラリは `yutat23/dnbn.net` リポジトリで管理されています。

### GitHub Packagesから使用（推奨）

#### 1. nuget.config を作成

プロジェクトのルートに `nuget.config` を作成：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="github" value="https://nuget.pkg.github.com/yutat23/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

#### 2. 認証を設定

Personal Access Tokenを作成し、認証を設定：

```powershell
# Windows PowerShell
dotnet nuget add source https://nuget.pkg.github.com/yutat23/index.json `
  --name github `
  --username yutat23 `
  --password YOUR_GITHUB_TOKEN `
  --store-password-in-clear-text
```

#### 3. パッケージを追加

```bash
dotnet add package dnbn.net --version 1.0.0
```

### その他の方法

- **ローカルプロジェクト参照**: `dotnet add reference ../path/to/dnbn.net/dnbn.net.csproj`
- **ローカルNuGetパッケージ**: `dotnet pack -c Release` でパッケージを作成

詳細は以下を参照してください：
- [docs/SETUP_GITHUB.md](./docs/SETUP_GITHUB.md) - GitHubリポジトリのセットアップ手順
- [docs/USAGE.md](./docs/USAGE.md) - 使用方法の詳細
- [docs/PRIVATE_REPO.md](./docs/PRIVATE_REPO.md) - プライベートリポジトリでの管理方法

## 詳細

詳細な要件定義については、プロジェクトの要件定義書を参照してください。

