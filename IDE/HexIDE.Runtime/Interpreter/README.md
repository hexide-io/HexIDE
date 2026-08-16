# HexIDE VB6 Interpreter

A small, embedded, tree-walking interpreter for classic Visual Basic 6 / VBA. It parses VB6 to a concrete
syntax tree and **executes it** — the same engine backs F5 in the IDE, the MCP `run_project` automation, and
every unit test. It exists so you can paste some VB6 and watch it *run*, right now, cross-platform.

It is **not**, and by design never will be, a serious VB6 runtime. Read the next section before contributing.

---

## Status: approximation only — permanently

This is the single most important thing to understand about this folder.

**The interpreter is an *approximation*, by permanent design.** Its purpose is **out-of-box tyre-kicking** —
run a snippet, drive a demo, sanity-check behaviour — **not** serious fidelity migrations or shipping a program's
runtime. Full VB6/VBA fidelity is the job of a real language engine, reached over a replaceable out-of-process
backend, not baked in here.

This is a **ceiling, not a way-station.** The interpreter is never meant to reach parity, and that constraint
**survives going public and taking PRs**. Scope-creep toward "a real runtime" — exhaustive quirk coverage,
COM/`CreateObject`, reference-count-perfect object destruction, a bytecode VM, *any* static analysis — is out of
scope **on principle**, regardless of how good the code is.

When triaging a change, the question is:

> **"Does this keep it a simple out-of-box demonstrator?"** — *not* "is this more correct?"

A PR that makes the interpreter more accurate but pushes it toward being a compiler frontend is a **decline**,
not a merge. That is not a judgement on the work; it is a category boundary. The demonstrator and the real engine
are different things, and keeping them different is *why* this project can stay a lean, replaceable shell instead
of a half-built compiler nobody finishes.

## Two hard limits

### 1. CST, not AST — no static / semantic analysis, ever

HexIDE's in-process language tooling operates at the **syntactic (CST) level only**. This interpreter demonstrates
understanding of VB6 by **executing** it — it walks the parse tree directly. Its runtime scope table and runtime
type semantics are **execution machinery**, which is in bounds.

It does **not** build a semantic (bound) AST or perform static semantic analysis: no cross-file/whole-program
name binding, no type inference, no semantic diagnostics beyond syntax, no rename / find-all-references /
workspace-symbol resolution, no code-fix or refactoring engine. Producing and analysing a bound AST is the
exclusive job of a real language engine, delivered over the LSP/backend seam. If a task needs a bound AST or a
semantic model, **it does not belong in this folder.** (See the "Language-Analysis Boundary" section of the
repo-root `CLAUDE.md`.)

### 2. No pre-execution analysis

Everything here happens **at execution time**. There is no compile pass, no ahead-of-time checking. Where real
VB6's *compiler* would reject a program before it runs — an ambiguous name, a wrong-module qualifier, a type
mismatch — this interpreter either resolves permissively or raises the equivalent error **at the point of
execution**. The one-line contract, for users:

> *The interpreter does not attempt pre-execution analysis. If you need that, use a more capable runtime.*

This is why, for example, cross-module name ambiguity surfaces as a runtime error rather than a compile error —
a deliberate, documented divergence, not a bug.

## Fidelity means runtime-execution fidelity — verified against `vb6.exe`

Within its scope, correctness means **running VB6 the way real VB6 runs it**, not matching a spec. Every numeric
rule, coercion, overflow code, rounding mode, `Format` edge, and intrinsic result is pinned against the **real
`vb6.exe`** — the fidelity oracle — before a test expectation is committed. Documentation and memory are
repeatedly wrong here; the oracle has overturned many "obvious" assumptions. Verified facts (and the reusable
`On Error Resume Next` `/make` harness) live in `docs/vb6-fidelity-oracle.md`.

**Reproducing VB6's *intended* behaviour, not its bugs:** where VB6 had a genuine defect, this interpreter does
the right thing. Where VB6 has a *quirk that programs depend on* (banker's rounding, `For Each` column-major
traversal, `DateDiff` boundary counting), the quirk is reproduced faithfully.

## Walls are a feature, not a backlog

Every construct this interpreter does **not** implement is documented as a **wall** — a marker of exactly where
"runnable demonstrator" ends and "use a real engine" begins. The deferred lists in
`openspec/specs/interpreter-advanced/spec.md` are a **boundary map**, not a to-do list. Hitting a wall is a
positive outcome: it tells a user precisely when to reach for a full language engine behind the replaceable
backend seam. Contributions that document a wall crisply are as welcome as ones that move it.

The **complete, categorised gap list** (Missed / Deferred / Walled / Partial) lives in
[`docs/interpreter-gaps.md`](../../../docs/interpreter-gaps.md) — the output of a multi-agent audit, and the
documented home for every gap so the "every wall is documented" rule stays discharged.

