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
   - Then one slice per group of rows, each removing them: the run engine with its debugger wiring, the running
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

## Namespace renames, in phase 4's window

**Core's 54 files under `HexIDE.Runtime.*` namespaces are renamed in phase 4's window** (supervisor, 2026-11-23). Once the runtime has gone, those names would mislead every reader and every add-in author, and 0.x is the cheapest time to change them.
- **The names are decided here,** so phase 4 only applies them.
- **One PR does it,** generated mechanically. It's reviewed as a diff of `namespace` and `using` lines, so a reviewer can trust it without reading every file.
- **The names fold into the IDE's existing `HexIDE.<area>` scheme.** At `6d421e942`, none of the three named targets declares a type that clashes with a Core type moving in.

| Core folder | Today | Becomes | On the add-in contract? |
|---|---|---|---|
| `ProjectElements/` (18 files) | `HexIDE.Runtime.ProjectElements` | `HexIDE.Projects`, the IDE's 22-file namespace | **Yes:** 11 files are under the public API guard (`IDE/HexIDE.Core/.editorconfig`, #4609). `IAddinProjectTemplate.Create` returns a `ProjectDefinition` |
| `Debugging/` (2) | `HexIDE.Runtime.Debugging` | `HexIDE.Debugging`, the IDE's 13-file namespace | No |
| `Serialization/` (2) | `HexIDE.Runtime.Serialization` | `HexIDE.Serialization`, which already holds `ReadableJsonEncoder` | **Yes:** `IDeserializeErrorSink` and `SerializedProjectGroup` |
| `Components/` (10) | `HexIDE.Runtime.Components` | Settled by slice 4, which decides what stays as the component contract. The candidate is `HexIDE.Components` | **Yes:** `ComponentInstance`, `EventClass`, `IComponentClass` and `PropertyClass`. `IHexIdeHost.RegisterComponent` takes an `IComponentClass` |
| `BuiltinTypes/` (22) | `HexIDE.Runtime.BuiltinTypes` | Settled by slice 7, since VB6's value types may go with the provider | No |

**Three of these are on the add-in contract,** so renaming them breaks the build of every add-in that names them. That makes the rename an API change:
- the RDCore team hears about it through rdcore's desk before the release that carries it;
- the changelog records it as breaking;
- the rename PR updates Core's public API baseline (`IDE/HexIDE.Core/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`) as the analyzer requires.

The contract was read from HexIDE's own public API guard, not from any add-in's source. The provider's own `HexIDE.Runtime` namespaces are a separate question for phase 4.
