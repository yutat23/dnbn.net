# Dnbn.Sample

English | [日本語](./README.ja.md)

A set of **scenario-based demos** for dnbn.net (a TCP messaging library).
Each scenario is self-contained in one file, in a form you can copy into your own code.

## How to run

```bash
# Choose from the menu
dotnet run --project Samples/Dnbn.Sample

# Run a scenario by number (example: scenario 3)
dotnet run --project Samples/Dnbn.Sample -- 3
```

Scenarios 1 through 6 run without interaction. Scenario 7 waits for Enter so you can inspect the Web UI. Scenario 8 is interactive.

## Scenarios

| # | Scenario | What it demonstrates |
|---|---|---|
| 1 | Quick start | Minimal server/client and request/response with `SendAsync` |
| 2 | Chat / broadcast | Multi-session management, `BroadcastAsync`, `OnMessageReceived` (push), Rx subscription |
| 3 | Failures and automatic reconnect | Unlimited `ConnectionRetryPolicy`, `ConnectionInfo.IsReconnecting`, `InterruptReconnectDelay`, `WaitForConnectionAsync` |
| 4 | KeepAlive and liveness | `KeepAliveConfig`, response matching with `ResponsePredicate`, no-response detection via `KeepAliveTimeoutCount` |
| 5 | Legacy protocols | Terminator framing with Shift-JIS, fixed-length framing, length-prefixed framing |
| 6 | Request control | Timeout, automatic resend with `RetryPolicy`, predicate matching with `SendAndWaitAsync`, notifications (`NotificationPredicate` / `SendOneWayAsync`), FIFO pipeline |
| 7 | Operations monitoring | `IMessageFilter` (checksum attach/verify), `ConnectionInfo` statistics, Web UI dashboard with message history and send |
| 8 | Interactive playground | appsettings.json + DI (`AddDnbnNet`), runtime changes to KeepAlive/timeout |

## Configuration files

- `appsettings.json` — DI configuration example used by scenario 8 (playground)
- Other scenarios set configuration in code so they are easier to copy

## Receive paths (important)

The client has three receive paths. Read the samples with this split in mind.

1. **Return value of `SendAsync`** — response to a request you sent (`OnMessageReceived` does not fire)
2. **`OnMessageReceived` / `MessageReceived` (Rx)** — push notifications from the server, unrelated to a request
3. **`OnKeepAliveResponseReceived`** — response to a KeepAlive message (matched with `ResponsePredicate`)
