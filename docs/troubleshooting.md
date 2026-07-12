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
- 応答しないコマンドを誤って`SendAsync`で送っていないか（`SendOneWayAsync`を使用）

## メッセージが分割/結合される

TCPはストリームなので、区切り設定が合っていないと複数電文が結合されたり途中で切れたりします。

- テキスト系なら `MessageTerminator`
- 固定長なら `FixedHeaderLength` と `FixedBodyLength`
- 可変長なら `FixedHeaderLength`、`LengthFieldOffset`、`LengthFieldLength`

複数の終端候補が同じ位置で一致する場合は最長一致になります。CR/LF/CRLFを受ける場合は `["\r\n", "\r", "\n"]` のように設定できます。TCPチャンクがCRで終わった場合は、CR単独かCRLFかを確定するため、次の1バイトを受信するまでその電文の通知を保留します。

## timeout後に次の要求が誤った応答を受ける

相関IDがなくFIFOで応答を対応付けるプロトコルでは、timeout後の遅延応答が後続要求へ対応付く可能性があります。次を設定してください。

```json
{
  "MaxConcurrentResponseWaits": 1,
  "IncompleteRequestRecovery": "Reconnect",
  "WaitForConnectionOnSend": true
}
```

## `OnMessageReceived` に応答が来ない

クライアントの `SendAsync` が受け取った応答は、通常 `OnMessageReceived` には流れません。`OnMessageReceived` はサーバープッシュなど、リクエストと無関係に届く通知向けです。

通知電文を応答扱いさせたくない場合は `NotificationPredicate` を設定してください。

## KeepAlive応答と通知が混ざる

KeepAliveは通常要求と同じFIFO応答キューで相関されるため、要求・応答が順序どおりに返るプロトコルでは `SendAsync` の応答を横取りしません。また、通常要求が応答待ちの間はKeepAlive送信自体が延期されます。

ただし `ResponsePredicate` 未設定の場合、KeepAlive応答待ちの間に届いたサーバープッシュ通知はKeepAlive応答として消費されます。プッシュ通知があるプロトコルでは、コードで `KeepAliveConfig.ResponsePredicate` と `NotificationPredicate` を設定してください。`NotificationPredicate` にマッチした電文は、KeepAlive応答を含むすべての応答マッチングより優先して通知として配信されます。

## KeepAliveのタイムアウトで切断される

KeepAlive応答が間隔（`IntervalSeconds`）内に返らない場合、既定では接続を切断します（`DisconnectOnTimeout: true`）。タイムアウト後に遅れて届いたKeepAlive応答が、後続の通常要求の応答として誤って相関されるのを防ぐためです。切断はNW障害として扱われるため、`ConnectionRetryPolicy` が設定されていれば自動再接続します。

タイムアウト後も接続を維持する必要がある場合は `DisconnectOnTimeout: false` を明示できます。ただし、応答内容でKeepAliveを区別できないプロトコルでは遅延応答の誤相関リスクが残ります。

## 無通信中の切断に気づけない（送信時に初めてエラーになる）

NW障害や中継機器の無通信タイムアウトで接続が切れても、無通信のままではOSが切断を検知できず、次の `SendAsync` で初めてエラーになることがあります。対策:

- `TcpKeepAlive`（TCPレベルのキープアライブ）を有効にすると、OSが定期的にプローブを送信して切断を検知します。切断が検知されると受信ループが終了し、`ConnectionRetryPolicy` を設定していれば自動再接続が動作します。
- 相手アプリケーションの生存確認まで必要な場合（プロセスは生きているがハングしている等）は、電文を送信する `KeepAlive`（アプリケーションレベル）を使用してください。両者は併用できます。

## 受信バッファが大きくなる

終端文字または長さフィールドの設定は必須です。不完全・矛盾した設定はendpoint生成時に例外になります。大きな宣言長や終端未到着によるメモリ使用を制限するには`MaxReceiveBufferBytes`も設定してください。
