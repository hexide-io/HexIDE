# HexIDE — VB6 IDE Fidelity Catalog

**Purpose:** Systematic assessment of every user-facing surface against VB6 IDE behaviour. Used to drive implementation work.  
**Status legend:** `Done` · `Partial` · `Stub` · `Missing` · `Windows-only`  
**This file contains only in-scope features.** Menu items and toolbar buttons that belong to out-of-scope features are not listed here — they are catalogued by UI surface in [OUT_OF_SCOPE.md](OUT_OF_SCOPE.md).  
**`Windows-only` means the feature is in scope but gated on `OperatingSystem.IsWindows()`.** See the Windows-only section at the bottom of this file.
**`Partial` does not always mean *yet*.** A few rows are partial **by design** and will not advance here — the language-intelligence ones, where going further needs a bound AST that HexIDE's own tooling does not build (see the CST-not-AST limit in CLAUDE.md). Those say so in their Notes, and the missing depth belongs to a language engine attached over the LSP seam rather than to a backlog. Read the Notes before treating a `Partial` as available work.
**Modern-shell disposition:** whether a fidelity row is kept, changed, or removed in the modernised (Evolution-tier) IDE is catalogued in the maintainers' Evolution catalog — check it before implementing a row verbatim.

---

## File Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| New Project | Ctrl+N | Done | |
| Open Project… | Ctrl+O | Done | |
| Add Project… | — | Done | Adds second project; implicit `CurrentGroup` created when >1 project loaded |
| Remove *{project}* | — | Done | |
| Save Project | — | Partial | Saves every member HexIDE can reproduce; members it cannot are left untouched on disk and reported together in one dialog. With a group open it saves every loaded project and the group as one operation: every destination, the `.vbg` path included, is asked for first, then every file is checked against its code page, and only then is anything written, so a dismissed picker or a refusal leaves every file as it was. Recognised `.vbp` lines retain their read order, including kept and reclassified members (#855). See `Save {file}` |
| Save Project As… | — | Done | |
| Save *{file}* | Ctrl+S | Partial | **All 22 of VB6's own template forms survive a save byte-for-byte**, companions included, and none is held read-only. The last held, `Web Browser.frm`, was freed by [#60](https://github.com/hexide-io/HexIDE/issues/60): the companion records cited by a control or property HexIDE does not model are carried with it, and a save rewrites each citation to wherever its record lands. Menu trees round-trip as of #83, container nesting as of #84, and the companion-file model closed the rest (#107–#113); VB6's seven `.vbp` files round-trip as of #116. A save now writes a designer file and its companion **together or not at all** ([#148](https://github.com/hexide-io/HexIDE/issues/148)) — the text write used to be unconditional while the companion write could refuse on a measure of its own, leaving a `.frm` citing renumbered offsets into a companion that still held the old partition, which then reopened as faithful. A form separated from its `.frx` is held too, as of [#146](https://github.com/hexide-io/HexIDE/issues/146) — the gate counts what the `.frm` *cites* rather than what the companion yielded, so an absent or truncated companion is visible to it; previously such a form opened freely and an ordinary Ctrl+S stripped every citation from the developer's own file. Still `Partial`, not `Done`: the measured corpus is VB6's own templates rather than real-world projects. A control HexIDE does not model is carried, and since [#157](https://github.com/hexide-io/HexIDE/issues/157) is shown in the designer where VB6 shows it, so `Web Browser.frm` edits with its ImageList and Toolbar in view. Epic [#21](https://github.com/hexide-io/HexIDE/issues/21); contract in [serialization-round-trip](../openspec/specs/serialization-round-trip/spec.md) |
| Save *{file}* As… | — | Done | Subject to the same faithfulness gate as `Save {file}`, at a new path too, and refused *before* the destination is asked for. It used to be exempt — "the original file is not at risk" — which is true of the original and silent about the copy: `WriteCompanionBinary` can only protect a companion that already exists, so at a new path the blobs are dropped, and the copy then reopens as **faithful** because the citations that flagged it are exactly what went missing ([#143](https://github.com/hexide-io/HexIDE/issues/143)). **Not covered:** the Make EXE package still serializes every form to a temp directory with no gate |
| Print… | — | Stub | Bound to `NYICommand`; shows "not yet implemented" |
| Print Setup… | — | Stub | Bound to `NYICommand` |
| Make *{project}*… | — | Partial | Invokes HexIDE interpreter export; no real native-code compile. Repeated Makes to the same path replace the project archive with the current source (#748). The made program runs every form, standard module and class module of the project, from its startup form or from `Sub Main`, read by the loader the IDE's own open uses ([#1062](https://github.com/hexide-io/HexIDE/issues/1062)). Its packaged source copies use UTF-8 with a BOM so non-ASCII text survives a different host code page (#731). It still stops silently on a run-time error ([#757](https://github.com/hexide-io/HexIDE/issues/757)) |
| Make *{project}* with VB6… | — | Done | Shells out to VB6.EXE if installed |
| Make Project Group… | — | Stub | Offered only with a group open, as in VB6 (#746); removes `DisabledCommand`. VB6's builds the group's projects through a "Build the following projects" dialog. HexIDE's says the feature isn't implemented yet and writes nothing ([#1515](https://github.com/hexide-io/HexIDE/issues/1515)). It used to save every project and the group, which is Save Project Group's job |
| MRU file list | — | Done | "Recent Projects ►" submenu in File menu; backed by `IRecentProjectsService`; refreshes on project open/close |
| Exit | Ctrl+Q | Done | Calls `IClassicDesktopStyleApplicationLifetime.Shutdown()` |

---

## Edit Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Undo | Ctrl+Z | Done | AvaloniaEdit undo; in the code window it steps past the writes the IDE makes itself (see Code Editor Window, Undo / Redo; #273) |
| Redo | Ctrl+Shift+Z | Done | AvaloniaEdit redo |
| Cut | Ctrl+X | Done | |
| Copy | Ctrl+C | Done | |
| Paste | Ctrl+V | Done | |
| Paste Link | — | Missing | Windows-only; see below. OLE linked object; depends on OLE Container infrastructure |
| Remove | — | Stub | MenuItem present, no handler; context-dependent in VB6 |
| Delete | Del | Done | |
| Select All | Ctrl+A | Done | |
| Find… | Ctrl+F | Done | |
| Find Next | F3 | Done | |
| Replace… | Ctrl+H | Done | |
| Indent | Ctrl+Tab | Done | AvaloniaEdit indent |
| Outdent | Shift+Tab | Done | AvaloniaEdit outdent |
| Insert File… | — | Done | Puts a text file's contents in place of the selection, or, when the selection is in the header or a member's `Attribute` lines, after them on lines of its own (#273). Its picker is the window manager's, so automation's `answer_next_file_dialog` answers it ([#532](https://github.com/hexide-io/HexIDE/issues/532)). **This row said `Stub`, "no file-insertion handler wired", until 2026-10-14, though the handler (`CodeEditorView.InsertFile`) has been wired since the repository's first commit** |
| List Properties/Methods | Ctrl+J | Done | Triggers LSP completion |
| List Constants | Ctrl+Shift+J | Done | Triggers LSP completion |
| Quick Info | Ctrl+I | Done | Ctrl+I calls `TriggerQuickInfo()` → LSP hover at caret; auto-hover on mouse-over also active |
| Parameter Info | Ctrl+Shift+I | Done | Triggers LSP signature help |
| Complete Word | Ctrl+Space | Done | Triggers LSP completion |
| Bookmarks | Ctrl+F2 / F2 / Shift+F2 / Ctrl+Shift+F2 | Done | Toggle/next/previous/clear-all; cyan circle gutter margin; persisted per project in the `.user.hexproj` sidecar via `UserSidecarService` (**exceeds VB6**, which lost bookmarks on close). Not set on a line of the header or of a member's `Attribute` lines, which are read-only (#273) |

---

## View Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Code | — | Done | Opens code editor for active form/module |
| Object | Shift+F7 | Done | Opens form designer for active form |
| Definition | — | Done | LSP go-to-definition |
| Last Position | — | Stub | Always disabled |
| Object Browser | — | Partial | Two-panel browser implemented: library/class/member tree, search (local filter plus a workspace-wide `workspace/symbol` query of every capable running server), Back/Forward, Go-to-Definition, LSP member loading, description panel; pre-launch gaps: Unicode placeholder icons pending bitmap replacement (#11), plus three behavioural defects — member list cached so later edits do not appear, double-click opens the containing file rather than the member definition, Escape does not close (#24); intrinsic control class names shown and searchable (#905) |
| Immediate Window | Ctrl+G | Done (Phase 4 + P7c) | Editable buffer; `?expr`/`Print expr`/bare expr evaluates against the paused frame. A `?` line prints as `Debug.Print` does, ` 5 ` for 5 and lists with `;` and `,`, as VB6's Immediate window was measured to (#1386); a bare **assignment or `Set` now EXECUTES** and mutates the frame (`count = 5` assigns, `?count = 5` compares — P7c); user Sub/Function calls still rejected (deadlock-prone — D14–D15); Debug.Print output lands here; MCP `evaluate` |
| Locals Window | — | Done (Phase 3 + P8) | Expandable tree (Expression/Value/Type) of the paused frame; UDT/array/object drill-in, cycle+depth guarded; a **live control expands to its VB6 property surface** (P8, D7) and tree **expansion is preserved across a break** (D10); native `TreeView` (TreeDataGrid is commercially licensed in 12.x); MCP `get_locals`. Residual: form `Me`/child-control-tree/proxy expansion (D7) |
| Watch Window | — | Done (P6a) | Watches window (Expression/Value/Type/Context, expandable tree), re-evaluates each watch on every break, blanks on continue; Add/Edit/Delete + the three VB6 watch types; MCP `add_watch`/`get_watches` |
| Call Stack… | Ctrl+L | Done (Phase 5) | Dockable Call Stack tool window; lists the running activation chain (Module.Procedure + Line, current frame arrowed) from `IDebugController.GetCallStack()`; rebuilt on each break, cleared on Continue; MCP `get_call_stack`. Additive vs VB6's modal list — D16–D17 |
| Project Explorer | Ctrl+R | Done | Menu item enabled; opens/focuses Project Explorer tool window |
| Properties Window | F4 | Done | |
| Form Layout Window | — | Done | Monitor illustration with correct aspect ratio and live form thumbnail (StartUpPosition, Left, Top, Width, Height) |
| Property Pages | — | Missing | Windows-only; see below. `IPropertyPage`/`IPropertyPageSite` COM runtime display |
| Toolbox | — | Done | |
| Color Palette | — | Done | |
| Toolbars > Standard | — | Done | Toggle persisted; right-click context menu on toolbar band |
| Toolbars > Edit | — | Done | 12 buttons: IntelliSense, indent/outdent, breakpoint, bookmarks |
| Toolbars > Debug | — | Done | 12 buttons: Start/Break/End, breakpoint, Step Into/Over/Out, Locals/Immediate/Watches/Call Stack, Quick Watch |
| Toolbars > Form Editor | — | Done | Bring to Front/Back, 6 alignment, 3 make-same-size, Lock Controls; visibility persisted to settings |

---

## Project Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Add Form | — | Done | Creates Form2, Form3… with defaults and opens the designer. Like every new component it has no file until the project is saved, as in VB6 (#500) |
| Add MDI Form | — | Missing | MDI is not COM-dependent; implementable cross-platform as an Avalonia container with floating child sub-windows |
| Add Module | — | Done | Creates Module1, Module2… with defaults; opens code editor; saves to project dir |
| Add Class Module | — | Done | Creates Class1, Class2… with defaults; opens code editor; saves to project dir |
| Add User Control | — | Done | Creates UserControl1, UserControl2… with empty designer surface; opens designer; saves .ctl to project dir. The designer draws it as VB6's does, a borderless surface with no title bar, and names it UserControl in its tab and the object box (#1544). Its Properties window lists only properties VB6's UserControl has (#1585); its own, CanGetFocus, ControlContainer, InvisibleAtRuntime, Public and the rest, are not modelled ([#1631](https://github.com/hexide-io/HexIDE/issues/1631)). Its code window offers VB6's 35 UserControl events with their arguments, and a double-click on its background writes UserControl_Initialize (#1632) |
| Add Property Page | — | Done | Creates PropertyPage1, PropertyPage2… with empty designer surface; opens designer; saves .pag to project dir. Drawn and named as VB6 does, a borderless surface named PropertyPage (#1544); its Properties window lists only properties VB6's PropertyPage has (#1585), not yet HelpContextID or StandardSize ([#1631](https://github.com/hexide-io/HexIDE/issues/1631)). Its code window offers VB6's 24 PropertyPage events, and a double-click on its background writes PropertyPage_SelectionChanged (#1632) |
| Add File… | — | Done | Adopts an existing file: classified by extension, VB6 source joins as a form/module, anything else as a related document. Multi-select; re-picking a carried file re-opens it instead of duplicating |
| Remove *{file}* | — | Stub | MenuItem present, no Command binding; use project tree context menu |
| References… | — | Done | Registry enumeration on Windows; Browse button; persisted to .vbp. Lists VB6's three fixed rows, then the project's references in order, OLE Automation among them; OK with nothing changed leaves them as they were (#1026, measured). The title and the Language line differ from VB6's ([#1468](https://github.com/hexide-io/HexIDE/issues/1468)) |
| Components… | — | Stub | Dialog opens; Apply command is a no-op; only lists hardcoded built-in controls |
| *{project}* Properties… | — | Partial | Name/description/startup form/project type editable; project type dropdown limited to EXE |

---

## Format Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Align (submenu) | — | Done | Full multi-select alignment (Lefts/Rights/Tops/Bottoms/Centers H/V) |
| Make Same Size (submenu) | — | Done | Same Width / Same Height / Same Size; requires multi-select |
| Size to Grid | — | Done | Snaps all selected controls to grid |
| Horizontal Spacing (submenu) | — | Done | Make Equal / Increase / Decrease / Remove |
| Vertical Spacing (submenu) | — | Done | Make Equal / Increase / Decrease / Remove |
| Center in Form > Horizontally | — | Done | Single-control centering, in whole twips as VB6 centres: ScaleWidth - Width (#929, measured) |
| Center in Form > Vertically | — | Done | Single-control centering, in whole twips as VB6 centres (#929, measured) |
| Order > Bring to Front | — | Done | |
| Order > Send to Back | — | Done | |
| Lock Controls | — | Done | Toggles drag/resize; persisted in .frm (LockControls = -1) |

---

## Debug Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Step Into | F8 | Done | Steps one statement, descending into called Subs/Functions; F8 from idle starts + breaks at the first statement (VB6 start-and-step). Native debugger Phase 2 |
| Step Over | Shift+F8 | Done (Phase 5) | Runs a called Sub/Function to completion, breaks at the next statement in the current frame (depth-based over the activation stack); on a non-call = Step Into. Native debugger Phase 5; MCP `step_over` |
| Step Out | Ctrl+Shift+F8 | Done (Phase 5) | Runs the rest of the current procedure, breaks in the caller; from the top-level frame runs to completion. Native debugger Phase 5; MCP `step_out` |
| Run To Cursor | Ctrl+F8 | Done (P7a) | Runs to the caret line (a one-shot temp breakpoint in the gate); paused → continue to target, running → arm, idle → start-and-run. MCP `run_to_cursor`, which refuses a line of the header or of a member's `Attribute` lines (#273); the menu does not, and from such a line runs on as from any line that never executes |
| Add Watch… | — | Done (P6a) | Opens the Add Watch dialog (Expression + Context + the three VB6 Watch Types); adds to the Watches window. MCP `add_watch` |
| Edit Watch… | Ctrl+W | Done (P6a) | Re-opens the Add Watch dialog for the Watches window's selected row |
| Quick Watch… | Shift+F9 | Done (P6a) | Opens the Add Watch dialog pre-filled with the identifier under the caret |
| Toggle Breakpoint | F9 | Done | Toggles a breakpoint on the active editor's caret line (red gutter margin, click-to-toggle); persists per-project in the `.user.hexproj` sidecar. Native interpreter debugger, Phase 1. Refused on a line of the header or of a member's `Attribute` lines, which never execute, and the status bar says why ([`debugger-vb6-divergences.md`](debugger-vb6-divergences.md), "No breakpoint or bookmark on a header line or a member's `Attribute` line"; #273) |
| Clear All Breakpoints | Ctrl+Shift+F9 | Done | Removes every breakpoint in the project |
| Set Next Statement | Ctrl+F9 | Done (P7b) | Moves the execution point to the caret line without running the statements between. **Top-level-body granularity only** — a nested target / a move while paused inside a block is refused (an interpreter limit — the top-level body is a pc-addressable loop, nested blocks are recursive descent; D18). MCP `set_next_statement` |
| Show Next Statement | — | Done (P7a) | Reveals the paused module's current-statement line (scrolls to + shows the amber bar) |

---

## Run Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Start | F5 | Done | Runs project via HexIDE interpreter in a new window |
| Start With Full Compile | Ctrl+F5 | Stub | Calls same code path as Start; compile step not distinct |
| Break | Ctrl+Break | Done | Requests a pause at the next statement gate while the interpreter is running |
| End | — | Done | |
| Restart | Shift+F5 | Done | |
| Start with VB6 | Alt+F5 | Done | Shells out to VB6.EXE if installed |

---

## Tools Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Add Procedure… | — | Done | Generates Sub/Function/Property/Event stubs |
| Procedure Attributes… | — | Stub | Bound to `NYICommand` ([#62](https://github.com/hexide-io/HexIDE/issues/62)). Since #273 the `Attribute` lines this dialog writes are read-only in the code window, so it is the only way the IDE could change them. Until it exists they cannot be edited in the IDE, and a save keeps them, re-qualified when their member is renamed (see Code Editor Window) |
| Menu Editor… | Ctrl+E | Done | Full menu tree editor |
| Options… | — | Done | 2-pane TreeView redesign (Phase 46, complete): Environment/Editor/Form Designer/Advanced pages; per-page + global reset-to-defaults; Add-Ins section with per-add-in details, activate/deactivate, and add-in-contributed settings pages (`host.Options.RegisterOptionsPage`) |

---

## Window Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Split | — | Stub | Always disabled |
| Tile Horizontally | — | Done | Publishes rearrange event; MDI host responds |
| Tile Vertically | — | Done | Publishes rearrange event; MDI host responds |
| Cascade | — | Done | Publishes rearrange event; MDI host responds |
| Arrange Icons | — | Stub | MenuItem present, no Command binding |
| Reset Window Layout | — | Done | **HexIDE addition** (not in VB6) — rebuilds the default tool-window layout, keeping open documents. See [ide-state-persistence](../openspec/specs/ide-state-persistence/spec.md) Phase 4 |
| Window list | — | Done | Dynamic items from `MdiWindowManager.Windows` |

---

## Help Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Contents… | — | Stub | Bound to `NYICommand` |
| Index… | — | Stub | Bound to `NYICommand` |
| Search… | — | Stub | Bound to `NYICommand` |
| Technical Support | — | Stub | Bound to `NYICommand` |
| Avalonia on the Web | — | Done | Opens GitHub repo URL |
| About Avalonia Visual Basic… | — | Done | |

---

## Standard Toolbar

| Button | Status | Notes |
|---|---|---|
| Add Project (with flyout: Standard EXE / ActiveX EXE / DLL / Control) | Done | Only Standard EXE creates a project; the ActiveX EXE, DLL and Control entries are refused. An implicit group is created when >1 project is loaded |
| Add Form (with flyout) | Done | Flyout wired: Form, MDI Form (missing — not yet implemented), Module, Class Module, User Control, Property Page, Add File… |
| Menu Editor | Done | |
| Open Project | Done | |
| Save Project | Done | |
| Cut | Done | |
| Copy | Done | |
| Paste | Done | |
| Find | Done | |
| Undo | Done | |
| Redo | Done | |
| Start | Done | |
| Break | Done | Requests a pause at the next statement gate while the interpreter is running |
| End | Done | |
| Project Explorer | Done | Opens/focuses tool window |
| Properties | Done | Opens/focuses tool window |
| Form Layout | Done | Opens Form Layout tool window with live form position thumbnail |
| Object Browser | Partial | Opens Object Browser document tab; Unicode placeholder icons pending (#11); stale member list, double-click position, and Escape-to-close outstanding (#24); intrinsic control class names shown and searchable (#905) |
| ToolBox | Done | |
| Data View | Stub | Button present; no Data View window |
| Position display (twips) | Done | Shows selected component position |
| Size display (twips) | Done | Shows selected component size |

---

## Code Editor Window

| Feature | Status | Notes |
|---|---|---|
| Syntax highlighting | Done | In the editor itself, from `VB6.xshd.xml`, with a dark palette swapped in for a dark theme (`SyntaxHighlightingTheme`); not from the language server, as this row used to say. Keywords match regardless of case, including `Begin` and standalone `End` (#716). The header and member `Attribute` lines are drawn in the theme's read-only grey instead (#273) |
| Error/warning squiggles | Done | Full LSP diagnostic pipeline; inline markers. The bundled server reports nothing that starts on a line of the header or of a member's `Attribute` lines (#273) |
| Object/Procedure dropboxes | Done | Two combos at top; navigate to handler on select. **Since the code window holds the whole file (#273), the search for the handler starts at the top of the header**, so text naming it in a designer property or a member's `Attribute` line can take the caret there, and a handler not yet written is then never stubbed ([#702](https://github.com/hexide-io/HexIDE/issues/702), [#711](https://github.com/hexide-io/HexIDE/issues/711)) |
| Auto-complete (IntelliSense) | Partial | LSP completion: 87 keywords, 42 built-ins, and every name declared in the current file. **No position awareness** — the same list comes back wherever the caret is — and no member access (`obj.`) or cross-file symbols. Position awareness is ordinary work; the other two need name binding, which belongs to a real language engine over the replaceable LSP seam, not to HexIDE's own server (see the CST-not-AST limit in CLAUDE.md and [`lsp-server-features.md`](lsp-server-features.md)) |
| Signature help | Partial | LSP signature help across 89 VB6 built-ins, with active-parameter tracking through nested parentheses. **User-defined procedures show nothing** — matching a call site to its declaration is name binding, which belongs to a real language engine over the replaceable LSP seam, not to HexIDE's own server (see the CST-not-AST limit in CLAUDE.md and [`lsp-server-features.md`](lsp-server-features.md)) |
| Go to Definition | Partial | Works within the same file, for procedure-level symbols (`Sub`, `Function`, `Property`, `Enum`, `Type`) but not variables. **Cross-file navigation is not future work** — it needs a workspace model and name binding, which belongs to a real language engine over the replaceable LSP seam, not to HexIDE's own server (see the CST-not-AST limit in CLAUDE.md and [`lsp-server-features.md`](lsp-server-features.md)). Resolving *variable* declarations within the file is ordinary work and stays on this side of the line |
| Rename symbol | Partial | LSP rename with an inline input dialog, applied as one atomic edit (a single Ctrl+Z undoes it). **Lexical, not semantic, and single-file: it renames every whole-word case-insensitive match, including unrelated symbols that happen to share the name.** That is a correctness hazard rather than a missing nicety, and it is the reason this is not `Done` — semantic rename is named explicitly in the CST-not-AST limit, so it belongs to a real language engine over the replaceable LSP seam, not to HexIDE's own server (see the CST-not-AST limit in CLAUDE.md and [`lsp-server-features.md`](lsp-server-features.md)). No `prepareRename` is sent, so the server never validates the position. **The IDE refuses, with a reason, a rename that would damage the file (#273)**, so the old wording here, that the position was never pre-validated, no longer holds on the IDE's side. Before the prompt it refuses a caret in the header or a member's `Attribute` lines, and a name the designer block declares (a control is renamed in the Properties window, as in VB6). After it, a new name that is not a VB6 identifier, or is one of the six words `ProjectNaming.IsReservedWord` refuses (`Rem`, `Empty`, `New`, `Nothing`, `Me`, `Attribute`), each measured refused on `vb6.exe`; no other keyword is refused. And a server's answer that would change the header or a member's `Attribute` lines is refused whole rather than applied in part, unless the change is the renamed member's own qualifier in its own run. Those may follow the rename, and the document's next save rewrites them to the declared name in any case, as VB6's does. The bundled server keeps to the same lines itself since #273: it matches nothing in the header, and in a member's `Attribute` lines only the renamed member's own qualifier |
| Format document | Done | LSP formatting; configurable format-on-save. **Never applied as sent** (#273): the answer is reduced to the lines it changes, and any change to the header or a member's `Attribute` lines is dropped. The bundled server used to answer with one edit over the whole document, and applied as sent that re-indented a form's designer block and, because the code is split from the header by the header's length, cut into the code below it: Format on Save lost a VB6-authored form's whole attribute block and the sixteen lines after it. Since #273 the bundled server answers with one edit per run of changed lines and leaves the header and every member's run out of all of them (`VbFormatter.Edits`), so the reduction is what protects the file from any other server |
| Find / Replace | Partial | See Dialogs section. Searches any document with a text buffer — code windows and the carried-file editor alike — under either of two scopes; a third, project-wide, is not offered ([#372](https://github.com/hexide-io/HexIDE/issues/372)). A match in the header or a member's `Attribute` lines is skipped by every scan and by Replace All (#273), as VB6's Find never searched the header it hid. Text that exists only there is reported not found even when it is on screen, and Replace All does not say it left matches there ([#705](https://github.com/hexide-io/HexIDE/issues/705)). The review that built this found two older scan defects: Up from offset 0 under All Open Documents searches the whole active document first ([#703](https://github.com/hexide-io/HexIDE/issues/703)), and Find Next skips a match right after the one selected ([#704](https://github.com/hexide-io/HexIDE/issues/704)) |
| Undo / Redo | Done | AvaloniaEdit native. Undo in the code window never undoes a write the IDE made itself, which since #273 lands in the same buffer: a designer change's header, a form's `VB_Name` after a rename, the attribute qualifiers a save rewrote. It steps past them to the developer's own last edit and puts them back, and a code edit undone across one of them cannot then be redone |
| Indent / Outdent | Done | AvaloniaEdit native |
| Cut / Copy / Paste / Select All | Done | |
| Add Procedure | Done | Stub generation dialog |
| Line numbers | Done | `ShowLineNumbers="True"` on `TextEditor`; rendered by AvaloniaEdit's built-in margin. **Counted from the first line of the file, header included** (#273), as are the status bar, breakpoints, bookmarks, the current-statement bar, the Call Stack, the automation tools and the add-in surface, so in a class with the standard header the first line of code is line 14. A folded header or attribute run is skipped in the margin like any folded lines. VB6's code window had no margin and hid the header; see [`debugger-vb6-divergences.md`](debugger-vb6-divergences.md), "Line numbers count from the top of the file, header included". A sidecar written before the change counts from the first line of code, and is migrated as its project opens |
| Breakpoint margin | Done | Red gutter dots (click-to-toggle) via `BreakpointMargin`; native interpreter debugger, Phase 1. A click on a line of the header or of a member's `Attribute` lines sets nothing, the folded header's row included (#273) |
| Current-line indicator | Done | Amber full-width "current statement" bar via `CurrentLineRenderer`, driven by the debugger's Stopped event; native interpreter debugger, Phase 1 |
| Code folding | Done | `FoldingManager` installed; the bundled server's `VbFoldingProvider` over LSP, merged with the folds the code window makes itself: the file's header, folded when the window opens and present with no server attached, and each member's `Attribute` run, folded into the end of the line it describes and, for a procedure's, nested inside the server's fold of the procedure (#273). A one-line header, a `.bas`'s `Attribute VB_Name`, has no fold, since folding one line hides nothing |
| Quick Info on hover | Done | 400 ms delay hover → LSP `textDocument/hover` → tooltip; Ctrl+I triggers immediately. In **break mode** the same pipeline branches to a **Data Tip** — the hovered variable's live value (native debugger P6c) |
| Bookmarks | Done | `IBookmarkService` + `BookmarkMargin` gutter (cyan circles); toggle/navigate/clear-all keybindings; persisted in the `.user.hexproj` sidecar; not set on a line of the header or of a member's `Attribute` lines (#273) |
| Procedure View / Full Module View toggle | Missing | Won't implement — closed by the Evolution catalog's Remove table; code folding / sticky scroll / outline are the modern replacements |
| Insert File | Done | See the Edit menu row. Catalogued here as `Stub`, "no handler", while it worked |
| Find scope (project / all docs) | Partial | **All Open Documents** works — Find and Replace All both cross every open document with a text buffer, and a match in another one brings that tab to the front. **Current Project** is not offered at all: the combo entry was removed rather than left decorative, because a module that is not open has only its last-*saved* text in `ModuleDefinition.Code`, so searching it would report hits at offsets that no longer exist ([#372](https://github.com/hexide-io/HexIDE/issues/372)) |
| View Object (switch to designer) | Done | |
| Read-only banner for unsaveable forms | Done | **HexIDE addition** (not in VB6) — a form HexIDE cannot reproduce on save opens with its code editor read-only and a banner giving the reason (`CodeEditorViewModel.IsReadOnly`). Viewing still works, and so does running, unless the form holds a control HexIDE cannot create or a data-bound control (#859), which is refused when it runs ([#857](https://github.com/hexide-io/HexIDE/issues/857)). Typing is refused by the code window's own section provider (`CodeWindowReadOnlySections`), which carries this verdict beside the read-only regions since the editor's `IsReadOnly` binding was removed (#273); the programmatic writers (Replace All, formatting, rename, Insert File, add-ins, automation) do not ask it yet ([#701](https://github.com/hexide-io/HexIDE/issues/701)). The form as a whole is not a read-only region: its code still takes breakpoints, and Find searches all of it. Rationale in [serialization-round-trip](../openspec/specs/serialization-round-trip/spec.md) |
| File header and member `Attribute` lines in the code window | Done | **Diverges from VB6 by design**: VB6's code window hid these lines, and HexIDE's shows them, read-only (#273). The window holds the whole file: a `.cls`'s `VERSION`/`BEGIN`…`END` block and leading `Attribute` run, a `.bas`'s `Attribute VB_Name` line, a form's, UserControl's or PropertyPage's designer block and attribute run, and the `Attribute` lines VB6 writes after a procedure's first line or after a module-level variable. All are read-only and drawn in the theme's read-only grey (`ReadOnlyText`). The header is folded when the window opens, unless it is one line, as a `.bas`'s is; each member's run is folded into a box at the end of the line it describes. Only the IDE changes them: a designer change rewrites the header as an edit the code window's Undo steps past, a form's `VB_Name` follows its rename, deleting or cutting a described line takes its run along, and Enter at the end of one puts the new line after the run. A paste or Insert File replacing the whole declaration keeps the run under the first compatible logical declaration in the inserted text, including after comments or blank lines (#1039). A run typed by hand under a declaration is read-only from the moment it describes it, which for a procedure is its first `.`. A whole replacement (add-in `SetContent`, automation `set_file_content`) is refused if it would change the header; a member's run may move with its member, be renamed with it or go with its declaration, and any other change to one is refused. A save rewrites each procedure's run, and the run a variable's rename left, to name the member as declared, whenever VB6 would write the file, as VB6 does; otherwise the lines are kept byte for byte. Add-in range edits also compare the complete result and refuse added, extended or orphaned member runs (#1012). A run under no line it can describe is ordinary text. Editing a member's attributes is the Procedure Attributes dialog's job, which does not exist yet ([#62](https://github.com/hexide-io/HexIDE/issues/62)) |

---

## Form Designer

| Feature | Status | Notes |
|---|---|---|
| Draw control from toolbox | Done | Click-drag on canvas spawns control |
| Grid display | Done | Visual dot grid; configurable size |
| Snap to grid | Done | Controlled by Options |
| Select single control | Done | |
| Move control | Done | Drag via title grip |
| Resize control (all 8 handles) | Done | Full ResizeAdorner |
| Delete selected control | Done | |
| Bring to Front / Send to Back | Done | |
| Center Horizontally / Vertically | Done | Single control only |
| Double-click → open event handler | Done | Generates handler stub; opens code editor. Since the code window holds the whole file (#273), the search for an existing handler starts in the header, so text naming it in a designer property or a member's `Attribute` line can take the caret there, and a handler not yet written is then never stubbed ([#702](https://github.com/hexide-io/HexIDE/issues/702), [#711](https://github.com/hexide-io/HexIDE/issues/711)) |
| Properties grid integration | Done | Bidirectional; all property types |
| Color picker (via Color Palette) | Done | |
| Font picker (via Property grid) | Done | |
| Form resize | Partial | Bottom, right, and bottom-right handles present; top and left edges not resizable |
| Multi-select | Done | Rubber-band and Ctrl+click; group drag; primary/secondary handles |
| Alignment commands (align left/right/top/bottom) | Done | Full set via Format > Align; Form Editor toolbar. Centers and Middles land where VB6's do, in whole twips (#929, measured) |
| Make Same Size commands | Done | Same Width / Height / Size via Format > Make Same Size |
| Spacing commands | Done | Horizontal and Vertical spacing submenus fully implemented. Make Equal truncates the gap to a whole twip and leaves the outermost controls, as VB6 does (#929, measured across) |
| Copy / Paste controls | Done | Ctrl+C/V duplicates selected control(s) with offset; multi-paste accumulates offset. A control's properties HexIDE does not model are copied with it (#1453). A copied container is pasted without the controls in it HexIDE does not model, whose names would repeat the originals' ([#1458](https://github.com/hexide-io/HexIDE/issues/1458)) |
| Cut control | Done | Ctrl+X removes selected controls; Ctrl+V restores at offset, with what HexIDE carries without modelling: a control's own unknown properties, and the controls inside a container, with the companion records they cite. Before [#1453](https://github.com/hexide-io/HexIDE/issues/1453) both were left out, and the next save lost them. Cut into another form, the controls inside go only if that form declares the libraries the first one did (#1458) |
| Undo / Redo | Partial | Phase 1: structural ops (add/delete/cut/paste/z-order/lock) undoable via Ctrl+Z. Phase 2: move/resize via drag undoable. Phase 3 (properties/format) pending. |
| Tab Order editor | Missing | No visual tab-order tool. TabIndex is assigned as VB6's designer assigns it (#925, measured): a control added or pasted takes the next after the highest, counting only the classes that have one, a Label and a Frame included, so the first is 0 and a Timer takes none. Deleting a control leaves a gap; what VB6 does then is not measured, though it renumbers a file's gaps when it loads one (`docs/vb6-designer-geometry.md`) |
| Lock Controls | Done | Toggles drag/resize; persisted in .frm; toolbar toggle reflects state |
| Smart / alignment guides | Missing | Grid only |
| Property search / filter | Missing | No search box in Properties window |
| Control rendering quality | Partial | Bitmap snapshot via `ControlRenderer`; text/font preview approximate |
| Read-only designer for unsaveable forms | Done | **HexIDE addition** (not in VB6) — a form HexIDE cannot reproduce opens read-only with a banner (`FormEditViewModel.IsReadOnly`). None of VB6's own 22 template forms is held now: #60 carries the companion records cited by unmodelled controls, including `Web Browser.frm`'s ImageList. The remaining gates are described in the Save {file} row above ([#21](https://github.com/hexide-io/HexIDE/issues/21)) |
| Controls HexIDE does not model | Done | **Diverges from VB6 by design** in one respect: each is drawn as a box where VB6's IDE draws the empty PictureBox it puts in such a control's place, at its `Left` and `Top`, sized by its `Width` and `Height` or VB6's 1200 × 480 twips without them (measured; `_ExtentX`/`_ExtentY` play no part), and HexIDE labels the box with the control's class where VB6 leaves it empty. Inert: it cannot be selected or moved, and the control is written back from the block it was read as ([#157](https://github.com/hexide-io/HexIDE/issues/157)). A box inside a Frame or PictureBox moves, deletes and undoes with it. A box is drawn in its own place among the controls the designer models, as VB6 stacks the control: over its container, and over or under each control as the file and Bring to Front or Send to Back put it (#1553) |

---

## Project Explorer (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Project / Forms / Modules tree | Changed | VB6's type-grouped view deliberately replaced by an always-on filesystem hierarchy (muscle-memory override, 2026-06-12) — see [project-explorer](../openspec/specs/project-explorer/spec.md) |
| View Code | Done | |
| View Object | Done | |
| Set as Startup Project | Done | |
| Add Form (context menu) | Done | The same operation as Project > Add Form: the form's file is written and its designer opens (#468). It used to add a form with no file and open nothing. |
| Remove Form | Done | |
| Project Properties (context menu) | Done | |
| Toggle folders | Removed | VB6's button toggled the virtual type folders; both removed with the filesystem-hierarchy rendering (labelled commit, Classic-fork revertible) — [project-explorer](../openspec/specs/project-explorer/spec.md) Phase 1 |
| Save *{file}* (context menu) | Stub | Menu item present; no Command binding |
| Save *{file}* As… (context menu) | Stub | Menu item present; no Command binding |
| Add Module / Class / UserControl (context menu) | Missing | Only "Add Form" in the Add submenu; Module/Class available via Project menu |
| Drag-and-drop reorder | Missing | No drag support; drag-move-to-folder sketched in [project-explorer](../openspec/specs/project-explorer/spec.md) Phase 3 |
| Print (context menu) | Stub | Bound to `DisabledCommand` |
| Directory tree for subfolder members | Done | **HexIDE addition** — always-on filesystem hierarchy below the `.vbp`, [project-explorer](../openspec/specs/project-explorer/spec.md) Phase 1 (implemented 2026-06-12) |
| Outside-member rendering (`..\` / absolute) | Missing | Renders at project root today (filename caption); relative-path captions + the absolute-entry resolution fix (`Path.Join` never root-detects) planned — [project-explorer](../openspec/specs/project-explorer/spec.md) Phase 2 |
| New Folder / move file to folder | Missing | Deferred placeholder, [project-explorer](../openspec/specs/project-explorer/spec.md) Phase 3 (includes fixing new files always created flat beside the `.vbp`) |

---

## Properties Window (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Property list — categorized view | Done | |
| Property list — alphabetic view | Done | Alphabetic and Categorized tabs both present |
| String, numeric, boolean editors | Done | |
| Colour picker editor | Done | |
| Font picker editor | Done | |
| Enum / dropdown editor | Done | |
| Property value validation | Done | |
| Real-time sync with designer | Done | Bidirectional |
| Property search / filter | Missing | |
| Object dropdown (select component) | Done | ComboBox lists all components on the active form; selection synced bidirectionally with the designer |
| Help description panel | Done | Description panel at bottom shows selected property name and description text |
| Left, Top, Width and Height in VB6's units | Done | As VB6's window shows them (#1636): a control's in its container's ScaleMode (twips inside a Frame), a form's own size with its frame, three decimals at most; typed values in the same units, rounded to whole twips. A form with a menu shows Left and Top by the rule where VB6 read 105, 105, unexplained. The toolbar's readout is still pixels ([#1641](https://github.com/hexide-io/HexIDE/issues/1641)) |

---

## Toolbox (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Cursor / pointer tool | Done | |
| Label | Done | |
| TextBox | Partial | Text, Change, the colours, focus and the mouse events run. A drawn TextBox starts with its name as its Text, as VB6's designer gives it ([#926](https://github.com/hexide-io/HexIDE/issues/926)). SelStart, SelLength and SelText read and write at run time as vb6.exe measured ([#1150](https://github.com/hexide-io/HexIDE/issues/1150)). MultiLine and ScrollBars lay the box out as in VB6: Enter breaks a MultiLine box's line, it wraps unless it has a horizontal scroll bar, and it draws the bars it asks for ([#1340](https://github.com/hexide-io/HexIDE/issues/1340)). Its own Font is drawn, and its font members read and write as below ([#915](https://github.com/hexide-io/HexIDE/issues/915)). MaxLength, Locked, PasswordChar and Alignment are kept in the file but not applied at run time. Every control with a font, and the form, reads and writes FontName, FontSize, FontBold, FontItalic, FontUnderline and FontStrikethru as vb6.exe measured, sizes snapped to whole pixels and bitmap fonts to their own heights ([#1343](https://github.com/hexide-io/HexIDE/issues/1343)); the Font object itself is [#934](https://github.com/hexide-io/HexIDE/issues/934) |
| Frame | Done | ToolTipText, read and set as vb6.exe does (#1403). |
| CommandButton | Done | ToolTipText, read and set as vb6.exe does (#1403). The arrow keys move to the previous or next control in its container, as in VB6 (#1625). |
| CheckBox | Done | GotFocus and LostFocus, as vb6.exe raises them (#1101). The arrow keys move to the previous or next control in its container, as in VB6 (#1625). |
| OptionButton | Done | GotFocus and LostFocus, as vb6.exe raises them. One that takes the focus by `SetFocus` or the Tab key is selected, raising Click after GotFocus (#1101). An option group is one Tab stop, its selected option, as in VB6 (#1583). The arrow keys move to the previous or next control in its container by TabIndex, wrapping, and select an option they land on, as in VB6 (#1625). |
| ComboBox | Partial | One GotFocus and one LostFocus, whether the combo or its edit box takes the focus (#1101). `ListCount`, `List(i)`, `AddItem` with an index and `RemoveItem` as vb6.exe gives them (#821). `Text` at Style 0 and 1, in the designer and at run time, and a drawn combo starts with its name as its Text (#926). `Style` as vb6.exe and its designer give it, and a Dropdown List's Text selecting the item it names or raising 383 ([#1428](https://github.com/hexide-io/HexIDE/issues/1428)). A Simple Combo is drawn as its edit box over its list, always shown, and takes the pointer as vb6.exe's does (#1603); its Height doesn't snap to whole rows as VB6's does ([#1655](https://github.com/hexide-io/HexIDE/issues/1655)). The designer draws a Dropdown List's box with its name, as VB6's does (#1604). Click, Change and DropDown come where vb6.exe raises them, for the mouse, the keys, typing and code ([#1429](https://github.com/hexide-io/HexIDE/issues/1429)). No `Sorted` |
| ListBox | Done | Takes the focus itself, raising one GotFocus and one LostFocus however its items take it (#1101). Click comes where vb6.exe raises it: on a left press on an item, before MouseUp, and when code or an arrow key moves the selection (#1374). `ListCount`, `List(i)`, `AddItem` with an index and `RemoveItem` as vb6.exe gives them (#821). `Selected(i)`, `SelCount` and `Text`, read and written, as vb6.exe gives them on a single-select list (#1395). `MultiSelect`, Simple and Extended, with vb6.exe's mouse, keys and code, `ListIndex` the focused item (#1413). No `Sorted` |
| HScrollBar | Done | Takes the focus, raising GotFocus and LostFocus (#1101). |
| VScrollBar | Done | Takes the focus, raising GotFocus and LostFocus (#1101). |
| Timer | Done | |
| DriveListBox | Missing | Not in toolbox. Tracked with its two siblings as one item — the VB6 idiom is the change-event chain, so one without the others is unusable ([#164](https://github.com/hexide-io/HexIDE/issues/164)) |
| DirListBox | Missing | Not in toolbox — see DriveListBox |
| FileListBox | Missing | Not in toolbox — see DriveListBox |
| Shape | Done | |
| Line | Partial | A form holding one loads, runs and saves byte for byte, VB6's About Dialog included. It draws pixel for pixel as VB6 does, and its members read and write from code (oracle, "The Line control"). The designer draws it and moves it by the rectangle it covers. Not yet in the toolbox, and its endpoints can't be dragged on their own ([#163](https://github.com/hexide-io/HexIDE/issues/163)) |
| Image | Done | `ImageComponentClass` + `VBImage`; decodes an .frx picture record by peeling both the record framing and the StdPicture preamble. Raises Click and the mouse events, hit-tested across its whole rectangle with or without a picture (#974); and DblClick, in place of a double click's second MouseDown (#1031) |
| OLE | Missing | Windows-only; see below. OLE Container control; in-process COM hosting |
| PictureBox | Done | Takes the focus, whether or not it holds controls, raising GotFocus and LostFocus (#1101). |
| CommonDialog | Partial | ShowOpen, ShowSave and ShowFont run, through the IDE's own pickers and font dialog, which an automation client answers with `answer_next_file_dialog`. Cancel, CancelError, FileName, FileTitle, Filter, DefaultExt, the Font properties and the control's 102 constants are as vb6.exe measured (oracle, "The CommonDialog control"). It lives only at run time: the designer shows the block the form was read with and saves it byte for byte. ShowColor, ShowPrinter and ShowHelp are not implemented ([#1084](https://github.com/hexide-io/HexIDE/issues/1084)) |
| Common controls (MSCOMCTL et al.) | Missing | ListView, TreeView, ImageList, ProgressBar and the rest of the ActiveX controls VB6 shipped in the box, re-implemented cross-platform under VB6's own type names, members and `.frm`/`.frx` shape, with nothing hosted through COM. The epic is [#1081](https://github.com/hexide-io/HexIDE/issues/1081), its foundation [#1082](https://github.com/hexide-io/HexIDE/issues/1082), and the controls #1083 to #1100 |
| Custom tab / component groups | Missing | Single flat list; no tab support |
| Add/remove controls via Components dialog | Stub | Components dialog Apply is a no-op |
| Tooltips on hover | Missing | No ToolTip shown for toolbox items |

---

## Immediate Window (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Text display area | Done | AvaloniaEdit document present |
| Execute VB6 expression on Enter | Done | In break mode: a `?` line prints as Print does, lists included (#1386); a bare assignment or `Set` runs (D14); user calls are refused (D15) |
| Print output during run (Debug.Print) | Done | `Debug.Print` in the live F5 run routes to the Immediate window (via `VBDebugConsole`) |
| Inspect variable value | Done | `? x` while paused, a local, a module variable or another form's control (`? Form1.Text1(0).Text`, #1386) |

---

## Locals Window (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Variable tree | Done | Expandable Expression/Value/Type tree of the paused frame (UDT/array/object/live-control drill-in); native debugger Phase 3 + P8; MCP `get_locals` |

---

## Watch Window (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Watch expression list | Done | Expandable Expression/Value/Type/Context rows, re-evaluated each break; native debugger P6a; MCP `get_watches` |
| Add watch from editor selection | Done | Quick Watch (Shift+F9) opens the Add Watch dialog pre-filled with the identifier under the caret; native debugger P6a |

---

## Form Layout Window (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Form position preview on screen layout | Done | Live form thumbnail; reacts to StartUpPosition, Left, Top, Width, Height property changes |

---

## Color Palette (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| BackColor / ForeColor tab selection | Done | |
| 48-colour VB6 palette | Done | |
| Live sync with selected component | Done | |

---

## Dialogs

| Dialog | Status | Notes |
|---|---|---|
| New Project | Done | New / Existing / Recent tabs |
| Project Properties | Partial | EXE-only project type; startup form selection works |
| References | Done | Registry enumeration (Windows); Browse; persisted to .vbp. VB6's three fixed rows head the list, and the project's own OLE Automation reference is an ordinary row that OK keeps (#1026) |
| Components | Stub | `ApplyCommand` is empty; Designers and Insertable Objects lists always empty |
| Add Procedure | Done | |
| Find / Replace | Partial | Searches any document that has a text buffer, not only VB6 code windows — the carried-file editor included. Both offered scopes are honoured (Current Module, All Open Documents); project-wide is deferred to [#372](https://github.com/hexide-io/HexIDE/issues/372). Edit ▸ Find / Replace / F3 grey out on a document with nothing to search (the designer, the Object Browser, the Translation Editor, Language & Debug Servers), and the modeless dialog says so if the active tab changes under it. A match in the header or a member's `Attribute` lines is skipped (#273), without saying so yet ([#705](https://github.com/hexide-io/HexIDE/issues/705)) |
| Options | Done | 2-pane TreeView redesign (Phase 46, all 5 phases complete): pages, reset-to-defaults, Add-Ins section + add-in-contributed settings seam |
| Add-In Manager | Done | In-process managed add-in system (Phase 44); menu/tool-window/event API. The standalone Add-In Manager add-in was retired (Phase 46 P4) — enable/disable now lives in Tools→Options › Add-Ins. **Security Phase 9** (done): add-ins are now signed folder packages verified offline against a baked-in root before loading; per-add-in collectible ALC; unsigned local builds load only under flag-gated **developer mode** (Phase 48 — `--developer-mode` + the persisted `LoadUnsignedAddins` capability, double-gated). **Phase 10** (done): first-load consent gate — non-first-party add-ins are not loaded until the user Allows a per-add-in modal (consent keyed by manifest hash, re-prompts on update); Options → Add-Ins surfaces trust/consent + Revoke. **Phase 11** (done): HexIDE-root-signed revocation list checked at load (revoke by build hash / publisher / intermediate); freshest-validly-signed of bundled-floor + `%AppData%` cache + opt-in blocking startup fetch (`RevocationListUrl`); fail-open on fetch, fail-closed on a known revocation; build-hash revocation hits everyone, but publisher/intermediate revocation **exempts key-pinned first-party** add-ins → `AddinStatus.Revoked`. Live mid-session unload deferred to Phase 12. **Phase 13** (done): optional publisher logo (manifest `logoPath` → a hash-listed in-package PNG; verifier-sanitized + traversal-guarded) shown on the Options → Add-Ins page; hardened `SafeImageDecoder` (PNG IHDR dimension gate, reject > 1 MB / > 1024 px before decode, `null`-on-failure, no new dependency); confined to the Options page, never the consent prompt. **Phase 14** (done): read-only **trust-chain inspector** — the verified signing chain (publisher → marketplace → HexIDE root) with per-link `SHA-256(SPKI)` key fingerprints, surfaced via a subtle "Trust details…" modal at consent + a "Trust" expander on Options → Add-Ins; root + first-party fingerprints published in [TRUST.md](TRUST.md) and echoed in About (a test pins them so they can't drift). Active key-change detection (TOFU) deferred to Phase 15. See the [addin-system](../openspec/specs/addin-system/spec.md) and [addin-trust](../openspec/specs/addin-trust/spec.md) specs and [developer-mode spec](../openspec/specs/developer-mode/spec.md) |
| Menu Editor | Done | Full insert/delete/indent/property editing |
| Runtime Error | Partial | A run-time error nothing handles halts the program at its statement, with nothing running behind the dialog. Debug, the default button, breaks there with the procedure live, and Continue runs the statement again. End ends the run ([#1011](https://github.com/hexide-io/HexIDE/issues/1011)). Continue stays disabled, as in VB6's dialog for this error. Help, which VB6 offers, is disabled too. Also reports what ends a run on its own, which used to reach only the log or an unrecorded message box: a construct HexIDE does not implement, a fault in the engine (as VB6's "Internal error", 51), and an error `Sub Main` does not handle ([#754](https://github.com/hexide-io/HexIDE/issues/754)) |
| Save Changes | Partial | Per-file checkbox UI present; selective-save logic unclear |
| Font | Done | |
| About | Done | |
| Object Browser | Partial | Implemented as a Document tab; Unicode placeholder icons pending (#11); stale member list, double-click position, and Escape-to-close outstanding (#24); intrinsic control class names shown and searchable (#905) |
| Color dialog (property editor) | Done | Inline picker in Properties grid |
| Input Box (VB6 runtime) | Done | |
| Message Box (VB6 runtime) | Done | |
| Add Form / Module / Class wizard | Missing | No "Add Item" dialog; forms created with defaults only |
| Procedure Attributes | Missing | No dialog (NYI, [#62](https://github.com/hexide-io/HexIDE/issues/62)). The `Attribute` lines it would show and edit are in the code window since #273, read-only and folded into the line of the member they describe, so this dialog is the only edit route the IDE could offer for them |

---

## Status Bar

| Panel | Status | Notes |
|---|---|---|
| Message / Ready text | Done | "Ready", "Running…", transient messages |
| Line, Col display | Done | Updates with editor cursor. The line counts from the top of the file, header included (#273) |
| INS / OVR mode | Done | Reflects AvaloniaEdit insert mode |
| Caps Lock / Num Lock indicators | Missing | VB6 shows these; not present |

---

## Keyboard Shortcuts

| Shortcut | Action | Status | Notes |
|---|---|---|---|
| Ctrl+N | New Project | Done | |
| Ctrl+O | Open Project | Done | |
| Ctrl+S | Save file | Done | |
| Ctrl+Z | Undo | Done | |
| Ctrl+Shift+Z | Redo | Done | |
| Ctrl+X / C / V | Cut / Copy / Paste | Done | |
| Ctrl+A | Select All | Done | |
| Ctrl+F | Find | Done | |
| F3 | Find Next | Done | |
| Ctrl+H | Replace | Done | |
| Ctrl+Tab | Indent | Done | |
| Shift+Tab | Outdent | Done | |
| Ctrl+J | List Properties/Methods | Done | |
| Ctrl+Shift+J | List Constants | Done | |
| Ctrl+I | Quick Info | Done | Triggers LSP hover at caret position |
| Ctrl+Shift+I | Parameter Info | Done | |
| Ctrl+Space | Complete Word | Done | |
| F4 | Properties Window | Done | |
| Shift+F7 | View Object | Done | |
| Ctrl+G | Immediate Window | Done | |
| Ctrl+R | Project Explorer | Done | Functional; note: ReplaceCommand also registers Ctrl+R — should be Ctrl+H (see ApplicationCommands.cs) |
| F5 | Start | Done | |
| Ctrl+F5 | Start with Full Compile | Stub | Same as F5 |
| Ctrl+Break | Break | Done | Pauses the running interpreter at the next statement (native debugger, Phase 1) |
| Shift+F5 | Restart | Done | |
| F8 | Step Into | Done | Step one statement / start-and-step from idle (native debugger, Phase 2) |
| Shift+F8 | Step Over | Done | Run called proc to completion, break in current frame (native debugger, Phase 5) |
| Ctrl+Shift+F8 | Step Out | Done | Run rest of proc, break in caller (native debugger, Phase 5) |
| Ctrl+F8 | Run to Cursor | Done | Runs to the caret line — a one-shot temp breakpoint (native debugger P7a) |
| Ctrl+F9 | Set Next Statement | Done | Moves the execution point to the caret line (top-level-body only — D18); native debugger P7b |
| Shift+F9 | Quick Watch | Done | Opens Add Watch pre-filled with the caret identifier (native debugger P6a) |
| F9 | Toggle Breakpoint | Done | Toggles a breakpoint on the caret line (native debugger, Phase 1) |
| Ctrl+Shift+F9 | Clear All Breakpoints | Done | |
| Ctrl+E | Menu Editor | Done | |
| Alt+F5 | Run with VB6 | Done | |
| F2 | Object Browser | Partial | OpenObjectBrowserCommand registered on F2; conflicts with NextBookmarkCommand (also F2) — bookmark navigation takes priority when code editor is focused |
| Ctrl+Shift+F2 | Last Position | Missing | Menu item still disabled; Ctrl+Shift+F2 gesture consumed by ClearAllBookmarksCommand |
| F7 | View Code | Missing | No global shortcut (only context-menu/toolbar) |
| Ctrl+F2 | Next Procedure | Missing | Procedure navigation shortcut |

---

## Interpreter / Runtime

> **Full gap catalogue:** [`docs/interpreter-gaps.md`](interpreter-gaps.md) — the authoritative, categorised
> (Missed / Deferred / Walled / Partial) list of interpreter gaps from the 2026-08-03 multi-agent audit. The rows
> below are the headline status; the catalogue is the complete map.
>
> **Full language coverage:** [`docs/MISSING_LANGUAGE.md`](MISSING_LANGUAGE.md) — all 1183 VB6 built-in
> statements, functions, operators, keywords, literals, directives, constants and in-box objects, each with its
> support level, ordered by what happens when the user presses F5.

| Feature | Status | Notes |
|---|---|---|
| Run VB6 project in-process | Partial | Runs via one `BasicInterpreter` (F5 = MCP `run_project` = the ~383 tests all use the same engine — no separate stub), but that engine is only ~25% language-complete (~10% for a real program) — see the rows below |
| Standard control set (Label, TextBox, Button, etc.) | Partial | The controls modelled by `FormDeserializer.AllSupportedComponents` are created and run. A project UserControl placed on a form is drawn from its form part, or as a labelled placeholder if it has none. Other types are preserved for save and shown as inert designer boxes; a form holding a control the engine cannot create is refused at F5. See [unmodelled controls](#unmodelled-controls) |
| Form events (Load, Click, Change, Timer) | Partial | `Form_Load`/`Resize`, `Click`, `Change` and `Timer` fire, Click on the controls that wire it and on the Form, where it follows MouseUp for every button and needs the release over the form (#1364). `MouseDown`, `MouseUp` and `MouseMove` fire on every control VB6 gives them and the Form, in VB6's order against Click (#974). `Initialize`, `QueryUnload` and `Unload`, with Cancel, and `Terminate` fire for every form of the program, from `Unload` and the close box (#973, #822). DblClick fires on the Form and every control VB6 gives one, in vb6.exe's order with real input (#1031). `Activate` and `Deactivate` fire as the program's active form changes, and `Resize` once as a form is first shown after Load and then for each change of size or state (#994); size assignments raise it inside the statement, including while hidden in Load (#1570). `Key*` and `ComboBox` events are not wired |
| Several forms: default instances, `Load`, `Show`, `Show vbModal`, `Hide`, `Unload`, `Sub Main` | Partial | A run is one program of every form (#822): a form's name makes its default instance where it is first used, its control or built-in member loads it, `Show` and `Hide` show and hide it, and the run lasts until no form is loaded and `Sub Main`, where the project starts there, has returned. `Show vbModal` waits on the program's events until the form has gone, with the other forms held meanwhile, and raises 401, 402, 405 and the style's 5 where VB6 does. A held form is drawn as it was, takes no input, refuses its close box and reads `Enabled` False, as VB6's disabled window does. It is held rather than disabled because disabling greys every control. Unlike VB6's, it can still be dragged by its title bar, and activating it brings the modal form forward. What setting `Enabled` under a modal form does is unmeasured. All measured on vb6.exe, native and p-code agreeing. The desktop, single-view and Standalone hosts run every form, the Standalone one since [#1062](https://github.com/hexide-io/HexIDE/issues/1062). Still to come: `New Form2` and the `Forms` collection (#822) |
| A running form's window: BorderStyle, ControlBox, MinButton, MaxButton | Done | The window vb6.exe makes, measured: no frame for None (Fixed Single's with a menu), a resizing frame only for the sizable styles, the close box with ControlBox, no caption bar with ControlBox False and an empty Caption, the buttons as MinButton and MaxButton say but never on a Fixed Dialog, and a tool window's caption for the tool windows. On Windows the window is given vb6.exe's own styles bit for bit ([#1575](https://github.com/hexide-io/HexIDE/issues/1575)); elsewhere Avalonia has no tool window, no caption without a close box, and no maximize box on a Fixed Single form. The icon is the form's own, none for Icon = (None), which keeps a Fixed Dialog's caption bare, and HexIDE's mark where the file has no Icon line, as VB6 gives its default there ([#1594](https://github.com/hexide-io/HexIDE/issues/1594)). Kept as the file gave it if the Caption or ControlBox changes at run time |
| VB6 built-in functions | Partial | **~80** implemented via a data-driven registry (interpreter-core Phase 3, complete), all pinned against `vb6.exe`: **Strings** (`Left/Right/Mid/Len/InStr/InStrRev/Replace/Trim/LTrim/RTrim/UCase/LCase/StrReverse/Space/String/Chr/Asc/Str/Val`), **Conversion** (`CByte/CInt/CLng/CSng/CDbl/CCur/CDec/CBool/CStr/CDate`, `Hex/Oct`), **Math** (`Abs/Int/Fix/Sgn/Sqr/Sin/Cos/Tan/Atn/Exp/Log/Round`, `Rnd` + `Randomize`), **Inspection** (`TypeName/VarType/IsNumeric/IsDate/IsEmpty/IsNull/IsArray/IsObject`), **Array** (`Array/Split/Join/Filter/LBound/UBound`), **Date/Time** (`Now/Date/Time/Timer/Year/Month/Day/Hour/Minute/Second/Weekday/DateSerial/TimeSerial/DateValue/TimeValue/DateAdd/DateDiff/DatePart/MonthName/WeekdayName`), **`Format`** (the full mask mini-language: numeric/date/string/Boolean) + `MsgBox/InputBox`. **Still missing:** the `$`-typed twins (`Format$/Left$/Mid$/…`) run as calls since #1071, but a bare `Date$` or `Command$` with no parentheses still needs the type-hint dispatch (#742); a few intrinsics (`StrConv/WeekdayName` locale edges) remain — but the everyday surface is covered. |
| User-defined `Function` declarations | Done | Declared + callable from statements and expressions; recursion works; return via the function-name idiom (interpreter-core Phase 1) |
| `Sub`/`Function` parameters | Done | ByRef (VB6 default — aliases the caller's slot, so mutations propagate) / ByVal (copy) / `Optional` + evaluated defaults bound (Phase 1); named arguments bound by name and evaluated in parameter order, into procedures, methods and the intrinsics VB6 takes names for (#1465); `ParamArray` still pending |
| Data types & numeric semantics | Done | Real value model — Byte / 16-bit Integer / Long / Currency / Decimal / Date + true Variant. Magnitude literal typing (`%`/`&`/`@`/`!`/`#` suffixes, `&H` hex + `&O` octal as two's-complement bit-patterns, exponent); VB6 arithmetic result-type table + Overflow (Err 6); integer `\`/`Mod` with operand banker's-rounding + divide-by-zero (Err 11); Date arithmetic (`Date+n→Date`, `Date−Date→Double`) + `#..#` literals; Currency 4-dp banker's + range; Empty/Null semantics; `&H`→colour interop at the property boundary — **all verified against real `vb6.exe`**. Coercion on store into a declared variable or array element, on function-return and on ByVal-param. Interpreter-core Phase 2 (2.1–2.6) complete. **Control property assignment** converts to the property's type with VB6's range checks (#768); a Date into a String property is written as `CStr` writes it, in the user's locale (#777); a palette colour is refused as not implemented (#786); reversed scroll bars preserve Value and reverse thumb movement (#787), and `Tag`, hex strings and control default members still differ (#211, #788, #790); a control's position is read and written in twips, or its container's ScaleMode, as a Single rounded to whole twips (#933, #791); a colour reads back as its Long, and a Label's colours paint when set (#789, #795) — see `vb6-fidelity-oracle.md`, "HexIDE, after #774". |
| Object model (`Set` / `New` / classes / UDT / `Property`) | Partial | None of these throw. `Is` and `TypeOf…Is` work; `Set`, `New`, `Type…End Type`, classes, `Property` and `Event` all work with restrictions (arrays of a UDT, parameterized properties, `As New`). `Implements` ships as of #186. Per-construct detail: [MISSING_LANGUAGE.md](MISSING_LANGUAGE.md) |
| Statements: `Const`, `With`, `While…Wend`, `For Each` | Done | interpreter-core Phase 4, all vs `vb6.exe` where it mattered: `While…Wend`, `For Each` over arrays (**column-major** — first subscript fastest, oracle-corrected), `Const` (plain slot + PrePass hoist), `With` (leading-dot → target-stack for Control/CSharpProxy; `With New`/`<userObject>` deferred to the object model). Joins the already-working `For`/`Do`/`If`/`Select Case`. |
| Debug.Print | Done | Routed to the Immediate window in the live F5 run (via `VBDebugConsole`); captured directly under test |
| MsgBox / InputBox | Partial | Core prompt/icon/button/result-mapping + InputBox title/default work; **but** default-button/modality/help bits + InputBox `xpos`/`ypos`/`helpfile`/`context` are ignored (2026-08-03 gap audit — was mislabeled "Done"). See [interpreter-gaps.md](interpreter-gaps.md). |
| Timer control | Done | |
| Error handling (On Error Goto / Resume / Err) | Done | interpreter-core Phase 5. **5a:** `On Error Resume Next` (per-block trap → skip faulting statement) + the global `Err` object (`Number`/`Description`/`Source`, `Raise`/`Clear`); natural errors (overflow 6, div-by-zero 11, subscript 9, type-mismatch 13) are trappable via the Phase-2 mapping; legacy `Error n`. **5b:** `On Error GoTo <label>`, `Resume`/`Resume Next`/`Resume <label>`, line labels, `GoTo`, `On Error GoTo 0` (a pc-driver over the procedure body's top-level statements); `Resume` with no active error = Error 20. **Documented limit:** a fault nested inside a top-level construct resumes after the whole construct (nested-granular `Resume` needs a CFG rewrite, which belongs to a real language engine). |
| Breakpoint / step debugging | Done | Native debugger v1 (Phases 1–4) + v2 (P5–P8) COMPLETE: breakpoints, Break/Continue/End, `Stop`, the amber current-statement bar, **Step Into/Over/Out**, the **Call Stack window (Ctrl+L)**, the **Locals window** (expandable tree, now with a live control's **property surface** + preserved expansion), the evaluating **+ assigning** Immediate window, **Watches** (incl. Break-When-True/Changed **conditional break**), **Data Tips** (hover), **Run To Cursor (Ctrl+F8)**, **Set Next Statement (Ctrl+F9)** — all against the interpreter (cooperative async pause-gate on `IDebugController`). Deferred: calling user procs from Immediate (deadlock-prone), modern per-line conditional-breakpoints/hit-counts (additive Evolution item), the D7 residuals (form `Me` / child-control-tree / proxy expansion). |
| External debug adapter | Partial | Explicit JSON launch configuration; F5/Break/End and steps route to the external session, with selected-frame Locals, expression Watches, capability-gated identifier hover values and Immediate REPL evaluation, selectable Call Stack, source reveal, session-only gutter/F9 breakpoint editing and verification, output and DAP protocol capture. Single global stop; thread selection, external break-condition watches, conditional breakpoints and command-specific MCP inspection remain deferred. See [launch configuration](debug-launch.md). |
| Watch / variable inspection at runtime | Done | Watches window (Add/Edit/Quick Watch, expandable tree; MCP `add_watch`/`get_watches`) + the Locals window + Data Tips (hover in break mode) — native debugger P6/P8 |
| Call stack | Done | Call Stack window (Ctrl+L) — native debugger P5; MCP `get_call_stack` |
| DriveListBox / DirListBox / FileListBox | Missing | Controls not implemented ([#164](https://github.com/hexide-io/HexIDE/issues/164)) |
| Line control | Partial | Runs as VB6 draws it and reads its members, measured. Not in the toolbox yet ([#163](https://github.com/hexide-io/HexIDE/issues/163)) |
| OLE / COM automation | Missing | Windows-only; see below. `CreateObject`, late-bound dispatch |
| File I/O (Open/Close/Write/Read) | Partial | Sequential Open/Close/Print #/Line Input #, FreeFile/EOF (#1145); filesystem statements and CurDir/FileLen/FileDateTime/GetAttr/Dir (#1561), measured with compiled VB6 EXEs. Binary/random channels and remaining channel functions are deferred. Host attributes and long-name wildcards differ as recorded in the fidelity oracle. |
| Screen / Printer objects | Missing | |
| App object | Partial | `App.Title`, `EXEName`, `Path`, `ProductName`, `Major`/`Minor`/`Revision`, `PrevInstance` and the version-info strings, seeded from the project (#136). Values follow VB6 at DESIGN TIME, which is where the interpreter permanently sits — `EXEName` is the `.vbp` file name and `ProductName` is empty, both measured under F5. `Title` is writable; `LogEvent`/`StartLogging`/`TaskVisible` are not implemented |
| Clipboard object | Missing | |
| Collection object | Missing | |
| Registry functions (GetSetting etc.) | Done | GetSetting, SaveSetting, DeleteSetting and GetAllSettings, measured (#1562): the registry key VB6 uses on Windows, a file in the user-data directory elsewhere |

---

## Debugger features (native interpreter debugger — v2 complete; a few remain a compiled-debug backend's lane)

| Feature | Status | Notes |
|---|---|---|
| Object Browser (F2) | Partial | Implemented: library/class/member tree, search (local filter plus a workspace-wide `workspace/symbol` query of every capable running server), LSP member loading, description panel, Go-to-Definition; pre-launch gaps: Unicode placeholder icons pending bitmap replacement (#11), plus three behavioural defects — member list cached so later edits do not appear, double-click opens the containing file rather than the member definition, Escape does not close (#24); intrinsic control class names shown and searchable (#905) |
| Data Tips (hover during debug) | Done | Hover a variable in break mode → its live value (`id = value`), evaluated against the paused frame (branches the editor hover pipeline); native debugger P6c |
| Edit-and-Continue | Affordance | True hot-patch is a permanent wall for the tree-walker (execution position is the live C# call stack, not a re-pointable PC) — a compiling backend's lane. But editing code while running/paused pops VB6's own "This action will reset your project. Do you want to continue?" prompt (Yes stops + keeps the edit, No reverts) — a faithful, recognisable affordance rather than a dead end |
| Breakpoint condition / hit count | Partial | VB6's watch-based conditional break shipped (Break-When-True/Changed watches evaluated at the pause-gate — P6b). Modern VS-style per-line breakpoint condition + hit count is a deferred additive Evolution item (reuses the same gate machinery) |
| Watch types (Break When Value Is/Changes) | Done | The Add Watch dialog's three watch types; Break-When-True (level-triggered) / Break-When-Changed (edge-triggered) evaluated at the pause-gate — native debugger P6b |
| Call Stack window | Done (Phase 5) | Dockable pane; running activation chain (Module.Procedure + Line) from `IDebugController.GetCallStack()`, current frame arrowed; MCP `get_call_stack` |
| Project Group (.vbg) | Done | Full round-trip: open/save `.vbg`; Project Explorer shows group root node; `StartupProject=` preserved |
| Multiple projects open simultaneously | Done | Opening a `.vbg` loads all member projects; group root shown in Project Explorer |

---

## Windows-only (IsWindows-gated)

Features that depend on the Windows COM/OLE infrastructure. In scope on Windows only (`OperatingSystem.IsWindows()`); not available on macOS/Linux. The `.vbp` source round-trip is preserved so projects can still be opened in VB6 and run natively on any platform.

| Feature | Status | Notes |
|---|---|---|
| **COM automation** (`CreateObject`, late-bound dispatch) | Missing | Core plumbing for automating Excel, Access, and other COM servers from VB6 code |
| **OLE Container control** | Missing | In-process COM hosting for embedded OLE objects; the planned Toolbox entry will be visible on Windows only. Today an OLE block follows [unmodelled controls](#unmodelled-controls) |
| **Paste Link** (Edit menu) | Missing | Creates an OLE linked object; depends on OLE Container infrastructure |
| **ActiveX EXE / ActiveX DLL** (project types) | Missing | COM server project types; the New Project dialog lists these on every platform, looking like the other entries, but refuses them when chosen |
| **Property Pages** (COM runtime display) | Missing | `IPropertyPage`/`IPropertyPageSite` display when an OCX is right-clicked at design time; `.pag` file authoring is cross-platform and tracked above |
