# TODO

Tactical items — bugs, missing polish, small features noticed during testing.
Items here may graduate to ROADMAP phases when they grow in scope.
When GitHub Issues are adopted, batch-import these as issues.

> **See also [`bug-backlog.md`](bug-backlog.md)** — 37 adversarially-verified defects from the 2026-08-09
> whole-codebase bug-hunt (6 HIGH · 19 MED · 12 LOW), ranked by severity. The For…Next HIGH trio is being fixed
> first; the rest await triage.

---

## Localization (RTL / i18n polish)

- [ ] **Audit directioned icons for RTL mirroring** — toolbar/command icons that encode a horizontal direction (Back/Forward, Undo/Redo, Indent/Outdent, Step Into/Over/Out, Next/Previous bookmark, tree expanders, etc.) should be flipped under an RTL language (`ar`, `he`). Avalonia mirrors *layout* via `FlowDirection` but does NOT auto-flip bitmap/`PathIcon` glyphs, so a "Back" arrow still points left under Arabic. Identify the directioned icons and mirror them (e.g. a `ScaleTransform` `-1,1` gated on RTL `FlowDirection`, or RTL-specific glyphs). Surfaced during the Arabic (P17) RTL pack.
- [ ] **Access-key convention for non-Latin scripts** — the `ar`/`zh`/`ja`/`he` packs currently OMIT the `_` access-key mnemonics (Latin accelerators don't fit those scripts). The conventional CJK form is `文件(F)` (Chinese/Japanese append the Latin accelerator in parentheses); Arabic/Hebrew usually drop them. Future polish: add `(F)`-style access keys for the CJK packs so Alt-accelerators work while showing native text.

---

## Serialization

- [ ] **Persist project `Description`** — `ProjectDefinition.Description` is set via Project Properties (UI) but never written to / read from the `.vbp`, so a user-entered description is lost on save. Decide the canonical `.vbp` key and wire it through the (de)serializer. (Not a round-trip *drop* of existing `.vbp` content — an unparsed `Description=` line is already preserved as an unknown — but a UI-field persistence gap.)
- [ ] **Quote-wrap exotic `Name`s on serialize** — `ProjectSerializer` writes `Name=` unquoted; the deserializer already unquotes. A project name containing `"`, `;`, `=`, or edge whitespace would not be VB6-canonical on save (it is idempotent/non-lossy, but not quote-correct). Quote+escape on write to match VB6.
- [ ] **Binary unknown pass-through** (deferred from `serialization-round-trip` Phase 3) — when an unknown OCX property references a binary blob (e.g. `UnknownPicture = "Form1.frx":0A00`), Phase 2 logs and drops both the text line and the blob. Full fidelity requires threading a `RawFrxEntry` (offset → raw bytes) through `FrxDeserializer` → `FormDefinition` → `FrxSerializer` so unknown blobs are re-emitted at their original offsets, and the referencing property lines are stored alongside `UnknownRawPropertyLines` and re-emitted verbatim. Complex because interleaving changed known-property blobs with fixed-offset unknown blobs requires careful offset bookkeeping. If this becomes a priority, write a dedicated spec.

---

## Crash recovery / project identity

- [ ] **True autosave (write the real files)** — the [`crash-recovery`](../openspec/changes/2026-08-16-add-crash-recovery/) change is **backup-only** (it never writes your real `.frm`/`.bas`/`.cls`; explicit Save still owns the files). A separate future spec could add an opt-in *true* autosave that periodically writes the actual project files (Google-Docs style). It would need its own file-watcher self-write suppression and a deliberate opt-in, since it departs from the VB6/VS explicit-save model.
- [ ] **Generalize `ProjectId` beyond recovery** — `crash-recovery` introduces a stable `[HexIDE] ProjectId` GUID in the `.vbp`, used *only* as the recovery match key. The user sidecar (`.user.hexproj`) and recent-projects are still **path-keyed**, which is fragile across folder moves/renames. Consider re-keying them off `ProjectId` (with a migration for existing path-keyed data) so project identity is path-independent everywhere.

---

## MCP dev/automation server

- [ ] **`take_snapshot` / `dump_visual_tree` are blind to runtime modal dialogs layered over a running form.** When a VB6 program shows a `MsgBox` (or the runtime surfaces a compile/runtime-error dialog) *on top of* a running `VBFormRuntime` window, the snapshot/tree tools capture the form window underneath, not the modal — `activeDialog` reports the form, and the error/`MsgBox` is invisible until the form is torn down (only then does it surface as the active window). The "prefer a visible modal dialog" selection logic should also enumerate the runtime's managed `MessageBox`/`InputBox` and error-dialog top-level windows (they're separate `Window`s owned outside the form's control-view tree) and prefer them. Surfaced 2026-07 while driving a `Debug.Print`/recursion demo — the agent could not see a `MsgBox` or a "Variable not defined" error dialog over the running form.

---

## Open Project

- [ ] `Object=` keys (ActiveX/OCX component registration, e.g. `Object={GUID}#2.0#0; MSCOMCTL.OCX`) are currently preserved verbatim via `UnknownPreSectionLines` (round-trip safe) but not semantically modelled. A dedicated `ocx-registration` spec is needed to: parse into a typed `List<VbOcxRegistration>` on `ProjectDefinition`, surface in the Project Properties Components tab, and eventually wire into type library loading (Windows-only, deferred to the `type-library-metadata` spec).
- [ ] `Reference=` keys (standard VB6 type library references) are similarly preserved verbatim but not modelled. Likely companion work to `Object=` above.

## Project Explorer

- [ ] **Glyph overlays for missing / unsupported project items** (needs its own spec). Items that are parsed but not loaded into the live model are now round-trip-preserved verbatim (`ProjectDefinition.PreservedItemLines`) but **not shown** in the project tree: a `Form=`/module whose file is missing on disk, and unsupported types (`UserDocument=`/`.dob`). A future spec should surface these in the project explorer with a distinct "missing file" / "unsupported" glyph overlay (VB6 itself shows missing files greyed/with an error icon), letting the user see and act on them rather than them being invisible-but-preserved.

## Splash / New Project dialog

- [x] "Existing" tab — embedded file browser with path navigation, folder/project icons, filename and file-type filter (Phase 18)
- [x] "Recent" tab — MRU list backed by `IRecentProjectsService`, persisted to `%APPDATA%/HexIDE/recent-projects.json` (Phase 18)
- [ ] Help button disabled

## Menus — NYI commands

These menu items are wired to `NYICommand` (shows "not yet implemented" message):

- [ ] File → Print / Print Setup
- [ ] Tools → Procedure Attributes
- [ ] Help → Contents / Index / Search / Technical Support
- [x] Code editor context menu: Cut/Copy/Paste, List Properties/Methods, List Constants, Parameter Info, Complete Word, Indent/Outdent, Go to Definition, Rename, Format Document
- [ ] Code editor context: Quick Info (NYI — needs type inference), Bookmarks (NYI)

## Menus — disabled items

- [x] View → Definition (now wired to GoToDefinitionCommand)
- [ ] View → Definition via Shift+F2 (Shift+F2 shortcut NYI — F12 still works)
- [ ] View → Last Position
- [ ] View → Call Stack
- [ ] View → Property Pages (Windows-only; see MISSING_FEATURES Windows-only section)
- [ ] Window → Split

## Keyboard shortcut conflicts

- [x] `Ctrl+R` was claimed by both `OpenProjectExplorerCommand` and `ReplaceCommand` — fixed: `ReplaceCommand` now uses `Ctrl+H` via `GetReplaceKeyGesture()`
- [x] `F2` was claimed by both `OpenObjectBrowserCommand` and `NextBookmarkCommand` — fixed: bare `F2` gesture removed from `NextBookmarkCommand`; `OpenObjectBrowserCommand` owns `F2`

---

## Dialogs — disabled tabs

**Options dialog** (`OptionsView.axaml`):
- [ ] Editor Format tab (syntax highlighting config)
- [x] General tab (Phase 20)
- [ ] Docking tab
- [x] Environment tab (Phase 20)
- [ ] Advanced tab

**Project Properties** (`ProjectPropertiesView.axaml`) — only General tab partially works:
- [ ] Make tab (EXE compilation settings)
- [ ] Compile tab (code generation options)
- [ ] Component tab
- [ ] Debugging tab
- [ ] Threading/compatibility options on General tab

**Other dialogs:**
- [ ] Components → Browse button disabled
- [ ] References → Browse and Help buttons disabled
- [ ] Add-In Manager → Help button disabled

## Visual designer

- [x] **UserControl placeholder rendering** — `PlaceholderComponentClass`, `UserControlComponentClass`, `SpawnComponentsForDesigner`, and 3-pass load order all implemented (see `usercontrol-placeholder` and `usercontrol-rendering` specs). UC-within-UC falls back to placeholder (Phase 2 deferred).
- [x] Cut/Copy/Paste in form designer context menu (Phase 33 Phase 4)
- [ ] Project explorer → Print context menu disabled

## IDE infrastructure

- [ ] **Add-in tool-window layout persistence** — an add-in's tool windows register asynchronously, so
  placement is deferred and the saved manifest has nothing to match them against on restore. Deferred at
  Phase 56 as "spec Phase 6"; rehomed here in Phase 60 because migrated specs carry requirements, not phases.
- [ ] **Open-document / file-tab session restore** — which editors and designers were open is not restored
  with the layout. Spans `ProjectManager` and `EditorService`, not just the dock manifest. Deferred at
  Phase 56 as "spec Phase 7"; rehomed here in Phase 60. Both are out of scope for
  [ide-state-persistence](../openspec/specs/ide-state-persistence/spec.md), which covers window geometry and
  the tool-window arrangement only.
- [ ] `ThemeService.Apply(Fluent)` throws `NotImplementedException` — Fluent theme not yet built
- [ ] Value converters have `ConvertBack` stubs that throw (ColorToBrush, IntToThickness, etc.) — not urgent unless two-way binding is needed
- [ ] `ProjectService.cs` line 359 — self-documented bad design in path handling for Make Project
- [ ] **`--server-port <n>` on an already-bound port fails silently.** Launching a second `HexIDE.Desktop`
  with `--server-port 5123` when another instance already owns 5123 does **not** error — it opens a normal IDE
  window that simply never serves MCP, so a tool client keeps talking to the *old* instance with no signal
  anything is wrong (cost me a whole demo run on a stale instance). The launcher should detect the bound port
  and either fail loudly (non-zero exit + message) or refuse to start the MCP-less window. Dev-loop mitigation:
  always `Stop-Process HexIDE.Desktop` and confirm the port is free *before* relaunching.

## Runtime interpreter

The VB6 interpreter has 200+ `NotImplementedException` throws across `StatementExecutor`, `ExpressionExecutor`, and `PrePass`. This is expected — the interpreter covers a subset of VB6. Key missing areas that affect real projects:

- [ ] `Property Get/Let/Set` procedures not implemented in PrePass
- [ ] `Enum` / `Type` declarations not implemented in PrePass
- [ ] `Event` / `WithEvents` / `RaiseEvent` not implemented
- [ ] `ByRef` parameter passing not supported
- [ ] `New` keyword (object creation) not implemented
- [ ] `Like` / `Is` / `TypeOf` operators not implemented
- [ ] File I/O statements (`Open`, `Close`, `Input`, `Print #`, etc.)
- [ ] `GoTo` / `GoSub` / `On Error GoTo`
- [ ] `Const` declarations
- [ ] Mod operator fails with floats/doubles (noted in `OperatorTests.cs` lines 277-278)

## LSP server

- [ ] **`Option Explicit` undeclared-variable check has no VB6 symbol table → false positives.** The
  `VbScopeAnalyzer` flags VB6 **intrinsics** (`RGB`, `Sin`, `Cos`, `vbPixels`, …), **form controls**
  (`Timer0`, `Label2`, …), and **project class/module references** (`Class1`, …) as "Variable 'X' is not
  declared." Surfaced in the MCP demo bug-sweep: `get_diagnostics` was so noisy that the one *real* error (the
  `Scale` reserved-word use) was buried, and "clean diagnostics" is not a trustworthy build-ready signal for a
  consuming agent. Fix needs a baseline symbol table: the VB6 intrinsic/runtime library, the form's controls
  (from the `.frm`), and the project's classes/modules — exclude those from the undeclared check.
  - *(FIXED — was initially mis-filed here as a "line-continuation" bug. Real cause: a blank line between
    statements inside a `For`/`For Each` body. The loop body parses via `unterminatedBlock`, whose
    inter-statement separator allowed only one end-of-statement, so a blank line ended the body early and
    orphaned the rest before `NEXT` → cascading false syntax errors. Fixed by making the separator `+`
    (absorb blank lines), matching `block`. **NB this note originally cited a test class that went with the
    GPLv3 server in the 2026-07-26 language-server swap (its inputs were GPL and could not be carried over —
    see [`lsp-parity-matrix.md`](lsp-parity-matrix.md)); neither current grammar has an `unterminatedBlock`
    rule either. Kept for the diagnosis, not as a pointer to live coverage.)*

