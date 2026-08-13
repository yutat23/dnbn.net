# 利用例

[English](../usage.md) | 日本語

## DIなしで直接使う

```csharp
using Dnbn.Configuration;
using Dnbn.Core;
using TcpClient = Dnbn.Core.TcpClient;

await using var server = new TcpServer(new ServerConfig
{
    Name = "Server",
    ListenPort = 5000,
    MessageTerminator = "\n",
});

server.OnMessageReceivedAsync += async (message, sessionInfo, cancellationToken) =>
{
    await server.SendAsync(sessionInfo.SessionId, $"OK:{message.Text?.Trim()}", cancellationToken);
};

await server.StartAsync();

var clientConfig = new ClientConfig
{
    Name = "Client",
    RemoteHost = "127.0.0.1",
    RemotePort = 5000,
    MessageTerminator = "\n",
};

await using var client = new TcpClient(
    clientConfig,
    new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort));

await client.ConnectAsync();
var response = await client.SendAsync("PING");
```

## DIで使う

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddDnbnNet(configuration);

await using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<ITcpMessengerFactory>();

var server = factory.CreateServer("MainServer");
var client = factory.CreateClient("MainClient");
```

### 設定済みクライアントをホストと連動させる

ASP.NET Core / Generic Hostでは、設定済みクライアントをkeyed singletonとして登録し、起動時の接続と停止時の切断を自動化できます。既存の `AddDnbnNet` の後に追加します。

```csharp
services.AddDnbnNet(configuration);
services.AddDnbnNetHostedClients(configuration);

var client = serviceProvider.GetRequiredKeyedService<ITcpClient>("MainClient");
```

一覧が必要な処理では `IDnbnClientRegistry` を注入できます（`IDnbnClientCollection`も互換性のため利用可能）。クライアントは名前ごとに単一インスタンスで、keyed serviceとregistryから同じインスタンスが返ります。

DBなどから起動時に組み立てた型付き設定も登録できます。

```csharp
services.AddDnbnNet(new TcpMessengerConfig());
services.AddDnbnNetHostedClients(dynamicClientConfigs);
```

自動接続を行わず名前付きregistryだけを使う場合は、
`connectOnHostStart: false` を指定できます。

未接続時の送信を接続待ちにしたい場合は、対象クライアントに次を設定します。既定値は `false` のため、既存コードの即時例外動作は変わりません。

```json
{
  "WaitForConnectionOnSend": true,
  "WaitForConnectionTimeoutMilliseconds": 10000
}
```

## プッシュ通知を受ける

`SendAsync` の応答ではなく、サーバーから任意タイミングで届く通知は `OnMessageReceived` で受けます。

```csharp
client.OnMessageReceived += (_, message) =>
{
    Console.WriteLine($"push: {message.Text}");
};
```

通知電文を応答マッチングから除外したい場合は `NotificationPredicate` を設定します。

```csharp
client.NotificationPredicate = message =>
    message.Text?.StartsWith("EVENT:") == true;
```

## 条件付きで応答を待つ

```csharp
var response = await client.SendAndWaitAsync(
    "STATUS",
    message => message.Text?.StartsWith("OK") == true,
    TimeSpan.FromSeconds(3));
```

## 応答を待たずに送る

```csharp
await client.SendOneWayAsync("NOTIFY");
```

応答しないコマンドには必ずこちらを使います。応答待ちtimeoutや`MaxConcurrentResponseWaits`の枠は使用しません。

## FIFO応答相関を安全に使う

```json
{
  "MaxConcurrentResponseWaits": 1,
  "IncompleteRequestRecovery": "Reconnect",
  "WaitForConnectionOnSend": true,
  "WaitForConnectionTimeoutMilliseconds": 10000
}
```

送信済み要求がtimeout/cancelになると、遅延応答を後続要求へ誤対応させないため接続を再確立します。`KeepConnection`は後方互換用で、wire書き込み開始後に未完了になった場合は警告ログが出ます。

## ブロードキャスト

```csharp
await server.BroadcastAsync("SERVER_MAINTENANCE");
```

## 接続状態を見る

```csharp
var info = client.ConnectionInfo;
Console.WriteLine($"connected={info.IsConnected}, sent={info.MessagesSent}, received={info.MessagesReceived}");

var serverInfo = server.ConnectionInfo;
Console.WriteLine($"running={serverInfo.IsRunning}, sessions={serverInfo.ConnectionCount}");
```
