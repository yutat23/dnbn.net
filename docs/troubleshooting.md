# トラブルシューティング

## 接続できない

確認すること:

- `RemoteHost` と `RemotePort` が正しいか
- サーバーが `StartAsync` 済みか
- ファイアウォールやポート競合がないか
- 接続リトライが必要なら `ConnectionRetryPolicy` が設定されているか

## メッセージが返らない

確認すること:

- サーバー側で `SendAsync(sessionId, ...)` を呼んでいるか
- `MessageTerminator` などのメッセージ境界設定が送受信で合っているか
- `TimeoutMilliseconds` が短すぎないか
- サーバー側イベントハンドラで例外が出ていないか

## メッセージが分割/結合される

TCPはストリームなので、区切り設定が合っていないと複数電文が結合されたり途中で切れたりします。

- テキスト系なら `MessageTerminator`
- 固定長なら `FixedHeaderLength` と `FixedBodyLength`
- 可変長なら `FixedHeaderLength`、`LengthFieldOffset`、`LengthFieldLength`

## `OnMessageReceived` に応答が来ない

クライアントの `SendAsync` が受け取った応答は、通常 `OnMessageReceived` には流れません。`OnMessageReceived` はサーバープッシュなど、リクエストと無関係に届く通知向けです。

通知電文を応答扱いさせたくない場合は `NotificationPredicate` を設定してください。

## KeepAlive応答と通知が混ざる

KeepAlive応答と通常通知を明確に分ける必要がある場合は、コードで `KeepAliveConfig.ResponsePredicate` と `NotificationPredicate` を設定してください。

## 受信バッファが大きくなる

終端文字や長さフィールドを設定していない場合、メッセージ境界を判断できずバッファが伸びる可能性があります。プロトコル仕様を見直し、必要に応じて `MaxReceiveBufferBytes` を設定してください。

