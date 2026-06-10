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

サーバー設定またはクライアント設定で `EnableMessageLogging` を有効にします。

```json
{
  "EnableMessageLogging": true
}
```

アプリ側のログレベルも、該当カテゴリのログが出る設定にしてください。

