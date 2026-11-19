# #4775: deep recursion and garbage collection

Measured 2026-11-19 against `33cbe5281`, Linux x64 under WSL, .NET SDK 11.0.100 / runtime 11.0.0,
Release, Intel Core i9-9980XE. No debugger. Each invocation uses a fresh 64 MiB thread through
`UiThread.RunOnNewThread`. Two 5,000-level runs warm the parser and tiered JIT before the table.
The original issue's Windows figures are **not** reproduced here. Windows Release measurements from the
review follow under [Windows](#windows). Source: [#4775 and its supervisor comment](https://github.com/hexide-io/HexIDE/issues/4775).
These are HexIDE host measurements, not new VB6 oracle measurements.

## Result

**GC causes the extra deep-recursion cost in this environment.** Removing collections with either a
256 MiB initial gen0 budget or a 256 MiB no-GC region removes most of the cost, without changing
interpreter code. Allocation grows approximately with depth. With ordinary collection, elapsed time
jumps when deeper calls trigger more collections. The separate GC-event trace confirms that the
collections themselves take hundreds of milliseconds deep in the walk (table below).

This identifies GC as the cause, rather than a per-call operation walking `activationStack`.
Deep stack/root scanning is consistent with these results, but this experiment does **not** isolate
native stack scanning from every other part of GC root processing. The retained-activation
experiment below further separates deep-stack cost from retained activation-object count. Do not report the precise native
GC subroutine, or the Windows cause, as measured. The managed sampling profile found no growing
stack search in `RunProcedure`, `StatementExecutor` or debugger hooks. Source inspection finds
constant-time depth guards and a backwards identity search on return, whose first comparison
matches in this synchronous recursion.

No product change or process GC setting is proposed. Avoiding collections removes the symptom but
is only a diagnostic: a finite budget cannot accommodate arbitrary program execution. Reducing
linear per-call allocation would move collection boundaries; these measurements alone do not establish
a local allocation change that makes ordinary deep recursion linear. The issue's alternative,
recording the runtime cause while leaving product code alone, is the deliverable here. Under WSL the
existing runaway takes about 1.25 seconds normally and 0.33–0.37 seconds without collections. On Windows,
a warm runaway takes 0.92–1.20 s, and the first one in a process 2.92 s ([Windows](#windows)). Cutting what a
call allocates, so there are fewer collections, is the lever that's ours, and is
[#4919](https://github.com/hexide-io/HexIDE/issues/4919).

## Returning normally

Each cell lists three complete-run times in milliseconds. Counts are `(gen0, gen1, gen2)` as returned
by `GC.CollectionCount`: higher-generation collections are included in lower-generation counts.
Allocation is measured with `GC.GetAllocatedBytesForCurrentThread`; raw samples are in `results/`.
Construction precedes the timer; `Execute`, its declaration work, descent, return and output are timed.
Each run asserts the requested depth and `Err.Number = 0`, so an early Error 28 cannot look fast.
The diagnostic's printed results are checked after timing.

| Depth | Ordinary GC ms | Collections | Raised gen0 budget ms | Collections | No-GC region ms | Collections |
|---|---|---|---|---|---|---|
| 1000 | 43.6 / 26.1 / 28.4 | 0/0/0 | 77.4 / 52.8 / 46.5 | 0/0/0 | 47.9 / 32.1 / 46.4 | 0/0/0 |
| 2000 | 155.2 / 164.3 / 131.6 | 1/0/0 | 104.3 / 86.0 / 90.2 | 0/0/0 | 68.7 / 54.5 / 56.3 | 0/0/0 |
| 3000 | 132.8 / 144.8 / 138.2 | 1/0/0 | 80.3 / 86.9 / 46.5 | 0/0/0 | 63.4 / 56.7 / 54.9 | 0/0/0 |
| 4000 | 324.2 / 297.7 / 335.4 | 2/1/0 | 103.0 / 114.2 / 73.4 | 0/0/0 | 67.5 / 65.1 / 68.1 | 0/0/0 |
| 5000 | 363.6 / 410.8 / 475.2 | 2/1/0 | 153.0 / 105.3 / 86.5 | 0/0/0 | 95.7 / 90.7 / 88.8 | 0/0/0 |

Ordinary allocation: 7.73 / 15.49 / 22.94 / 30.35 / 38.51 MB for the five depths (decimal MB).
The other processes settle at slightly different tiered-JIT allocation levels: about 8 / 16 / 24 / 32 / 40 MB.
No product code differs between columns; these are interventions on GC, not before/after a fix.

## Retaining the same activation objects after return

A second experiment promotes the heap at the leaf, retains every `StatementExecutor` in an array,
and forces three gen0 collections there. After return, it forces the same three collections while
keeping that array alive. Thus the activation objects remain retained in both comparisons, and
most objects have already been promoted before timing. This does not claim identical roots or
slot contents: normal return still releases owned slots. It separates the cost of collecting on
the deep stack from simply having allocated more activation objects.

| Depth | Collections at the leaf, ms | Collections after return, ms |
|---|---|---|
| 1000 | 34.036 / 77.706 / 33.731 | 0.428 / 0.220 / 0.150 |
| 2000 | 74.341 / 73.153 / 68.937 | 0.423 / 0.252 / 0.194 |
| 3000 | 99.695 / 110.332 / 108.564 | 0.408 / 0.271 / 0.187 |
| 4000 | 134.135 / 257.396 / 137.200 | 0.485 / 0.207 / 0.156 |
| 5000 | 113.129 / 229.790 / 118.166 | 1.168 / 0.344 / 0.175 |

The cost persists for empty-gen0 collections while the deep frames are present, then drops by
orders of magnitude after return despite retained activations. This is evidence of deep-stack/root
processing during GC, rather than a growing per-call interpreter search. The exact native routine
has not been sampled: `collect-linux --profile cpu-sampling` was attempted and returned
`Permission denied (os error 13)` (`results/native-profile.txt`).

## Runaway

The diagnostic prints depth 5,000 before making the refused call, then the outer handler prints 28.
Both are asserted. `MaxCallDepth` and the real stack probe are unchanged.

| Mode | Three times, ms | Collections | Bytes allocated |
|---|---|---|---|
| runaway | 1320.2 / 1244.9 / 1251.6 | 9/2/0 | 119481440 |
| runaway-no-gc | 373.6 / 367.5 / 327.1 | 0/0/0 | 119481440 |

`ErrorUnwindTests.ARunawaysError28Unwind_AllocatesAsMuchPerLevelAsAShallowOne` is a structural
regression guard. A runaway on a 64 MiB thread runs until a guard raises Error 28, and the test reads
the depth it reached: the stack probe stops a Windows Debug build first (4,819 levels in one run),
`MaxCallDepth` elsewhere. It then raises Error 28 at a fifth of that depth. The runaway's walk and
unwind must allocate less than 1.2 times as much per level as the shallow one. On Windows Debug the
two came to 45,199 and 50,159 bytes a level.

- **It passes on unchanged main.** It guards; it proves no fix.
- **It has teeth.** A temporary mutation allocating `64 × activationStack.Count` bytes per call in
  `RunProcedure` failed it: 200,242 against 81,253 bytes a level.
- **No timing:** there is no wall-clock threshold and no CI GC configuration change.

## Windows

Measured in review, 2026-11-19, Windows x64 (Windows Server 2025), Release, at the lane's commit on
`33cbe5281`, with this probe's modes run as `run.sh` runs them. "Cold" rows are the two warm-up runs,
the first two in each fresh process, which the probe now records. They come from a second pass of the
default and runaway modes, whose warm rows matched the first pass's (return 5,000: 472–567 ms;
runaway: 917–1088 ms).

| Case | Three times, ms | gen0/gen1/gen2 | Bytes allocated |
|---|---|---|---|
| return 1,000 | 26.0 / 22.1 / 22.9 | 0/0/0 | 7,873,952–8,029,352 |
| return 5,000 | 489.5 / 613.3 / 636.8 | 3/1/0 | 39,989,496–39,990,024 |
| return 1,000, gen0 budget raised to 256 MiB | 27.4 / 26.4 / 26.8 | 0/0/0 | 7,909,168–7,941,352 |
| return 5,000, gen0 budget raised to 256 MiB | 67.1 / 65.2 / 65.6 | 0/0/0 | 39,989,496–39,990,024 |
| return 5,000, no-GC region | 65.9 / 68.3 / 65.6 | 0/0/0 | 39,989,496–39,990,024 |
| runaway | 1053.4 / 1183.0 / 1203.4 | 9/2/0 | 119,481,440–119,481,704 |
| runaway, no-GC region | 256.6 / 224.1 / 223.1 | 0/0/0 | 119,481,440–122,107,360 |
| **return 5,000, cold** (first, second) | **1472.8** / 548.7 | 4/0/0, 2/1/0 | 64,275,624, 39,501,176 |
| **runaway, cold** (first, second) | **2918.9** / 1023.3 | 13/2/0, 9/2/0 | 177,059,408, 128,796,128 |

- **GC is the cost on Windows too.** With the gen0 budget raised, 1,000 levels take 26–27 ms and 5,000
  take 65–67 ms, no worse than linear. With ordinary GC, 5,000 take 490–640 ms.
- **A first run costs most.** It allocates more and collects more. Unoptimised first-tier code
  allocating more is a likely reason, reasoned, not checked.
- **The issue's 5.44 s Release runaway is not reproduced** at `33cbe5281`, warm or cold.

## Profile

The installed diagnostic tool was `dotnet-trace` 10.0.750501. In this version `cpu-sampling` belongs
to `collect-linux`, which requires administrative perf-event access. The managed sampling profile
is named `dotnet-sampled-thread-time`. That profile plus `gc-verbose` was used on the initial inline
`If n < target Then Up n + 1` probe, before adding leaf-output assertions and warm-up. Its top-method
report is preserved in `results/sampled-top.txt`. `RunProcedure` has 0.38% exclusive sampled time;
ANTLR context extraction has 2.64%. There is no debugger attached. Whole-process waiting accounts
for most sampled time; this is **not** a native CPU profile of GC.

Sampling deep managed stacks is itself expensive: at 5,000 calls the profiled runs took seconds.
Those timings are not included as benchmark results. A second trace used only `gc-collect` on the
final warmed probe. `results/gc-traced.csv` records those runs and `results/gc-events.csv` records
GC start time and elapsed duration; the two explicit full collections before each run are outside
its timer. For the final three 5,000-level runs:

| Run | Whole run ms | Individual GC durations ms | Sum ms |
|---|---|---|---|
| 1 | 714.6 | 104.398 / 193.857 / 305.244 | 603.499 |
| 2 | 937.8 | 146.117 / 262.620 / 340.303 | 749.040 |
| 3 | 780.4 | 118.984 / 210.980 / 297.558 | 627.522 |

Tracing changes collection scheduling (three gen0-inclusive collections here, versus two without
tracing). It corroborates where time is spent; it does not replace the untraced table.

## Reproduce

Run `bash scripts/probes/4775/run.sh`. It writes new CSV files under `artifacts/4775/` and changes no
IDE GC configuration. Columns: case, depth, repeat, milliseconds, gen0, gen1, gen2, allocated bytes,
no-GC-region active. The no-GC mode asserts that its requested region started successfully.
The opt-in diagnostic otherwise skips visibly in ordinary test runs.

For a GC-only trace, install the same diagnostic tool into `artifacts/4775/tools`, set
`HEXIDE_RECURSION_PROBE=1` and `HEXIDE_RECURSION_OUTPUT` to an output CSV, then run:

```sh
artifacts/4775/tools/dotnet-trace collect --profile gc-collect -o artifacts/4775/gc.nettrace -- \
  dotnet IDE/HexIDE.Runtime.Tests/bin/Release/net11.0/HexIDE.Runtime.Tests.dll -class '*RecursionCostProbe'
```

Use `--profile dotnet-sampled-thread-time,gc-verbose --format Speedscope` for the managed sampling
profile. The direct xunit executable takes `-class`; `dotnet test` takes `-- --filter-class`.
To print GC event durations, run `TraceSummary` with `TraceToolDirectory` set to the installed tool's
`tools/net8.0/any` directory (it references the tool's existing diagnostic assemblies, with no new
repository package dependency):

```sh
dotnet run --project scripts/probes/4775/TraceSummary -p:TraceToolDirectory=<tool-assembly-directory> -- \
  artifacts/4775/gc.nettrace
```

The measured tables and summaries live here because `artifacts/` is cleared after the run.

The lane's full runs under WSL passed: `HexIDE.Runtime.Tests` 10,846 (Debug) and 10,847 (Release),
`HexIDE.Tests` 10,442, and `HexIDE.Runtime.Integration.Tests` 4,170.
