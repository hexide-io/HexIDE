# MCP dev-server gaps (found in use)

Limitations of HexIDE's embedded MCP automation server hit while driving the IDE this session (the
`Debug.Print`→Immediate fix and the Phase-2 colour visual-verification). The MCP server is a **dev/automation
tool, `#if DEBUG`-only** (compiled out of Release), so these are developer-workflow gaps, not product bugs.
Recorded here so they can be fixed deliberately; the generic trio (`dump_visual_tree` / `interact` /
`inspect_element`) otherwise worked well for non-modal surfaces.

---

## 1. Blind to runtime modal dialogs layered over a running form

**Symptom.** When a *running* VB6 program shows a `MsgBox`, or the runtime surfaces a compile/runtime-error
dialog (e.g. "Variable not defined"), `take_snapshot` and `dump_visual_tree` capture the `VBFormRuntime` window
*underneath* — `activeDialog` reports the form, and the modal is invisible. The modal only becomes visible to
the tools once the form is torn down (`stop_project`), at which point it lingers as the active window.

**How it bit.** The `Debug.Print` "Variable not defined (Debug)" error dialog was invisible to me — the user
had to tell me it was on screen. Same for a program's `MsgBox`.

**Root cause.** The snapshot/tree tools enumerate the active `VBFormRuntime` window; the runtime's managed
`MessageBox`/`InputBox` and error dialogs are **separate top-level windows** owned outside that control-view
hierarchy, and the "prefer a visible modal dialog" selection logic doesn't enumerate them.

**Workarounds used.** (a) `stop_project` to surface a lingering dialog, then `take_snapshot`; (b) read the
Serilog log at `%LOCALAPPDATA%\HexIDE\logs\ide\ide-*.log` (flushed by `shutdown_ide`) to see errors that were
logged rather than dialog'd.

**Fix.** The modal-preference logic should also enumerate the runtime's managed dialog top-level windows and
prefer them (reporting the title in `activeDialog`), the same way it prefers an IDE modal. *(Already filed in
`docs/TODO.md` → "MCP dev/automation server"; this consolidates it.)*

---

## 2. `set_control_property` only handles string / number / bool

**Symptom.** Setting an **enum** property fails — `set_control_property(Label0, "BackStyle", "1")` →
*"Property 'BackStyle' has type 'BackStyles' which is not supported by set_control_property"*. **Colour**
(`VBColor`) properties (`BackColor`/`ForeColor`) are likewise unsettable.

**How it bit.** I couldn't make a label opaque, nor set a control's colour, via the designer tool — so the
Phase-2 colour verification had to be done by *running code* (`Me.BackColor = &HC0FFC0` in `Form_Load`) and
snapshotting the result, rather than a designer property set.

**Fix.** Accept enum values (by member name or ordinal) and `VBColor` values (a hex `OLE_COLOR` string like
`"&H00FF0000&"`, or an `R,G,B` triple). Better: route the incoming string through the **same property-editor
coercion the designer's property grid uses**, so every editable property type is settable through one path.

---

## 3. MCP tools drop on IDE shutdown and don't re-attach mid-session

**Symptom.** MCP tools are discovered at session start. Shutting the IDE down — which is **required** to run
the `vb6.exe` oracle and for any rebuild that holds file locks on the runtime DLLs — disconnects the `hexide`
server and **removes its 38 tools from the session**; `ToolSearch("mcp__hexide__…")` then returns "no matching
deferred tools." Relaunching the IDE (health 200) does **not** re-register them mid-session; it took a user
**session resume** to bring them back.

**How it bit.** The documented "MCP Dev Loop" (`shutdown_ide` → `dotnet build` → relaunch → verify via MCP)
breaks at the last step: after shutting down for the build, the tools are gone until a resume. This session's
colour verification stalled here until the user resumed.

**Distinction from the known caveat.** `CLAUDE.md` already says MCP **schema changes** need a session restart.
This is different — the schema is unchanged; the tools just need **re-attaching** after the server bounces on
the same port.

**Workarounds used.** (a) Prefer **headless integration tests** (`HexIDE.Integration.Tests`, `Avalonia.Headless`)
for verification that doesn't strictly need a live IDE — often more rigorous and lock-free anyway (this is how
the `TrySet` colour-boundary path got its permanent guard). (b) Ask for a session resume when live MCP is
genuinely required after a shutdown.

**Fix consideration.** Auto-reconnect the MCP client when a known server reappears on its port, or a
lightweight "reconnect MCP" affordance — so the shutdown→build→relaunch→verify loop keeps the tools live
without a full resume.

---

