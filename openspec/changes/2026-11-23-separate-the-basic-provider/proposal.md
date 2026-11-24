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

## The composition point

When `HexIDE` stops naming the provider, something still has to start it. Today `App.axaml` merges the provider's
resources, and `DISetup` wires its engine. In phase 2 that becomes one file in `HexIDE.Desktop`:
- **One entry type in the provider** registers what it contributes through calls shaped like `IHexIdeHost`'s: its
  resources, component registry, designer pictures, run engine and project type.
- **The Desktop file** creates that type and passes it the host. It's the only product file left naming the
  provider. The guard's list ends phase 2 with its rows, and with a `HexIDE.Desktop` → `HexIDE.Runtime` project
  reference in place of `HexIDE`'s.
- **In phase 3,** loading the bundled add-in replaces that one direct call. Nothing else changes.

Phase 3 turns these registration points into add-in API, so they're designed as add-in API now, not as a private
seam made public later (supervisor, 2026-11-23):
- **They only add.** A registration that collides with a key the IDE or another provider holds is refused and
  logged, naming both owners. Provider resources never supply `Str.*` keys, theme keys or anything the consent and
  trust dialogs read, which is what keeps a third-party provider from rewording a "Verified publisher" prompt.
- **They're reachable only through the host interface an add-in receives after consent.** The bundled provider gets
  no back door the third-party route lacks.
- **Phase 2 tests both:** a second registration of an existing key is refused, and a provider can't reach a consent
  dialog's strings. The delta's requirements say so, so phase 3's security review checks them rather than
  discovering them.

**Slice 2.7 builds it,** with resources as the first registration (`App.axaml`'s merge of the provider's dictionaries), and the other slices add theirs. That adds an `IHexIdeHost` member on the add-in trust boundary, so it's security work: built by a Claude session and reviewed by the supervisor before merge.

**The provider's strings register the same way** (slice 2.9). Its language packs are embedded in its own assembly, and the IDE reaches them through a pack area the entry type registers, which is no `TypeRef`. So the guard can't see that dependency, but the entry type's own row covers it, and phase 3 removes that row.

**Rejected: finding the provider's entry by assembly name.** Phase 3 replaces it anyway, and reflection hides the
dependency from the guard.

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
| `Components/` (10) | `HexIDE.Runtime.Components` | Settled by slice 2.4, which decides what stays as the component contract. The candidate is `HexIDE.Components` | **Yes:** `ComponentInstance`, `EventClass`, `IComponentClass` and `PropertyClass`. `IHexIdeHost.RegisterComponent` takes an `IComponentClass` |
| `BuiltinTypes/` (22) | `HexIDE.Runtime.BuiltinTypes` | Settled by slice 2.7, since VB6's value types may go with the provider | No |

**Three of these are on the add-in contract,** so renaming them breaks the build of every add-in that names them. That makes the rename an API change:
- the RDCore team hears about it through rdcore's desk before the release that carries it;
- the changelog records it as breaking;
- the rename PR updates Core's public API baseline (`IDE/HexIDE.Core/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`) as the analyzer requires.

The contract was read from HexIDE's own public API guard, not from any add-in's source. The provider's own `HexIDE.Runtime` namespaces are a separate question for phase 4.

## Decided since this record merged

Agreed while slicing phase 2. Each is confirmed by the supervisor unless it says otherwise.

- **Registration is deny-by-default.** An owner-keyed grant in the IDE's source decides which owner may register
  resources (#5396), a language pack area (#5394), or a component beyond a plain control. In phase 2 the grant names the
  Desktop composition's origin, which isn't first-party-signed. Phase 3 switches it to a first-party signature with the
  `hexide.basic` id. Task 3.1 says so. The delta's requirements "A registration beyond a plain control needs the
  provider's grant" and "A granted registration still only adds" pin it.
- **Designer roles (2.4a, #5478).** `IComponentClass` gains `IsContainer` and a role: `Control` by default, then `Form`,
  `Menu` and `Line`. Both are default interface members, so a component that names neither is a plain control.
- **Component registration (2.4c).** This was agreed with hexide-frm, and the supervisor reviews the draft.
  - `RegisterComponent` becomes owned, a toolbox item may be registered only by its component's owner, and
    `ComponentRegistry` starts empty.
  - Control libraries are first-party in phase 2, because a library writes `Object=` lines into users' files.
  - `IControlLibrary` is public in Core. Designer-time hooks go on the designer contract, not on `IComponentClass`.
  - **Its second part (#5594) owns the components and the toolbox.** The provider registers its Form, its controls and
    their toolbox items through the owned host, with `IHexIdeHost.RegisterToolboxItem` new for the items. The control
    libraries register the same way in a third part.
- **The Object Browser's in-box members (2.4d, #5610).** Agreed with the supervisor, as two PRs.
  - **Its first part reads members through Core.** `IComponentMemberTable.MembersIn(IControlLibrary?)` is optional and
    library-aware, because Common Controls 5.0 and 6.0 offer the same class with different members. Its
    `ComponentMemberKind` is named apart from the type library's `MemberKind`. Whether a member is written in a `.frm`,
    and whether HexIDE implements it, stay with the runtime.
  - **A listing generated before the change pins it.** Every member the Object Browser lists for an in-box class or
    library, with its kind and HexIDE-only note, must stay the same through both parts.
  - **Its second part takes a library's classes and references from the registered libraries.**
- **Designer pictures (2.4b).** This was agreed with hexide-frm, and the supervisor reviews the draft.
  - `RegisterDesignerPictures` is one service per host and is grant-gated, since it draws every form's controls. A
    third-party component is drawn by that service through its own `IComponentClass`.
  - A Line's units stay with the provider, and the picture reply carries the control's bounds. The designer contract
    gains no unit member.
  - **The precondition:** each in-box control renders byte-identically under the provider's host and under
    `ControlRenderer`, with Skia at scaling 1 and 1.5.
  - **Its second part (#5591) registers the drawer and keeps where it draws.** The designer lends the drawer its
    `ControlRenderer` through Core's `IDesignerDrawingSurface`, so each control is drawn where it always was. A Line's
    units and a host of the provider's own, with the precondition above, come in a later part.
- **The picture decoders get a product assembly of their own (2.5b, #5474),** which the IDE, the provider and
  `HexIDE.Standalone` reference.
  - After phase 4 it stays in HexIDE and is consumed by HexIDE.Basic as Core is, so a decoder fix ships in HexIDE's
    release.
  - A program built with File > Make carries the decoder in its runner, so a decoder security fix reaches it only when
    the program is made again. A changelog entry for such a fix says so.
- **Text and editing helpers go where 11-16's rule puts them:**
  - `ReadOnlyRegions` and `SourceLines` go to Core (2.5a, #5440), and so do the project file's keys and path helpers (2.5c, #5521);
  - keyword casing and block auto-close go to the IDE (2.6, #5405).
- **Still open: a public shape for a project's targets (2.12, #5404).** The draft is on #5404
  (issuecomment-7092255114). It has `ProjectDefinition.Targets` and `AddinProjectContent.Targets` as lists of engine
  ids, with rules for an empty list, one known id, an unknown id and several ids. Its wording waits on RDCore's
  maintainer, and this record gains it once that's answered.
