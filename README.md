# dnbn.net

English | [日本語](./README.ja.md)

![language](https://img.shields.io/badge/language-C%23-green?logo=csharp)
![dotnet](https://img.shields.io/badge/dotnet-8.0%20%7C%20netstandard2.0-blue?logo=dotnet)
[![NuGet version](https://img.shields.io/nuget/v/dnbn.net)](https://www.nuget.org/packages/dnbn.net/)

A .NET library for sending and receiving custom TCP message protocols.

It supports both server and client for legacy-style TCP protocols such as terminator-delimited, fixed-length, and length-prefixed variable-length messages.

## Supported frameworks

| Target | Runtime |
|---|---|
| `net8.0` | .NET 8 or later |
| `netstandard2.0` | .NET Framework 4.6.2 or later (4.7.2 or later recommended), and others |

- `dnbn.net.WebUI` (the Web UI package) requires .NET 8 or later because it uses ASP.NET Core.
- Detailed TCP-level KeepAlive parameters (`TcpKeepAlive` Time/Interval/RetryCount) are available on .NET Framework only on Windows 10 1709 or later. On unsupported environments, only `SO_KEEPALIVE` is enabled.

## Features

- Create TCP servers and clients
- Request/response messaging with `SendAsync`
- Fire-and-forget sending with `SendOneWayAsync`
- Push receive events from the server
- Receive subscriptions via `OnMessageReceived` and `IObservable`
- Message framing for terminator-delimited, fixed-length, and length-prefixed variable-length protocols
- Arbitrary encodings such as Shift-JIS
- Connection retry, message send retry, and KeepAlive
- Multi-client session management and broadcast
- Message filter pipeline
- Connection state and statistics
- Observe connection state transitions with `ConnectionState` and `OnConnectionStateChanged` (including auto-reconnect)
- Send/receive diagnostics via `OnMessageTrace`, covering requests, responses, notifications, and KeepAlive
- Named client registration with Generic Host, including automatic connect/disconnect
- Limit in-flight response waits, with safe connection recovery after timeout or cancel
- Awaitable server handlers that preserve receive order within a session
- Optional Web UI package

## Installation

```bash
dotnet add package dnbn.net
```

To use the Web UI, add the extra package:

```bash
dotnet add package dnbn.net.WebUI
```

## Documentation

- [Configuration reference](./docs/configuration.md)
- [Message protocols](./docs/protocols.md)
- [API overview](./docs/api-reference.md)
- [Usage examples](./docs/usage.md)
- [Web UI](./docs/web-ui.md)
- [Logging](./docs/logging.md)
- [Troubleshooting](./docs/troubleshooting.md)
- [Changelog](./CHANGELOG.md)

## Quick start

This is a minimal example that starts a server and a client in the same process.

```csharp
using Dnbn.Configuration;
using Dnbn.Core;
using TcpClient = Dnbn.Core.TcpClient;

var port = 15201;

await using var server = new TcpServer(new ServerConfig
{
    Name = "EchoServer",
    ListenPort = port,
    Encoding = "UTF-8",
    MessageTerminator = "\n",
});

server.OnMessageReceivedAsync += async (message, sessionInfo, cancellationToken) =>
{
    await server.SendAsync(
        sessionInfo.SessionId,
        $"ECHO: {message.Text?.Trim()}",
        cancellationToken);
};

await server.StartAsync();

var clientConfig = new ClientConfig
{
    Name = "EchoClient",
    RemoteHost = "127.0.0.1",
    RemotePort = port,
    Encoding = "UTF-8",
    MessageTerminator = "\n",
    TimeoutMilliseconds = 5000,
    MaxConcurrentResponseWaits = 1,
    IncompleteRequestRecovery = IncompleteRequestRecovery.Reconnect,
    WaitForConnectionOnSend = true,
};

await using var client = new TcpClient(
    clientConfig,
    new TcpTransport(clientConfig.RemoteHost, clientConfig.RemotePort));

await client.ConnectAsync();

var response = await client.SendAsync("hello");
Console.WriteLine(response.Text);

await client.DisconnectAsync();
await server.StopAsync();
```

## appsettings.json and DI

When configuring from a settings file, use the `dnbn.net` section.

```json
{
  "dnbn.net": {
    "Servers": [
      {
        "Name": "MainServer",
        "ListenPort": 5000,
        "Encoding": "UTF-8",
        "MessageTerminator": "\n"
      }
    ],
    "Clients": [
      {
        "Name": "MainClient",
        "RemoteHost": "127.0.0.1",
        "RemotePort": 5000,
        "Encoding": "UTF-8",
        "MessageTerminator": "\n",
        "TimeoutMilliseconds": 5000,
        "MaxConcurrentResponseWaits": 1,
        "IncompleteRequestRecovery": "Reconnect",
        "WaitForConnectionOnSend": true,
        "ConnectionRetryPolicy": {
          "MaxRetryCount": -1,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 1000,
          "MaxDelayMs": 10000
        }
      }
    ]
  }
}
```

```csharp
using Dnbn.Core;
using Dnbn.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddDnbnNet(configuration);

await using var provider = services.BuildServiceProvider();

var factory = provider.GetRequiredService<ITcpMessengerFactory>();
var server = factory.CreateServer("MainServer");
var client = factory.CreateClient("MainClient");
```

The `TcpMessenger` section name and `AddTcpMessenger` remain for backward compatibility. New code should use `dnbn.net` and `AddDnbnNet`.

## Message boundaries

TCP is a stream, so you must configure how messages are delimited.

### Terminator

For text-oriented protocols, set `MessageTerminator`.

```json
{
  "MessageTerminator": "\r\n"
}
```

To use different terminator candidates on receive only, set `ReceiveMessageTerminator`.

```json
{
  "MessageTerminator": "\r",
  "ReceiveMessageTerminator": ["#", "?"]
}
```

### Fixed length

For protocols with a fixed header and body length, set `FixedHeaderLength` and `FixedBodyLength`.

```json
{
  "FixedHeaderLength": 4,
  "FixedBodyLength": 20
}
```

### Length-prefixed variable length

For protocols that encode the body length in a header length field, set the field offset and size.

```json
{
  "FixedHeaderLength": 6,
  "LengthFieldOffset": 2,
  "LengthFieldLength": 4
}
```

Terminator-based framing, or length-based framing (fixed-length or length-prefixed), is required. Incomplete or conflicting settings throw when the endpoint is created.

## Common settings

### Connection retry

```json
{
  "ConnectionRetryPolicy": {
    "MaxRetryCount": -1,
    "RetryDelayStrategy": "Exponential",
    "InitialDelayMs": 1000,
    "MaxDelayMs": 60000
  }
}
```

`MaxRetryCount: -1` means unlimited connection retries.

### KeepAlive

```json
{
  "KeepAlive": {
    "Enabled": true,
    "IntervalSeconds": 30,
    "Message": "PING",
    "DisconnectOnTimeout": true
  }
}
```

KeepAlive responses are delivered through `OnKeepAliveResponseReceived`. `DisconnectOnTimeout` defaults to `true`. On a response timeout, the connection is closed to prevent late responses from being correlated incorrectly. If `ConnectionRetryPolicy` is set, the client reconnects automatically.

### Message logging

```json
{
  "EnableMessageLogging": true
}
```

`EnableMessageLogging` can be used on both server and client settings. When `true`, message contents are logged at `Information`. When `false` (the default), they are logged at `Debug`.

## Receive event model

Client receive paths differ by purpose.

| Path | Purpose |
|---|---|
| Return value of `SendAsync` | Response to a request you sent |
| `OnMessageReceived` / `MessageReceived` | Push notifications unrelated to a request |
| `OnKeepAliveResponseReceived` | Response to a KeepAlive message |

Responses received by `SendAsync` do not normally flow into `OnMessageReceived`. To split notification messages explicitly, set `NotificationPredicate`.

Send commands that do not expect a response with `SendOneWayAsync`, not `SendAsync`. `SendAsync` is a response-required contract. For protocols that identify responses in FIFO order, use `MaxConcurrentResponseWaits: 1` together with `IncompleteRequestRecovery: Reconnect`.

## Web UI

The Web UI is provided as a separate package, `dnbn.net.WebUI`. You can inspect connection state, send/receive counts, session information, and more in a browser.

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

After startup, it is available at `http://localhost:8080` by default.

## Samples

The sample project includes runnable scenarios for each feature.

```bash
dotnet run --project Samples/Dnbn.Sample
```

You can also run a scenario directly by number.

```bash
dotnet run --project Samples/Dnbn.Sample -- 1
```

| # | Description |
|---|---|
| 1 | Quick start |
| 2 | Chat / broadcast |
| 3 | Failures and automatic reconnect |
| 4 | KeepAlive and liveness monitoring |
| 5 | Shift-JIS, fixed-length, and length-prefixed framing |
| 6 | Timeout, retry, and response matching |
| 7 | Filters, statistics, and Web UI with message history and send |
| 8 | Interactive playground for appsettings.json and DI |

See [Samples/Dnbn.Sample/README.md](./Samples/Dnbn.Sample/README.md) for details.

### Using from .NET Framework

A sample console app for .NET Framework 4.8 is in [Samples/Dnbn.Sample.NetFramework](./Samples/Dnbn.Sample.NetFramework/) (written within C# 7.3. Run with `dotnet run` or Visual Studio on Windows).

```bash
cd Samples/Dnbn.Sample.NetFramework
dotnet run
```

## Logging

dnbn.net uses `Microsoft.Extensions.Logging`. Configure any logging provider on the application side, such as Console, Serilog, NLog, or log4net.

To use log4net, add `Microsoft.Extensions.Logging.Log4Net.AspNetCore` in the application, then call `AddDnbnNet`.

```csharp
services.AddLogging(builder => builder.AddLog4Net());
services.AddDnbnNet(configuration);
```

`AddTcpMessengerWithLog4net` remains for compatibility but is deprecated. Calling it throws `NotSupportedException`.
