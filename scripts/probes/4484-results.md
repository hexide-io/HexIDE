# #4484 validation (WSL and Windows, 2026-11-17–18)

Baseline: `a1c0be6f3` (`upstream/main` when this run began), StreamJsonRpc 2.24.92,
.NET 11.0.0, Ubuntu under WSL. The baseline test assembly includes the new churn
harness, `NullLogger` clients, stop-time output, and an inert default-false
`TryEndOutput` interface member and timeout constant so the new tests compile.
Its handlers and teardown retain main's behaviour. Immutable copies of the built
assemblies keep baseline and branch processes separate. No package pins changed.
The churn implementation was adapted from this repository's `wip/3520-churn`
commit `f15fc9270960f1d4175c3b513cd877419ca99852`, without its experimental adapters
or poison pools. The cause and earlier measurements are recorded on
[#3520](https://github.com/hexide-io/HexIDE/issues/3520); the final scope is
[#4484](https://github.com/hexide-io/HexIDE/issues/4484)'s round 4 comment.

## Regression teeth

On the baseline, the stdio-shaped client-first arm (`CHURN=12`, eight checked
pairs) detected its first bad frame at **9.261 seconds** from the ledger's start.
It finished after 16,212 checked frames and 1,490 teardowns, with one bad frame.
A new `Content-Length` header appeared inside another connection's JSON string.
The framing assertion failed; the run ended after 10.357 seconds overall.

Five half-close cases fail before implementation: the two `StopEndsOutput...`
rows, the unsupported-transport call assertion, and the stdio/TCP end-of-input
wire tests. The leave-running row already passes on main and is a regression
guard. Two additional unit tests fail under isolated mutations: replacing the interlocked
swap with a plain read fails the 999-suppressed-disposals assertion; replacing the
private array pool with the shared pool fails the cross-connection isolation
assertion. Those are mutation checks of the new helper, not additional failures
claimed against main. Both protections were restored and rebuilt afterwards.
No existing test's expectation was changed. The two wire tests accept a
`JsonRpcRequest` subclass: the formatter returns its own derived request type.

## Stop timings

Milliseconds measured around `StopAsync`, excluding startup. These are single
samples, including scheduler noise. The existing time bounds were not changed.

| Case | Main | Branch (58-test focused run) |
|---|---:|---:|
| Server stops reading (#1221) | 4,010 | 4,005 |
| Server never answers shutdown (#392) | 2,010 | 2,002 |
| Reply stalls halfway | 2,018 | 2,001 |
| Slow shutdown, stdio | 1,514 | 2,526 |
| Slow shutdown, named pipe | 1,514 | 1,533 |
| Unanswered shutdown helper, stdio | 3,518 | 4,528 |
| Unanswered shutdown helper, named pipe | 3,544 | 3,533 |

Stdio helpers that keep output open spend the new one-second output-end wait.
The blocked-write path skips it. Named pipes cannot half-close. Scheduling adds
small differences beyond the configured wait; these measurements do not claim
an exact wall-clock bound. All 23 baseline fault/slow-shutdown cases passed.

## Windows (2026-11-18)

Windows Server 2025, .NET 11.0.0, Debug builds of this branch and of a baseline worktree: this branch's
`WireStressTests.cs` with each `new LspMessageHandler(` replaced by `new HeaderDelimitedMessageHandler(`,
`NullLogger` kept. Each run had its own four cores (processor affinity, `DOTNET_PROCESSOR_COUNT=4`), below-normal
priority, `CHURN=12`, eight checked pairs, run directly with the in-process xunit runner.

**Teeth.** The baseline's stdio client-first arm failed at **124.878 s**, after 539,181 checked frames and
25,149 teardowns, with one bad frame: a `Content-Length: 48811` header inside another connection's frame.

**20-minute arms on this branch.** All passed with no bad frames:

| Arm | Checked frames | Teardowns | Bad | Memory after full GC |
|---|---:|---:|---:|---:|
| stdio client-first | 5,268,322 | 219,561 | 0 | 36.3 MB |
| stdio half-close | 5,259,704 | 199,939 | 0 | 35.4 MB |
| TCP client-first, sockets closed after RPC (see below) | 2,375,623 | 444,447 | 0 | 24.6 MB |

**The TCP round now closes its sockets, as the client does.** Its first version left them to the handlers.
- **The leak:** that version ended its 20 minutes at 111.7 MB after a full GC, against 28.3 MB on the baseline.
  Heap snapshots at 100 s and 280 s showed the branch's live `LspMessageHandler` count growing from 66 to 105,
  and its 128 KB arrays from 115 to 205. The baseline held 30 handlers throughout.
- **The cause:** about one round in 1,000 kept both of its connections. `LspMessageHandler` closes a stream it
  both reads and writes only once its last write ends, so when both peers' writes block on each other, neither
  handler can end the connection.
- **Why it's the harness:** the client always closes the socket or pipe after `DisposeRpc` (`StopAsync`, the
  restart, and `ConnectAsync` before a reconnect). `BlockedConnectionStopTests` now pins that for TCP and named
  pipes.
- **The new round:** the TCP round now disposes RPC and then closes its sockets. The close is abortive (linger
  0), because graceful closes exhausted Windows' loopback ports within five minutes of churn (error 10048), on
  the baseline too. With it, the heap snapshots hold 16 handlers, the eight pairs' two sides, at both times. A
  300-second run passed on both: the branch did 123,130 teardowns and holds 23.4 MB, and the baseline did
  192,662 and holds 26.0 MB. Over 20 minutes the baseline checked 2,080,980 frames in 593,312 teardowns with no
  bad frame and held 26.1 MB. It was marked failed because one churn round got Windows error 10055 (no socket
  buffer space), a limit of churning that fast, not a frame fault.

**The stdio fixture's setup waits are 30 s, named `StdioSetupTimeout`.** They were 5 s. On Windows an
anonymous pipe's async read holds a thread-pool thread until data or end of input arrives. With twelve churners
on four cores, a new round's first read could wait past 5 s for a thread. Bisected over 8 × 45 s, with the
handler swapped between this branch's, StreamJsonRpc's own over the shared pool, and private pools with and
without the guard: setup timeouts hit 2 of 8 runs regardless of the handler, all with 0 bad frames. These waits
establish a round; they measure nothing.

**No-churn cost**, 60 s, two rounds, the same workload on both:

| | Allocated per frame | Frames per second | Memory after full GC |
|---|---:|---:|---:|
| Branch | 40,271 / 40,277 bytes | 8,198 / 8,638 | 25.4 / 25.7 MB |
| Baseline | 40,215 / 40,218 bytes | 8,423 / 8,609 | 24.8 / 24.7 MB |

That's +0.15% allocation, throughput within the rounds' spread, and about 0.8 MB more retained by the
per-connection pools.

**Stopping several servers.** The registry now stops its servers concurrently. Three servers in the test
server's `slow` stdio mode, which keeps its output open, were measured over three rounds with a scratch probe:
- one server's stop: 3.05–3.08 s on this branch, against 2.06–2.09 s on main (the new 1 s output-end wait);
- three through the registry: **3.07–3.11 s** on this branch, against 6.15–6.17 s on main, where the stops ran
  one after another.

**Tests.** The full suite passed: 10,031 passed, 119 skipped, 0 failed. `RealServerShutdownTests` passed 6 of 6
on both builds. Teeth of the tests this branch adds on Windows:
- **`BlockedConnectionStopTests`:** with `TcpLspTransport` and `NamedPipeLspTransport` not closing in
  `DisposeAsync`, both cases fail.
- **`StoppingStopsEveryServerAtOnce`:** with the registry stopping servers one after another, it fails in 10.4 s.

## Harness setup and incomplete runs

The first stdio client-first run did not return after its 20-minute load and was
interrupted; it is not counted as a passing run. Adding watchdogs exposed that
waiting for cancelled sends before ending the pipes did not model product
teardown: product disposes RPC, then its transport. The harness now ends its
pipes before awaiting those sends. A second diagnostic run was stopped while
that correction was being made, also without a passing result.

A further client-first run stopped churning at 14,337 teardowns although checked
traffic continued. An auxiliary diagnostic run also hit thread-start resource
exhaustion and a peer cleanup timeout; it was not counted as validation. An owned
process dump showed these concurrent stacks on .NET 11.0.0 under Linux:

- `StdioRound` → `PipeStream.Dispose` → `SafePipeHandle.Dispose` → `Socket.Dispose`
  → `SafeSocketHandle.CloseAsIs` (spinning).
- `PipeStream.ReadAsyncCore` → `SafePipeHandle.CreatePipeSocket` → `Socket.Dispose`
  → `SafeSocketHandle.CloseAsIs` (spinning).

The fixture was racing initial lazy Unix pipe-socket setup with disposal. The
final stdio fixture first observes a framed notification and actual peer input
before releasing the flood and tearing down. This models an established
connection, with both directions used, and still tears down mid-write. Merely
waiting for the old writer's flush did not establish that the peer had received
bytes, so the input drain signals receipt explicitly. This is a fixture setup
change, not a product teardown redesign. No claim about an upstream fix is made.

Subsequent diagnostic runs use a separate `TMPDIR` as well as `XDG_RUNTIME_DIR`:
parallel isolated processes can otherwise have ambiguous default diagnostic
socket names. The dump collector checked the test command line before collecting
only the requested owned process. No other clone was inspected or changed.

## Reproduction

Before builds/tests, remove `/mnt/` entries from PATH, export
`UseSharedCompilation=false`, and set `XDG_RUNTIME_DIR` to a fresh writable temp
directory, as the task's WSL instructions require. For each branch churn arm:

```sh
cd IDE
HEXIDE_WIRE_STRESS_SECONDS=1200 HEXIDE_WIRE_STRESS_CHURN=12 \
  HEXIDE_WIRE_STRESS_CHURN_ORDER=stdio-client-first \
  dotnet test HexIDE.Tests/ -- --filter-method '*ConcurrentConnectionsKeepEveryFrameValid' \
  --hangdump --hangdump-timeout 25m \
  --report-xunit --report-xunit-filename 4484-churn.xml
```

Repeat with `stdio-half-close` and `tcp-client-first`. The TCP arm uses TCP for
both checked and churn pairs. For the no-churn cost comparison, omit CHURN and
CHURN_ORDER and set SECONDS to 60. The baseline uses the identical checked-pair
workload with `HeaderDelimitedMessageHandler(Stream, Stream, formatter)`.
Allocations include the entire harness and are divided by checked frames;
throughput is checked frames divided by elapsed load time. Full-GC memory is
the whole managed test heap, with the checked connections and ledger still live.

The direct executions of immutable assembly copies used xunit's in-process
runner (`dotnet <test.dll> -method '*ConcurrentConnectionsKeepEveryFrameValid'
-showLiveOutput -xml <report.xml>`); ordinary `dotnet test` uses MTP's options
shown above. Both execute the same test method and assertions.

To reproduce the baseline stress arm from source, bring only the current
`WireStressTests.cs` onto the baseline commit and replace its
`new LspMessageHandler(...)` calls with `new HeaderDelimitedMessageHandler(...)`.
Keep the `NullLogger` change in both arms; otherwise NSubstitute retains cancelled
requests through recorded log arguments and the memory comparison measures the
logger harness instead of the transport.