## AI Chat add-in

- [ ] **Stale default model.** `ChatSettings.cs:17` defaults `modelId` to `claude-opus-4-8`; the current
  Opus is `claude-opus-5`. One-line currency fix, deliberately deferred 2026-08-16 (user call: AI Chat is
  fine as-is for alpha, time better spent on launch blockers). Same staleness in the spec's schema sample
  at `openspec/specs/addin-system/spec.md:589`.
- [ ] **The shipped default path runs through Anthropic's OpenAI-compatibility layer.** Verified reachable
  (`POST https://api.anthropic.com/v1/chat/completions` → 401 `authentication_error`, not 404), and the
  OpenAI-compatible design is a deliberate spec decision (`addin-system/spec.md:561`) that buys OpenAI /
  Ollama / LM Studio / local models. But Anthropic documents that layer as a migration aid rather than a
  production surface: no adaptive thinking, no effort control, weaker tool-use fidelity. If AI Chat is
  going to be a first-class pillar (a Wave 3 item in the Evolution catalog), the native `/v1/messages` client should sit
  alongside the OpenAI one, selected by base-URL host — not replace it.
- [ ] **Settings are read once at panel construction** — editing `chat-settings.json` needs an IDE restart.
- [ ] **No cancellation.** `ChatViewModel.RunAgenticLoopAsync` never passes a `CancellationToken` to
  `StreamAsync`, so an in-flight response cannot be stopped; `IsResponding` blocks input until it ends.

## Code quality notes

These are self-documented design concerns in the existing code:
- [ ] `ApplyAllUnsavedChangesEvent` — marked "pretty bad design" (lazy form saving architecture)
- [ ] `ProjectToolViewModel.cs:193` — logic doesn't belong in VM
- [ ] `CommandManager.cs` — multiple TODO markers on WPF→Avalonia DataContext binding
