# DAP client implementation

`HexIDE.Dap` is the byte-stream client foundation for [#696](https://github.com/hexide-io/HexIDE/issues/696).
The IDE can explicitly [launch an external debug session](debug-launch.md) from a JSON configuration and
route its run controls and inspection windows to it. Ordinary interpreter launches remain available when no
external session is active. The wire foundation is [#1160](https://github.com/hexide-io/HexIDE/issues/1160);
process ownership is [#1162](https://github.com/hexide-io/HexIDE/issues/1162), and shared capture is
[#1166](https://github.com/hexide-io/HexIDE/issues/1166). Asynchronous sessions and real-process acceptance
are [#1168](https://github.com/hexide-io/HexIDE/issues/1168).

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
and wire traffic. The caller owns the log. The IDE session host supplies the application's shared log
and registers the latest external session in Language & Debug Servers with a distinct DAP identity.

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

## Asynchronous sessions

`DapSession.Start(processOptions)` owns the adapter process; `new DapSession(connection)` owns an existing
connection. Do not consume that connection's events or send commands independently once transferred.
The session has a single launch and cannot be restarted. Dispose it and create a new one for another run.

`LaunchAsync(adapterId, arguments, breakpoints, token)` sends initialize with one-based coordinates and
native paths, starts launch, waits for initialized, replaces each supplied source's breakpoints, selects
advertised default exception filters, and sends configurationDone only when supported. It awaits the launch
response **after** configuration, since adapters may defer that response. Initialized may arrive before the
initialize response. All startup work shares a 60-second deadline (configurable for a supplied connection),
in addition to the wire request limits. Failed/cancelled startup releases the owned connection/process.
The returned breakpoint responses retain verified flags, relocated lines and adapter messages.

Consume the bounded `Updates` channel promptly from one background reader. Each update carries a snapshot
and, where applicable, the original event, including output and extension fields. Overflow ends the connection
and exposes a fault through `Snapshot`; it never silently drops live debugger state. `Completion` means the
event reader and owned cleanup have finished. `ProcessCompletion`, available for process-backed sessions,
retains exit code, forced termination and stderr/cleanup diagnostics. A program's terminated event sets
Ended but does not imply the adapter process exited: call `DisconnectAsync` to send disconnect with
terminateDebuggee and then dispose. Plain disposal is the bounded fallback, not a protocol disconnect.

Stopped snapshots carry a session-specific `DapStop`. Pass it to Threads/StackTrace/Scopes/VariablesAsync.
The raw response retains thread/frame IDs (including frame zero), source paths/references, variable
references and unknown fields. Reads reject a stop that expired before or during the request. Call
`IsCurrent(stop)` again **when applying a result on the UI thread**: a stop can expire after a method returns.
The library never dispatches UI work. This first session models one globally stopped program; per-thread
running state and partial-thread resumes are not implemented.

`ResumeAsync` accepts continue, next, stepIn or stepOut. It invalidates inspection before sending and never
overwrites a later stopped event on receipt of its response. A definitive refusal restores a fresh stop;
timeout/cancellation is uncertain and ends the session rather than claiming it is still paused. `PauseAsync`
requests a break while running; only a stopped event confirms it. Capabilities retain the initialize response.
`EvaluateAsync(stop, frameId, expression)` sends adapter-specific Immediate syntax with `context: repl`.
Frame zero is valid. The raw result preserves type, variable reference and extension fields. The stop is
validated before and after the request; callers must also validate their selected-frame identity before
presenting the result. REPL evaluation has no optional capability flag and can have side effects. A failed
or timed-out request is never retried. The IDE displays result text/errors and refreshes selected-frame locals.
Dynamic capability changes and live breakpoint replacement remain future session operations.

## Verification

`HexIDE.Tests/DebugAdapter/DapConnectionTests.cs` provides an independent peer that reads and writes literal
DAP wire frames over pipes. It checks envelopes, UTF-8 framing, fragmentation, response correlation, error
preservation, reverse requests, bounds, late replies, EOF, disposal and cancellation during/around writes.
`DapProcessTests` launches the independent `HexIDE.Dap.TestAdapter` fixture and checks launch parameters,
stderr pressure, exit codes, final responses, pending requests and process-tree cleanup.
`DapCaptureTests` checks correlation, exact bodies, retention, local outcomes, capture overflow, process
diagnostics and path pseudonymisation through the shared exporter.
`DapSessionTests` checks handshake ordering, capability gating, breakpoint replies, stale inspections,
fast stops, refused/cancelled resumes, startup deadlines, terminal state, output and overflow.

```text
dotnet test IDE/HexIDE.Tests -- --filter-class "*DapConnectionTests"
dotnet test IDE/HexIDE.Tests -- --filter-class "*DapProcessTests"
```

The opt-in [real adapter smoke](dap-adapter-smoke.md) launches a separately installed adapter and its real
components. It is not downloaded or run by ordinary CI; the smoke executable is built with the IDE solution.

## Remaining work

- Rich launch settings UI and workspace preparation, including any VB6 project conversion.
- Multi-thread inspection.
- Structured breakpoint verification UI and command-specific MCP routing.
- Watches/hover evaluation, live breakpoint updates, dynamic capabilities and capability-dependent UI.
- Automated acquisition and cross-platform real-adapter acceptance in CI.
- Add-in registration through [#891](https://github.com/hexide-io/HexIDE/issues/891).

Protocol reference: [Debug Adapter Protocol specification](https://microsoft.github.io/debug-adapter-protocol/specification).