## 4. Can't snapshot the IDE while a form is running (debugger paused-state)

**Symptom.** While a VB6 program is *running* — including when the **interpreter is paused at a breakpoint** —
`take_snapshot` captures the `VBFormRuntime` window, not the IDE main window (`activeDialog` reports the form).
So the IDE's **paused-state editor** — the amber current-statement bar and the red breakpoint gutter as they
appear *during* a break — cannot be captured. (This is the inverse of gap #1: there the modal *over* the form is
invisible; here the *IDE behind* the form is.)

**How it bit.** Verifying the interpreter debugger (Phase 1): the pause was fully confirmable via
`get_debug_state` (Paused / module / 1-based line / reason) and the reveal was confirmable *after* `stop_project`
(the caret lands on the break line — `Ln 6, Col 1` in the status bar), and the red dot is snapshottable while
*not* running — but the **amber bar while paused** could only be confirmed by the **user watching the live IDE**.
The functional path is the same `Stopped`-event handler that `get_debug_state` reflects, so it's provable; the
one *pixel* needs a human.

**Also blocks DRIVING the IDE while a form runs (not just snapshotting).** `dump_visual_tree` walks the *active
window* — which is the running `VBFormRuntime` — so it never returns the IDE's code-editor control, and
`press_key` / `type_text` (which need a path from `dump_visual_tree`) therefore can't target the editor while a
form is up. **How it bit (Phase-3 E&C affordance):** the "edit code while running → VB6 reset-project prompt" can't
be triggered over MCP — sending an editor keystroke needs the IDE editor addressable, which it isn't while the form
is foreground. The prompt logic is VM-tested (`ConfirmResetWhileRunningAsync`, `IsProjectRunning`) and the run-state
(`IsSessionActive`) is runtime-tested, but the live keystroke→dialog step needs the **user** (or a fix below).

**How it bit (Phase-5 Call Stack window).** The populated Call Stack pane (and by extension the populated Locals
pane) while paused can't be pixel-snapshotted for the same reason. Two escape hatches were tried and **both fail**:
(1) breaking early in `Form_Load` **before** the form is shown does **not** hand the IDE the foreground — the
`VBFormRuntime` window already exists and is preferred the moment the run starts, even pre-`Show`; (2)
`set_window_state("Maximized")` on the IDE main window does **not** override `take_snapshot`'s form preference
(`activeDialog` still reports the form). So the earlier "break in `Form_Load`-before-show → IDE foreground" note is
**wrong** — the only reliable IDE-foreground state is `stop_project`, which clears the paused panes. The pane's data
was instead verified via `get_call_stack` (the exact model the pane binds), its behaviour via `step_over`/`step_out`
+ `get_debug_state`, its binding via VM tests, and its **chrome** (title + "Procedure"/"Line" headers) via a
post-`stop_project` IDE snapshot — only the *populated rows* pixel needs a human.

**Root cause.** Same window-selection logic as gap #1: the running form is a separate top-level window that
`take_snapshot` **and** `dump_visual_tree` prefer; there's no way to ask for the IDE main window specifically while
a form is up. Confirmed (P5) that `set_window_state` doesn't change which window is captured — the preference is in
the capture-target selection, not window Z-order/activation.

**Workarounds used.** (a) `get_debug_state` for the pause fact; (b) a post-`stop_project` IDE snapshot to confirm
the caret-reveal + Immediate output + tool-pane chrome; (c) the user confirming live paused-state pixels
(amber bar, populated Locals/Call Stack rows).

**Fix consideration.** A `take_snapshot` **and** `dump_visual_tree` target/scope parameter
(e.g. `window: "ide" | "form" | "auto"`), so debugger/IDE-chrome verification can force the main window even while a
form runs. This is now the single highest-value dev-server fix — it blocks pixel-verifying *every* paused-state tool
pane (Locals, Call Stack, and future Watches/data-tips).

---

## 5. Can't select / delete / reorder a designer control via MCP

**Symptom.** A control placed with `add_control` is created and auto-selected, but there is no way to (a) select a
*different, existing* control, (b) delete a control, or (c) exercise undo/redo of a designer edit through MCP. The
controls drawn on the designer canvas are **not individual nodes in `dump_visual_tree`** (the canvas paints them; the
tree shows only the Properties-pane `ObjectSelector` combo and dock chrome), so `interact`/`press_key` have no path
to target a specific control, and there is no `select_control` / `delete_control` tool.

