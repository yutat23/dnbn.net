# 利用例

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

server.OnMessageReceived += async (_, e) =>
{
    await server.SendAsync(e.sessionInfo.SessionId, $"OK:{e.message.Text?.Trim()}");
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

