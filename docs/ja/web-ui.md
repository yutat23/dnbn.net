# Web UI

[English](../web-ui.md) | 日本語

Web UI は `dnbn.net.WebUI` パッケージで提供されるオプション機能です。.NET 8 以降専用です。

```bash
dotnet add package dnbn.net.WebUI
```

## 設定

```json
{
  "dnbn.net": {
    "WebUI": {
      "Enabled": true,
      "Port": 8080,
      "BindAddress": "localhost",
      "UpdateIntervalSeconds": 1,
      "EnableLogging": true,
      "EventTimelineCapacity": 200,
      "EnableMessageHistory": false,
      "MessageHistoryCapacity": 200,
      "MessageHistoryMaxPayloadBytes": 512,
      "AllowSendFromUI": false,
      "SendAuthToken": null
    }
  }
}
```

## 起動

```csharp
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.WebUI;

var webUI = new WebUIService(
    new ITcpServer[] { server },
    new ITcpClient[] { client },
    new WebUIConfig
    {
        Enabled = true,
        Port = 8080,
        BindAddress = "localhost",
        UpdateIntervalSeconds = 1
    });

await webUI.StartAsync();
```

Web UI内部のHTTPホストはCtrl-Cを独自に処理しません。ASP.NET Core / Generic Hostでは外側のホストの停止トークンを `StartAsync` に渡し、アプリケーション停止と連動させてください。

拡張メソッドも利用できます。

```csharp
using Dnbn.Extensions;

var webUI = await server.StartWebUIAsync(config.WebUI);
```

複数のサーバー/クライアントをまとめて表示する場合:

```csharp
var webUI = await servers.StartWebUIAsync(clients, config.WebUI);
```

`WebUIConfig.Enabled` が `true` でない場合、`StartWebUIAsync` は `null` を返します。

## エンドポイント

| パス | 説明 |
|---|---|
| `/` | Web UI |
| `/api/status` | 全体ステータス |
| `/api/status/client` | クライアント状態 |
| `/api/status/server` | サーバー状態 |
| `/api/status/stream` | SSEストリーム |
| `/api/health` | ヘルスチェック |
| `/api/timeline` | 接続・切断・状態遷移・エラーのリングバッファ履歴 |
| `/api/messages` | 送受信メッセージ履歴（既定OFF） |
| `/api/analytics` | クライアント別の応答時間 min / avg / p95 / max |
| `/api/send` | Web UI送信（既定OFF、`POST`） |

## 運用・診断機能

イベントタイムラインは常に固定件数だけ保持します。Web UI開始時点ですでに接続・起動済みの場合は、`ConnectedAt` / `StartedAt` と既存セッション情報から初期イベントを復元します。メッセージ履歴は電文内容をメモリに保持するため、`EnableMessageHistory` を明示的に有効化した場合だけ記録されます。件数と1件あたりのペイロード上限を超えたデータは、古い項目またはペイロード末尾から破棄されます。

TIMELINEとMESSAGESの `TARGET` で、クライアントまたはサーバー単位に絞り込めます。クライアント／サーバー一覧の行をクリックすると詳細モーダルが開き、その対象だけのイベントログ・メッセージログ・応答時間統計を表示します。モーダルを開いている間もログは自動更新されます。メッセージ表示はTEXT/HEXを切り替えられます。

APIから絞り込む場合は、`source` と `sourceType`（`Client` または `Server`）をクエリに指定します。

```text
/api/timeline?source=MainClient&sourceType=Client
/api/messages?source=MainServer&sourceType=Server
```

応答時間は、保持中の `Response` トレースからクライアント別に計算します。本格的な長期監視ではなく、その場の障害調査向けです。

## Web UIからの送信

送信機能は既定で無効です。有効にする場合は、少なくともトークンを設定してください。

```json
{
  "AllowSendFromUI": true,
  "SendAuthToken": "十分に長いランダムな値"
}
```

送信APIはトークンを `X-Dnbn-Send-Token` ヘッダーで受け取ります。Web UIの送信はアプリ本体と同じ接続・送信キュー・応答マッチングを共有します。応答のある電文は通常送信を使い、ONE-WAYは応答のない電文だけに使ってください。