## Architecture

A recursive tree-walk — no bytecode, no instruction pointer.

| Piece | Role |
|-------|------|
| `BasicInterpreter` | Entry point. Parses each module, runs `PrePass`, owns the module registry + program-global `Err`/`Debug`, drives execution. One unified engine — F5, MCP `run_project`, and the tests all construct this class. |
| `PrePass` | Hoists declarations before execution: procedures (with visibility), module-level `Dim`/`Const`, `Type`/`Enum` definitions. |
| `StatementExecutor` | Walks statements. Returns a `ControlFlow` (Exit/Resume/…). Per-activation error mode and `With` stack live here (not ambient), so async re-entrancy is safe. |
| `ExpressionExecutor` | Evaluates expressions to a `Vb6Value`. Shared implicit-call resolver (variables, qualified names, member/field access). |
| `Vb6Value` | The value model — a readonly struct: `ValueType` + `object?`. Byte/Integer(16-bit)/Long/Single/Double/Currency/Decimal/Date/String/Boolean/Null/Empty/Nothing/Object/UDT/… |
| `ModuleInfo` / `ModuleRegistry` | Project-wide module registry (Standard singletons; Class templates). Faithful VB6 name-resolution precedence at runtime. |
| `VB6BuiltIns.*` | ~80 intrinsics grouped by area (Strings/Math/Conversion/DateTime/Format/Inspection/Array), consulted **last** so a user procedure of the same name shadows them. |
| `VbUdt` / `VbErr` | User-defined-type instances (value semantics) and the `Err` object. |

Grammar: `Grammar/VB6.g4` (proleap lineage), generated by `Antlr4BuildTasks` at build time.

## What runs today

Interpreter-**core** is complete: user `Sub`/`Function` (params, `ByRef`/`ByVal`/`Optional`, recursion), the full
value model + VB6 arithmetic/coercion/overflow rules, ~80 intrinsics including the complete `Format` engine, the
control-flow and statement set, and error handling (`On Error Resume Next` / `On Error GoTo` / `Resume` / `Err`).

Interpreter-**advanced** is in progress (`openspec/specs/interpreter-advanced/spec.md`): the multi-module registry
and user-defined `Type`s + `Enum`s have landed; the class object model, `Property`, events, file I/O, and the
`$`-typed intrinsic twins are on the roadmap or past the wall. See [`interpreter-gaps.md`](../../../docs/interpreter-gaps.md).

Everything landed is covered by ~640 runtime tests (all `vb6.exe`-verified) plus headless integration tests.

## Running & testing

```sh
# All interpreter tests (from repo root)
cd IDE && dotnet test HexIDE.Runtime.Tests/

# A single test
cd IDE && dotnet test HexIDE.Runtime.Tests/ --filter "FullyQualifiedName~UdtTests.CopyOnAssign_IsIndependent"
```

Runtime tests inherit `BaseVBTestFixture`: call `await Run("<VB6 code>")` (or `RunModules(...)` for multi-module),
then assert with `AssertDebugLog([...])` against captured `Debug.Print` output. Type suffixes distinguish
subtypes: `42!` = Single, `42#` = Double, `42&` = Long.

For any behaviour **not yet landed** here, do not trust `run_project` — verify against the real VB6 toolchain
(`Make`/`Run with VB6`, which shells `vb6.exe /make`).

## Contributing — what belongs here

**In scope:** runtime-execution features that keep this a simple demonstrator; making a *landed* behaviour match
`vb6.exe` (with a pinned oracle test); documenting a new wall.

**Out of scope (by design — please don't):** any static/semantic analysis (limit #1); any pre-execution/compile
checking (limit #2); pushing toward full fidelity or a "real runtime" (the permanent cap); COM/`CreateObject`
(that is the separate Windows-gated engine work); a bytecode VM or CFG lowering. If you want those, the place for
them is a real language engine over the replaceable backend seam — not this interpreter.

**The neighbour wall — Microsoft dialects only.** This interpreter targets the classic Microsoft-era dialect
family (VB6 / VBA / VBScript) and will **never** interpret the *extended* language surface that
[twinBASIC](https://twinbasic.com/) adds beyond classic VB/VBA. Those extensions are someone else's work and
their reason to exist; we won't even *appear* to compete with them. The test: *does this construct ship in a
Microsoft-era VB6/VBA/VBScript?* If no, it's an extension — and such a construct should fail cleanly and point
you **to twinBASIC**, not get implemented here.

If a change would make the interpreter *more correct* but *less obviously a demonstrator*, open an issue to
discuss the boundary before writing code.

## Licence

MIT, like the rest of `IDE/`.
