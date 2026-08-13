# Troubleshooting

English | [日本語](./ja/troubleshooting.md)

## Cannot connect

Check:

- `RemoteHost` and `RemotePort` are correct
- The server has already called `StartAsync`
- There is no firewall block or port conflict
- `ConnectionRetryPolicy` is set if you need connection retry

## No message comes back

Check:

- The server calls `SendAsync(sessionId, ...)`
- Message-boundary settings such as `MessageTerminator` match on both sides
- `TimeoutMilliseconds` is not too short
- The server event handler is not throwing
- You are not using `SendAsync` for a command that has no response (use `SendOneWayAsync`)

## Messages are split or concatenated

TCP is a stream. If framing settings do not match, multiple messages can be joined or cut in the middle.

- Text protocols: `MessageTerminator`
- Fixed length: `FixedHeaderLength` and `FixedBodyLength`
- Variable length: `FixedHeaderLength`, `LengthFieldOffset`, `LengthFieldLength`

When multiple terminator candidates match at the same position, longest match wins. To accept CR/LF/CRLF, you can set `["\r\n", "\r", "\n"]`. If a TCP chunk ends on CR, the parser holds that message until the next byte confirms whether it is CR alone or CRLF.

## The next request receives the wrong response after a timeout

For protocols without a correlation ID that match responses in FIFO order, a late response after timeout can be paired with a later request. Set:

```json
{
  "MaxConcurrentResponseWaits": 1,
  "IncompleteRequestRecovery": "Reconnect",
  "WaitForConnectionOnSend": true
}
```

## Responses do not arrive on `OnMessageReceived`

Responses received by the client's `SendAsync` do not normally flow into `OnMessageReceived`. `OnMessageReceived` is for notifications unrelated to a request, such as server push.

Set `NotificationPredicate` if you do not want notification messages treated as responses.

## KeepAlive responses mix with notifications

KeepAlive is correlated on the same FIFO response queue as normal requests, so it does not steal `SendAsync` responses on protocols that return replies in order. KeepAlive sends themselves are also deferred while a normal request is waiting.

If `ResponsePredicate` is unset, a server push that arrives while a KeepAlive response is pending is consumed as the KeepAlive response. For protocols with push notifications, set `KeepAliveConfig.ResponsePredicate` and `NotificationPredicate` in code. Messages that match `NotificationPredicate` are delivered as notifications before any response matching, including KeepAlive.

## Disconnected by KeepAlive timeout

If a KeepAlive response does not return within `IntervalSeconds`, the default is to disconnect (`DisconnectOnTimeout: true`). This prevents a late KeepAlive response from being correlated as the reply to a later normal request. The disconnect is treated as a network failure, so `ConnectionRetryPolicy` triggers auto-reconnect when set.

Set `DisconnectOnTimeout: false` only if you must keep the connection after timeout. For protocols that cannot distinguish KeepAlive by content, late-response miscorrelation remains a risk.

## Idle disconnects are not noticed until the next send

If the network or an intermediary times out an idle connection, the OS may not notice until the next `SendAsync`. Mitigations:

- Enable `TcpKeepAlive` (TCP-level keep-alive) so the OS sends probes and detects the drop. When the drop is detected, the receive loop ends, and `ConnectionRetryPolicy` starts auto-reconnect if set.
- If you also need application liveness (the process is up but hung), use `KeepAlive` (application-level messages). The two can be used together.

## The receive buffer grows large

Terminator framing or length-based framing (fixed-length or length-prefixed) is required. Incomplete or conflicting settings throw when the endpoint is created. Set `MaxReceiveBufferBytes` as well to limit memory use from a large declared length or a missing terminator.
