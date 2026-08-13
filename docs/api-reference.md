# API overview

English | [日本語](./ja/api-reference.md)

See XML comments in the code for full signatures. This page covers the main APIs you touch at use time.

## ITcpMessengerFactory

Entry point when using `AddDnbnNet` with DI.

```csharp
var factory = provider.GetRequiredService<ITcpMessengerFactory>();
var server = factory.CreateServer("MainServer");
var client = factory.CreateClient("MainClient");
```

To create endpoints from typed config objects, use `ITypedTcpMessengerFactory`. The interface is separate so existing `ITcpMessengerFactory` implementations stay compatible.

```csharp
var typed = provider.GetRequiredService<ITypedTcpMessengerFactory>();
var client = typed.CreateClient(clientConfig);
```

## ITcpServer

Main members:

| Member | Description |
|---|---|
| `StartAsync()` | Start the server |
| `StopAsync()` | Stop the server |
| `SendAsync(sessionId, message)` | Send to a specific session |
| `BroadcastAsync(message)` | Send to all sessions |
| `GetSession(sessionId)` | Get a session |
| `GetAllSessions()` | Get all sessions |
| `ConnectionInfo` | Connection state and statistics |
| `OnMessageReceived` | Message received event |
| `OnMessageReceivedAsync` | Async handler awaited in session receive order |
| `OnClientConnected` | Client connected event |
| `OnClientDisconnected` | Client disconnected event |
| `OnError` | Error event |
| `MessageReceived` | Rx Observable |

The concrete `TcpServer` also implements `IAsyncDisposable`. `OnMessageReceivedAsync` has no default interface implementation on `netstandard2.0`, so custom `ITcpServer` implementations on that target must define the event.

## ITcpClient

Main members:

| Member | Description |
|---|---|
| `ConnectAsync()` | Connect |
| `DisconnectAsync()` | Disconnect |
| `SendAsync(message)` | Send and wait for a response |
| `SendAndWaitAsync(message, predicate, timeout)` | Wait for a matching response |
| `SendOneWayAsync(message)` | Send without waiting for a response |
| `WaitForConnectionAsync(timeout)` | Wait until connected |
| `InterruptReconnectDelay()` | Skip the current reconnect backoff wait |
| `NotificationPredicate` | Predicate for notification messages |
| `KeepAlive` | Get/set KeepAlive config |
| `TimeoutMilliseconds` | Get/set the default timeout |
| `RetryPolicy` | Message send retry policy |
| `ConnectionRetryPolicy` | Connection retry policy |
| `ConnectionInfo` | Connection state and statistics, including `IsReconnecting` and `KeepAliveTimeoutCount` |
| `State` | Detailed connection state (`ConnectionState`) |
| `OnConnected` | Connected event |
| `OnDisconnected` | Disconnected event |
| `OnError` | Error event |
| `OnMessageReceived` | Push notification received event |
| `OnKeepAliveResponseReceived` | KeepAlive response event |
| `OnConnectionStateChanged` | Connection state change event |
| `OnMessageTrace` | Diagnostic event for all send/receive, including requests, responses, notifications, and KeepAlive |
| `MessageReceived` | Rx Observable |

`SendAsync` / `SendAndWaitAsync` require a response. `SendOneWayAsync` does not. `MaxConcurrentResponseWaits` limits only the former.

The concrete `TcpClient` also implements `IAsyncDisposable`. `OnMessageTrace` has no default interface implementation on `netstandard2.0`, so custom `ITcpClient` implementations on that target must define the event.

## IDnbnClientRegistry

Resolves named clients that participate in Generic Host as a single instance per name. You can register both config-file clients and typed dynamic configs.

### ConnectionState

`State` and `OnConnectionStateChanged` let you observe auto-reconnect, which `OnConnected` / `OnDisconnected` alone cannot show.

| Value | Meaning |
|---|---|
| `Disconnected` | Not connected (initial, after an intentional disconnect, or after giving up reconnect) |
| `Connecting` | Connecting via `ConnectAsync` (including retry waits) |
| `Connected` | Connected |
| `Reconnecting` | Auto-reconnecting after a network failure (including retry waits) |

```csharp
client.OnConnectionStateChanged += (_, e) =>
{
    Console.WriteLine($"{e.previous} -> {e.current}");
};
```

### MessageTrace

`OnMessageTrace` also observes `SendAsync` responses and KeepAlive, which do not flow into `OnMessageReceived`. Outbound `RawData` / `Text` include the terminator actually written to the wire. `Message` on the event is a diagnostic snapshot; mutating it does not affect send/receive.

```csharp
client.OnMessageTrace += (_, trace) =>
{
    Console.WriteLine($"{trace.Timestamp:o} {trace.Direction} {trace.Kind} {trace.Message.Text}");
};
```

## Message

| Property | Description |
|---|---|
| `RawData` | Received or sent bytes |
| `Text` | String after encoding conversion |
| `Code` | Message code for application use |
| `Timestamp` | Message creation time |
| `Metadata` | Extra data used by filters and similar |

Creation helpers:

```csharp
var message = Message.FromString("HELLO", Encoding.UTF8);
var binary = Message.FromBytes(bytes, Encoding.UTF8);
```

## IMessageFilter

You can transform messages before send and after receive. Use this for checksums, validation, logging, or protocol-specific conversion.

```csharp
public interface IMessageFilter
{
    Task<Message> OnSendingAsync(Message msg, IMessageContext ctx);
    Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx);
}
```

Pass filters to the constructor, or register `IMessageFilter` implementations in DI. `TcpMessengerFactory` receives them as `IEnumerable<IMessageFilter>` and applies them to every server and client it creates.

```csharp
await using var client = new TcpClient(
    clientConfig,
    transport,
    logger,
    filters: new[] { new MyFilter() });

services.AddSingleton<IMessageFilter, MyFilter>();
services.AddDnbnNet(configuration);
```
