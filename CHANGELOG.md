# Changelog

## 1.6.1

- 接続失敗を繰り返す再接続で、試行ごとにソケットが破棄されず残るリソースリークを修正
- 接続失敗・キャンセル・TCP KeepAlive設定失敗・ストリーム取得失敗時に、生成済みソケットを即時破棄するよう修正
- 論理的な切断時は`IsConnected`の値にかかわらずトランスポートをクリーンアップ

### Compatibility

- 論理的な切断では未接続状態でも`ITransport.DisconnectAsync`を呼ぶ。独自トランスポート実装は、未接続・切断済み状態からの呼び出しを安全に処理する必要がある
- 切断処理の開始後は、リソースと内部状態を確実にクリーンアップするため利用者の`CancellationToken`では中断しない

## 1.6.0

- `netstandard2.0`ターゲットを追加し、.NET Framework 4.6.2以降（4.7.2以降を推奨）から利用可能に
- netstandard2.0ビルドを既存テスト一式で検証するテストプロジェクト`Dnbn.Tests.NetStandard`をCIに追加
- .NET Framework 4.8のサンプルプロジェクト`Samples/Dnbn.Sample.NetFramework`を追加（C# 7.3の範囲で記述）
- サンプルプロジェクトを旧ブランド名の`TcpMessenger.Sample`から`Dnbn.Sample`へリネーム

### Compatibility

- net8.0ターゲットの公開API・動作は変更なし
- netstandard2.0では、`ITcpClient.OnMessageTrace`と`ITcpServer.OnMessageReceivedAsync`に既定実装がないため（インターフェイスの既定実装が利用不可）、これらのインターフェイスを独自実装する場合は両イベントの実装が必要
- TCPレベルKeepAliveの詳細パラメータ（Time/Interval/RetryCount）は、.NET FrameworkではWindows 10 1709以降で有効（未対応環境ではSO_KEEPALIVEの有効化のみ）
- `dnbn.net.WebUI`は引き続き.NET 8以降専用

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
