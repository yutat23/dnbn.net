# Configuration reference

English | [日本語](./ja/configuration.md)

`appsettings.json` uses the `dnbn.net` section. The `TcpMessenger` section is still loaded for backward compatibility. New code should use `dnbn.net`.

```json
{
  "dnbn.net": {
    "Servers": [],
    "Clients": [],
    "WebUI": {
      "Enabled": false
    }
  }
}
```

## Full configuration sample

This sample lists every property that can be set from JSON (or XML configuration binding). See the tables below for what each property means.

Use either terminator framing (`MessageTerminator` / `ReceiveMessageTerminator`) or length-based framing (`FixedHeaderLength` / `FixedBodyLength` / `LengthFieldOffset` / `LengthFieldLength`). The sample uses `TerminatorServer` / `TerminatorClient` for the former and `LengthFieldServer` / `LengthFieldClient` for the latter.

```json
{
  "dnbn.net": {
    "Servers": [
      {
        "Name": "TerminatorServer",
        "ListenPort": 5000,
        "BindAddress": "127.0.0.1",
        "Encoding": "UTF-8",
        "MessageTerminator": "\r\n",
        "ReceiveMessageTerminator": [ "\r\n", "\n" ],
        "ClientIdentification": "SourceEndpoint",
        "EnableMessageLogging": true,
        "MaxReceiveBufferBytes": 1048576,
        "TcpKeepAlive": {
          "Enabled": true,
          "TimeSeconds": 60,
          "IntervalSeconds": 10,
          "RetryCount": 5
        }
      },
      {
        "Name": "LengthFieldServer",
        "ListenPort": 5001,
        "BindAddress": "0.0.0.0",
        "Encoding": "Shift-JIS",
        "ClientIdentification": "SourceEndpoint",
        "FixedHeaderLength": 8,
        "LengthFieldOffset": 4,
        "LengthFieldLength": 4,
        "EnableMessageLogging": false,
        "MaxReceiveBufferBytes": 1048576
      }
    ],
    "Clients": [
      {
        "Name": "TerminatorClient",
        "RemoteHost": "192.168.1.10",
        "RemotePort": 5000,
        "Encoding": "UTF-8",
        "MessageTerminator": "\r\n",
        "ReceiveMessageTerminator": [ "\r\n", "\n" ],
        "TimeoutMilliseconds": 5000,
        "SendQueueCapacity": 1000,
        "MaxConcurrentResponseWaits": 1,
        "IncompleteRequestRecovery": "Reconnect",
        "WaitForConnectionOnSend": true,
        "WaitForConnectionTimeoutMilliseconds": 10000,
        "EnableMessageLogging": true,
        "MaxReceiveBufferBytes": 1048576,
        "RetryPolicy": {
          "MaxRetryCount": 3,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 500,
          "MaxDelayMs": 60000,
          "FailOnTimeout": true,
          "FailOnErrorResponse": true
        },
        "ConnectionRetryPolicy": {
          "MaxRetryCount": -1,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 1000,
          "MaxDelayMs": 30000
        },
        "KeepAlive": {
          "Enabled": true,
          "IntervalSeconds": 30,
          "Message": "PING",
          "DisconnectOnTimeout": true
        },
        "TcpKeepAlive": {
          "Enabled": true,
          "TimeSeconds": 60,
          "IntervalSeconds": 10,
          "RetryCount": 5
        }
      },
      {
        "Name": "LengthFieldClient",
        "RemoteHost": "192.168.1.20",
        "RemotePort": 5001,
        "Encoding": "Shift-JIS",
        "FixedHeaderLength": 8,
        "FixedBodyLength": 128,
        "TimeoutMilliseconds": 3000
      }
    ],
    "WebUI": {
      "Enabled": true,
      "Port": 8080,
      "UpdateIntervalSeconds": 1,
      "BindAddress": "localhost",
      "EnableLogging": true,
      "EventTimelineCapacity": 200,
      "EnableMessageHistory": true,
      "MessageHistoryCapacity": 200,
      "MessageHistoryMaxPayloadBytes": 512,
      "AllowSendFromUI": true,
      "SendAuthToken": "your-secret-token"
    }
  }
}
```

`ClientConfig.NotificationPredicate` and `KeepAliveConfig.ResponsePredicate` are code-only properties, so they are not included in this sample.

## ServerConfig

Set under `dnbn.net.Servers`. `Name` is the identifier used by `ITcpMessengerFactory.CreateServer(name)`.

