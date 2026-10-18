# DAP client implementation

`HexIDE.Dap` is the byte-stream client foundation for [#696](https://github.com/hexide-io/HexIDE/issues/696).
It does not yet let the IDE launch or select an external debugger. The runtime debugger and F5 behavior
are unchanged. The first slice is [#1160](https://github.com/hexide-io/HexIDE/issues/1160).

## Available API

Construct `DapConnection` with readable adapter output and writable adapter input. Successful construction
transfers ownership of both streams. Reading begins immediately; consume `Events` through a single consumer.
The caller supplies any UI dispatch, event handling and session policy.

`RequestAsync(command, arguments, cancellationToken)` sends a DAP request. Arguments are an optional JSON
object. A successful response retains the complete JSON message; a failed response raises
`DapRequestException`, whose `Response` retains the adapter message and structured error body. Unknown
capability and extension fields are preserved. No capability is automatically advertised or negotiated.

The client uses DAP `seq`, `type`, `command`, `arguments`, `request_seq`, `success`, `event` and `body` fields.
It does not use JSON-RPC's `id`, `method` or `params`. Content-Length counts UTF-8 bytes, and writes are
serialized so concurrent requests do not interleave their frames. Replies may arrive out of order.

All reverse requests currently receive an explicit unsuccessful response. Do not advertise reverse-request
capabilities such as `supportsRunInTerminalRequest` until a host implementation is added.

## Lifetime and limits

Defaults in `DapConnectionOptions`:

| Limit | Default |
|---|---|
| Message body | 16 MiB |
| Header | 8 KiB |
| Pending requests | 256 |
| Queued events | 256 |
| Request deadline, including writer wait | 30 seconds |

The limits are configurable per connection. A full event queue closes the connection explicitly rather
than dropping state or growing memory indefinitely. Consumers must drain output events as well as debugger
state events. The pending-request limit refuses additional work without closing a healthy connection.

Timeout/cancellation ends the local request wait; it does not undo remote execution. There is no retry or
automatic DAP cancel request. A late response to an abandoned request is ignored. Cancellation or failure
during writing closes the connection, since the peer may have received only part of a frame.

EOF, malformed messages and connection disposal settle pending requests. `Completion` returns the first
closure exception, or null after intentional disposal. The event stream also completes with the failure.
Dispose the connection even after input ends. Streams must support cancellation or unblock when disposed.

## Verification

`HexIDE.Tests/DebugAdapter/DapConnectionTests.cs` provides an independent peer that reads and writes literal
DAP wire frames over pipes. It checks envelopes, UTF-8 framing, fragmentation, response correlation, error
preservation, reverse requests, bounds, late replies, EOF, disposal and cancellation during/around writes.

```text
dotnet test IDE/HexIDE.Tests -- --filter-class "*DapConnectionTests"
```

These tests establish the wire boundary. They do not substitute for launching a real external adapter and
debugging a real program, which remains an acceptance requirement for the later process/session integration.

## Remaining work

- Process ownership, executable/configuration selection and child-process cleanup.
- DAP protocol capture feeding the existing inspector and connection-list UI.
- Asynchronous active-session abstraction, separate from interpreter-only execution hooks.
- Workspace preparation and launch sequencing through configurationDone.
- Remote breakpoint verification, stack/scopes/variables, evaluation, commands and MCP routing.
- Real-process interoperability tests and capability-dependent UI.
- Add-in registration through [#891](https://github.com/hexide-io/HexIDE/issues/891).

Protocol reference: [Debug Adapter Protocol specification](https://microsoft.github.io/debug-adapter-protocol/specification).
