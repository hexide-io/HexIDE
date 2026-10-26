# Operator-chain and continuation depth probes (#1223)

Measured on 2026-10-25 in WSL, Ubuntu 26.04.1, Linux x64, .NET SDK 10.0.113,
with an 8192 KiB shell stack limit and Debug builds. This records HexIDE behaviour,
not VB6 acceptance, numeric results, or a portable safe nesting limit. No production
behaviour changed. The issue's later comment narrows the original probe request:
parentheses and nested blocks already have measured parser guards; this pass probes
operator chains, continuations and other walks.

**Runtime follow-up (#1893):** the table below records the original failures. Expression execution now
checks available host stack space and raises trappable Error 28; pre-pass walks skip expression subtrees.
`ExpressionDepthGuardTests` covers plain and continued 10,000-term chains and resumption afterwards.
The module-level Const regression also runs on an explicit 1 MiB worker stack: before the second rework,
it terminated the host with `Stack overflow.` in `ModuleLevelBounds.Walk` (WSL, 2026-10-25); setting
`DOTNET_DefaultStackSize=100000` alone did not reproduce that failure in this test host. The validation
walk now probes available stack space before recursing and raises Error 28. All nine
`ExpressionDepthGuardTests` cases pass with `DOTNET_DefaultStackSize=100000`; the IDE-side
`MainViewViewModelTests` F5 cases cover both a directive and a module-level Const. The explicit-worker
Const regression is the one demonstrated to terminate the pre-fix host in this rework.
The original LSP observations are separate from this runtime fix. VB6 acceptance of these sources remains
unmeasured.

## Reproduce

Build and run [ParseDepthProbe](../tools/ParseDepthProbe/README.md). Each case has its
own child process because stack overflows terminate .NET. The runner supplies a
30-second process timeout, 10 seconds per wire reply, disables core dumps, and writes
phase markers and stderr under `artifacts/parse-depth/`. It also emits the exact
modules for the supervisor's IDE check. Generated files and crash logs are not source.

Each module is `Sub Main`, declaring `a As Long`, assigning `a = 1&`, declaring `x`,
and assigning an expression to `x`. For N = 100, 1,000, 10,000 and 100,000:

- **Chain:** N copies of `a+`, then `a` (N additions, N+1 operands).
- **Continuations:** N copies of space + underscore + LF, then `a`.
- **Continued chain:** N copies of `a+` + space + underscore + LF, then `a`.

The source lengths are respectively 2N+52, 3N+52 and 5N+52 characters. A short
identifier keeps the largest plain chain below the LSP's 400,000-character gate.
The combination deliberately tests how that gate interacts with continuation text.

## Results

**OK** means the selected operation completed; it does not assert the computed value.
**SO** means stderr recorded `Stack overflow.` (direct child exit -6/SIGABRT).
**Size** means the source-size gate paused analysis and returned no tree, so the
visitor was **not exercised**. The wire crash produced EOF and a stack-overflow trace
before diagnostics. There were no timeouts in this final 72-case matrix.

| Shape | N | Runtime pre-pass + execution | LSP diagnostics + folding | Symbols | Declared types | Option Explicit walk | didOpen + documentSymbol |
|---|---:|---|---|---|---|---|---|
| Chain | 100 | OK | OK | OK | OK | OK | OK |
| Chain | 1,000 | OK | OK | OK | OK | OK | OK |
| Chain | 10,000 | SO | OK | OK | OK | OK | OK |
| Chain | 100,000 | SO | OK | SO | SO | SO | SO |
| Continuations | 100 | OK | OK | OK | OK | OK | OK |
| Continuations | 1,000 | OK | OK | OK | OK | OK | OK |
| Continuations | 10,000 | OK | OK | OK | OK | OK | OK |
| Continuations | 100,000 | OK | OK | OK | OK | OK | OK |
| Continued chain | 100 | OK | OK | OK | OK | OK | OK |
| Continued chain | 1,000 | OK | OK | OK | OK | OK | OK |
| Continued chain | 10,000 | SO | OK | OK | OK | OK | OK |
| Continued chain | 100,000 | SO | Size | Size | Size | Size | Size |

The direct LSP walks parse independently in separate processes. Direct diagnostics
returned zero diagnostics and a tree except in the oversized combined case, whose
single diagnostic was `File too large for live analysis; diagnostics paused.`
The Option Explicit host explicitly calls `GetOptionExplicitDiagnostics`; that check
is normally disabled in live diagnostics. The folding provider is a line scan, not
a recursive visitor. The wire host initializes the real stdio server, sends
`textDocument/didOpen`, waits for published diagnostics, then requests document
symbols to establish that the server can still answer. It shuts down each server
and closes stdin after exit to release the framework's input reader.

## Failure paths

- **Interpreter:** all sampled constructors/parses/pre-passes completed. Execution
  overflowed in `ExpressionExecutor.VisitVsAdd` / `EvaluateValue` / `EvaluateScalar`
  at both 10,000 and 100,000 additions, with or without continuations. The first
  failing sampled size is 10,000, not a measurement of the exact threshold.
- **LSP symbols:** `SymbolVisitor` overflowed through generated `VisitVsAddSub` →
  `AbstractParseTreeVisitor.VisitChildren` → context `Accept`. The real didOpen
  process had the same symbol-visitor stack before publishing diagnostics at
  100,000 additions. The first failing sampled size is 100,000.
- **LSP declared types and Option Explicit:** each separately overflowed through the
  same generated addition visitor / `VisitChildren` / `Accept` recursion at 100,000.
  Success at 10,000 bounds only these samples on this stack.

The existing guards count active parser rule entries, not the depth of the finished
left-associated tree. The symbol visitor's overridden `Visit` is not a bound on
this traversal either: generated visitors call `VisitChildren`, which calls the
child's `Accept` directly. The overflow traces repeatedly show that path. A parser
budget cannot catch an overflowing later walk. These are measured hardening gaps;
choosing and implementing a mitigation is separate from this measurement issue.

## Limits and supervisor check

No real VB6 run was made, so no VB6 syntax refusal, nesting threshold or numeric
expectation is inferred. Both grammars remain unchanged. No language support status
or MISSING_LANGUAGE count changes; its addition and continuation Detail notes link
here. No new regression tests or fail-before teeth are claimed: this chore adds a
reproducer and measurements and leaves production behaviour unchanged.

The live IDE opening path (editor/procedure list, debugger and any other UI walks)
was not exercised in this WSL session. The supervisor should open **each generated
module in its own disposable IDE process**, observe whether opening, diagnostics and
the procedure list complete, then run F5 separately with startup `Sub Main`. Retain
crash logs and distinguish an LSP subprocess exit from an IDE exit. In particular,
check opening the 100,000 plain chain and running the 10,000 plain/continued chains;
headless successes do not certify the other IDE walks. Exact failure thresholds,
other operator families, other recursive walks and CRLF variants remain unmeasured.
The external parser named in the issue is not present in this repository and was not probed. Parenthesis/block guard
coverage remains with `ParserDepthGuardTests` and `ParseHardeningTests`, as the issue's
comment directs.

## LSP visitor hardening (#1894)

The symbol, declaration collector and Option Explicit visitors now bound `VisitChildren`
recursion at 500 levels. Regression tests cover 100,000 additions, declarations and symbols
following the chain, and an undeclared reference in a later statement. The original measurements
above describe the pre-fix build; the interpreter execution overflow remains outstanding.
