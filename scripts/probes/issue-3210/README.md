# Parse and walk stack measurements (#3210)

Measurement-only deliverable requested by the last comment on
[issue #3210](https://github.com/hexide-io/HexIDE/issues/3210). No product guard,
threading policy, language inventory, oracle page or release fragment is changed.
The supervisor chooses the fix. All source references below refer to baseline
`04c3c52757a6198761db698bd874959122a53b4f` (use `prepare.py`'s printed full SHA for the exact checkout).

Measured 2026-11-06 on Ubuntu 26.04, WSL2 x64 (kernel
6.18.33.2-microsoft-standard-WSL2), SDK 10.0.114, runtime 10.0.14, Debug build.
These are **host implementation measurements**, not new VB6 measurements. The VB6
reference depths, 7,227 nested If blocks and 7,643 parentheses inside a Sub, come
from #3210 and [#3202](https://github.com/hexide-io/HexIDE/issues/3202).

## Reproduce

From the repository root:

```sh
export PATH="$(printf '%s' "$PATH" | tr ':' '\n' | grep -v '^/mnt/' | paste -sd:)"
python3 scripts/probes/issue-3210/prepare.py
export XDG_RUNTIME_DIR="$PWD/scripts/probes/issue-3210/.scratch/xdg"
mkdir -p "$XDG_RUNTIME_DIR"
dotnet build scripts/probes/issue-3210/StackProbe.csproj -p:UseSharedCompilation=false
GLIBC_TUNABLES=glibc.pthread.stack_cache_size=0 \
  dotnet scripts/probes/issue-3210/bin/Debug/net10.0/StackProbe.dll threads
python3 scripts/probes/issue-3210/measure.py
python3 scripts/probes/issue-3210/collect-observations.py
python3 scripts/probes/issue-3210/summarize.py
```

`prepare.py` exports **HEAD** into the ignored `.scratch/` under this directory.
It refuses to overwrite an existing scratch tree. It raises the runtime parse
guard and the two LSP visitor guards to 1,000,000, and bypasses the expression
execution-stack probe in that copy only. The parser trials directly instantiate
each generated parser without a depth listener, separately in SLL and LL. Both
use BailErrorStrategy on these syntactically valid inputs. This isolates each
prediction mode; it does not measure an SLL failure followed by LL recovery.
The source has no directives, counter lists or numbered closing lines.

Every trial is a fresh child process with core dumps disabled. Its setup thread
has a requested 256 MiB stack; it builds the tree/interpreter before the measured
walk begins. `READY` separates setup from the measured stage. The measured stage
runs on a new thread of the requested size. On Linux, `pthread_getattr_np` and
`pthread_attr_getstack` report its actual size, which the driver verifies.
The final probe also prints the actual parser prediction mode; the driver
checks it independently against the stage name. See [mode-sanity.txt](mode-sanity.txt)
for the four final shallow mode checks. `PASS` means the stage returned; execution also must print exactly one value, 1.
A failing bound requires `READY` and the CLR's `Stack overflow.` report. A timeout,
Error 28, setup crash or ordinary exception is **not** counted as an overflow.
Every reported bisection boundary is repeated on both sides.

Normal `debug` uses a real DebugController, including its timer yields. `step`
posts Step Into at each actual stop, suspending and resuming at every statement.
`debug-sync` moves the controller's private last-yield tick into the future in the
probe only: it measures an uninterrupted debug segment without timing-dependent
yields. This is a frame-cost isolation, not the live F5 schedule.

Tiered compilation is disabled (`DOTNET_TieredCompilation=0`) to avoid changing
JIT tiers between trials. The assemblies remain Debug assemblies. glibc's pthread
stack cache is disabled before starting each child. Without that, the inventory
actually observed a request for 4 MiB receive a cached 8 MiB stack; see
[thread-stacks.txt](thread-stacks.txt) and the corrected
[thread-stacks-cache-disabled.txt](thread-stacks-cache-disabled.txt).

`measure.py --sizes 1 4 16` requests the issue's example sizes. The complete fits
use smaller verified stacks to keep cold nested-block parsing affordable, with
larger-stack cross-checks reported separately. These are extrapolations to the
VB6 depths, not demonstrations that an entire IDE run reaches those depths.
Each child has a 180-second deadline. `--stages`, `--shapes`, `--sizes`, `--jobs`
and `--output` select a slice. `--bounded-depth` overrides the endpoint for a
nonrecursive stage. An incomplete or unstable bisection exits nonzero; its other
successful rows remain usable. Trial traces stay under `.scratch/trials/`;
the CSVs and this report are the durable results.

## Where recursion runs today (source audit)

| Stage | Production thread / scheduling | Linux stack here | Windows standard .NET 10 host, documented/source-derived |
|---|---|---:|---:|
| LSP diagnostics SLL and fallback LL | `VbDiagnosticsProvider.RunWithinCore` puts the complete parse in `Task.Run`; both predictions run on that same pool worker | 8 MiB | 1.5 MiB default host reserve; host/configuration can override |
| LSP declaration and symbol visitors, inlay hints, folding | `DocumentStore.Refresh` after awaiting analysis; normal async completion resumes on the pool because the scheduler installs no SynchronizationContext. If the awaited work is already complete, it can stay on the scheduler thread | pool and scheduler defaults both 8 MiB | both default to host reserve, normally 1.5 MiB |
| LSP direct synchronous provider entry points | Caller thread, no scheduling of their own; `GetSymbols(string)` also parses there | caller's stack | caller's stack |
| Option Explicit checker | Default-off (`EnableUndeclaredVariableCheck = false`). If enabled it runs with diagnostics on the parse worker; direct callers use their own thread | 8 MiB for diagnostics | normally 1.5 MiB |
| Runtime SLL, fallback LL and PrePass | Synchronous `BasicInterpreter.BuildModule`, reached through `VBProgram` construction on the F5/UI call path | UI entry thread, approximately 8 MiB | executable entry thread's reserve, normally 1.5 MiB |
| StatementExecutor and ExpressionExecutor, F5 and stepping | UI thread. Await continuations capture Avalonia's context; debugger gates and its periodic `Task.Yield()` resume there | same UI stack, with suspended portions held in Tasks | same UI stack |
| Existing fixed-stack tests | `StackSpaceTests.RunOnOwnStack` uses 4 MiB (#2953/#2955); `ExpressionDepthGuardTests` uses 1 MiB; the older #2622 standalone probe uses 1 MiB | explicit request; cache reuse can enlarge it | explicit request, subject to OS rounding |

Read paths: [diagnostics](../../../LspServer/HexIDE.VbLspServer/VbDiagnosticsProvider.cs),
[store](../../../LspServer/HexIDE.VbLspServer/DocumentStore.cs),
[scheduler wrapper](../../../LspServer/HexIDE.VbLspServer/TimingScheduler.cs),
[F5](../../../IDE/HexIDE/IDE/ProjectRunnerService.cs),
[program construction](../../../IDE/HexIDE.Runtime/VBProgram.cs),
[interpreter](../../../IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs),
[debugger](../../../IDE/HexIDE.Runtime/Debugging/DebugController.cs).
The thread attribution is a source audit, **not a live IDE trace**.

The pinned framework's
[SingleThreadScheduler](https://github.com/CppCXY/LanguageServer.Framework/blob/a1637c22b3eaba207ad58ae5892c613a6ae7fe10/LanguageServer.Framework/Server/Scheduler/SingleThreadScheduler.cs)
starts a `new Thread` without a stack-size argument and blocks on handler Tasks;
it supplies ordering, not an async continuation context.

.NET's [Thread constructor documentation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.-ctor?view=net-10.0)
describes an explicit maximum stack size and OS rounding; its legacy 1 MB remarks
are not a portable guarantee for a .NET 10 executable. .NET 10's Windows
[linker configuration](https://github.com/dotnet/runtime/blob/v10.0.0/eng/native/configurecompiler.cmake)
sets `/STACK:0x180000` for ordinary hosts. The runtime
[thread creation code](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/vm/threads.cpp)
uses the configured default or the host's default reservation. On glibc Linux,
[PAL initialization](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/pal/src/init/pal.cpp)
and [pthread creation](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/pal/src/thread/thread.cpp)
leave the system default unless overridden. Here `ulimit -s` is 8192 KiB;
the attached pthread inventory measured 8 MiB for pool/default managed threads.
Windows was not run in this Linux sandbox; its sizes above are source-derived.

Parse and pre-pass could be isolated on a sized worker returning trees/tables
before execution starts; this is a design possibility, not a verified product change. Moving execution to an arbitrary worker is **not** safe with the
current contracts: controls are accessed directly and DebugController assumes
one shared UI thread. It would require either hosting the entire Avalonia UI on
a deliberately sized thread, or an explicit dispatcher/marshalling and debugger
synchronization design. Neither is authorized by this measurement request.

## Recursion inventory beyond the named visitors

`PrePass.VisitSubStmt` constructs `ProcedureInfo`: `DeclaredConsts` and
`DeclaredLocals` recursively collect declarations, and `NoteDefaultMember` does
another recursive walk. All three skip `ValueStmtContext`, so parenthesized
expressions do not deepen these walks. Nested blocks do.

The LSP declaration collector, undeclared-variable checker and symbol visitor
recurse through generated `VisitChildren` methods, currently capped at rule
depth 500. The `scope` probe measures the public Option Explicit operation,
including its initial declaration collection, rather than attributing its total
to the checker alone. That checker is off in production diagnostics.

Folding scans lines and stores open blocks in an explicit `Stack`. Inlay hints
also use an explicit node stack; their normal text extraction slices source
ranges. Formatter, protected-region scanning, closing-line-number handling and
text helpers are scans, not recursive tree visitors. Conditional compilation has
its own bounded expression parser; the two measured source shapes contain no
conditional directives. None of these source observations measures other VB6
constructs, malformed trees, different expression operators, or procedure calls.

## Measurements

[boundaries.csv](boundaries.csv) contains 72 stable, adjacent pass/overflow
boundaries (each repeated on both sides) and 15 successful bounded endpoints.
[observations.csv](observations.csv) preserves 982 distinct verified-size case/outcome observations:
526 returned, 444 reported CLR stack overflow, and 12 hit the existing procedure
entry Error 28 check on 64/128 KiB before any nesting work. Error 28 is not a CLR
overflow and those trials contribute no boundary or fitted slope.

There are 22 fitted stage/shape pairs, each with at least three actual stack
sizes. Fit `stackBytes = intercept + bytesPerLevel * firstOverflowDepth` by ordinary
least squares; the last column is `2 * (intercept + bytesPerLevel * referenceDepth)`.
Values are approximate, despite displaying two decimal places for reproducibility.
The generated source adds a local declaration and an output check: it was never
run through VB6 and is not claimed to fit VB6's procedure-size ceiling itself.
The issue's depths are capacity reference points, not new VB6 limits inferred here.

<!-- stack-table -->

| Stage | Shape | Stack KiB: last passing depth | Fitted bytes/level | 2x model at VB6 reference (MiB) |
|---|---|---|---:|---:|
| debug | parens | 256: 434; 384: 682; 512: 930; 1024: 1923 | 528 | 7.76 |
| debug-sync | if | 256: 75; 384: 120; 512: 165 | 2913 | 40.23 |
| declarations | if | 64: 27; 128: 66; 256: 144 | 1680 | 23.20 |
| declarations | parens | 64: 135; 128: 330; 256: 720 | 336 | 4.94 |
| execute | if | 256: 75; 384: 120; 512: 165; 1024: 345 | 2913 | 40.23 |
| execute | parens | 384: 675; 512: 923; 1024: 1916 | 528 | 7.77 |
| folding | if | 64: 14454; 128: 14454; 256: 14454 (bounded) | no depth-dependent call stack in this shape | ≤0.125; 64 KiB also passed 2x depth |
| folding | parens | 64: 15286; 128: 15286; 256: 15286 (bounded) | no depth-dependent call stack in this shape | ≤0.125; 64 KiB also passed 2x depth |
| inlay | if | 64: 400; 128: 400; 256: 400 (bounded) | no depth-dependent call stack in this shape | not established at VB6 reference depth |
| inlay | parens | 64: 15286; 128: 15286; 256: 15286 (bounded) | no depth-dependent call stack in this shape | ≤0.125; 64 KiB also passed 2x depth |
| lsp-parse-ll | if | 128: 37; 256: 81; 512: 166 | 3053 | 42.11 |
| lsp-parse-ll | parens | 64: 9; 128: 28; 256: 64; 512: 137 | 3592 | 52.42 |
| lsp-parse-sll | if | 128: 35; 256: 83; 384: 130 | 2759 | 38.10 |
| lsp-parse-sll | parens | 128: 28; 256: 64; 512: 137 | 3605 | 52.61 |
| prepass | if | 64: 71; 128: 153; 256: 316 | 803 | 11.08 |
| prepass | parens | 64: 15286; 128: 15286; 256: 15286 (bounded) | no depth-dependent call stack in this shape | ≤0.125; 64 KiB also passed 2x depth |
| runtime-parse-ll | if | 64: 14; 128: 35; 256: 84 | 2785 | 38.44 |
| runtime-parse-ll | parens | 64: 10; 256: 65; 1024: 288; 4096: 1177 | 3537 | 51.62 |
| runtime-parse-sll | if | 128: 39; 256: 87; 512: 185 | 2691 | 37.13 |
| runtime-parse-sll | parens | 256: 65; 512: 139; 1024: 288 | 3525 | 51.45 |
| scope | if | 64: 26; 128: 65; 256: 143 | 1680 | 23.20 |
| scope | parens | 64: 134; 128: 329; 256: 719 | 336 | 4.94 |
| step | parens | 256: 432; 384: 681; 512: 929; 1024: 1922 | 528 | 7.76 |
| syntax | if | 64: 13; 128: 39; 256: 84 | 2785 | 38.43 |
| syntax | parens | 64: 10; 128: 28; 256: 64 | 3641 | 53.12 |
| symbols | if | 64: 26; 128: 66; 256: 144; 512: 300 | 1676 | 23.14 |
| symbols | parens | 64: 134; 128: 330; 512: 1500 | 336 | 4.94 |

The LL parser uses normal LL prediction with a bail policy on valid source; its
error recovery stack cost is not measured. The 1/4 MiB runtime-parens cross-checks
agree closely with the smaller-stack slope. All remaining projections to tens
of MiB are extrapolations. They do not prove safety at the reference depths,
on other syntax, on Windows, in Release, or with a debugger attached to the CLR.

### Timer-driven debugging and stepping

Normal F5 debugging of nested blocks has no stable overflow threshold in these
trials. With a 256 KiB stack a depth-96 run returned while a depth-80 run overflowed;
on 1 MiB a depth-472 run returned while a depth-352 run overflowed. A timer yield
can release native frames before the walk gets deeper. Bisection cannot fit a
single bytes-per-level value to that schedule. The `debug-sync` experiment holds
that periodic yield off, and measures 2,913 bytes/level: the same slope as ordinary
execution. It is an uninterrupted-segment estimate, not a live F5 ceiling.

Stepping nested If blocks did not overflow in the completed trials: depth 2,048
on 256 KiB, and 1,024 on 384 KiB, 512 KiB and 1 MiB all returned and printed 1.
Higher setup parses hit the child deadline (for example 4,096 on the 256 KiB run),
so these are lower bounds, not a measured ceiling or a fitted zero slope. The
await at each stop retains progress in Tasks instead of retaining the entire
uninterrupted native call chain. Parentheses within one statement still recurse;
the stepping-parens measurements quantify that case separately.

Some exact near-overflow trials hung after `READY` instead of emitting a CLR
stack-overflow report, including runtime parser depth 29 on 128 KiB, symbol walk
depth 728 on 256 KiB, and expression execution depth 432 on 256 KiB. A corrected
SLL If parse at depth 178 on 512 KiB also timed out. Those trials are not counted
as either passing or overflowing, and alternative sizes supplied the third point.
No reported regression test skips around an overflow or timeout.

## Round 2 — first slice, 2026-11-06

This is a **partial** round 2 deliverable: SyntaxChecker measurements and the
bundled server's reply-shape audit. Conditional compilation, ModuleLevelBounds,
the debugger/EnC recursion audit and the unmodified execution-stack checks remain
outstanding. No Windows measurements or projections were attempted. No product
behaviour changed, so there are no new regression tests or before-fix teeth.

The probe was rebuilt against the existing scratch product sources from round 1;
HEAD `65ca4abf` adds only the round 1 probe to baseline `04c3c527`.
`syntax` invokes the real `SyntaxChecker.Run`, including its default prediction
mode and error policy, on the requested measured thread. Its only bypass is the
existing scratch runtime ParseDepthGuard default of 1,000,000. Unlike the isolated
parser stages, the probe does not select SLL or LL for SyntaxChecker.
Production SyntaxChecker runs synchronously on its caller's thread; it creates
no worker or sized stack itself ([source](../../../IDE/HexIDE.Runtime/Interpreter/SyntaxChecker.cs)).

Reproduce this slice after the existing prepare/build steps:

```sh
python3 scripts/probes/issue-3210/measure.py --stages syntax \
  --sizes .0625 .125 .25 --jobs 3 --output round2-syntax.csv
```

[round2-syntax.csv](round2-syntax.csv) records six adjacent boundaries, with both
sides repeated. The 54 distinct verified-size observations added to
[observations.csv](observations.csv) comprise 22 returns and 32 CLR overflows;
repeated trials collapse to one observation. Both shape fits use three sizes.
The table above gives 2,785 bytes per If level and 3,641 per parenthesis level,
with 2x extrapolated reservations of 38.43 and 53.12 MiB respectively. These are
small-stack Debug fits, not reference-depth demonstrations. The recommendation
remains a **64 MiB prototype**, conditional on completing the missing stages;
this slice raises the largest modeled requirement from 52.61 to 53.12 MiB.

### JSON nesting: source audit, not a wire trial

Nested source cannot produce nested document symbols in the bundled server.
`VbSymbolProvider.SymbolVisitor` adds every declaration to one `List<LspSymbol>`;
`LspSymbol` has only Name, Kind and Range, with no Children member.
[DocumentSymbol](../../../LspServer/HexIDE.VbLspServer/LspRequestHandlers.cs)
(lines 85–106 at the baseline) writes that list as one JSON array of objects,
with range and selectionRange objects, and never writes a children array.
Visiting more nested blocks increases traversal depth, not reply nesting.

The other handlers in that same file write fixed-shape replies: flat arrays of
hints, foldings, completions, highlights, edits, built-ins or diagnostics;
hover/signature/definition wrappers; and the fixed workspace-edit wrapper for
rename. None recursively serializes a parse node or embeds arbitrary source JSON
in its reply. Source depth can change array lengths and strings, not the JSON
container depth. Consequently there is no source-induced level-65 reply to
measure on either side of this bundled-server seam.

The client's [DocumentSymbol.Flatten](../../../IDE/HexIDE.Core/Lsp/Messages/LspMessages.cs)
comment concerns hierarchical replies from **other language servers** and trees
constructed in memory. Its JSON depth limit does not establish a nesting limit
for this server's source. This audit does not measure a foreign server or the
client's response to a synthetic over-depth reply.

## Round 3 — 2026-11-06

This round measures the remaining conditional-compilation, bounds-validation and
execution checks, and audits debugger/EnC lookups. Product sources are unchanged.
Start at branch HEAD `a7dc51e0` (same product baseline `04c3c527`). Use
`python3 scripts/probes/issue-3210/prepare.py --round3` in a fresh scratch tree,
then the existing build command. Unlike the default round 1 profile, this profile
**keeps ExpressionExecutor's execution-stack check**. It also keeps both original
conditional-expression checks at 300 while lifting the ordinary module parser's
default guard. The conditional server guard remains 64. ModuleLevelBounds is
entirely unmodified. Reusing a default-profile build invalidates the guard trials.

### Added measurements

The table below uses the same reference depths and OLS model as the earlier
table. `-guard` rows measure a **safe Error 28**, with both execution-stack checks
unmodified; their fitted intercept includes reserved CLR headroom. Their CSV's
historical `overflow_depth` column means **first Error 28**, not CLR overflow.
The driver rejects a fatal overflow for these rows. Other rows deliberately run
isolated unchecked stages to find CLR-overflow boundaries. Every boundary has
adjacent endpoints repeated. No timeout is a failing bound.

<!-- round3-stack-table -->

| Stage | Shape | Stack KiB: last passing depth | Boundary | Bytes/level | 2x model at reference (MiB) |
|---|---|---|---|---:|---:|
| bounds-guard | parens | 256: 172; 512: 545; 1024: 1289 | Error 28 | 704 | 10.53 |
| bounds-procedure | parens | 64: 158; 80: 204; 96: 251 | CLR overflow | 352 | 5.15 |
| cc-lsp-eval | parens | 64: 29; 128: 80; 256: 183 | CLR overflow | 1276 | 18.65 |
| cc-lsp-parse | parens | 64: 11; 128: 29; 256: 65 | CLR overflow | 3641 | 53.12 |
| cc-runtime-eval | parens | 128: 29; 256: 90; 384: 152 | CLR overflow | 2131 | 31.20 |
| cc-runtime-parse | parens | 64: 11; 128: 29; 256: 66 | CLR overflow | 3570 | 52.09 |
| execute-guard | if | 1024: 306; 2048: 666; 8192: 2827 | Error 28 | 2911 | 40.43 |
| execute-guard | parens | 1024: 1593; 2048: 3466; 8192: 14700 | Error 28 | 560 | 8.46 |

<!-- round3-stack-table-end -->

- [round3-execution.csv](round3-execution.csv),
  [round3-endpoints.csv](round3-endpoints.csv): execution with the guards intact.
- [round3-conditional-parse.csv](round3-conditional-parse.csv),
  [round3-conditional-eval.csv](round3-conditional-eval.csv): the default-mode
  directive-expression parsers (BailErrorStrategy) without their listeners, and
  the actual evaluators invoked on trees prepared on the setup stack. Evaluator
  trials omit parsing and the finished-tree test, which are separate stages.
- [round3-conditional-blocks.csv](round3-conditional-blocks.csv): complete
  preprocessing of 14,454 nested `#If True Then` blocks, retaining the original
  expression guards. Runtime: 128, 256, 512 KiB; server: 64, 128, 256 KiB.
  All six returned with the directives removed.
  The scanner uses `Stack<Branch>` and a physical-line loop in
  [ConditionalCompilation.Preprocess](../../../IDE/HexIDE.Core/Language/ConditionalCompilation.cs),
  lines 124–251. Block nesting adds heap entries, not recursive native frames;
  no block-depth limit was reached. This is a host endpoint, **not** a new VB6
  limit or a claim that VB6 accepts that many conditional directives.
- [round3-bounds.csv](round3-bounds.csv),
  [round3-bounds-procedure.csv](round3-bounds-procedure.csv): the private async
  `ModuleLevelBounds.Walk` on a parenthesized literal, with its physical-stack
  probe intact, and public `CheckProcedure` on the same expression, with its
  depth-500 check intact. Reflection only accesses these actual methods; it
  does not replace their recursion. The former awaits the returned Task on the
  single-thread pump. The latter's smaller-stack overflows occur before 500.
  Their enclosing const/bounds declaration, expression evaluation, and name
  resolution are not part of these isolated measurements.

The new parser/evaluator and bounds slopes do not exceed the earlier largest
model, approximately 53.12 MiB with a 2x margin. That does **not** establish that
64 MiB is safe for arbitrary input, recovery, Release, or Windows.

### Conditional compilation: actual limits and visible refusals

[conditional-limits.py](conditional-limits.py) tests complete preprocessing with
original limits, on a verified 8 MiB thread. Both adjacent endpoints repeat.
[round3-conditional-limits.csv](round3-conditional-limits.csv) records the messages.

| Consumer | Expression in | Last recognized parenthesis pairs | First refused | Observable result at refusal |
|---|---|---:|---:|---|
| Interpreter | `#If … Then` | 297 | 298 | Compile error: `An # ElseIf, # Else, or # EndIf must be preceded by an # If clause`, at the `#End If` (line 3) |
| Interpreter | `#Const C = …` | 297 | 298 | Directive stays in the text; SyntaxChecker reports `mismatched input '#Const' expecting <EOF> in line 1:0` |
| Server | `#If … Then` | 61 | 62 | Same missing-`#If` directive error; Applied.Error causes the directive diagnostic to be published |
| Server | `#Const C = …` | 61 | 62 | Directive stays in the text; GetDiagnostics reports `Syntax error: unexpected '#Const'` |

The runtime finished-tree check is independently relevant.
[round3-conditional-tree.txt](round3-conditional-tree.txt) inspects 297, 298 and
299 pairs in that order: tree depths 300, 301 and 302. At 298 the **parser guard
passes**, but `ConstantExpression.TryParse` returns null because of the iterative
finished-tree check. At 299 the parser guard also refuses. A null expression is
not a recognized directive; that explains both visible outcomes above. The
server's parser listener at 64 refuses earlier; its catch also returns null.
The finished-tree scan itself uses an explicit stack, so its own call stack does
not grow per parenthesis. The evaluators do recurse, as the table measures.

### Debugger and Edit-and-Continue: source audit

These lookups do not recursively descend a source tree for either shape. They
run synchronously on the shared UI thread; no worker or sized stack is created.
There is no additional per-nesting native frame cost to fit for these lookups.
This is a code audit, not a live stepping or UI verification.

| Operation | Deciding code | Why source nesting does not deepen the lookup |
|---|---|---|
| Statement location / current arrow | `StatementExecutor.VisitBlock` (469), `ExecuteProcedureBody` (5059), `ToCallStackFrame` (314), `Locate` (5254) | Read `stmt.Start.Line` or cached `currentLine`; an error's first location is stored on its Data. No search of descendants |
| Breakpoint storage and matching | `BreakpointService.Toggle`, `GetBreakpoints`; `DebugController.SetBreakpoints`, `MatchesBreakpoint` (304) | Store line sets and compare module/line as the execution walk reaches a gate; no executable-line tree resolution |
| Run to cursor | `ProjectRunnerService.RunToCursorProject` (589); `DebugController.RunToCursor` (124), `MatchesRunTo` (307) | Store one module/line target and compare it at the same gate |
| Set Next Statement | `DebugController.SetNextStatement` (396); `IDebugFrame.SetNextStatement` in StatementExecutor (322) | Scan `_bodyStmts` top-level array. Nested source/targets are refused, not searched |
| Edit-and-Continue | `CodeEditorView.StartResetPrompt`, `ShowResetPromptAsync` (around 1729 onward) | Reset-or-revert editing affordance; does not patch a suspended tree or map its statements. A new run uses the already measured parse/pre-pass paths |

Sources: [StatementExecutor](../../../IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs),
[DebugController](../../../IDE/HexIDE.Runtime/Debugging/DebugController.cs),
[BreakpointService](../../../IDE/HexIDE/Debugging/BreakpointService.cs),
[ProjectRunnerService](../../../IDE/HexIDE/IDE/ProjectRunnerService.cs),
[CodeEditorView](../../../IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs).
ErrorLocation's recursion follows `InnerException`, not source nesting, and the
normal statement loops note location without creating a wrapper per block.
This audit does not claim that all expression evaluation reachable from a watch
or Immediate command is iterative; that uses the recursive evaluators above.

### ModuleLevelBounds: scope and separate limit

[ModuleLevelBounds](../../../IDE/HexIDE.Runtime/Interpreter/ModuleLevelBounds.cs)
does recurse over **expressions**, not enclosing statement blocks. `Check` and
`CheckConst` enter its async `Walk` only for module-level array bounds / constants;
`CheckProcedure` enters only for local constant initializers / local bounds.
The call sites are `StatementExecutor` 676, 4172, 4364–4367. Neither original
`v = ((1))` nor nested-If assignment fixture invokes these checkers. Nesting an
If around a declaration does not add frames inside the checker; declaration
collection over blocks was measured separately in round 1.

For expressions they are additional stages that a general fix must accommodate.
The table measures their parenthesis cost. `CheckProcedure` also refuses when
its `depth > 500`, independently of available stack. On a verified 1 MiB thread,
498 parenthesis pairs pass and 499 produce `NotImplementedException: Procedure
constant expression is too deeply nested`, twice each; see
[round3-bounds-limit.csv](round3-bounds-limit.csv). That is the isolated expression
entry's limit; extra grammar wrappers for array bounds can change the source
pair count. A larger thread alone does not lift this guard.

### Execution checks and the parse-only-worker question

Both `BasicInterpreter.RunProcedure` and `ExpressionExecutor.Visit` retain their
original `TryEnsureSufficientExecutionStack` calls. The source has one Sub entry,
no procedure recursion. RunProcedure checks entry once; the depth-dependent
refusals recorded here originate in ExpressionExecutor.Visit (its first stack
frame is printed). A condition is evaluated at every nested If, so the expression
probe also protects growing statement execution. The debugger is detached for
these uninterrupted trials; ordinary timer-yielding F5 still has round 1's
schedule-dependent stack release, and single-step still suspends at gates.

On a verified **8 MiB** thread, **2,827 nested If blocks pass; 2,828 raise 28**,
and **14,700 parenthesis pairs pass; 14,701 raise 28**. Both sides repeat. The If
boundary uses four selected-endpoint children because searching depth 4,096 hit
the 180-second child deadline. It is not inferred from that timeout.
The 8 MiB If endpoints took 127–154 seconds each including setup on this host;
those elapsed times are not parse-only or execution-only timings.

Using the **Linux** three-size fits in round3-models.csv, a hypothetical 1.5 MiB
stack crosses the Error 28 model near **487 If blocks** and **2,530 parenthesis
pairs**. This is arithmetic using Linux Debug frames and CLR headroom, **not a
Windows measurement or a Windows limit**. No Windows process was run. Windows
frame costs, JIT code and headroom still require the Windows session.

Thus a parse-only sized worker leaves a distinct execution limit: even on this
8 MiB thread, uninterrupted block execution raises 28 below the measured VB6
reference of 7,227. Parentheses reach past the reference on this stack, but the
hypothetical smaller-stack projection does not. Timer-driven debugging can
release native frames; do not read these uninterrupted boundaries as stable
interactive-debug ceilings.

Reproduce the added stages after preparing with `--round3` and building:

```sh
python3 scripts/probes/issue-3210/measure.py --stages execute-guard --sizes 1 2 8 --jobs 3 --output round3-execution.csv
# The search above times out for If/8 at depth 4096; measure its adjacent boundary separately.
python3 scripts/probes/issue-3210/endpoints.py --depths 2827 2828
python3 scripts/probes/issue-3210/conditional-limits.py
python3 scripts/probes/issue-3210/measure.py --stages cc-runtime-parse cc-lsp-parse --shapes parens --sizes .0625 .125 .25 --output round3-conditional-parse.csv
python3 scripts/probes/issue-3210/measure.py --stages cc-runtime-eval --shapes parens --sizes .125 .25 .375 --output runtime-eval.csv
python3 scripts/probes/issue-3210/measure.py --stages cc-lsp-eval --shapes parens --sizes .0625 .125 .25 --output server-eval.csv
# Combine these two evaluator CSVs, keeping one header, as round3-conditional-eval.csv.
python3 scripts/probes/issue-3210/measure.py --stages cc-runtime --shapes if --sizes .125 .25 .5 --bounded-depth 14454 --output runtime-blocks.csv
python3 scripts/probes/issue-3210/measure.py --stages cc-lsp --shapes if --sizes .0625 .125 .25 --bounded-depth 14454 --output server-blocks.csv
# Combine these two scanner CSVs as round3-conditional-blocks.csv.
python3 scripts/probes/issue-3210/measure.py --stages bounds-guard --shapes parens --sizes .25 .5 1 --output round3-bounds.csv
python3 scripts/probes/issue-3210/measure.py --stages bounds-procedure --shapes parens --sizes .0625 .078125 .09375 --output round3-bounds-procedure.csv
python3 scripts/probes/issue-3210/endpoints.py --stage bounds-procedure --shape parens --depths 498 499 --stack-mib 1 --jobs 2 --output round3-bounds-limit.csv
python3 scripts/probes/issue-3210/summarize-round3.py
```

The `cc-runtime-inspect parens <depth> 8388608` child entry produces the finished-tree
inspection (depths 297, 298, 299). Bisections initially attempted a runtime evaluator
on 64 KiB and runtime preprocessing on 64 KiB; those overflowed even at depth zero
/ at the fixed shallow-expression work. They supply no per-level fit and no
block-depth ceiling. Larger sizes supply all three usable points instead.
These exclusions and the depth-4096 If timeout are deliberate, visible probe
failures; no probe was skipped. `round3-observations.csv` preserves the new stage
outcomes without changing the earlier rounds' datasets. `round3-endpoints.csv`
and `round3-bounds-limit.csv` additionally retain repeat identity.

## Recommendation for the supervisor

**Prototype direction 1: bounded larger stacks, starting at 64 MiB.** The largest
completed modeled requirement with a 2x stack margin is approximately
53.12 MiB, for SyntaxChecker and server directive-expression parsing. Runtime parsing is about 51.6 MiB, uninterrupted block execution about
40.2 MiB, LSP declaration/symbol walks about 23.2 MiB, and block pre-pass about
11.1 MiB. No measured stage's slope alone makes a larger x64 stack impractical
or forces an iterative rewrite. This is a prototype recommendation, not a chosen
fix or a claim that a 64 MiB stack is proven sufficient.

Keep a physical depth bound appropriate to the sized stack and preserve #1930's
process-survival guarantee. Keep the LSP wall-clock pause outcome independent of
stack capacity; accepting deeper syntax does not establish that live analysis
finishes within its budget. Account for outstanding parse workers: abandoning a
Task does not cancel its parse, so larger per-worker stacks need a bounded worker
policy. Neither total memory use nor throughput was measured here.

Round 3 also requires accounting for the independent conditional-expression
limits (runtime finished-tree 300, server parser 64) and CheckProcedure depth 500.
Sizing only the ordinary parser worker leaves those limits in place. It also
leaves execution on the UI stack, where the intact physical probes raise Error 28
as described above. These are additional conditions on a prototype, not
authorized changes to the product guards.

The runtime cannot simply switch execution onto a worker while leaving the UI
and debugger contracts as they are. Review deliberately sizing the UI thread
itself, or a dispatcher/marshalling design, before implementing direction 1 for
execution. Direction 2 remains the alternative if those constraints or Windows
measurements make sized stacks unacceptable. Direction 3 would still refuse the
measured-valid parenthesized programs and does not address the whole issue.

## Remaining measurements and decisions

- Windows frame costs and actual IDE thread stack inventory; the Windows thread
  defaults above are source-derived only. Also measure Release before making any
  shipping limit depend on these Debug costs.
- Full reference-depth and adversarial-input runs on the proposed sized stacks,
  including stack-space headroom, UI dispatcher ownership and debugger resumes.
  No product limit should be raised from the extrapolation alone.
- A stable maximum for normal timer-yielding F5 block execution cannot be obtained
  by this bisection. The controlled uninterrupted segment and the observed
  non-monotonic results are the available measurements.
- Reference-depth stepping of blocks and inlay hints over nested blocks; only
  the explicit bounds above were completed. The inlay traversal is iterative by
  inspection, but its full-depth input was not validated in this run.
- The Option Explicit checker alone was not isolated from its declaration pass.
  `scope` measures the public combined operation; the live check remains off.
- Heap use, parse latency at the reference depths, cancellation/worker bounds,
  and whether to size the UI thread are decisions/measurements for a prototype.

## Round 1 validation

- Probe build: succeeded, zero warnings and errors. Shared compilation was disabled
  after a compiler-server write received a read-only-filesystem error in the sandbox.
- Four shallow parser-mode sanity checks passed, with actual SLL/LL values printed
  and checked independently. Initially an ambiguous suffix test selected LL for
  both names; every SLL-labelled result from that version was discarded and rerun.
- `cd IDE && dotnet test HexIDE.Tests/ -- --filter-class "*MissingLanguageDocTests"`:
  **18 passed, 0 failed, 0 skipped**, after routing NuGet's writable caches into the
  clone and using the existing package cache as a fallback. A direct xunit run
  also passed all 18. MISSING_LANGUAGE counts and rows did not move.
- Full runtime suite: **no completed result**. The required `dotnet test` invocation
  was attempted in full; bounded retries, including a final run after stopping
  the larger probes, timed out after 600 seconds without a summary. A direct
  full xunit fallback also timed out after 600 seconds; its long-running report
  named existing grammar/corpus and runtime tests. No full-suite pass is claimed.
- Four skips were observed in that incomplete direct run, providing no coverage:
  `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`,
  `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`,
  `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`,
  and `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`.
  All require the absent VB6 template tree. The incomplete run cannot establish
  the total number of skips.
- Regression-test teeth: **0 new product tests**, because no product behaviour
  changes in this measurement-only deliverable. The 412 distinct CLR-overflow
  observations exercise the probe's child-process failure path; they are host
  measurements, not before/after regression coverage.
- Tree hygiene: passed with nonblocking warnings; identity scan was unavailable
  because its optional pattern was unset. No changes outside this directory are
  intended for the supervisor's WIP commit. No changelog, oracle or language
  inventory edit is appropriate to the latest comment's directory-only scope.

## Round 2 validation

- Probe build succeeded with zero warnings/errors. Six adjacent boundaries passed
  the driver's repeated endpoint checks (66 child trials, 54 distinct cases).
  The observations contain each reported endpoint and no duplicate rows.
- `python3 -m py_compile scripts/probes/issue-3210/measure.py` and
  `git diff --check` passed.
- MissingLanguageDocTests: **18 passed, 0 failed, 0 skipped**, both directly with
  xUnit's `-class '*MissingLanguageDocTests'` and with
  `cd IDE && dotnet test HexIDE.Tests/ --no-restore --no-build -- --filter-class '*MissingLanguageDocTests'`.
  The first normal invocation failed on the read-only external NuGet cache;
  a retry with writable caches inside the clone restored successfully but reached
  its 300-second timeout during the build. MISSING_LANGUAGE rows/counts did not move.
- Full `cd IDE && dotnet test HexIDE.Runtime.Tests/` was attempted with the
  Windows PATH entries removed. It reached the test runner, then timed out after
  600 seconds (exit 124) without a summary or individual test results. No runtime
  suite pass, failure count or skip count can be established from that run.
- Tree hygiene passed: nine existing warnings; its optional identity scan skipped
  because `HEXIDE_IDENTITY_PATTERN` is unset. No measurement probe skipped.
- Changes are confined to this probe directory and left uncommitted for the
  supervisor. No product code, oracle, language inventory or changelog changed,
  as requested by the measurement-only brief.

## Round 3 validation

- All four requested items are covered: original execution checks; conditional
  compilation limits and frame costs; debugger/EnC lookup audit; and bounds
  validation costs / depth guard. No Windows measurements were attempted.
- Final probe build: zero warnings, zero errors. Python syntax compilation and
  `git diff --check` passed. All eight new models use three verified stack sizes:
  24 repeated adjacent boundaries, plus four directive boundaries, one repeated
  procedure-depth refusal and six successful scanner endpoints. The separate
  finished-tree inspection distinguishes the two runtime directive guards.
- `round3-observations.csv` holds 320 distinct new-stage observations: 175 pass,
  53 Error 28, 70 CLR overflow, 22 directive refusal. It excludes repeat identities
  and the specially named endpoint traces; those eight trials are preserved in
  `round3-endpoints.csv` and `round3-bounds-limit.csv`. Fatal overflow trials are
  measurements in disposable children, not test failures hidden as passes.
- Scratch-source comparison confirmed BasicInterpreter and ModuleLevelBounds
  equal the product sources, and ExpressionExecutor equal after removing the
  scratch-only warning pragma left from round 1. Both execution probes are intact.
- Full `cd IDE && dotnet test HexIDE.Runtime.Tests/`: attempted with Windows PATH
  entries removed and writable in-clone NuGet/CLI caches. Build succeeded and the
  runner started, then the 600-second deadline expired (exit 124) without a test
  summary. No pass/fail/skip count can be established from this incomplete run.
- MissingLanguageDocTests: **18 passed, 0 failed, 0 skipped**, using
  `dotnet test HexIDE.Tests/ --no-build --no-restore -- --filter-class '*MissingLanguageDocTests'`.
  The normal build-and-test invocation restored and built several dependencies
  but timed out after 300 seconds without reaching its runner. The completed
  invocation used the existing test binary; no test or product source changed.
  MISSING_LANGUAGE rows and counts remain unchanged.
- No measurement probe skipped. No new product regression tests or changed
  behaviour: **before/after teeth count 0**. The optional hygiene identity scan
  skipped because its pattern is unset; that is not runtime coverage.
- Tree hygiene passed with nine existing nonblocking warnings. No changelog,
  oracle, language inventory, spec or product file changed, following the latest
  comment's measurement-only directory scope. Changes are left uncommitted.
