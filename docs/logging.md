# Logging

English | [日本語](./ja/logging.md)

dnbn.net uses `Microsoft.Extensions.Logging`. The library does not depend on a specific logging implementation.

## Console logging

```csharp
services.AddLogging(builder => builder.AddConsole());
services.AddDnbnNet(configuration);
```

## log4net

To use log4net, add `Microsoft.Extensions.Logging.Log4Net.AspNetCore` in the application.

```bash
dotnet add package Microsoft.Extensions.Logging.Log4Net.AspNetCore
```

```csharp
services.AddLogging(builder => builder.AddLog4Net());
services.AddDnbnNet(configuration);
```

`AddTcpMessengerWithLog4net` remains for compatibility but is deprecated. Calling it throws `NotSupportedException`.

## Message send/receive logging

Message contents are always logged, at the following levels:

- `EnableMessageLogging: true`: `Information`
- `EnableMessageLogging: false` (default): `Debug`

Enable `EnableMessageLogging` on the server or client config to emit message contents even when the app's minimum level is `Information`.

```json
{
  "EnableMessageLogging": true
}
```

If you leave it `false`, you can still see the same logs by lowering the category to `Debug` in the application.

## Connection, disconnect, and reconnect logs

Connection, disconnect, and reconnect logs include peer identity:

- Client: destination `host:port` (for example, `TCP Client 'MainClient' disconnected from 192.168.1.10:5000`)
- Server: session ID (including source `IP:Port`) and the remote endpoint