| Property | Type | Default | Description |
|---|---:|---:|---|
| `Name` | `string` | `""` | Server name |
| `ListenPort` | `int` | `0` | Listen port |
| `BindAddress` | `string` | `"0.0.0.0"` | Listen IP address |
| `Encoding` | `string` | `"UTF-8"` | Character encoding |
| `MessageTerminator` | `string?` | `null` | Default terminator for send, and for receive when receive candidates are unset |
| `ReceiveMessageTerminator` | `string[]?` | `null` | Terminator candidates on receive |
| `ClientIdentification` | `ClientIdentification` | `SourceEndpoint` | Client identification. Only `SourceEndpoint` is implemented. `HeaderBased` fails validation |
| `FixedHeaderLength` | `int?` | `null` | Header length for fixed-length or length-prefixed framing |
| `FixedBodyLength` | `int?` | `null` | Body length for fixed-length framing |
| `LengthFieldOffset` | `int?` | `null` | Start offset of the length field in the header |
| `LengthFieldLength` | `int?` | `null` | Length field size in bytes. Must be 1, 2, or 4 |
| `EnableMessageLogging` | `bool` | `false` | Message send/receive logging (`true`: Information, `false`: Debug) |
| `MaxReceiveBufferBytes` | `int?` | `null` | Receive buffer cap. When set, must be 1 or greater |
| `TcpKeepAlive` | `TcpKeepAliveConfig?` | `null` | TCP-level keep-alive (applied to accepted client sockets) |

## ClientConfig

Set under `dnbn.net.Clients`. `Name` is the identifier used by `ITcpMessengerFactory.CreateClient(name)`.

| Property | Type | Default | Description |
|---|---:|---:|---|
| `Name` | `string` | `""` | Client name |
| `RemoteHost` | `string` | `""` | Remote host |
| `RemotePort` | `int` | `0` | Remote port |
| `Encoding` | `string` | `"UTF-8"` | Character encoding |
| `MessageTerminator` | `string?` | `null` | Default terminator for send, and for receive when receive candidates are unset |
| `ReceiveMessageTerminator` | `string[]?` | `null` | Terminator candidates on receive |
| `RetryPolicy` | `RetryPolicy?` | `null` | Message send retry |
| `ConnectionRetryPolicy` | `RetryPolicy?` | `null` | Reconnect retry on connect failure or disconnect |
| `TimeoutMilliseconds` | `int` | `5000` | Default timeout for `SendAsync` |
| `SendQueueCapacity` | `int` | `1000` | Max send-queue size. When full, send calls wait for a slot |
| `MaxConcurrentResponseWaits` | `int?` | `null` | Max in-flight response waits. `SendOneWayAsync` is excluded. `null` means unlimited |
| `IncompleteRequestRecovery` | `IncompleteRequestRecovery` | `KeepConnection` | Recovery after timeout/cancel once wire write has started. Prefer `Reconnect` |
| `WaitForConnectionOnSend` | `bool` | `false` | Wait for a connection when sending while disconnected. Default throws `InvalidOperationException` immediately |
| `WaitForConnectionTimeoutMilliseconds` | `int` | `10000` | Max wait for connection-on-send. Times out with `TimeoutException` |
| `KeepAlive` | `KeepAliveConfig?` | `null` | Application-level KeepAlive (liveness via sending a message) |
| `TcpKeepAlive` | `TcpKeepAliveConfig?` | `null` | TCP-level keep-alive |
| `FixedHeaderLength` | `int?` | `null` | Header length for fixed-length or length-prefixed framing |
| `FixedBodyLength` | `int?` | `null` | Body length for fixed-length framing |
| `LengthFieldOffset` | `int?` | `null` | Start offset of the length field in the header |
| `LengthFieldLength` | `int?` | `null` | Length field size in bytes. Must be 1, 2, or 4 |
| `EnableMessageLogging` | `bool` | `false` | Message send/receive logging (`true`: Information, `false`: Debug) |
| `MaxReceiveBufferBytes` | `int?` | `null` | Receive buffer cap. When set, must be 1 or greater |

`NotificationPredicate` is set from code. It cannot be included in JSON/XML configuration.

`SendAsync` / `SendAndWaitAsync` require a response. `SendOneWayAsync` does not. For protocols without a correlation ID other than FIFO, limit concurrent response waits to 1 and reconnect when an in-flight request becomes incomplete, so a late response cannot be matched to a later request. When using `Reconnect`, also enable `WaitForConnectionOnSend` if sends should wait for the new connection.

