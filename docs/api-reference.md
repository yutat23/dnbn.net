# API概要

詳細なシグネチャはコード上の XML コメントも参照してください。ここでは利用時に触る主要APIだけをまとめます。

## ITcpMessengerFactory

DIで `AddDnbnNet` を使う場合の入口です。

```csharp
var factory = provider.GetRequiredService<ITcpMessengerFactory>();
var server = factory.CreateServer("MainServer");
var client = factory.CreateClient("MainClient");
```

## ITcpServer

主なメンバー:

| メンバー | 説明 |
|---|---|
| `StartAsync()` | サーバーを起動 |
| `StopAsync()` | サーバーを停止 |
| `SendAsync(sessionId, message)` | 特定セッションへ送信 |
| `BroadcastAsync(message)` | 全セッションへ送信 |
| `GetSession(sessionId)` | セッション取得 |
| `GetAllSessions()` | 全セッション取得 |
| `ConnectionInfo` | 接続状態と統計 |
| `OnMessageReceived` | メッセージ受信イベント |
| `OnClientConnected` | クライアント接続イベント |
| `OnClientDisconnected` | クライアント切断イベント |
| `OnError` | エラーイベント |
| `MessageReceived` | Rx Observable |

## ITcpClient

主なメンバー:

| メンバー | 説明 |
|---|---|
| `ConnectAsync()` | 接続 |
| `DisconnectAsync()` | 切断 |
| `SendAsync(message)` | 送信して応答を待つ |
| `SendAndWaitAsync(message, predicate, timeout)` | 条件に合う応答を待つ |
| `SendOneWayAsync(message)` | 応答を待たずに送信 |
| `WaitForConnectionAsync(timeout)` | 接続完了を待つ |
| `InterruptReconnectDelay()` | 再接続の待機を中断 |
| `NotificationPredicate` | 通知電文の判定 |
| `KeepAlive` | KeepAlive設定の取得/変更 |
| `TimeoutMilliseconds` | 既定タイムアウトの取得/変更 |
| `RetryPolicy` | メッセージ送信リトライ設定 |
| `ConnectionRetryPolicy` | 接続リトライ設定 |
| `ConnectionInfo` | 接続状態と統計 |
| `State` | 詳細な接続状態（`ConnectionState`） |
| `OnMessageReceived` | プッシュ通知受信イベント |
| `OnKeepAliveResponseReceived` | KeepAlive応答イベント |
| `OnConnectionStateChanged` | 接続状態変化イベント |
| `OnMessageTrace` | 要求・応答・通知・KeepAliveを含む全送受信の診断イベント |

### ConnectionState

`State` プロパティと `OnConnectionStateChanged` イベントで、`OnConnected` / `OnDisconnected` だけでは分からない「自動再接続中かどうか」を観測できます。

| 値 | 意味 |
|---|---|
| `Disconnected` | 未接続（初期状態、意図的な切断後、再接続の断念後） |
| `Connecting` | `ConnectAsync` による接続試行中（リトライ待機中を含む） |
| `Connected` | 接続済み |
| `Reconnecting` | NW障害後の自動再接続中（リトライ待機中を含む） |

```csharp
client.OnConnectionStateChanged += (_, e) =>
{
    Console.WriteLine($"{e.previous} -> {e.current}");
};
```

### MessageTrace

`OnMessageTrace` は、`OnMessageReceived` には流れない `SendAsync` の応答やKeepAliveも観測します。送信方向の `RawData` / `Text` は終端文字を含む実送信内容です。イベントの `Message` は診断用スナップショットなので、変更しても実際の送受信処理には影響しません。

```csharp
client.OnMessageTrace += (_, trace) =>
{
    Console.WriteLine($"{trace.Timestamp:o} {trace.Direction} {trace.Kind} {trace.Message.Text}");
};
```

## Message

| プロパティ | 説明 |
|---|---|
| `RawData` | 受信/送信バイト列 |
| `Text` | エンコーディング変換後の文字列 |
| `Code` | アプリ側で使えるメッセージコード |
| `Timestamp` | メッセージ生成時刻 |
| `Metadata` | フィルターなどが使う追加情報 |

作成ヘルパー:

```csharp
var message = Message.FromString("HELLO", Encoding.UTF8);
var binary = Message.FromBytes(bytes, Encoding.UTF8);
```

## IMessageFilter

送信前/受信後にメッセージを加工できます。チェックサム付与、検証、ログ、プロトコル固有の変換に使います。

```csharp
public interface IMessageFilter
{
    Task<Message> OnSendingAsync(Message msg, IMessageContext ctx);
    Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx);
}
```
