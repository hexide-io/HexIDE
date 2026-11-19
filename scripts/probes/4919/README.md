# Procedure-call allocation (#4919)

This opt-in diagnostic measures HexIDE, not VB6. The requirements and the Windows baseline belong to
[issue #4919](https://github.com/hexide-io/HexIDE/issues/4919), its latest brief. No oracle
measurements are added here.

Run in a fresh process for each mode, with no debugger:

```sh
export UseSharedCompilation=false
export XDG_RUNTIME_DIR="$(mktemp -d)"
export HEXIDE_RECURSION_COST=1
dotnet run --project scripts/probes/4919 -c Release
dotnet run --project scripts/probes/4919 -c Release -- runaway
dotnet run --project scripts/probes/4919 -c Release -- throw
dotnet run --project scripts/probes/4919 -c Release -- profile
```

Drop WSL's `/mnt/` entries from PATH before building or testing, as CLAUDE.md describes. Without the
opt-in environment variable the executable prints a skip and does no work. Each invocation uses a new
64 MiB thread (STA on Windows, as UiThread starts it); its first execution is cold and the next three executions reuse the same interpreter
and parse tree. Parsing is outside the measured interval. Each execution must complete on that thread,
and the probe checks the resulting error number. `throw` raises Error 5 at the same depth as the return
case, separating exceptional unwind allocation from normal return. `runaway` reaches the existing
stack/depth guard. The bytes-per-level denominator is 5,000; no GC settings or depth guards are changed.

`profile` samples CLR allocation ticks by type. Its counts and timing include observer overhead and
must not be used as an allocation budget. EventListener receives native runtime events on a delivery
thread: a StackTrace taken there is the listener's stack, **not** the allocation stack. Method attribution
below distinguishes code inspection from measured allocation stacks.

## Cache ownership

`StatementExecutor.ReadBody` builds the statements, label map, line-number array and closing-target map
once per BlockContext. CollectLineHeads and ClosingTargetsOf perform all map writes during construction;
RunPass, GoSub, error handling and the debugger only read the completed maps/array. Argument slots,
identifier text and binary operand arrays likewise depend only on their parse contexts and are only read
after construction. ConditionalWeakTable keys use context identity and let a discarded tree be collected.

Declared locals, local constants, For bounds/steps, With targets and temporary frames remain per activation.
The first three containers are lazy; they are never cached or shared. The public expression-evaluation
signatures remain Task-based; internal synchronous completion paths avoid those task/state-machine
allocations, and await each ValueTask only once. Awaited default-member reads still resolve once.

## Measurements

Measured 2026-11-19 under WSL/Linux x64, .NET 11.0.100, Release, no debugger, baseline
`aecde6610` versus this change. Unprofiled fresh-process invocations, one per case. These are local
observations, not Windows measurements or timing assertions. The first warm execution still showed
tiering costs; the table retains it separately rather than calling it steady state.

| Case | Before bytes/level | After bytes/level | Before gen0 | After gen0 | Before ms | After ms |
|---|---:|---:|---:|---:|---:|---:|
| Return, cold | 11,581.5 | 10,726.1 | 5 | 4 | 1,495.7 | 1,038.4 |
| Return, first warm | 7,265.1 | 6,575.5 | 2 | 3 | 386.4 | 522.1 |
| Return, next two warm | 7,251.5–7,353.0 | 3,920.8–3,974.5 | 3–4 | 2 | 340.6–530.2 | 259.2–359.0 |
| Runaway, cold | 30,388.0 | 28,022.2 | 11 | 11 | 2,523.6 | 2,130.2 |
| Runaway, first warm | 25,083.4 | 22,667.6 | 11 | 9 | 1,970.1 | 1,628.4 |
| Runaway, next two warm | 21,579.3–21,579.4 | 19,250.8 | 9 | 7–8 | 817.6–846.4 | 661.2–707.1 |
| Error 5 at 5,000, cold | 36,758.4 | 31,920.5 | 14 | 12 | 3,191.1 | 2,427.8 |
| Error 5 at 5,000, first warm | 25,884.1 | 22,923.8 | 11 | 10 | 1,221.8 | 1,015.0 |
| Error 5 at 5,000, next two warm | 23,684.1 | 20,419.8 | 9–10 | 8 | 915.7–951.4 | 709.9–724.8 |

The returning call allocates about 46% less once warm. Error 5 at the same depth adds about
16.4 KB/level before and 16.5 KB/level after, including exception traces and faulted async-task machinery.
No exception rethrow site was changed: RunProcedure still throws afresh at each level, and the nearest
eligible handler still traps it. Removing that cost would require a further design/measurement, rather
than weakening #3250's rule. The issue's Windows allocation profile attributes 53% of runaway allocation
to stack-trace buffers and another 12% to faulted-task bookkeeping; this run does not claim those
percentages were remeasured locally.

The cold runaway remains above one second here. The remaining unwind cost and the first-warm tiering
allocation are visible in the table; their attribution to cold JIT/tiering is reasoned, not traced.
The Windows measurements below, from the review, show the same: the cold runaway stays above three seconds.

## Windows (review)

Measured 2026-11-19 on Windows x64, .NET 11.0.100, Release, no debugger: `upstream/main` `4cf20ffbb` against
this change merged with it. Each case ran in three fresh processes, with the probe above copied unchanged into a
checkout of main. "Cold" is a process's first execution; "warm" is its third and fourth, so the first warm one,
which still showed tiering, is left out.

| Case | Before bytes/level | After bytes/level | Before gen0 | After gen0 | Before ms | After ms |
|---|---:|---:|---:|---:|---:|---:|
| Return, cold | 11,874–14,293 | 9,668–10,767 | 5–6 | 4 | 1,971–2,252 | 1,349–1,674 |
| Return, warm | 7,214–7,353 | 3,953–4,049 | 2–4 | 1–3 | 374–871 | 219–506 |
| Runaway, cold | 29,944–30,225 | 27,838–27,892 | 11 | 11 | 3,236–3,993 | 3,251–3,457 |
| Runaway, warm | 21,579 | 19,283 | 8–10 | 7–8 | 952–1,178 | 983–1,141 |
| Error 5 at 5,000, cold | 34,265–36,765 | 31,929–33,244 | 13–14 | 12–13 | 3,978–4,106 | 3,408–3,742 |
| Error 5 at 5,000, warm | 23,684 | 20,452 | 9–11 | 7–8 | 1,039–1,417 | 956–1,222 |

- **A returning call allocates about 45% less once warm,** and collects less often.
- **A runaway's time is unchanged within the spread,** cold and warm. It allocates about 11% less a level, but the
  unwind's cost per level, described above, is untouched. So a first runaway in a process still takes over three
  seconds to reach Error 28 in Release, against #4919's aim of about one. This change doesn't meet that aim.
- **The depth a runaway reaches is unchanged.** A scratch probe, not committed, recursed to Error 28 on a fresh
  64 MiB STA thread, three executions per process. In Release, both reached `MaxCallDepth`, 5,000. In Debug, main
  stopped at 4,729 levels cold and 4,952 warm, and this change at 4,737 and 4,946.
- **Tests on Windows:** `HexIDE.Runtime.Tests` in full in Debug, 10,949 passed and 25 skipped. In Release,
  `ProcedureAllocationTests`, `ErrorUnwindTests`, `GoSubTests`, `ActivationSlotTests`, `UiThreadTests`,
  `DeclareCallTests` and `NamedArgumentTests` gave 129 passed and 14 skipped.

The structural test measures an ordinary 5,000-level return, on the real procedure-call path, with no
debugger. Debug measured 14,689 bytes/level after the change; its budget is 18,400 (about 1.25 times).
The allocation test uses a 128 MiB thread stack: the Windows check of `53579cad9` hit Error 28
before completing its 5,000-level return on 64 MiB. The issue also records the Windows Debug
stack guard stopping at 4,819 levels. This test-only stack size lets the fixed-depth allocation
measurement finish; the standalone probe still uses 64 MiB, and the production stack and guards
are unchanged. With it, the test passes on Windows in Debug and in Release.
Release measured about 3,975; its budget is 5,000. The same Debug test on main measured 20,680 and fails
the final budget. No wall-clock limit is asserted. Existing language-correctness tests remain regression
guards; this change does not introduce any new VB6 result expectations.

## Code inspection

The opt-in EventListener samples the three warm executions only; parsing and the cold execution are
excluded. See the sample counts below. Method names here come from reading their allocation sites,
**not from allocation stacks**:

- `StatementExecutor.ExecuteProcedureBody` / `ReadBody`: statement arrays, label dictionaries and closing
  dictionaries; `ClosingTargetsOf` / `NestedClosingNames`: traversal stacks and iterators. The former
  rebuilt them every call; the new ReadBody builds them once. LineNumbersOf already cached its array
  on main; caching the complete body also avoids its repeated lookup.
- `ExpressionExecutor.ResolveCallArgs` / `EvaluateCallArgs` / `ArgSlots`: argument-slot lists and a
  redundant ToList copy; the lists are now built once per ArgsCallContext.
- `ExpressionExecutor.VisitVsAdd` and binary comparison visitors: ANTLR valueStmt child arrays.
  They now read a context-keyed array. Identifier GetText calls in the two executors now cache their
  text by the identifier context.
- `EvaluateValue`, `EvaluateScalar`, `GetTwoValues`, `ResolveArg` and `ResolveDefaultMember`: task and
  async-state-machine allocations on paths that complete synchronously. Local ValueTask helpers and
  completed-result branches remove those allocations without changing the public Task signatures.
- `StatementExecutor` construction: three activation-owned dictionaries/sets are now lazy; the unused
  initial closing-target dictionary is gone. ExecutionEnvironment, owned/ref/fixed-copy lists,
  StatementExecutor, expression visitors, parameter values and statement-temporary frames still allocate.

## Allocation-stack profile

`dotnet-trace` 10.0.750501 was installed locally under artifacts, as the brief names
that tool. Its pinned NuGet registration metadata and package nuspec both declare MIT (checked
2026-11-19). It is an opt-in diagnostic tool, not a product or test
package dependency. The analyzer references the TraceEvent and FastSerialization assemblies supplied
by that tool; it adds no NuGet PackageReference.

The `gc-verbose` EventPipe profiles include allocation stacks. ProbeEvents marks each execution, and
`analysis/` selects only iterations 2 and 3 (two warm 5,000-level returns). It attributes each allocation
tick to its nearest interpreter frame, keeping the sampled type beside the method. All 691 baseline
samples and all 384 after-change samples in those intervals had an interpreter frame. Selected leading
sites follow; counts are samples, not counts of objects. AllocationAmount64 is a sampling weight, not an
exact charge to a particular type, so the table does not present it as measured object bytes.

| Before site | Sampled type | Samples |
|---|---|---:|
| BasicInterpreter.ResolveDefaultMember | Task of Vb6Value | 61 |
| ExpressionExecutor.EvaluateValue | Task of Vb6Value | 58 |
| ExpressionExecutor.ResolveImplicitCall | String / char array / StringBuilder | 33 / 31 / 15 |
| BasicInterpreter.RunProcedure | StatementExecutor | 33 |
| ExpressionExecutor.EvaluateScalar | Task of Vb6Value | 33 |
| ExpressionExecutor.GetTwoValues | Task of operand pair | 21 |
| ExpressionExecutor.VisitVsCompare / VisitVsAdd | ValueStmtContext array | 20 / 14 |
| StatementExecutor constructor | local-name HashSet / For-state dictionary | 12 / 12 |

| After site | Sampled type | Samples |
|---|---|---:|
| ExpressionExecutor.ResolveCallArgs | CallArg array | 29 |
| ExpressionExecutor.VisitVsLiteral | String / char array / StringBuilder | 28 / 18 / 11 |
| BasicInterpreter.RunProcedure | StatementExecutor | 22 |
| ExpressionExecutor.GetTwoValuesSameTypesOrNull | Task of operand pair | 18 |
| ExpressionExecutor.VisitVsICS | Task of object | 16 |
| ExpressionExecutor.ResolveImplicitCall | boxed Vb6Value | 16 |
| BasicInterpreter.BindParameters | HashSet entries / slot-index array | 15 / 14 |
| ExecutionEnvironment.CreateProcedureScope | HashSet / dictionary | 13 / 12 |

The remaining literal text, argument/slot containers, boxed values and visitor tasks are measured
remaining sites. They are not silently claimed eliminated. No state is pooled across activations.

Reproduce the stack profile after installing the named tool (run once against main and once against
this change, in separate copies, then compare the analyzer outputs):

```sh
dotnet tool install dotnet-trace --version 10.0.750501 --tool-path artifacts/4919-tools
trace_tool_dir="$PWD/artifacts/4919-tools/.store/dotnet-trace/10.0.750501/dotnet-trace/10.0.750501/tools/net8.0/any"
dotnet build scripts/probes/4919 -c Release
HEXIDE_RECURSION_COST=1 artifacts/4919-tools/dotnet-trace collect \
  --profile gc-verbose --providers HexIDE-RecursionCost --show-child-io \
  --output artifacts/4919.nettrace -- \
  dotnet scripts/probes/4919/bin/Release/net11.0/RecursionCostProbe.dll
dotnet run --project scripts/probes/4919/analysis -c Release \
  -p:TraceToolDirectory="$trace_tool_dir" -- artifacts/4919.nettrace
```

The stack profile was collected after the allocation-only edits, using an isolated main source copy
inside this clone for the baseline. The initial unprofiled measurements preceded those edits. The
baseline copy was then removed. Raw traces under artifacts are disposable; the reproducible analyzer,
measured tables and method attribution live here.

Warm allocation-tick samples: 1041 before, 578 after, over three 5,000-level
returns each. Counts are samples, not object counts or bytes; GC/tiering and observer overhead affect
which allocations are sampled. Selected types:

| Type | Before samples | After samples |
|---|---:|---:|
| Task of Vb6Value | 186 | 0 |
| Task of object | 85 | 67 |
| HashSet of string | 81 | 28 |
| StringBuilder | 69 | 27 |
| String | 57 | 47 |
| Closing-target dictionary | 32 | 0 |
| ValueStmtContext array | 10 | 0 |