## RetryPolicy

| Property | Type | Default | Description |
|---|---:|---:|---|
| `MaxRetryCount` | `int` | `3` | Max retry count. For connection retry, `-1` means unlimited |
| `RetryDelayStrategy` | `RetryDelayStrategy` | `Exponential` | `Fixed` or `Exponential` |
| `InitialDelayMs` | `int` | `500` | Initial delay |
| `MaxDelayMs` | `int` | `60000` | Maximum delay |
| `FailOnTimeout` | `bool` | `true` | Treat timeout as failure |
| `FailOnErrorResponse` | `bool` | `true` | Treat error responses as failure |

Setting `RetryPolicy` resends the request message. Do not set it for commands that must not run twice. Use `ConnectionRetryPolicy` when you only need to retry establishing a connection.

## KeepAliveConfig

| Property | Type | Default | Description |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | Enable KeepAlive |
| `IntervalSeconds` | `int` | `30` | Send interval (response timeout uses the same value) |
| `Message` | `string` | `""` | KeepAlive message to send |
| `DisconnectOnTimeout` | `bool` | `true` | Disconnect on response timeout. Treated as a network failure, so `ConnectionRetryPolicy` triggers auto-reconnect when set |

`ResponsePredicate` is set from code. It cannot be included in JSON/XML configuration.

KeepAlive responses are correlated in the same FIFO order as normal requests. KeepAlive sends themselves are deferred while a normal request is waiting for a response. `DisconnectOnTimeout` defaults to `true`. A connection with no KeepAlive response cannot be trusted for FIFO correlation, so the client disconnects to prevent a late KeepAlive response from being delivered to a later normal request. Set `false` only when you must keep the connection.

## TcpKeepAliveConfig

This configures OS TCP keep-alive (socket option `SO_KEEPALIVE`). It helps the OS detect network failures or idle timeouts even when the application is not sending data, so the first error is less likely to appear only on the next `SendAsync`. It is independent of `KeepAliveConfig` (application-level messages) and can be used together with it.

| Property | Type | Default | Description |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | Enable TCP keep-alive |
| `TimeSeconds` | `int` | `60` | Idle time before the first probe (seconds) |
| `IntervalSeconds` | `int` | `10` | Probe retry interval (seconds) |
| `RetryCount` | `int` | `5` | Probe retries before the connection is considered dead |

When unset (`null`), the OS default applies. On environments that do not support fine-grained `TimeSeconds` / `IntervalSeconds` / `RetryCount` control, only the basic keep-alive option is enabled. On .NET Framework, those detailed parameters work on Windows 10 1709 or later.

```json
{
  "dnbn.net": {
    "Clients": [
      {
        "Name": "MyClient",
        "RemoteHost": "192.168.1.10",
        "RemotePort": 5000,
        "TcpKeepAlive": {
          "Enabled": true,
          "TimeSeconds": 60,
          "IntervalSeconds": 10,
          "RetryCount": 5
        }
      }
    ]
  }
}
```

## WebUIConfig

Web UI settings are consumed by the `dnbn.net.WebUI` package.

| Property | Type | Default | Description |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | Enable the Web UI |
| `Port` | `int` | `8080` | HTTP port |
| `UpdateIntervalSeconds` | `int` | `1` | SSE update interval |
| `BindAddress` | `string` | `"localhost"` | Bind address. `"*"` binds all addresses |
| `EnableLogging` | `bool` | `true` | Web UI logging |
| `EventTimelineCapacity` | `int` | `200` | Max connection/disconnect/state/error history entries |
| `EnableMessageHistory` | `bool` | `false` | Enable send/receive message history. Off by default because it stores payloads |
| `MessageHistoryCapacity` | `int` | `200` | Max message history entries |
| `MessageHistoryMaxPayloadBytes` | `int` | `512` | Max payload bytes kept per history entry |
| `AllowSendFromUI` | `bool` | `false` | Allow sending from the Web UI. Off by default |
| `SendAuthToken` | `string?` | `null` | Token required on `X-Dnbn-Send-Token` for the send API |

`EventTimelineCapacity` and the message-history limits are ring buffers. Older items are dropped when the cap is exceeded. If you enable `AllowSendFromUI`, set `SendAuthToken` and expose the Web UI port only on a trusted network.
