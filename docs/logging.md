# ログ

dnbn.net は `Microsoft.Extensions.Logging` を使います。ライブラリ側は特定のログ実装に依存しません。

## Consoleログ

```csharp
services.AddLogging(builder => builder.AddConsole());
services.AddDnbnNet(configuration);
```

## log4net

log4net を使う場合はアプリ側で `Microsoft.Extensions.Logging.Log4Net.AspNetCore` を追加します。

```bash
dotnet add package Microsoft.Extensions.Logging.Log4Net.AspNetCore
```

```csharp
services.AddLogging(builder => builder.AddLog4Net());
services.AddDnbnNet(configuration);
```

`AddTcpMessengerWithLog4net` は互換性のために残っていますが、現在は非推奨で、呼び出すと `NotSupportedException` になります。

## メッセージ送受信ログ

送受信した電文の内容は、常に以下のレベルでログ出力されます。

- `EnableMessageLogging: true` の場合: `Information` レベル
- `EnableMessageLogging: false`（既定）の場合: `Debug` レベル

サーバー設定またはクライアント設定で `EnableMessageLogging` を有効にすると、既定のログレベル設定（`Information` 以上）でも電文内容が出力されるようになります。

```json
{
  "EnableMessageLogging": true
}
```

`false` のままでも、アプリ側で該当カテゴリのログレベルを `Debug` まで下げれば従来どおり出力されます。

## 接続・切断・再接続のログ

接続、切断、再接続試行のログには相手先の識別情報が含まれます。

- クライアント側: 接続先の `host:port`（例: `TCP Client 'MainClient' disconnected from 192.168.1.10:5000`）
- サーバー側: セッションID（接続元の `IP:Port` を含む）と接続元エンドポイント

