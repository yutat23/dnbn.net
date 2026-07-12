# Changelog

## 1.5.1

- 送信フィルターの実行中にtimeout/cancelされた要求が後からwire送信される競合を修正
- 指数バックオフを上限値へ飽和させ、長時間の無限再接続で`int`オーバーフローしないよう修正
- `ConnectAsync` / `StartAsync`のCancellationTokenを接続・起動操作の期間だけ使用するよう修正
- `OnMessageReceivedAsync`内から`StopAsync`を呼んだ場合の自己待機デッドロックを修正
- prefixが重なる終端文字候補をTCPチャンク境界でも最長一致で解析
- Microsoft.Extensions依存を8.0系の修正版へ更新し、既知の脆弱な推移的依存を解消
- CI/publishのrestoreをNuGet脆弱性監査付きにし、監査警告をリリース失敗として扱う

### Compatibility

- CRとCRLFのようにprefixが重なる終端文字を併用した場合、短い終端文字だけでバッファが終わると、次の1バイトで候補が確定するまで受信通知を保留する
- `ConnectAsync` / `StartAsync`へ渡したCancellationTokenを完了後にcancelしても、確立済み接続・起動済みサーバーは停止しない

## 1.5.0

- 応答必須要求の同時待機数を制限する`MaxConcurrentResponseWaits`を追加
- 送信済み要求のtimeout/cancel後に接続を回復する`IncompleteRequestRecovery`を追加
- 送信完了と即時応答が競合しても、応答相関と診断イベントの因果順序を維持
- 応答しない送信を`SendOneWayAsync`として明確化
- 重複する終端文字候補を最長一致で解析
- セッション内順序を保つ`ITcpServer.OnMessageReceivedAsync`を追加
- イベント・Observable購読者の例外を通信ループから隔離
- 型付き動的クライアント登録と`IDnbnClientRegistry`を追加
- endpoint設定のfail-fast検証を追加
- TCPサーバーのライフサイクル直列化とバックグラウンドtask追跡を改善
- net8.0 / net10.0のテストとCI/publishゲートを追加

### Compatibility

- 既存の`ITcpMessengerFactory`は変更せず、型付き生成を`ITypedTcpMessengerFactory`へ分離
- `MaxConcurrentResponseWaits`の既定値はnull（従来どおり無制限）
- `IncompleteRequestRecovery`の既定値は`KeepConnection`（従来挙動）
- `ClientIdentification.HeaderBased`は未実装のため、黙って無視せず設定エラーになる
