# HexIDE — VB6 IDE Fidelity Catalog

**Purpose:** Systematic assessment of every user-facing surface against VB6 IDE behaviour. Used to drive implementation work.  
**Status legend:** `Done` · `Partial` · `Stub` · `Missing` · `Windows-only`  
**This file contains only in-scope features.** Menu items and toolbar buttons that belong to out-of-scope features are not listed here — they are catalogued by UI surface in [OUT_OF_SCOPE.md](OUT_OF_SCOPE.md).  
**`Windows-only` means the feature is in scope but gated on `OperatingSystem.IsWindows()`.** See the Windows-only section at the bottom of this file.
**Modern-shell disposition:** whether a fidelity row is kept, changed, or removed in the modernised (Evolution-tier) IDE is catalogued in the maintainers' Evolution catalog — check it before implementing a row verbatim.

---

## File Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| New Project | Ctrl+N | Done | |
| Open Project… | Ctrl+O | Done | |
| Add Project… | — | Done | Adds second project; implicit `CurrentGroup` created when >1 project loaded |
| Remove *{project}* | — | Done | |
| Save Project | — | Partial | Saves every member HexIDE can reproduce; members it cannot are left untouched on disk and reported together in one dialog. See `Save {file}` |
| Save Project As… | — | Done | |
| Save *{file}* | Ctrl+S | Partial | **21 of VB6's own 22 template forms survive a save byte-for-byte**, and the 22nd (`Web Browser.frm`) is **held read-only** rather than damaged — nothing saves lossily any more. Menu trees round-trip as of #83, container nesting as of #84, and the companion-file model closed the rest (#107–#113); VB6's seven `.vbp` files round-trip as of #116. A save now writes a designer file and its companion **together or not at all** ([#148](https://github.com/hexide-io/HexIDE/issues/148)) — the text write used to be unconditional while the companion write could refuse on a measure of its own, leaving a `.frm` citing renumbered offsets into a companion that still held the old partition, which then reopened as faithful. A form separated from its `.frx` is held too, as of [#146](https://github.com/hexide-io/HexIDE/issues/146) — the gate counts what the `.frm` *cites* rather than what the companion yielded, so an absent or truncated companion is visible to it; previously such a form opened freely and an ordinary Ctrl+S stripped every citation from the developer's own file. Still `Partial`, not `Done`: the holdout needs OCX hosting, the measured corpus is VB6's own templates rather than real-world projects, and unknown OCX properties citing a blob are still dropped on save ([#60](https://github.com/hexide-io/HexIDE/issues/60)). Epic [#21](https://github.com/hexide-io/HexIDE/issues/21); contract in [serialization-round-trip](../openspec/specs/serialization-round-trip/spec.md) |
| Save *{file}* As… | — | Done | Subject to the same faithfulness gate as `Save {file}`, at a new path too, and refused *before* the destination is asked for. It used to be exempt — "the original file is not at risk" — which is true of the original and silent about the copy: `WriteCompanionBinary` can only protect a companion that already exists, so at a new path the blobs are dropped, and the copy then reopens as **faithful** because the citations that flagged it are exactly what went missing ([#143](https://github.com/hexide-io/HexIDE/issues/143)). **Not covered:** the Make EXE package still serializes every form to a temp directory with no gate |
| Print… | — | Stub | Bound to `NYICommand`; shows "not yet implemented" |
| Print Setup… | — | Stub | Bound to `NYICommand` |
| Make *{project}*… | — | Partial | Invokes HexIDE interpreter export; no real native-code compile |
| Make *{project}* with VB6… | Alt+F5 (make) | Done | Shells out to VB6.EXE if installed |
| Make Project Group… | — | Done | Saves all loaded projects then prompts for `.vbg` path; removes `DisabledCommand` |
| MRU file list | — | Done | "Recent Projects ►" submenu in File menu; backed by `IRecentProjectsService`; refreshes on project open/close |
| Exit | Ctrl+Q | Done | Calls `IClassicDesktopStyleApplicationLifetime.Shutdown()` |

---

## Edit Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Undo | Ctrl+Z | Done | AvaloniaEdit undo |
| Redo | Ctrl+Shift+Z | Done | AvaloniaEdit redo |
| Cut | Ctrl+X | Done | |
| Copy | Ctrl+C | Done | |
| Paste | Ctrl+V | Done | |
| Paste Link | — | Windows-only | OLE linked object; depends on OLE Container infrastructure |
| Remove | — | Stub | MenuItem present, no handler; context-dependent in VB6 |
| Delete | Del | Done | |
| Select All | Ctrl+A | Done | |
| Find… | Ctrl+F | Done | |
| Find Next | F3 | Done | |
| Replace… | Ctrl+H | Done | |
| Indent | Ctrl+Tab | Done | AvaloniaEdit indent |
| Outdent | Shift+Tab | Done | AvaloniaEdit outdent |
| Insert File… | — | Stub | MenuItem present; no file-insertion handler wired |
| List Properties/Methods | Ctrl+J | Done | Triggers LSP completion |
| List Constants | Ctrl+Shift+J | Done | Triggers LSP completion |
| Quick Info | Ctrl+I | Done | Ctrl+I calls `TriggerQuickInfo()` → LSP hover at caret; auto-hover on mouse-over also active |
| Parameter Info | Ctrl+Shift+I | Done | Triggers LSP signature help |
| Complete Word | Ctrl+Space | Done | Triggers LSP completion |
| Bookmarks | Ctrl+F2 / F2 / Shift+F2 / Ctrl+Shift+F2 | Done | Toggle/next/previous/clear-all; cyan circle gutter margin; persisted per project in the `.user.hexproj` sidecar via `UserSidecarService` (**exceeds VB6**, which lost bookmarks on close) |

---

## View Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Code | — | Done | Opens code editor for active form/module |
| Object | Shift+F7 | Done | Opens form designer for active form |
| Definition | — | Done | LSP go-to-definition |
| Last Position | — | Stub | Always disabled |
| Object Browser | — | Partial | Two-panel browser implemented: library/class/member tree, search, Back/Forward, Go-to-Definition, LSP member loading, description panel; pre-launch gaps: Unicode placeholder icons pending bitmap replacement (#11), plus three behavioural defects — member list cached so later edits do not appear, double-click opens the containing file rather than the member definition, Escape does not close (#24) |
| Immediate Window | Ctrl+G | Done (Phase 4 + P7c) | Editable buffer; `?expr`/`Print expr`/bare expr evaluates against the paused frame; a bare **assignment or `Set` now EXECUTES** and mutates the frame (`count = 5` assigns, `?count = 5` compares — P7c); user Sub/Function calls still rejected (deadlock-prone — D14–D15); Debug.Print output lands here; MCP `evaluate` |
| Locals Window | — | Done (Phase 3 + P8) | Expandable tree (Expression/Value/Type) of the paused frame; UDT/array/object drill-in, cycle+depth guarded; a **live control expands to its VB6 property surface** (P8, D7) and tree **expansion is preserved across a break** (D10); native `TreeView` (TreeDataGrid is commercially licensed in 12.x); MCP `get_locals`. Residual: form `Me`/child-control-tree/proxy expansion (D7) |
| Watch Window | — | Done (P6a) | Watches window (Expression/Value/Type/Context, expandable tree), re-evaluates each watch on every break, blanks on continue; Add/Edit/Delete + the three VB6 watch types; MCP `add_watch`/`get_watches` |
| Call Stack… | Ctrl+L | Done (Phase 5) | Dockable Call Stack tool window; lists the running activation chain (Module.Procedure + Line, current frame arrowed) from `IDebugController.GetCallStack()`; rebuilt on each break, cleared on Continue; MCP `get_call_stack`. Additive vs VB6's modal list — D16–D17 |
| Project Explorer | Ctrl+R | Done | Menu item enabled; opens/focuses Project Explorer tool window |
| Properties Window | F4 | Done | |
| Form Layout Window | — | Done | Monitor illustration with correct aspect ratio and live form thumbnail (StartUpPosition, Left, Top, Width, Height) |
| Property Pages | — | Windows-only | `IPropertyPage`/`IPropertyPageSite` COM runtime display; Windows-only |
| Toolbox | — | Done | |
| Color Palette | — | Done | |
| Toolbars > Standard | — | Done | Toggle persisted; right-click context menu on toolbar band |
| Toolbars > Edit | — | Done | 12 buttons: IntelliSense, indent/outdent, breakpoint, bookmarks |
| Toolbars > Debug | — | Done | 12 buttons: run/break/end, step, windows, quick watch |
| Toolbars > Form Editor | — | Done | Bring to Front/Back, 6 alignment, 3 make-same-size, Lock Controls; visibility persisted to settings |

---

## Project Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Add Form | — | Done | Creates Form2, Form3… with defaults; opens designer; saves to project dir |
| Add MDI Form | — | Missing | MDI is not COM-dependent; implementable cross-platform as an Avalonia container with floating child sub-windows |
| Add Module | — | Done | Creates Module1, Module2… with defaults; opens code editor; saves to project dir |
| Add Class Module | — | Done | Creates Class1, Class2… with defaults; opens code editor; saves to project dir |
| Add User Control | — | Done | Creates UserControl1, UserControl2… with empty designer surface; opens designer; saves .ctl to project dir |
| Add Property Page | — | Done | Creates PropertyPage1, PropertyPage2… with empty designer surface; opens designer; saves .pag to project dir |
| Add File… | — | Stub | MenuItem present, no Command binding |
| Remove *{file}* | — | Stub | MenuItem present, no Command binding; use project tree context menu |
| References… | — | Done | Registry enumeration on Windows; Browse button; persisted to .vbp |
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
| Center in Form > Horizontally | — | Done | Single-control centering |
| Center in Form > Vertically | — | Done | Single-control centering |
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
| Run To Cursor | Ctrl+F8 | Done (P7a) | Runs to the caret line (a one-shot temp breakpoint in the gate); paused → continue to target, running → arm, idle → start-and-run. MCP `run_to_cursor` |
| Add Watch… | — | Done (P6a) | Opens the Add Watch dialog (Expression + Context + the three VB6 Watch Types); adds to the Watches window. MCP `add_watch` |
| Edit Watch… | Ctrl+W | Done (P6a) | Re-opens the Add Watch dialog for the Watches window's selected row |
| Quick Watch… | Shift+F9 | Done (P6a) | Opens the Add Watch dialog pre-filled with the identifier under the caret |
| Toggle Breakpoint | F9 | Done | Toggles a breakpoint on the active editor's caret line (red gutter margin, click-to-toggle); persists per-project in the `.user.hexproj` sidecar. Native interpreter debugger, Phase 1 |
| Clear All Breakpoints | Ctrl+Shift+F9 | Done | Removes every breakpoint in the project |
| Set Next Statement | Ctrl+F9 | Done (P7b) | Moves the execution point to the caret line without running the statements between. **Top-level-body granularity only** — a nested target / a move while paused inside a block is refused (an interpreter limit — the top-level body is a pc-addressable loop, nested blocks are recursive descent; D18). MCP `set_next_statement` |
| Show Next Statement | — | Done (P7a) | Reveals the paused module's current-statement line (scrolls to + shows the amber bar) |

---

## Run Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Start | F5 | Done | Runs project via HexIDE interpreter in a new window |
| Start With Full Compile | Ctrl+F5 | Stub | Calls same code path as Start; compile step not distinct |
| Break | Ctrl+Break | Stub | `CanExecute` hardcoded to `false`; `BreakCurrentProject()` throws `NotImplementedException` |
| End | — | Done | |
| Restart | Shift+F5 | Done | |
| Start with VB6 | Alt+F5 | Done | Shells out to VB6.EXE if installed |

---

## Tools Menu

| Item | Shortcut | Status | Notes |
|---|---|---|---|
| Add Procedure… | — | Done | Generates Sub/Function/Property/Event stubs |
| Procedure Attributes… | — | Stub | Bound to `NYICommand` |
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
| Add Project (with flyout: Standard EXE / ActiveX EXE / DLL / Control) | Done | All template types create a project; implicit group created when >1 project loaded |
| Add Form (with flyout) | Done | Flyout wired: Form, MDI Form (missing — not yet implemented), Module, Class Module, User Control, Property Page, Add File… (disabled) |
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
| Break | Stub | Button present; `CanExecute=false` always |
| End | Done | |
| Project Explorer | Done | Opens/focuses tool window |
| Properties | Done | Opens/focuses tool window |
| Form Layout | Done | Opens Form Layout tool window with live form position thumbnail |
| Object Browser | Partial | Opens Object Browser document tab; Unicode placeholder icons pending (#11); stale member list, double-click position, and Escape-to-close outstanding (#24) |
| ToolBox | Done | |
| Data View | Stub | Button present; no Data View window |
| Position display (twips) | Done | Shows selected component position |
| Size display (twips) | Done | Shows selected component size |

---

## Code Editor Window

| Feature | Status | Notes |
|---|---|---|
| Syntax highlighting | Done | Provided by LSP server |
| Error/warning squiggles | Done | Full LSP diagnostic pipeline; inline markers |
| Object/Procedure dropboxes | Done | Two combos at top; navigate to handler on select |
| Auto-complete (IntelliSense) | Done | LSP completion |
| Signature help | Done | LSP signature help |
| Go to Definition | Partial | Works within same file; cross-file navigation noted as future work in code |
| Rename symbol | Done | LSP rename with inline input dialog |
| Format document | Done | LSP formatting; configurable format-on-save |
| Find / Replace | Done | See Dialogs section |
| Undo / Redo | Done | AvaloniaEdit native |
| Indent / Outdent | Done | AvaloniaEdit native |
| Cut / Copy / Paste / Select All | Done | |
| Add Procedure | Done | Stub generation dialog |
| Line numbers | Done | `ShowLineNumbers="True"` on `TextEditor`; rendered by AvaloniaEdit's built-in margin |
| Breakpoint margin | Done | Red gutter dots (click-to-toggle) via `BreakpointMargin`; native interpreter debugger, Phase 1 |
| Current-line indicator | Done | Amber full-width "current statement" bar via `CurrentLineRenderer`, driven by the debugger's Stopped event; native interpreter debugger, Phase 1 |
| Code folding | Done | `FoldingManager` installed; `VbFoldingProvider` + LSP pipeline fully wired |
| Quick Info on hover | Done | 400 ms delay hover → LSP `textDocument/hover` → tooltip; Ctrl+I triggers immediately. In **break mode** the same pipeline branches to a **Data Tip** — the hovered variable's live value (native debugger P6c) |
| Bookmarks | Done | `IBookmarkService` + `BookmarkMargin` gutter (cyan circles); toggle/navigate/clear-all keybindings; persisted in the `.user.hexproj` sidecar |
| Procedure View / Full Module View toggle | Missing | Won't implement — closed by the Evolution catalog's Remove table; code folding / sticky scroll / outline are the modern replacements |
| Insert File | Stub | Menu item present; no handler |
| Find scope (project / all docs) | Partial | Scope dropdown shown in Find dialog; only current-module search implemented |
| View Object (switch to designer) | Done | |
| Read-only banner for unsaveable forms | Done | **HexIDE addition** (not in VB6) — a form HexIDE cannot reproduce on save opens with its code editor read-only and a banner giving the reason (`CodeEditorViewModel.IsReadOnly`). Viewing and running still work. Rationale in [serialization-round-trip](../openspec/specs/serialization-round-trip/spec.md) |

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
| Double-click → open event handler | Done | Generates handler stub; opens code editor |
| Properties grid integration | Done | Bidirectional; all property types |
| Color picker (via Color Palette) | Done | |
| Font picker (via Property grid) | Done | |
| Form resize | Partial | Bottom, right, and bottom-right handles present; top and left edges not resizable |
| Multi-select | Done | Rubber-band and Ctrl+click; group drag; primary/secondary handles |
| Alignment commands (align left/right/top/bottom) | Done | Full set via Format > Align; Form Editor toolbar |
| Make Same Size commands | Done | Same Width / Height / Size via Format > Make Same Size |
| Spacing commands | Done | Horizontal and Vertical spacing submenus fully implemented |
| Copy / Paste controls | Done | Ctrl+C/V duplicates selected control(s) with offset; multi-paste accumulates offset |
| Cut control | Done | Ctrl+X removes selected controls; Ctrl+V restores at offset |
| Undo / Redo | Partial | Phase 1: structural ops (add/delete/cut/paste/z-order/lock) undoable via Ctrl+Z. Phase 2: move/resize via drag undoable. Phase 3 (properties/format) pending. |
| Tab Order editor | Missing | TabIndex auto-assigned; no visual tab-order tool |
| Lock Controls | Done | Toggles drag/resize; persisted in .frm; toolbar toggle reflects state |
| Smart / alignment guides | Missing | Grid only |
| Property search / filter | Missing | No search box in Properties window |
| Control rendering quality | Partial | Bitmap snapshot via `ControlRenderer`; text/font preview approximate |
| Read-only designer for unsaveable forms | Done | **HexIDE addition** (not in VB6) — a form HexIDE cannot reproduce opens read-only with a banner (`FormEditViewModel.IsReadOnly`). 1 of VB6's own 22 template forms is in this state today — `Web Browser.frm`, whose pictures sit on an `MSComctlLib.ImageList` OCX (was 12 before #83, then 6 before #107–#113). That is the floor until OCX hosting lands ([#21](https://github.com/hexide-io/HexIDE/issues/21)) |

---

## Project Explorer (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Project / Forms / Modules tree | Changed | VB6's type-grouped view deliberately replaced by an always-on filesystem hierarchy (muscle-memory override, 2026-06-12) — see [project-explorer](../openspec/specs/project-explorer/spec.md) |
| View Code | Done | |
| View Object | Done | |
| Set as Startup Project | Done | |
| Add Form (context menu) | Done | |
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

---

## Toolbox (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Cursor / pointer tool | Done | |
| Label | Done | |
| TextBox | Done | |
| Frame | Done | |
| CommandButton | Done | |
| CheckBox | Done | |
| OptionButton | Done | |
| ComboBox | Done | |
| ListBox | Done | |
| HScrollBar | Done | |
| VScrollBar | Done | |
| Timer | Done | |
| DriveListBox | Missing | Not in toolbox. Tracked with its two siblings as one item — the VB6 idiom is the change-event chain, so one without the others is unusable ([#164](https://github.com/hexide-io/HexIDE/issues/164)) |
| DirListBox | Missing | Not in toolbox — see DriveListBox |
| FileListBox | Missing | Not in toolbox — see DriveListBox |
| Shape | Done | |
| Line | Missing | Not in toolbox. The only intrinsic that is not a rectangle — its geometry is X1/Y1/X2/Y2, which the designer's bounding-box model has no concept of ([#163](https://github.com/hexide-io/HexIDE/issues/163)) |
| Image | Done | `ImageComponentClass` + `VBImage`; decodes an .frx picture record by peeling both the record framing and the StdPicture preamble |
| OLE | Windows-only | OLE Container control; in-process COM hosting; Windows only |
| PictureBox | Done | |
| Custom tab / component groups | Missing | Single flat list; no tab support |
| Add/remove controls via Components dialog | Stub | Components dialog Apply is a no-op |
| Tooltips on hover | Missing | No ToolTip shown for toolbox items |

---

## Immediate Window (Tool Window)

| Feature | Status | Notes |
|---|---|---|
| Text display area | Done | AvaloniaEdit document present |
| Execute VB6 expression on Enter | Missing | No REPL; input not evaluated |
| Print output during run (Debug.Print) | Done | `Debug.Print` in the live F5 run routes to the Immediate window (via `VBDebugConsole`) |
| Inspect variable value | Missing | |

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
| References | Done | Registry enumeration (Windows); Browse; persisted to .vbp |
| Components | Stub | `ApplyCommand` is empty; Designers and Insertable Objects lists always empty |
| Add Procedure | Done | |
| Find / Replace | Partial | Scope (project / all docs) in UI but not enforced; searches active editor only |
| Options | Done | 2-pane TreeView redesign (Phase 46, all 5 phases complete): pages, reset-to-defaults, Add-Ins section + add-in-contributed settings seam |
| Add-In Manager | Done | In-process managed add-in system (Phase 44); menu/tool-window/event API. The standalone Add-In Manager add-in was retired (Phase 46 P4) — enable/disable now lives in Tools→Options › Add-Ins. **Security Phase 9** (done): add-ins are now signed folder packages verified offline against a baked-in root before loading; per-add-in collectible ALC; unsigned local builds load only under flag-gated **developer mode** (Phase 48 — `--developer-mode` + the persisted `LoadUnsignedAddins` capability, double-gated). **Phase 10** (done): first-load consent gate — non-first-party add-ins are not loaded until the user Allows a per-add-in modal (consent keyed by manifest hash, re-prompts on update); Options → Add-Ins surfaces trust/consent + Revoke. **Phase 11** (done): HexIDE-root-signed revocation list checked at load (revoke by build hash / publisher / intermediate); freshest-validly-signed of bundled-floor + `%AppData%` cache + opt-in blocking startup fetch (`RevocationListUrl`); fail-open on fetch, fail-closed on a known revocation; build-hash revocation hits everyone, but publisher/intermediate revocation **exempts key-pinned first-party** add-ins → `AddinStatus.Revoked`. Live mid-session unload deferred to Phase 12. **Phase 13** (done): optional publisher logo (manifest `logoPath` → a hash-listed in-package PNG; verifier-sanitized + traversal-guarded) shown on the Options → Add-Ins page; hardened `SafeImageDecoder` (PNG IHDR dimension gate, reject > 1 MB / > 1024 px before decode, `null`-on-failure, no new dependency); confined to the Options page, never the consent prompt. **Phase 14** (done): read-only **trust-chain inspector** — the verified signing chain (publisher → marketplace → HexIDE root) with per-link `SHA-256(SPKI)` key fingerprints, surfaced via a subtle "Trust details…" modal at consent + a "Trust" expander on Options → Add-Ins; root + first-party fingerprints published in [TRUST.md](TRUST.md) and echoed in About (a test pins them so they can't drift). Active key-change detection (TOFU) deferred to Phase 15. See the [addin-system](../openspec/specs/addin-system/spec.md) and [addin-trust](../openspec/specs/addin-trust/spec.md) specs and [developer-mode spec](../openspec/specs/developer-mode/spec.md) |
| Menu Editor | Done | Full insert/delete/indent/property editing |
| Runtime Error | Stub | Continue and Debug buttons always disabled; only End works |
| Save Changes | Partial | Per-file checkbox UI present; selective-save logic unclear |
| Font | Done | |
| About | Done | |
| Object Browser | Partial | Implemented as a Document tab; Unicode placeholder icons pending (#11); stale member list, double-click position, and Escape-to-close outstanding (#24) |
| Color dialog (property editor) | Done | Inline picker in Properties grid |
| Input Box (VB6 runtime) | Done | |
| Message Box (VB6 runtime) | Done | |
| Add Form / Module / Class wizard | Missing | No "Add Item" dialog; forms created with defaults only |
| Procedure Attributes | Missing | No dialog (NYI) |

---

## Status Bar

| Panel | Status | Notes |
|---|---|---|
| Message / Ready text | Done | "Ready", "Running…", transient messages |
| Line, Col display | Done | Updates with editor cursor |
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
> **Full language coverage:** [`docs/MISSING_LANGUAGE.md`](MISSING_LANGUAGE.md) — all 1182 VB6 built-in
> statements, functions, operators, keywords, literals, directives, constants and in-box objects, each with its
> support level, ordered by what happens when the user presses F5.

| Feature | Status | Notes |
|---|---|---|
| Run VB6 project in-process | Partial | Runs via one `BasicInterpreter` (F5 = MCP `run_project` = the ~383 tests all use the same engine — no separate stub), but that engine is only ~25% language-complete (~10% for a real program) — see the rows below |
| Standard control set (Label, TextBox, Button, etc.) | Partial | ~13 intrinsic controls instantiate + run; other types render as inert placeholders |
| Form events (Load, Click, Change, Timer) | Partial | `Form_Load`/`Resize`, `Click`, `Change` and `Timer` fire; `Unload`, `Key*`/`Mouse*` and `ComboBox` events are declared but never wired |
| VB6 built-in functions | Partial | **~80** implemented via a data-driven registry (interpreter-core Phase 3, complete), all pinned against `vb6.exe`: **Strings** (`Left/Right/Mid/Len/InStr/InStrRev/Replace/Trim/LTrim/RTrim/UCase/LCase/StrReverse/Space/String/Chr/Asc/Str/Val`), **Conversion** (`CByte/CInt/CLng/CSng/CDbl/CCur/CDec/CBool/CStr/CDate`, `Hex/Oct`), **Math** (`Abs/Int/Fix/Sgn/Sqr/Sin/Cos/Tan/Atn/Exp/Log/Round`, `Rnd` + `Randomize`), **Inspection** (`TypeName/VarType/IsNumeric/IsDate/IsEmpty/IsNull/IsArray/IsObject`), **Array** (`Array/Split/Join/Filter/LBound/UBound`), **Date/Time** (`Now/Date/Time/Timer/Year/Month/Day/Hour/Minute/Second/Weekday/DateSerial/TimeSerial/DateValue/TimeValue/DateAdd/DateDiff/DatePart/MonthName/WeekdayName`), **`Format`** (the full mask mini-language: numeric/date/string/Boolean) + `MsgBox/InputBox`. **Still missing:** the `$`-typed twins (`Format$/Left$/Mid$/…`) need the type-hint dispatch; a few intrinsics (`StrConv/WeekdayName` locale edges) remain — but the everyday surface is covered. |
| User-defined `Function` declarations | Done | Declared + callable from statements and expressions; recursion works; return via the function-name idiom (interpreter-core Phase 1) |
| `Sub`/`Function` parameters | Done | ByRef (VB6 default — aliases the caller's slot, so mutations propagate) / ByVal (copy) / `Optional` + evaluated defaults bound (Phase 1); `ParamArray` + named args still pending |
| Data types & numeric semantics | Done | Real value model — Byte / 16-bit Integer / Long / Currency / Decimal / Date + true Variant. Magnitude literal typing (`%`/`&`/`@`/`!`/`#` suffixes, `&H` hex + `&O` octal as two's-complement bit-patterns, exponent); VB6 arithmetic result-type table + Overflow (Err 6); integer `\`/`Mod` with operand banker's-rounding + divide-by-zero (Err 11); Date arithmetic (`Date+n→Date`, `Date−Date→Double`) + `#..#` literals; Currency 4-dp banker's + range; Empty/Null semantics; `&H`→colour interop at the property boundary — **all verified against real `vb6.exe`**. Coercion on function-return / ByVal-param. Interpreter-core Phase 2 (2.1–2.6) complete. Only local **assignment** coercion is deferred (needs a declared-type memory, so a `Variant` re-types correctly). |
| Object model (`Set` / `New` / classes / UDT / `Property`) | Missing | `Set`/`New`/`Is`/`TypeOf` and `Type…End Type`/class/`Property`/`Event` all throw |
| Statements: `Const`, `With`, `While…Wend`, `For Each` | Done | interpreter-core Phase 4, all vs `vb6.exe` where it mattered: `While…Wend`, `For Each` over arrays (**column-major** — first subscript fastest, oracle-corrected), `Const` (plain slot + PrePass hoist), `With` (leading-dot → target-stack for Control/CSharpProxy; `With New`/`<userObject>` deferred to the object model). Joins the already-working `For`/`Do`/`If`/`Select Case`. |
| Debug.Print | Done | Routed to the Immediate window in the live F5 run (via `VBDebugConsole`); captured directly under test |
| MsgBox / InputBox | Partial | Core prompt/icon/button/result-mapping + InputBox title/default work; **but** MsgBox `Title` is hardcoded `""`, and default-button/modality/help bits + InputBox `xpos`/`ypos`/`helpfile`/`context` are ignored (2026-08-03 gap audit — was mislabeled "Done"). See [interpreter-gaps.md](interpreter-gaps.md). |
| Timer control | Done | |
| Error handling (On Error Goto / Resume / Err) | Done | interpreter-core Phase 5. **5a:** `On Error Resume Next` (per-block trap → skip faulting statement) + the global `Err` object (`Number`/`Description`/`Source`, `Raise`/`Clear`); natural errors (overflow 6, div-by-zero 11, subscript 9, type-mismatch 13) are trappable via the Phase-2 mapping; legacy `Error n`. **5b:** `On Error GoTo <label>`, `Resume`/`Resume Next`/`Resume <label>`, line labels, `GoTo`, `On Error GoTo 0` (a pc-driver over the procedure body's top-level statements); `Resume` with no active error = Error 20. **Documented limit:** a fault nested inside a top-level construct resumes after the whole construct (nested-granular `Resume` needs a CFG rewrite, which belongs to a real language engine). |
| Breakpoint / step debugging | Done | Native debugger v1 (Phases 1–4) + v2 (P5–P8) COMPLETE: breakpoints, Break/Continue/End, `Stop`, the amber current-statement bar, **Step Into/Over/Out**, the **Call Stack window (Ctrl+L)**, the **Locals window** (expandable tree, now with a live control's **property surface** + preserved expansion), the evaluating **+ assigning** Immediate window, **Watches** (incl. Break-When-True/Changed **conditional break**), **Data Tips** (hover), **Run To Cursor (Ctrl+F8)**, **Set Next Statement (Ctrl+F9)** — all against the interpreter (cooperative async pause-gate on `IDebugController`). Deferred: calling user procs from Immediate (deadlock-prone), modern per-line conditional-breakpoints/hit-counts (additive Evolution item), the D7 residuals (form `Me` / child-control-tree / proxy expansion). |
| Watch / variable inspection at runtime | Done | Watches window (Add/Edit/Quick Watch, expandable tree; MCP `add_watch`/`get_watches`) + the Locals window + Data Tips (hover in break mode) — native debugger P6/P8 |
| Call stack | Done | Call Stack window (Ctrl+L) — native debugger P5; MCP `get_call_stack` |
| DriveListBox / DirListBox / FileListBox | Missing | Controls not implemented ([#164](https://github.com/hexide-io/HexIDE/issues/164)) |
| Line control | Missing | Not implemented ([#163](https://github.com/hexide-io/HexIDE/issues/163)) |
| OLE / COM automation | Windows-only | `CreateObject`, late-bound dispatch; Windows only |
| File I/O (Open/Close/Write/Read) | Missing | No VB6 file I/O statements |
| Screen / Printer objects | Missing | |
| App object | Partial | `App.Title`, `EXEName`, `Path`, `ProductName`, `Major`/`Minor`/`Revision`, `PrevInstance` and the version-info strings, seeded from the project (#136). Values follow VB6 at DESIGN TIME, which is where the interpreter permanently sits — `EXEName` is the `.vbp` file name and `ProductName` is empty, both measured under F5. `Title` is writable; `LogEvent`/`StartLogging`/`TaskVisible` are not implemented |
| Clipboard object | Missing | |
| Collection object | Missing | |
| Registry functions (GetSetting etc.) | Missing | |

---

## Debugger features (native interpreter debugger — v2 complete; a few remain a compiled-debug backend's lane)

| Feature | Status | Notes |
|---|---|---|
| Object Browser (F2) | Partial | Implemented: library/class/member tree, search, LSP member loading, description panel, Go-to-Definition; pre-launch gaps: Unicode placeholder icons pending bitmap replacement (#11), plus three behavioural defects — member list cached so later edits do not appear, double-click opens the containing file rather than the member definition, Escape does not close (#24) |
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
| **OLE Container control** | Missing | In-process COM hosting for embedded OLE objects; Toolbox entry visible on Windows only |
| **Paste Link** (Edit menu) | Missing | Creates an OLE linked object; depends on OLE Container infrastructure |
| **ActiveX EXE / ActiveX DLL** (project types) | Missing | COM server project types; New Project dialog offers these on Windows only |
| **Property Pages** (COM runtime display) | Missing | `IPropertyPage`/`IPropertyPageSite` display when an OCX is right-clicked at design time; `.pag` file authoring is cross-platform and tracked above |
