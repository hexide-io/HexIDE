# Separate the Basic provider inside the tree, under a guard (#3411)

## Why

HexIDE is moving its interpreter, its forms engine and the bundled VB6 language server out to a provider,
`HexIDE.Basic`, loaded as an add-in like any other ([#3411](https://github.com/hexide-io/HexIDE/issues/3411)). Jen
confirmed this as the first goal for the week of 2026-11-23. The epic's decisions are all recorded on #3411:
- `Components/` and `Serialization/` leave, and the designer is provider-fed (2026-11-10);
- the bundled language server goes with the provider (2026-11-20);
- the running controls, Make and Make with VB6, a neutral default profile, and the provider's strings all go
  with the provider (2026-11-22).

Two rules set the order:
- **No release may open VB files as plain text** (2026-11-20).
- **The first-start offer (#2904) and the marketplace aren't live.**

So every release this month still carries the provider. The move therefore means building `HexIDE.Basic`
separately and loading it as a bundled first-party add-in. It doesn't mean deleting it from the default install.

## What changes

The supervisor's move plan on #3411 (2026-11-23) has four phases, each leaving `main` releasable. This change
records the first two, which hexide-int leads.

1. **Measure.** Done on 2026-11-23 at `6d421e942` (v0.7.0). The full report is the phase 1 comment on #3411.
   - Read from the built DLLs' metadata, `HexIDE.dll` references 109 types in `HexIDE.Runtime` and
     `HexIDE.Desktop.dll` 13; no other product assembly references the provider.
   - `HexIDE.Core` holds 54 files under `HexIDE.Runtime.*` namespaces, and the provider uses four of Core's
     internal members.
   - The IDE's two test projects hold 262 test files that test both sides and 38 that test only the provider.
2. **Separate in the tree, under a guard.** The code stays where it is, and only the references change, so
   nothing needs freezing.
   - First, a test pins every reference from a product assembly into a provider assembly in a list that can
     only shrink (#5388).
   - Then one slice per group of rows, each removing them: the run engine, the debugger contract, the running
     controls, the designer's component model, the VB6 file formats and code-window text, the language helpers,
     VB6 value types and resources, Make, strings, a neutral default profile, #3663's test files, and Core's
     internals.
   - The slice list, with sizes and owners, is on #3411. The tasks below follow it.
3. **Flip registration** (the supervisor sequences this). VB6 stops being compiled into the IDE's composition
   and becomes a bundled first-party add-in, signed under the shipped root once #4109's point release is out.
4. **Split the repository** (the supervisor sequences this). The provider's folders move, with history, to
   `hexide-io/HexIDE.Basic`, and HexIDE's release builds a pinned commit of it and bundles the result.

## Impact

- **Phase 2 changes no behaviour.** Each slice moves a dependency behind a contract in `HexIDE.Core` or into the
  provider. A user sees the same IDE, and the guard's list gets shorter.
- **No freeze.** In-tree separation doesn't need one. The only freeze is phase 4's repository split, and the
  pool scripts will enforce it.
- **Lanes:** the mechanical slices go to lanes with cited briefs on the Week of 2026-11-23 milestone.
  - hexide-frm takes the running controls and the designer;
  - hexide-mcp takes the debugger and the automation tools;
  - hexide-gen takes profiles and strings;
  - hexide-int takes the rest and reviews the whole.

## Considered and rejected

- **A source-level guard, sorting files with Roslyn as `ProviderTestSort` does.** It compiles without source
  generators, and `HexIDE` depends on Pure.DI, `[Notify]` and compiled AXAML. Each built DLL's `TypeRef` table
  is exact, includes generated code and AXAML, and costs a fraction of the time. It names types, not files;
  each slice's brief finds the files.
- **A long freeze before anything moves.** It would idle the lanes in the week the runtime fixes are landing,
  and phase 2 doesn't need one. (Supervisor, on #3411.)
- **Splitting the repository first.** Every reference would break at once across two repositories, with no
  build to tell a missed one from a moved one. (Supervisor, on #3411.)
- **Dropping the provider from this month's releases.** It contradicts the rule that no release opens VB files
  as plain text. (Supervisor, on #3411.)

## Open

**Core's `HexIDE.Runtime.*` namespaces.** 54 Core files carry them.
- `ProjectElements` (118 product files use it) and `Debugging` (15) read as IDE contracts that stay, under a
  provider's namespace. Renaming them would touch about 150 files and conflict with nearly every open PR.
- `Components` (39) and `BuiltinTypes` (13) are the designer's contract, which slices 4 and 7 answer.

That question is the supervisor's, and is asked on #3411.