**How it bit (bug-hunt batch-2 designer fixes).** Verifying the four `FormEditViewModel` fixes — duplicate-name
avoidance (needs *delete then re-add*), multi-select **Delete** (needs a multi-selection), and undo **z-order**
restore (needs cut + undo) — was not drivable. Only the no-collision naming path was confirmed live (`add_control`
twice → `Command0`, `Command1`). The rest were verified by build + code review against the already-proven
`CutSelectedControls` path and the standard ascending-index restore invariant.

**Root cause.** The designer canvas is a custom-drawn surface; component VMs aren't surfaced as automation nodes, and
the designer's selection/delete/undo aren't exposed as `ICommand`s reachable via `interact invoke_command`.

**Fix consideration.** Small, high-leverage additions: `select_control(formName, controlName)` (drive the designer's
`SelectedComponent`/`SetSelectedComponents`), `delete_selected_controls`, and `designer_redo` to pair with the
existing `invoke_designer_undo` — plus surfacing each canvas control as a `dump_visual_tree` node with its name, so
`interact` can click/rubber-band it. That would make the whole designer edit loop (add → select → move → delete →
undo/redo) MCP-verifiable.

---

## 6. Can't open a Debug-menu dialog (e.g. Add Watch) via MCP to snapshot it

**Symptom.** Verifying debugger P6a's **Add Watch** dialog rendering couldn't be driven through MCP. The dialog opens
from a Debug-menu item / keyboard shortcut backed by a *routed* command (AvaloniaLabs CommandManager), not a VM
`ICommand`: `interact invoke_command AddWatchCommand` finds no such property on `MainViewViewModel`; `interact invoke`
on `MenuItem[Debug]` reports "element does not support 'invoke'" (a top-level menu exposes no invoke provider until
opened, and its children aren't in the tree until then). The Watches rows render as `TreeViewItem`s but don't
advertise a `selectionItem` provider, so selecting a row to enable `EditWatchCommand` (which opens the same dialog)
wasn't reachable either.

**How it bit (debugger P6a).** The Watches *window* was fully verified live — it renders (columns + rows in
`dump_visual_tree`, snapshot after Stop) and evaluates correctly while paused (`get_watches`: x=42, s=hello, x*2=84,
arr expandable). Only the **Add Watch dialog's** live render couldn't be reached; deferred to P6b (where its
Break-type radios become functional and it's exercised again), backed meanwhile by the dialog-VM unit tests.

**Fix consideration.** Best: (a) surface a `selectionItem` provider on tool-window TreeView rows so `interact select`
can set the selection (unlocking the row's context-menu commands like Edit/Delete); also useful: (b) make a top-level
`MenuItem` invoke open the menu and realize its children, or (c) a thin MCP action to open a named debugger dialog.

---

## 7. No pointer-hover action — can't trigger a data tip (or any hover) via MCP

**Symptom.** Debugger P6c's **Auto Data Tips** (hover an identifier in break mode → its live value) are triggered by
a `PointerMoved` dwell over a specific glyph. MCP's `interact` verbs are provider actions (invoke/select/set_value/
toggle/expand/collapse) + reflection (invoke_command/set_property), and `press_key` raises key events — none of them
move the pointer or raise a hover. So the data tip (and LSP quick-info hover, and any hover-only affordance) can't be
made to appear for a `take_snapshot`.

**How it bit (P6c).** Verified everything reachable another way: the typed-eval path is proven (`get_watches` live +
`WatchEvalTests`), the show/suppress DECISION is unit-tested (`DataTipTests`: a resolved value shows `x = 42`, a
keyword / out-of-scope / error shows nothing), and the word-extraction mirrors the proven `GetWordUnderCaret` (used
by Rename). Only the pointer-hover→tooltip plumbing itself is unverified live. A headless integration test could
raise a synthetic `PointerMoved`, but positioning it over an exact glyph in a headless `TextView` (offset→pixel needs
real layout) is unreliable enough that it wasn't worth it for low-risk plumbing.

**Fix consideration.** A small `hover(target, x?, y?)` MCP action that raises `PointerMoved` (and holds through the
dwell) on a control — or, for the editor specifically, `hover_identifier(name)` / `show_data_tip(line, col)` that
positions over a text offset — would make data tips and quick-info hover snapshot-verifiable.

---

## 8. `type_text` bypasses a read-only editor, and reports `mechanism: "keyboard"` while doing it

**Symptom.** Verifying the read-only editing gate (#22) against the running IDE, `type_text` successfully
inserted `XXX_SHOULD_NOT_APPEAR` into a code editor whose `TextEditor.IsReadOnly` was bound true. The result
reported `"mechanism":"keyboard"`, which reads as "a real key event went in" — so the first conclusion was
that the gate was broken. It was not.

**Cause.** The tool's own description says it inserts "at the caret **via the control's own API**", which is
a document mutation, not input. `IsReadOnly` on AvaloniaEdit guards the *editing UI*, so a direct
`Document.Insert` legitimately sidesteps it. The `mechanism: "keyboard"` label is the misleading part.

**Workaround.** Do not use `type_text` to test whether input is blocked. `press_key` raises real
`KeyDown`/`KeyUp`, but note gap #9 below before trusting a negative result from it either. The reliable
check is behavioural at a level the user cares about — here, invoking Save and confirming the file on disk
is byte-identical afterwards.

**Suggested fix.** Report `mechanism: "api"` (or `"document"`) when inserting programmatically, and reserve
`"keyboard"` for genuine key events. Optionally have `type_text` refuse, or warn, when the target editor is
read-only — silently mutating a read-only document is a surprising default for an automation tool.

## 9. `press_key` on a non-focusable container silently does nothing

**Symptom.** `press_key` against the document pane path returned `{"success":true,"detail":"pressed A"}` and
nothing happened — for **both** a read-only form and an editable one. Taken at face value that looks like
"read-only is working"; it is actually "the key went nowhere", and the two are indistinguishable from the
result.

**Cause.** `inspect_element` on that path shows `isKeyboardFocusable: false` and a `DocumentDock`
DataContext — the pane is a dock container, not the editor. The key is raised on a control that cannot take
focus, so nothing consumes it. `success: true` reports only that the event was raised.

**Workaround.** Do not infer "input was blocked" from a no-op `press_key`. Establish a positive control
first (the same key on a surface that *should* accept it), or verify at the model/file level instead.

**Suggested fix.** Have `press_key` resolve to the nearest focusable text surface the way `type_text`
resolves to the nearest editor, and report which control actually received the event — or return
`success:false` when the resolved target cannot take keyboard focus.

## 10. A crashed IDE is indistinguishable from a slow tool call

**Symptom.** Driving the designer to compose a screenshot, `add_control` returned success, then the next
call hung for the full 120 s timeout and every subsequent call failed with `Unable to connect. Is the
computer able to access the url?`. That message reads like a networking problem. The IDE had in fact
died — the tool call was fine, the process wasn't. The background-task notification said only
`transport dropped mid-call; response for tool "add_control" was lost`.

**Workaround.** When any tool starts failing to connect, check the process before changing approach:

```sh
powershell -NoProfile -Command "Get-Process HexIDE.Desktop -ErrorAction SilentlyContinue"
curl -s -m 4 -o /dev/null -w '%{http_code}' http://localhost:5123/health
```

If it's gone, the IDE log (`%LOCALAPPDATA%\HexIDE\logs\ide\`) will end cleanly with no exception,
because an unhandled render-thread exception terminates the process before Serilog flushes. The actual
stack is in the Windows Application event log:

```sh
powershell -NoProfile -Command "Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'; StartTime=(Get-Date).AddMinutes(-25)} | Where-Object { $_.Message -match 'HexIDE' } | Select-Object -First 1 -ExpandProperty Message"
```

That is how the `VBOptionButton` render crash was identified — nothing else surfaced it.

**Fix consideration.** Have the MCP layer distinguish "server unreachable" from "server was reachable and
has now gone", and surface the last few lines of the IDE log with the failure. A `--crash-log` style
handler that flushes Serilog on `AppDomain.UnhandledException` would also make the IDE log self-sufficient.

## 11. `add_control` mutates the designer but never persists

**Symptom.** Nine controls were added successfully via `add_control`; the IDE then crashed and **all of
them were lost** — `frmOrders.frm` on disk still held only the bare form. `set_control_property` saves;
`add_control` does not.

**Workaround.** For anything more than a couple of controls, author the `.frm` directly and open the
project — the format is small and well understood (`Left`/`Top`/`Width`/`Height` in twips, i.e. pixels
× 15, plus `Caption`/`Text`). That is also faster than one round trip per control, and it survives a
crash. Use `add_control` for interactive exploration, not for composing a form.

**Fix consideration.** Either save after `add_control` (consistent with `set_control_property`), or add
an explicit `save_form` tool so a caller can batch adds and commit once.

---

## Not an MCP gap (recorded to avoid confusion)

- **`Debug.Print` didn't reach the Immediate window** — that was an *interpreter* bug (the `Debug` object was
  seeded only in the test fixture, not the live F5 run), fixed this session (`VBDebugConsole`), not an MCP
  limitation. It's listed here only because gap #1 made it hard to *see* the resulting error.
