# Profile bundles — a feasibility survey

> **This is a dated survey, not a living document.** Everything below was measured against `75b6517` on
> 2026-09-14. The counts will rot the first time anyone edits a view; the conclusions are what to keep.
> It is deliberately **not** listed under "Living Documents" in `CLAUDE.md` — that section is for
> artefacts the build guards, and adding an unguarded one is how `docs/lsp-*.md` drifted into fiction.
> Citations are file plus symbol rather than line number, for the same reason.
>
> The [addendum](#addendum-2026-10-11-what-a-shipped-command-registry-adds) at the end was measured
> separately, against `f229c8e` on 2026-10-11, and has the same status.

## The question

`--personality <vb6|vbaode|vba>` was an early attempt at "here is a bunch of JSON to customise the IDE —
menus, toolbars, key bindings, themes". What it describes is really a **profile**: a set of related JSON
files that set up a base experience, which somebody outside this repository should be able to author and
ship. That makes safe mode, profile directories and personalities one axis rather than three features —
the only question is *which bundle loads*: none, one at a path, or one by name.

The stated ideal for the contribution model is: **"show this in-box command on this surface" most of the
time, not "give me your AXAML."**

This surveys what mature IDEs actually do about that, and what it would cost here.

## What the prior art agrees on

Five ecosystems were examined (see [Sources](#sources) for how far each was verified). Stripped of vendor
specifics, they converge on a small set of ideas, in dependency order:

1. **A command has an identity; a placement is a pure reference to it.** VS Code splits
   `contributes.commands` (id, title, icon, enablement — no location) from `contributes.menus`, whose
   entries carry only `command`, `when`, `group` and `alt`. No label, no icon, no markup. Everything
   follows from this: the same command reaches a menu, a toolbar, the palette and a keybinding with no
   per-surface authoring, and a third party places a built-in command exactly the way the host does.
2. **The surface vocabulary is closed and host-owned.** Extensions target named surfaces the shell
   defines; they cannot invent one. The cost is that every surface anyone might want must be enumerated
   before it is needed — VS Code keeps that table from exploding by using one id plus a `when` clause to
   select an instance, rather than an id per instance.
3. **Placement is an id *plus arguments*, not just an id.** This is what lets one command appear several
   times on a surface with independent state, and it is the join that keeps menus pure data instead of
   pure code.
4. **A placement never authors an accelerator.** The shortcut shown is derived from the live keymap, so a
   rebind cannot leave a stale label anywhere. `MenuUtils` refreshes command-backed menu items after
   `KeymapService.Apply`, including clearing labels for unbound commands (#732).
5. **Separate the predicate from the policy.** Declare *where* a command goes; leave *when it is
   available* to a code seam. The surface's owner — not the contributor — decides whether unavailable
   means greyed or hidden, which is why a third party can place a command onto a surface it knows nothing
   about.
6. **A broken placement degrades loudly, never silently.** The natural implementation gets this backwards:
   VS Code errors on an unknown *command* id but drops an unknown *surface* id in total silence, with no
   schema complaint and no runtime message. A bundle authored against last year's shell hits that path
   constantly, and a silent drop is indistinguishable from "the feature is off". The best answer on record
   is to log the failure attributed to the contributing bundle, drop the placement, and say the command is
   still reachable another way.
7. **The escape hatch names a callback, not a widget.** For what four keys cannot express — a computed
   submenu, a most-recently-used list, a combo box — the declaration names a callback whose contents the
   shell requests at open time. One parser, one placement model, one set of surface ids, and the shell
   keeps ownership of the widget. That is the alternative to "give me your AXAML".
8. **Publish the placeable command ids as versioned public API**, and design conflict handling on day one:
   merge, allocate a namespace, record provenance.

Two cautionary results are worth stating explicitly. Eclipse proves the positive — because a placement is
an ordinary string-addressed row of data, hosting a command someone else declared is the documented normal
case rather than a special capability. Visual Studio proves the negative twice: its command table *had*
this power but locked it behind a compiled binary artefact no user gesture or tool could reach, and its
successor narrowed placement to a CLR type, which made the foreign-command case inexpressible altogether.

## Where HexIDE stands, surface by surface

| Surface | Data-driven today? | Effort to get there | The one fact that decides it |
|---|---|---|---|
| **Key bindings** | Partly | S–M | A JSON keymap system already ships — loader, command registry, Options UI, live rebinding, conflict scan, its own openspec capability. |
| **Theme packs** | Partly | S | The pack format exists and is already JSON. Only the *discovery* is missing. |
| **Language packs** | Partly | M | Embedded packs already take a disk-backed override layer with change notification and validation. |
| **Settings / per-user paths** | Partly | S | `UserDataPath` is a single computed seam that nine of the ten per-user files already funnel through. |
| **Toolbars** | Not at all | M in-tree, L shippable | 55 buttons and one toggle, each an independently authored AXAML block in one file. |
| **Menus** | Not at all | L | 206 `MenuItem` elements across six AXAML files. No menu model in the IDE at any level. |
| **Add-in packaging** | n/a | L | Trust machinery is production-grade; the contribution surface is a demo. |

Sizes are T-shirt. "Shippable" means a third party authors the bundle; "in-tree" means the IDE renders its
own chrome from data it ships itself, which is a strictly smaller job.

## Three findings that decide the cost

### 1. The command registry the prior art calls the prerequisite already exists — for keymaps

`CommandKeyMapping.Table` in `IDE/HexIDE/Keymaps/KeymapPack.cs` is an `IReadOnlyDictionary<string,
RoutedCommand>` mapping command-name strings to live command instances, built so JSON keymap packs could
rebind gestures. `KeymapService` already reads a pack, resolves names through it, warns on unknown entries
and reverts on failure. It is `internal`, which the prior art's "publish the placeable ids as versioned public
API" lesson would have to change.

That is the single hardest piece of a data-driven menu or toolbar, written, shipping, and sitting in a file
nobody would look in for this. Because toolbar buttons and menu items reference the *same* static command
instances, `CanExecute` already greys both together.

Three qualifications matter before anyone prices work against it:

- **Coverage is about 90%, not 100%.** There are 104 `RoutedCommand` fields in `ApplicationCommands.cs` and
  101 table entries. `GoToDeclarationCommand`, `OpenCallStackCommand` and `ResetWindowLayoutCommand` are
  absent — and `OpenCallStackCommand` is used inside a toolbar band today.
- **A second resolution path is needed regardless.** Nine menu-bar items and six toolbar flyout items bind
  ViewModel commands directly and appear in no name registry at all. Most of the Project Explorer context
  menu is the same.
- **The table is unguarded and already drifting.** Its own comment says to keep it in sync with
  `ApplicationCommands.cs`; nothing does, and no test in any of the four test projects references it. While
  it only serves keymaps, drift costs a missing keybinding. If menus depend on it, drift costs a missing
  menu item.

There is also a closer precedent than anything in the IDE shell: **`MenuComponentClass` and
`VBLoader.BuildMenuItem` in `HexIDE.Runtime` are already a full recursive data-driven menu** — captions,
nesting, separators, per-item accelerators parsed from a string, enabled/checked/visible — built to render
the *user's* VB6 form menus. The IDE's own menu bar is the hand-written one.

### 2. Discovery is the gap, not format

Three pack systems — theme, keymap, language — are already JSON, already deserialize through a
source-generated context, already warn-and-skip unknown keys, already revert to a built-in default on
failure. The format a profile wants exists three times over.

Keymaps now discover JSON files in the active user-data directory's `keymaps` folder at startup (#414),
using filenames as selection names alongside the reserved Default and VB6 choices. Themes still read
`avares://` embedded resources against a hard-coded available-set array; a third party cannot ship a theme today. Language packs are the
partial exception and the useful one: `LocalizationService` merges disk-backed per-locale overrides over
the embedded pack on every apply, with change notification, orphan-key rejection and placeholder
validation. That is a working embedded-plus-disk-override layer a profile loader could extend rather than
invent.

Embedded discovery is constrained by `avares://`, which has no trim- or AOT-safe directory enumeration.
Disk keymaps use their filenames for discovery; a profile bundle would carry its own manifest.

**The IDE has three disk-scanned extension points**: add-in packages, user translations and keymaps. A
profile directory would add a fourth.

### 3. The add-in system is almost the bundle loader, and structurally cannot be it

`AddinRegistry` already scans a directory of third-party packages, treats each subdirectory as a bundle,
deserializes a JSON manifest, hash-checks every file, verifies a signature against a publisher chain,
gates on consent, checks revocation, and isolates each package so one bad bundle cannot abort startup.
Directory discovery, per-bundle manifests, failure isolation and a trust story all exist.

Two things stop it being the answer as it stands:

- **It is code-bearing.** A package is an assembly implementing `IAddin`. A data-only profile does not fit
  without changing that.
- **It loads too late.** Consent means a non-first-party add-in does not load until after the main window
  has opened. Anything shaping the *base* experience would arrive after the user has already seen the
  unshaped one — unless profiles are exempted from consent, which is its own decision with its own
  consequences.

Worth noting alongside: the trust machinery is far more complete than what it protects. Signing, a
three-link chain, hash-covered manifests, TOCTOU re-verification, a revocation list, collectible load
contexts — and what an add-in can actually *do* is two menu anchors, a tool window, a gesture, a control, a
template and one Options page. The publisher-identity step that would make any of it third-party has never
been exercised; the only identity ever minted is first-party.

## What a bundle could not express today

- **A new icon.** Referencing a *built-in* icon from JSON works right now — `IconFactory` resolves an
  arbitrary string key against application resources at runtime. Shipping a *new* one does not: there is no
  code path anywhere in the tree that turns path data into a geometry. Every shipped icon is a fill-less
  single-path geometry the host tints, which is also a real constraint on what a bundle author may author,
  and nothing currently states or enforces it.
- **A new user-facing string.** The disk-loading translation tier deliberately drops any key absent from
  the canonical `en` pack. A bundle's own buttons would need either literal strings (breaking the
  localisation guarantee) or a new pack tier that permits new keys.
- **Roughly twenty menu items that are not declarative.** Eight headers are format strings with a runtime
  argument; five are two-way settings toggles; one submenu is a bound collection; several bind visibility
  predicates. Each needs a name-to-observable or name-to-predicate registry that does not exist.
- **"Present but inert"** already has four distinct encodings in the tree — a disabled attribute, a
  not-yet-implemented command, a disabled command, and no command at all — which a schema would have to
  reconcile before it could express the state at all.

## What this implies for sequencing

The dependency order falls out of the findings rather than from preference:

1. **Guard and complete the command registry**, and decide whether ViewModel-bound commands join it. It is
   the prerequisite for every other surface, it is 90% built, and it is drifting *now* — that is true
   whether or not profiles ever happen.
2. **Add discovery to one pack system** — themes are the smallest and the spec already promises it. That
   builds the from-a-path loading every later surface needs, once, against the least risky payload.
3. **Key bindings next**, because everything except discovery already exists there.
4. **Menus and toolbars last**, and only against a settled answer to the trust question: if a profile
   bundle must be signed and verified like an add-in package, the third-party half of that work is a size
   larger than the estimates above.

Replacing `--personality` itself is nearly free. It changes exactly three things: two menu-item
visibilities (the same command relocated between two menus) and one list in the New Project dialog. It has
no test coverage at all, so nothing breaks — and nothing catches a regression either.

## Where specs and code disagree

The survey turned up eleven divergences, none of them the thing being surveyed. They are recorded here
because several bear directly on a profile design, and because `openspec/config.yaml` has a rule for this
case: write the intended behaviour as the requirement, open an issue, and link it from a note under
`## Purpose`.

The findings below are filed; rows marked resolved record fixes since the survey. The remaining spec
contradictions still want their `## Purpose` notes, the second half of that rule.

| Where | Divergence | Issue |
|---|---|---|
| `specs/theme-packs` | Requires a user theme with "no code change and no rebuild". Packs are compiled resources behind a hard-coded array; both are required. | [#413](https://github.com/hexide-io/HexIDE/issues/413) |
| `specs/keymap-packs` | Resolved: user JSON packs are discovered from disk alongside the two built-in choices. | [#414](https://github.com/hexide-io/HexIDE/issues/414) |
| `specs/ide-personalities` | States a personality controls toolbar buttons. No toolbar button is gated on personality anywhere. | [#419](https://github.com/hexide-io/HexIDE/issues/419) |
| `specs/icon-system` | A **Requirement**, not just the Purpose, says icons "SHALL be original vector geometries". They are Fluent UI System Icons — MIT, recorded in `THIRD-PARTY-NOTICES.md`, and the VB6-extracted artwork the clause was written against is gone. The licence position is sound; the word "original" is what is false. | [#420](https://github.com/hexide-io/HexIDE/issues/420) |
| `specs/addin-system` | Promises menu items at "a named parent location". The code has two fixed anchors and silently redirects anything else. | [#416](https://github.com/hexide-io/HexIDE/issues/416) |
| `specs/toolbars` | Requires all four toolbars in both toggle surfaces. The toolbar band's own context flyout offers three. | [#417](https://github.com/hexide-io/HexIDE/issues/417) |
| `CLAUDE.md` | "Every shipped pack is 100% complete, enforced at build" — nothing checks pack parity. `LocalizationCoverageTests` covers AXAML keys against `en`, VB properties against `Str.PropDesc.*`, and region packs for orphan keys; the parity tool is manual and CI never runs it. | [#421](https://github.com/hexide-io/HexIDE/issues/421) |
| `docs/MISSING_FEATURES.md` | Lists the Standard toolbar's Break button as permanently inert. It is wired and executes whenever a project is running. | [#422](https://github.com/hexide-io/HexIDE/issues/422) |
| `IDE/HexIDE/Keymaps/KeymapPack.cs` | `CommandKeyMapping.Table` says to keep itself in sync with `ApplicationCommands.cs`; three commands are missing and no test checks it. | [#415](https://github.com/hexide-io/HexIDE/issues/415) |
| Shipped artefacts | Two unit-test language packs (`zz`, `zz-ZZ`) are embedded in the Release assembly. Not selectable — `LanguagePack.cs` does not list them — so this is dead weight rather than a leak. | [#423](https://github.com/hexide-io/HexIDE/issues/423) |

The eleventh is a plain defect rather than a divergence, and is filed as one
([#418](https://github.com/hexide-io/HexIDE/issues/418)): under `--personality vba` the New Project dialog
offers no project types at all, so its OK button can never enable, while the Standard toolbar's Add Project
flyout hard-codes all four templates and bypasses the personality entirely. Two surfaces, disagreeing, and
one of them a dead end.

## Addendum (2026-10-11): what a shipped command registry adds

The five ecosystems above are *contribution* models: they describe how a third party places a command.
Since this survey, an open-source image editor, PhotoCraft (MIT OR Apache-2.0), has shipped the other
half. It is an application built outward from its command registry. Each of about 700 commands carries a
string id, a label, a menu path, a default shortcut, a parameter description, an enablement check and a
run function. Its menus, shortcuts, command palette, recorded macros, CLI, JSON control channel and MCP
server all dispatch by id.

It confirms the sequencing above: the registry comes first. It also shows six things the five ecosystems did
not. Its design was read at `65e8d5f`, and none of its code was carried over.

### The obvious reading is wrong, and the correction is the lesson

Its README says "one registry". The source shows **one id namespace over several tables**:

- the engine's commands;
- a separate shell table for window and panel commands;
- a data copy of the target application's whole menu tree.

Holding these together takes work. The check for whether a menu item is live combines a dozen sources. The
shortcut table merges six, with precedence rules that have needed fixes of their own.

What held together was the **id as the join**, plus tests proving the join holds. That bears directly on
finding 1's "second resolution path": the routed-versus-ViewModel split here is survivable, provided one id
space covers both sides and a test proves that every surface reaches the command it shows.

### The difference that decides the transfer: state, not focus

PhotoCraft's commands act on session state, such as the active document or the active layer. Their
enablement is a pure function of that state.

A HexIDE `RoutedCommand` works differently. It is resolved from the focused element, and `CanExecute` is
answered by whichever binding the route meets first. Most of the duplication follows from that:

- **View Code has three implementations:** `FormEditViewModel.ViewCode`, `CodeEditorViewModel.ViewCode` and
  `ProjectToolViewModel.ViewCodeCommand`.
- **`invoke_menu_item` finds a command through the menu**, by its displayed and localised text
  (`IDE/HexIDE/Automation/MenuPath.cs`), so whether it works depends on focus
  ([#678](https://github.com/hexide-io/HexIDE/issues/678)).
- **MCP tools re-implement the menu's guards** rather than sharing them.

None of this argues for dropping routing. VB6's own IDE is focus-driven, and that is fidelity. But a
registry entry is the natural place to resolve an explicit target, meaning the active document in the dock
rather than whatever has keyboard focus. That would give automation and add-ins a path that does not depend
on focus.

### Six things it adds to the prior art

1. **Enablement returns a reason, not a bool.** A shortcut that hits a disabled command shows the reason on
   the status bar instead of doing nothing, and a test pins that behaviour. In HexIDE the question "can
   Start run?" is answered three times:
   - the menu's predicate, with F5's continue-when-paused semantics;
   - `run_project`'s own refusal strings;
   - `IHexIdeHost.RunProject`.

   Commands bound to ViewModel methods have no predicate at all
   ([#733](https://github.com/hexide-io/HexIDE/issues/733)). One reason-returning predicate per command
   would give the menu tooltip, the status bar, the MCP refusal and AI Chat the same words. That meets
   `CLAUDE.md`'s rule that a surprising reply must explain itself, once, at the source.
2. **One dispatch point carries the cross-cutting policy.** Every caller goes through a single function,
   which does all of the following:
   - checks that the id is known and the parameters have the right shape;
   - applies a permission gate for untrusted sessions, including to commands that other commands run;
   - refuses while a background job is editing the same document;
   - catches crashes and reports them as errors, leaving the document unchanged;
   - writes the macro journal.

   HexIDE has no such point. The menu, each MCP tool and the add-in host make their own checks, and they
   have already diverged: Set Next Statement is scoped to the running project in the MCP tool but not in
   the menu ([#734](https://github.com/hexide-io/HexIDE/issues/734)). A per-caller policy would belong at
   such a point, for add-ins (which run with full trust today) or for the unauthenticated automation server
   ([#352](https://github.com/hexide-io/HexIDE/issues/352)).
3. **The automation surface is the registry.** The MCP server offers `command_list` (with live enabled
   state), `command_run` and `command_batch`. It keeps dedicated tools only for jobs a command cannot do,
   such as rendering a preview or inspecting the document. That is the `hexide-mcp-server` spec's own
   authoring policy taken to its conclusion.
4. **A registry turns "every command" into a loop.** Its tests include:
   - throwing adversarial parameters at every command and requiring an error rather than a crash;
   - checking that every shortcut a menu displays fires that item from where focus usually is;
   - checking that a disabled shortcut says why.

   [#415](https://github.com/hexide-io/HexIDE/issues/415) is HexIDE's first such test. The next would ask
   every registered command, by name, whether it can run, without throwing. Both have the same shape as the
   guards this repository already trusts, such as `ProtocolCoverageDocTests` and
   `CommandLineDocumentationTests`.
5. **Parity is generated from the target's own menu tree.** Every item in the data copy of the target's
   menus names a command id. An item is live if that id is registered, and disabled otherwise. A generator
   writes the parity checklist, and a test stops the live count from falling. Its README is honest that
   this "measures wiring, not behaviour".

   The HexIDE analogue is VB6's menu tree held as data, generating the IDE-menu rows of
   `MISSING_FEATURES.md`. It would also collapse the four "present but inert" encodings listed above into
   one: an id nobody has registered.

   VB6's extensibility model is understood to expose its menus as Office CommandBars, where built-in items
   carry numeric control ids that an add-in could find, place and execute. If that holds, the tree could be
   harvested from `vb6.exe` on the oracle VM rather than typed in, and that capability is itself an add-in
   fidelity gap (finding 3's "two menu anchors"). **Confirm both on the oracle before designing against
   either.**
6. **Labels translate by command id.** Menu labels go through a lookup keyed by command id, falling back to
   the English label. Scripts, the CLI and MCP see only ids, never translated text.

   HexIDE has four keys for Cut: `Str.Menu.Edit.Cut`, `Str.Designer.Context.Cut`,
   `Str.CodeEditor.Context.Cut` and `Str.Toolbar.Tip.Cut`. Each one costs a translation in every shipped
   pack. One key per command, with the access-key marker stripped for tooltips, would also remove
   `invoke_menu_item`'s dependence on displayed text.

It also confirms prior-art point 4 from the other direction. Its displayed shortcuts are derived from the
live binding table. HexIDE's are copied once, and six are hard-coded literals
([#732](https://github.com/hexide-io/HexIDE/issues/732)).

### Two things not to copy

- **Free-text parameters.** A command's parameters are described in a hand-written prose string, not a
  schema, although its own architecture document describes schemas. That falls short of the bar
  `CLAUDE.md` sets for a tool's generated schema. A HexIDE registry would carry typed parameter records.
- **Leaving the shell/engine split undecided.** The dozen-source live check and the six-way shortcut merge
  are what that costs. Decide early whether ViewModel-bound commands join the registry. That is step 1
  above, unchanged.

### What it changes in the sequencing

The order is unchanged. Step 1 now has a concrete shape:

1. Each entry in `CommandKeyMapping.Table`, or its successor, carries a label key, its gestures, and a
   function that returns why the command cannot run, or nothing when it can.
2. Land [#415](https://github.com/hexide-io/HexIDE/issues/415)'s guard.
3. Have `invoke_menu_item` resolve by command name.
4. Then retire each MCP tool that only re-implements a command, keeping those the authoring policy
   justifies on their own.

## Sources

Findings were produced by two parallel research passes, each followed by an adversarial verification stage
whose job was to falsify the first pass. **Verification coverage was not uniform, and the difference
matters when reading specific claims:**

- **HexIDE codebase survey** — all seven surfaces were verified. Every count in this document is the
  verifier's, not the original survey's, and several headline figures changed materially in the process.
- **Prior art** — VS Code and the Eclipse/Visual Studio pair were verified, and the VS Code account was
  corrected. The JetBrains, editor-tradition and declarative-breakdown passes **were not verified**; their
  contribution here is deliberately limited to model-level lessons rather than specific manifest keys,
  element names or file names, which are exactly what an unverified pass gets wrong.

Where a claim in this document names a HexIDE file or symbol, it was checked against the tree at
`75b6517`. Where it describes another product's model, treat it as a design lesson rather than as a
citation.

**The 2026-10-11 addendum was a single pass, not adversarially verified.**

- **PhotoCraft** was read directly at `65e8d5f` of
  [storytold/photocraft](https://github.com/storytold/photocraft), in these files:
  - `crates/engine/src/commands.rs`: `CommandSpec` and the registry;
  - `crates/engine/src/jobs.rs`: `Session::dispatch`;
  - `crates/ui-egui/src/menus.rs`, `menu_catalog.rs`, `shortcut_dispatch.rs` and its tests, `parity.rs`, and
    `i18n/mod.rs`;
  - `crates/automation`;
  - `crates/engine/tests/panic_hunt.rs`;
  - `docs/architecture.md`.

  Where its design document and its code disagree, as with parameter schemas, the code is what the addendum
  describes.
- **HexIDE** claims in the addendum were checked against `f229c8e`.
- **The VB6 CommandBars claim is from memory**, and is marked there as needing the oracle.
