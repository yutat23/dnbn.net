# Dnbn.Sample.NetFramework

English | [日本語](./README.ja.md)

A sample that uses dnbn.net (the `netstandard2.0` build) from a .NET Framework 4.8 console app.

It starts an echo server and a client in the same process and demonstrates the following:

1. Starting `TcpServer` and echoing with `OnMessageReceivedAsync`
2. Connecting `TcpClient` (with automatic reconnect via `ConnectionRetryPolicy`)
3. Request/response with `SendAsync`
4. Receiving a server-push message (`OnMessageReceived`)
5. Application-level KeepAlive (PING every 2 seconds, with response observation)

The code stays within C# 7.3 (the default compiler setting for .NET Framework projects), so you can take it into an existing legacy project. `await using` is unavailable, so cleanup calls `Dispose` after `DisconnectAsync` / `StopAsync`.

## How to run (Windows)

```bash
cd Samples/Dnbn.Sample.NetFramework
dotnet run
```

From Visual Studio, set `Dnbn.Sample.NetFramework` as the startup project.

On non-Windows OS, only build (compile verification) is supported, using the `Microsoft.NETFramework.ReferenceAssemblies` package.

## Using it in your own project

- In an SDK-style / PackageReference project, run `dotnet add package dnbn.net` (the `netstandard2.0` build is selected automatically for net461 or later; 4.7.2 or later is recommended).
- For old-style (packages.config) projects, migrate to PackageReference.
- Enable `AutoGenerateBindingRedirects` to resolve version differences among dependent packages (see this sample's csproj).
