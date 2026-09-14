# Profile bundles — a feasibility survey

> **This is a dated survey, not a living document.** Everything below was measured against `75b6517` on
> 2026-09-14. The counts will rot the first time anyone edits a view; the conclusions are what to keep.
> It is deliberately **not** listed under "Living Documents" in `CLAUDE.md` — that section is for
> artefacts the build guards, and adding an unguarded one is how `docs/lsp-*.md` drifted into fiction.
> Citations are file plus symbol rather than line number, for the same reason.

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
   rebind cannot leave a stale label anywhere.
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

What none of them can do is load from a path. Themes and keymaps read `avares://` embedded resources
against a hard-coded available-set array; a third party cannot ship a theme today. Language packs are the
partial exception and the useful one: `LocalizationService` merges disk-backed per-locale overrides over
the embedded pack on every apply, with change notification, orphan-key rejection and placeholder
validation. That is a working embedded-plus-disk-override layer a profile loader could extend rather than
invent.

The reason for the closure is documented and technical rather than lazy: `avares://` has no trim- or
AOT-safe directory enumeration, so anything loaded from disk must carry its own manifest — which a bundle
would carry anyway.

**The whole IDE has exactly two disk-scanned extension points**: add-in packages and user translations. A
profile directory would be the third, and the first that is neither signed code nor a per-user override.

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

All eleven are filed. The five spec contradictions still want their `## Purpose` notes, which is the
second half of that rule and is not done by filing alone.

| Where | Divergence | Issue |
|---|---|---|
| `specs/theme-packs` | Requires a user theme with "no code change and no rebuild". Packs are compiled resources behind a hard-coded array; both are required. | [#413](https://github.com/hexide-io/HexIDE/issues/413) |
| `specs/keymap-packs` | Asserts adding a pack "SHALL require no code change". The available set is a hard-coded two-element array. | [#414](https://github.com/hexide-io/HexIDE/issues/414) |
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
