# DAP client implementation

`HexIDE.Dap` is the byte-stream client foundation for [#696](https://github.com/hexide-io/HexIDE/issues/696).
It does not yet let the IDE launch or select an external debugger. The runtime debugger and F5 behavior
are unchanged. The wire foundation is [#1160](https://github.com/hexide-io/HexIDE/issues/1160);
process ownership is [#1162](https://github.com/hexide-io/HexIDE/issues/1162), and shared capture is
[#1166](https://github.com/hexide-io/HexIDE/issues/1166).

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

## Owning an adapter process

`DapProcess.Start(new DapProcessOptions(executable) { Arguments = [...], WorkingDirectory = ... })` launches
the given executable directly and returns an owner exposing `Connection`, `ProcessId` and `Completion`.
Arguments are individual strings, with no shell expansion. `Environment` overrides inherited variables;
a null value removes one. No executable discovery or managed-host fallback occurs: to launch a managed
assembly, choose the runtime host explicitly and pass the assembly path as its first argument.

Stderr drains continuously in chunks, even without newlines. `StandardErrorTail` retains the latest 16 Ki
characters by default, configurable through `StandardErrorTailCharacters`; `DroppedStandardErrorCharacters`
reports truncation. Stderr is never fed into the DAP frame reader.

Send any desired DAP `disconnect` request before disposal. Disposal itself closes the connection and stdin,
waits for process exit, then kills the still-live adapter's process tree if necessary. Protocol failure also
triggers cleanup automatically. `ShutdownTimeout` defaults to two seconds **per cleanup stage**, rather than
one total shutdown deadline. Concurrent disposals await the same completion task.

`Completion` returns `DapProcessResult`: the exit code, whether termination was forced, the connection failure,
any cleanup failure, the stderr tail and discarded-character count. Inspect it even after disposing. Process
exit first allows buffered protocol input to drain, with a timeout for inherited pipes that never reach EOF.
Tree termination is best effort through the operating system: it cannot contain descendants that detached
or survived a parent that already exited. It is not a process-group/job-object sandbox.

The process API does not send initialize/launch, select a backend, or register an IDE connection row. Its
diagnostics are available to callers and can feed the shared capture described below.

## Protocol capture

Create `new DapCapture(conversationLog, connectionId)` with a distinct id per session. Pass it as the optional
`capture` argument to `DapConnection`, or set `DapProcessOptions.Capture` to capture both process diagnostics
and wire traffic. The caller owns the log. A later IDE session host must supply the application's shared log;
there is no temporary launch UI or registry entry in this slice.

The existing inspector, queries and exports can consume the resulting records. DAP events appear as the
shared model's Notification kind, with their event name as the method label. Requests and responses pair by
direction and seq/request_seq; failed responses mark the exchange failed. Cancelled or abandoned local waits
end their records, while late responses remain separately visible. Outbound rows explicitly record a write
attempt, not proof of delivery, and failed writes carry a connection diagnostic.

Retained bodies are the original UTF-8 JSON, including whitespace and extension fields. The shared log's
opening-frame retention, arming, truncation and budgets apply. Queue overflow drops capture entries and
increments its loss counter without blocking DAP. Stderr is captured in bounded chunks; process lifecycle
does not record executable arguments or environment values.

Raw local inspection preserves paths. Pseudonymised exports rewrite complete JSON `path` string fields,
including plain DAP source paths, through the existing path policy. URI paths retain the existing URI rule.
This does not anonymise arbitrary launch fields, evaluated values, expressions, source text or free-form
output/diagnostics; review those before sharing, as with other protocol captures.

## Verification

`HexIDE.Tests/DebugAdapter/DapConnectionTests.cs` provides an independent peer that reads and writes literal
DAP wire frames over pipes. It checks envelopes, UTF-8 framing, fragmentation, response correlation, error
preservation, reverse requests, bounds, late replies, EOF, disposal and cancellation during/around writes.
`DapProcessTests` launches the independent `HexIDE.Dap.TestAdapter` fixture and checks launch parameters,
stderr pressure, exit codes, final responses, pending requests and process-tree cleanup.
`DapCaptureTests` checks correlation, exact bodies, retention, local outcomes, capture overflow, process
diagnostics and path pseudonymisation through the shared exporter.

```text
dotnet test IDE/HexIDE.Tests -- --filter-class "*DapConnectionTests"
dotnet test IDE/HexIDE.Tests -- --filter-class "*DapProcessTests"
```

These tests establish the wire boundary. They do not substitute for launching a real external adapter and
debugging a real program, which remains an acceptance requirement for the later process/session integration.

## Remaining work

- Executable/configuration selection in the IDE.
- Connect the capture and connection-registry UI to the future IDE session host.
- Asynchronous active-session abstraction, separate from interpreter-only execution hooks.
- Workspace preparation and launch sequencing through configurationDone.
- Remote breakpoint verification, stack/scopes/variables, evaluation, commands and MCP routing.
- Real-process interoperability tests and capability-dependent UI.
- Add-in registration through [#891](https://github.com/hexide-io/HexIDE/issues/891).

Protocol reference: [Debug Adapter Protocol specification](https://microsoft.github.io/debug-adapter-protocol/specification).
