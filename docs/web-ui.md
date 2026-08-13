# Web UI

English | [日本語](./ja/web-ui.md)

The Web UI is an optional feature provided by the `dnbn.net.WebUI` package. It targets .NET 8 or later.

```bash
dotnet add package dnbn.net.WebUI
```

## Configuration

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

## Startup

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

The HTTP host inside the Web UI does not handle Ctrl-C on its own. In ASP.NET Core / Generic Host, pass the outer host's stopping token to `StartAsync` so the UI stops with the application.

Extension methods are also available.

```csharp
using Dnbn.Extensions;

var webUI = await server.StartWebUIAsync(config.WebUI);
```

To display multiple servers and clients together:

```csharp
var webUI = await servers.StartWebUIAsync(clients, config.WebUI);
```

`StartWebUIAsync` returns `null` when `WebUIConfig.Enabled` is not `true`.

## Endpoints

| Path | Description |
|---|---|
| `/` | Web UI |
| `/api/status` | Overall status |
| `/api/status/client` | Client status |
| `/api/status/server` | Server status |
| `/api/status/stream` | SSE stream |
| `/api/health` | Health check |
| `/api/timeline` | Ring-buffer history of connect, disconnect, state changes, and errors |
| `/api/messages` | Send/receive message history (off by default) |
| `/api/analytics` | Per-client response time min / avg / p95 / max |
| `/api/send` | Web UI send (`POST`, off by default) |

## Operations and diagnostics

The event timeline always keeps a fixed number of entries. If a connection or server is already running when the Web UI starts, initial events are restored from `ConnectedAt` / `StartedAt` and existing session information. Message history stores payloads in memory, so it is recorded only when `EnableMessageHistory` is enabled. Data beyond the entry count or per-entry payload cap is dropped from older items or from the end of the payload.

Use `TARGET` on TIMELINE and MESSAGES to filter by client or server. Clicking a client or server row opens a detail modal with event logs, message logs, and response-time stats for that target. Logs keep updating while the modal is open. Message display can switch between TEXT and HEX.

To filter from the API, pass `source` and `sourceType` (`Client` or `Server`) as query parameters.

```text
/api/timeline?source=MainClient&sourceType=Client
/api/messages?source=MainServer&sourceType=Server
```

Response times are computed per client from retained `Response` traces. This is for on-the-spot troubleshooting, not long-term monitoring.

## Sending from the Web UI

Sending is disabled by default. If you enable it, set a token at minimum.

```json
{
  "AllowSendFromUI": true,
  "SendAuthToken": "a sufficiently long random value"
}
```

The send API accepts the token in the `X-Dnbn-Send-Token` header. Web UI sends share the application's connection, send queue, and response matching. Use normal send for messages that expect a response, and ONE-WAY only for messages that do not.
