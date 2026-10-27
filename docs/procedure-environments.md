# Procedure environments (#2120)

A procedure environment owns its parameters, locals, return name and `Me`. Other names fall back to the
module or instance environment, then that environment's `ProjectNames`. Instances still clone their
module template: this change removes only the copy made by `RunProcedure` on every call.

`TryGetOwnVariableLocation` deliberately does not follow the fallback. Its callers were checked:

- `ProjectNames`, `ModuleLevelBounds`, and module Const initialisation ask about module declarations.
- `StatementExecutor` asks about a just-allocated local for `As New` and Static retention.

None needs inherited names. Ordinary reads and writes use `TryGetVariableLocation`; a failed lookup
still creates an implicit variable in the current procedure. `DebugInspector` enumerates the call's
own bindings; the existing slot comparison also handles module-level frames, whose bindings already
appear beneath the module root. Module and instance enumeration elsewhere remains unchanged.

## Measurement

Measured under WSL on 2026-10-26, Release .NET 10, baseline `e7b0818e`, after #2038's SLL-first parser
change. A module declares N `Dim vI As Long` names, then `Sub Work(ByVal n As Long)` with body
`n = n + 1`. A top-level `For i = 1 To 100000` loop calls `Work i`. Stopwatch covers constructing the
interpreter and executing the whole program, as in #2120's original measurement. These are HexIDE
performance measurements, not VB6 semantic measurements.

| Module names | Initial before | Initial after |
|---|---|---|
| 10 | 1,939 ms | 1,614 ms; repeat 1,926 ms |
| 500 | 3,526 ms | 1,258 ms; repeat 1,173 ms |

With the final spelling-preservation code, warming both workloads once before measuring gave whole-run
10/500-name times of 1,283/4,802 ms with the old clone path and 818/1,432 ms with fallback scopes.
A separate final run timed construction and execution independently, again warming both workloads first:

| Module names | Construction | Execution (100,000 calls) |
|---|---|---|
| 10 | 16 ms | 820 ms |
| 500 | 352 ms | 752 ms |

The remaining whole-run difference includes startup; execution no longer scales with module names.
Wall-clock samples are observations, not timing assertions (other suites were running during the
warmed measurements). The temporary benchmark used fresh contexts/environments for each sample;
its workload above can be reproduced with `BaseVBTestFixture`'s `MockStdLib` and `BasicInterpreter`.

The four `ProcedureEnvironmentTests` failed with `CreateProcedureScope` forwarding to the old `Clone`
implementation, before replacing that implementation with a fallback scope. They check absence of
copied names, live module lookup, writes/implicit ownership, and shadowed bindings plus Locals grouping.
