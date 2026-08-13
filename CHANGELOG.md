# Changelog

English | [日本語](./CHANGELOG.ja.md)

## 1.6.1

- Fixed a resource leak where sockets were not disposed on each attempt during repeated reconnect failures
- Dispose created sockets immediately on connect failure, cancellation, TCP KeepAlive configuration failure, or stream acquisition failure
- Clean up the transport on logical disconnect regardless of `IsConnected`

### Compatibility

- Logical disconnect now calls `ITransport.DisconnectAsync` even when not connected. Custom transport implementations must handle calls from a disconnected or already-disconnected state safely
- After disconnect cleanup starts, the caller's `CancellationToken` does not abort it, so resources and internal state are always cleaned up

## 1.6.0

- Added a `netstandard2.0` target so the library can be used from .NET Framework 4.6.2 or later (4.7.2 or later recommended)
- Added `Dnbn.Tests.NetStandard` to CI to run the existing test suite against the netstandard2.0 build
- Added `Samples/Dnbn.Sample.NetFramework`, a .NET Framework 4.8 sample written within C# 7.3
- Renamed the sample project from the old brand `TcpMessenger.Sample` to `Dnbn.Sample`

### Compatibility

- Public API and behavior of the net8.0 target are unchanged
- On netstandard2.0, `ITcpClient.OnMessageTrace` and `ITcpServer.OnMessageReceivedAsync` have no default implementations (interface default implementations are unavailable). Custom implementations of these interfaces must define both events
- Detailed TCP-level KeepAlive parameters (Time/Interval/RetryCount) work on .NET Framework on Windows 10 1709 or later. On unsupported environments, only `SO_KEEPALIVE` is enabled
- `dnbn.net.WebUI` remains .NET 8 or later only

## 1.5.1

- Fixed a race where a request timed out or canceled during a send filter could still be written to the wire later
- Saturate exponential backoff at the maximum delay so long unlimited reconnect loops cannot overflow `int`
- Use the `CancellationToken` of `ConnectAsync` / `StartAsync` only for the duration of that connect or start operation
- Fixed a self-await deadlock when `StopAsync` is called from `OnMessageReceivedAsync`
- Parse overlapping terminator candidates with longest match even across TCP chunk boundaries
- Updated Microsoft.Extensions dependencies to 8.0.x patch versions to remove known vulnerable transitive dependencies
- CI/publish restore now includes a NuGet vulnerability audit, and audit warnings fail the release

### Compatibility

- When overlapping terminators such as CR and CRLF are used together, if the buffer ends on the shorter terminator, receive notification is held until the next byte confirms the candidate
- Canceling a `CancellationToken` passed to `ConnectAsync` / `StartAsync` after completion does not stop an established connection or a started server

## 1.5.0

- Added `MaxConcurrentResponseWaits` to limit concurrent response-required requests
- Added `IncompleteRequestRecovery` to recover the connection after timeout/cancel of a request already written
- Preserve causal order of response correlation and diagnostic events even when send completion races with an immediate response
- Clarified no-response sending as `SendOneWayAsync`
- Parse duplicate terminator candidates with longest match
- Added `ITcpServer.OnMessageReceivedAsync`, which preserves receive order within a session
- Isolate exceptions from event and Observable subscribers from the communication loop
- Added typed dynamic client registration and `IDnbnClientRegistry`
- Added fail-fast validation of endpoint configuration
- Improved TCP server lifecycle serialization and background task tracking
- Added net8.0 / net10.0 tests and CI/publish gates

### Compatibility

- Existing `ITcpMessengerFactory` is unchanged. Typed creation is split into `ITypedTcpMessengerFactory`
- Default `MaxConcurrentResponseWaits` is null (unlimited, as before)
- Default `IncompleteRequestRecovery` is `KeepConnection` (previous behavior)
- `ClientIdentification.HeaderBased` is unimplemented and now fails configuration validation instead of being silently ignored
