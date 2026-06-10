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
| `OnMessageReceived` | プッシュ通知受信イベント |
| `OnKeepAliveResponseReceived` | KeepAlive応答イベント |

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

