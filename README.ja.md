# dnbn.net

[English](./README.md) | 日本語

![language](https://img.shields.io/badge/language-C%23-green?logo=csharp)
![dotnet](https://img.shields.io/badge/dotnet-8.0%20%7C%20netstandard2.0-blue?logo=dotnet)
[![NuGet version](https://img.shields.io/nuget/v/dnbn.net)](https://www.nuget.org/packages/dnbn.net/)

TCP の独自電文を .NET から扱うためのメッセージ送受信ライブラリです。

終端文字、固定長、長さフィールド付き可変長のようなレガシー寄りの TCP プロトコルを、サーバー/クライアントの両方で扱えます。

## 対応フレームワーク

| ターゲット | 対応ランタイム |
|---|---|
| `net8.0` | .NET 8 以降 |
| `netstandard2.0` | .NET Framework 4.6.2 以降(4.7.2 以降を推奨)など |

※ `dnbn.net.WebUI`(Web UI パッケージ)は ASP.NET Core を使用するため .NET 8 以降専用です。
※ TCP レベル KeepAlive の詳細パラメータ(`TcpKeepAlive` の Time/Interval/RetryCount)は、.NET Framework では Windows 10 1709 以降で有効です(未対応環境では SO_KEEPALIVE の有効化のみ行われます)。

## 主な機能

- TCP サーバー/クライアントの作成
- `SendAsync` によるリクエスト/レスポンス型の送受信
- `SendOneWayAsync` による応答を待たない送信
- サーバーからのプッシュ受信イベント
- `OnMessageReceived` と `IObservable` による受信購読
- 終端文字、固定長、長さフィールド付き可変長のメッセージ分割
- Shift-JIS など任意エンコーディングの指定
- 接続リトライ、メッセージ送信リトライ、KeepAlive
- 複数クライアントのセッション管理とブロードキャスト
- メッセージフィルターパイプライン
- 接続状態と統計情報の取得
- `ConnectionState` と `OnConnectionStateChanged` による接続状態遷移の観測（自動再接続中の検知）
- `OnMessageTrace` による要求・応答・通知・KeepAliveを含む送受信診断
- Generic Host連動の名前付きクライアント登録・自動接続/切断
- 応答待ち要求数の制限と、timeout/cancel後の安全な接続回復
- セッション内の受信順序を保つawait可能なサーバーハンドラ
- オプションの Web UI パッケージ

## インストール

```bash
dotnet add package dnbn.net
```

Web UI を使う場合は追加パッケージを入れます。

```bash
dotnet add package dnbn.net.WebUI
```

## ドキュメント

- [設定リファレンス](./docs/ja/configuration.md)
- [メッセージプロトコル](./docs/ja/protocols.md)
- [API概要](./docs/ja/api-reference.md)
- [利用例](./docs/ja/usage.md)
- [Web UI](./docs/ja/web-ui.md)
- [ログ](./docs/ja/logging.md)
- [トラブルシューティング](./docs/ja/troubleshooting.md)
- [変更履歴](./CHANGELOG.ja.md)

## クイックスタート

以下はサーバーとクライアントを同じプロセスで起動する最小例です。

```csharp
using Dnbn.Configuration;
using Dnbn.Core;
using TcpClient = Dnbn.Core.TcpClient;

var port = 15201;

await using var server = new TcpServer(new ServerConfig
{
    Name = "EchoServer",
    ListenPort = port,
    Encoding = "UTF-8",
    MessageTerminator = "\n",
});

server.OnMessageReceivedAsync += async (message, sessionInfo, cancellationToken) =>
{
    await server.SendAsync(
        sessionInfo.SessionId,
        $"ECHO: {message.Text?.Trim()}",
        cancellationToken);
};

await server.StartAsync();

var clientConfig = new ClientConfig
{
    Name = "EchoClient",
    RemoteHost = "127.0.0.1",
    RemotePort = port,
    Encoding = "UTF-8",
    MessageTerminator = "\n",
    TimeoutMilliseconds = 5000,
    MaxConcurrentResponseWaits = 1,
    IncompleteRequestRecovery = IncompleteRequestRecovery.Reconnect,
    WaitForConnectionOnSend = true,
};

await using var client = new TcpClient(
    clientConfig,
    new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort));

await client.ConnectAsync();

var response = await client.SendAsync("hello");
Console.WriteLine(response.Text);

await client.DisconnectAsync();
await server.StopAsync();
```

## appsettings.json と DI

設定ファイルから構成する場合は、`dnbn.net` セクションを使います。

```json
{
  "dnbn.net": {
    "Servers": [
      {
        "Name": "MainServer",
        "ListenPort": 5000,
        "Encoding": "UTF-8",
        "MessageTerminator": "\n"
      }
    ],
    "Clients": [
      {
        "Name": "MainClient",
        "RemoteHost": "127.0.0.1",
        "RemotePort": 5000,
        "Encoding": "UTF-8",
        "MessageTerminator": "\n",
        "TimeoutMilliseconds": 5000,
        "MaxConcurrentResponseWaits": 1,
        "IncompleteRequestRecovery": "Reconnect",
        "WaitForConnectionOnSend": true,
        "ConnectionRetryPolicy": {
          "MaxRetryCount": -1,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 1000,
          "MaxDelayMs": 10000
        }
      }
    ]
  }
}
```

```csharp
using Dnbn.Core;
using Dnbn.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddDnbnNet(configuration);

await using var provider = services.BuildServiceProvider();

var factory = provider.GetRequiredService<ITcpMessengerFactory>();
var server = factory.CreateServer("MainServer");
var client = factory.CreateClient("MainClient");
```

`TcpMessenger` セクション名と `AddTcpMessenger` は後方互換性のために残っていますが、新しいコードでは `dnbn.net` と `AddDnbnNet` を使ってください。

## メッセージ境界

TCP はストリームなので、電文の区切り方を設定する必要があります。

### 終端文字

テキスト系プロトコルでは `MessageTerminator` を指定します。

```json
{
  "MessageTerminator": "\r\n"
}
```

受信時だけ別の終端文字候補を使いたい場合は `ReceiveMessageTerminator` を指定できます。

```json
{
  "MessageTerminator": "\r",
  "ReceiveMessageTerminator": ["#", "?"]
}
```

### 固定長

ヘッダ長とボディ長が固定のプロトコルでは `FixedHeaderLength` と `FixedBodyLength` を指定します。

```json
{
  "FixedHeaderLength": 4,
  "FixedBodyLength": 20
}
```

### 長さフィールド付き可変長

ヘッダ内の長さフィールドでボディ長を表すプロトコルでは、長さフィールドの位置とサイズを指定します。

```json
{
  "FixedHeaderLength": 6,
  "LengthFieldOffset": 2,
  "LengthFieldLength": 4
}
```

終端文字方式、または固定長／長さフィールド方式のいずれかの設定は必須です。不完全・矛盾した設定はendpoint生成時に例外になります。

## よく使う設定

### 接続リトライ

```json
{
  "ConnectionRetryPolicy": {
    "MaxRetryCount": -1,
    "RetryDelayStrategy": "Exponential",
    "InitialDelayMs": 1000,
    "MaxDelayMs": 60000
  }
}
```

`MaxRetryCount: -1` は接続リトライで無限リトライを表します。

### KeepAlive

```json
{
  "KeepAlive": {
    "Enabled": true,
    "IntervalSeconds": 30,
    "Message": "PING",
    "DisconnectOnTimeout": true
  }
}
```

KeepAlive 応答は `OnKeepAliveResponseReceived` で受け取れます。`DisconnectOnTimeout` の既定値は `true` です。応答タイムアウト時は遅延応答の誤相関を防ぐため接続を切断し、`ConnectionRetryPolicy` が設定されていれば自動再接続します。

### メッセージログ

```json
{
  "EnableMessageLogging": true
}
```

`EnableMessageLogging` はサーバー設定とクライアント設定の両方で使えます。`true` の場合は電文内容が `Information` レベルで、`false`（既定）の場合は `Debug` レベルで出力されます。

## 受信イベントの考え方

クライアント側の受信経路は用途で分かれます。

| 経路 | 用途 |
|---|---|
| `SendAsync` の戻り値 | 自分が送ったリクエストへの応答 |
| `OnMessageReceived` / `MessageReceived` | リクエストと無関係に届くプッシュ通知 |
| `OnKeepAliveResponseReceived` | KeepAlive メッセージへの応答 |

`SendAsync` が受け取った応答は、通常 `OnMessageReceived` には流れません。通知電文を明示的に分けたい場合は `NotificationPredicate` を設定できます。

応答しないコマンドは `SendAsync` ではなく `SendOneWayAsync` で送信してください。`SendAsync` は応答必須の契約です。FIFOで応答を識別するプロトコルでは、`MaxConcurrentResponseWaits: 1` と `IncompleteRequestRecovery: Reconnect` の組み合わせを推奨します。

## Web UI

Web UI は別パッケージ `dnbn.net.WebUI` で提供されます。接続状態、送受信数、セッション情報などをブラウザで確認できます。

```csharp
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.WebUI;

var webUI = new WebUIService(
    new ITcpServer[] { server },
    new ITcpClient[] { client },
    new WebUIConfig
    {
        Enabled = true,
        Port = 8080,
        BindAddress = "localhost",
        UpdateIntervalSeconds = 1
    });

await webUI.StartAsync();
```

起動後、既定では `http://localhost:8080` から確認できます。

## サンプル

サンプルプロジェクトには、機能ごとの実行シナリオがあります。

```bash
dotnet run --project Samples/Dnbn.Sample
```

番号を指定して直接実行することもできます。

```bash
dotnet run --project Samples/Dnbn.Sample -- 1
```

| # | 内容 |
|---|---|
| 1 | クイックスタート |
| 2 | チャット / ブロードキャスト |
| 3 | 障害と自動再接続 |
| 4 | KeepAlive と死活監視 |
| 5 | Shift-JIS、固定長、長さフィールド方式 |
| 6 | タイムアウト、リトライ、応答マッチング |
| 7 | フィルター、統計情報、メッセージ履歴・送信を含む Web UI |
| 8 | appsettings.json と DI の対話プレイグラウンド |

詳細は [Samples/Dnbn.Sample/README.ja.md](./Samples/Dnbn.Sample/README.ja.md) を参照してください。

### .NET Framework から使う

.NET Framework 4.8 のコンソールアプリから利用するサンプルは [Samples/Dnbn.Sample.NetFramework](./Samples/Dnbn.Sample.NetFramework/) にあります(C# 7.3 の範囲で記述。Windows で `dotnet run` または Visual Studio から実行)。説明は [README.ja.md](./Samples/Dnbn.Sample.NetFramework/README.ja.md) を参照してください。

```bash
cd Samples/Dnbn.Sample.NetFramework
dotnet run
```

## ログ

dnbn.net は `Microsoft.Extensions.Logging` を使います。Console、Serilog、NLog、log4net など、アプリ側で任意のログプロバイダーを設定してください。

log4net を使う場合は、アプリ側で `Microsoft.Extensions.Logging.Log4Net.AspNetCore` を入れてから `AddDnbnNet` を呼び出します。

```csharp
services.AddLogging(builder => builder.AddLog4Net());
services.AddDnbnNet(configuration);
```

`AddTcpMessengerWithLog4net` は互換性のために残っていますが、現在は非推奨で、呼び出すと `NotSupportedException` になります。
