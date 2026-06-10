# Web UI

Web UI は `dnbn.net.WebUI` パッケージで提供されるオプション機能です。

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
      "EnableLogging": true
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

拡張メソッドも利用できます。

```csharp
using Dnbn.Extensions;

var webUI = await server.StartWebUIAsync(config.WebUI);
```

複数のサーバー/クライアントをまとめて表示する場合:

```csharp
var webUI = await servers.StartWebUIAsync(clients, config.WebUI);
```

## エンドポイント

| パス | 説明 |
|---|---|
| `/` | Web UI |
| `/api/status` | 全体ステータス |
| `/api/status/client` | クライアント状態 |
| `/api/status/server` | サーバー状態 |
| `/api/status/stream` | SSEストリーム |
| `/api/health` | ヘルスチェック |

