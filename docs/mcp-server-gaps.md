# MCP dev-server gaps (found in use)

Limitations of HexIDE's embedded MCP automation server hit while driving the IDE this session (the
`Debug.Print`→Immediate fix and the Phase-2 colour visual-verification). The MCP server is a **dev/automation
tool, `#if DEBUG`-only** (compiled out of Release), so these are developer-workflow gaps, not product bugs.
Recorded here so they can be fixed deliberately; the generic trio (`dump_visual_tree` / `interact` /
`inspect_element`) otherwise worked well for non-modal surfaces.

---

## 1. Blind to runtime modal dialogs layered over a running form — **CLOSED** (#61, 2026-09-01)

> **Fixed.** The root cause below was close but not quite right: the dialogs *were* enumerated. Both
> `take_snapshot` and `ResolveActiveWindow` picked with `Windows.FirstOrDefault(w => w != mainWindow &&
> w.IsVisible)`, which is correct only while exactly one non-main window exists. A running program adds a
> `VBFormRuntime`; its `MsgBox` adds a second window *on top* — but the form was created first, so
> first-wins returned the form and the dialog above it was invisible.
>
> Both call sites now share `ForegroundWindow.Pick` (`IDE/HexIDE/IDE/`), which keys on **ownership**:
> every dialog is shown via `ShowDialog(owner)`, so a window owning a visible window is underneath it by
> construction. Discard those and the foreground is what remains. `IsActive` is only a tiebreak — it is
> false for every window when the app is backgrounded, and headless never sets it.
>
> Verified live: a `Form_Load` `MsgBox` over a running form is now captured by `take_snapshot`, reported
> in `activeDialog`, and its OK button is addressable via `dump_visual_tree` → `interact`. Dismissing it
> hands the foreground back to the form rather than stranding the tools on a dead dialog. Guarded by
> `ForegroundWindowTests`, which fail on the old rule.
>
> **`activeDialog` no longer reports the title alone.** A VB6 `MsgBox` reaches the runtime with an empty
> caption (issue #131), and a blank label reads as *"no dialog is open"* — the exact confusion this gap
> existed to remove. It now falls back to the content type, e.g. `MessageBox`.

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
the old `docs/TODO.md` MCP section; this consolidates it.)*

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

## MCP tools do not re-attach to a resumed session while the IDE keeps running

**Symptom.** HexIDE was running throughout (`HexIDE.Desktop` PID alive, port 5123 `LISTENING`), the session
was restarted twice specifically to pick the tools up, and `mcp__hexide__*` was still absent from the tool
set both times. This is distinct from the known "tools drop when the IDE shuts down" case: nothing shut
down, and the server was answering on its port the whole time.

**Cost.** It blocks the mandatory visual verification in CLAUDE.md, and the documented escape hatch — raw
HTTP — is explicitly forbidden, so the correct response is to stop and ask, which costs a round trip and
sometimes more than one.

**Workaround that does not violate the rule.** For an *isolated* surface, a headless Skia render test does
the job: `AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }` is already configured in
`TestApp.cs`, so `window.CaptureRenderedFrame()?.Save(path)` produces a real PNG that can be read back and
looked at. Used to verify the reworded read-only banner at English and German lengths, and at a narrow dock
width, without the running IDE. This does not replace MCP for anything live or modal — it only reaches
surfaces that can be constructed standalone.

**Suggested fix.** Either make tool discovery retry against `.mcp.json` when the endpoint becomes reachable
mid-session, or have the launcher expose a readiness signal the client re-polls, so a restart with the IDE
already up is sufficient. Failing that, document that the IDE must be started *after* the session, not
before — which is the opposite of the intuitive order and worth stating explicitly.

## 12. A menu popup on a running form cannot be seen or driven — **CLOSED** (2026-09-01)

> **Fixed for the tree and for driving; `take_snapshot` still cannot show a dropped-down menu.**
>
> The suggested fix said #61 would cover this. It did not, and could not: #61 picks between *windows*, and
> a popup never enters `lifetime.Windows`. Two separate causes, both now addressed.
>
> **Seeing.** A popup's content is not under the popup in the visual tree — it is realised in the popup's
> own root — so the walk crossed nothing and every open menu reported `"children": []`. The control-view
> collector (and the `#AutomationId` descendant search) now step through an open `Popup` into its content.
> Because `Resolve` shares that collector, the emitted paths round-trip, so `interact` and `press_key`
> reach popup items too.
>
> **Driving.** `MenuItemAutomationPeer` exposes **no providers at all** — not `invoke`, not
> `expandCollapse` — which is why every verb failed, not just `expand`. `Interact` now falls back to
> `MenuItem.Open()`/`Close()` for a submenu, and to raising `Click` for invoke (MenuItem's class handler
> for that event executes a bound `Command`, so one raise does what a real click does). `DescribeProviders`
> reports those tokens for a `MenuItem`, because an action nobody can see advertised is an action nobody
> tries — `expandCollapse` only where `HasSubMenu`, so a leaf still refuses honestly.
>
> **Separators** are no longer collapsed as structural. They match every "transparent wrapper" condition
> and are still not plumbing: a separator's *position* is the VB6-fidelity question. **They appear only
> under `interactiveOnly: false`** — the default filter drops them, since they have no providers and no
> interactive descendants.
>
> Verified live against `demo/bill-of-fare` while running: `File` and `View` open via `interact expand`;
> `Save As...` reports `isEnabled: false`; `PART_InputGestureText` carries the shortcut text; the
> `Separator` sits between `Zoom` and `Refresh`; `View ▸ Zoom ▸ Zoom In` — two popups deep — resolves and
> invokes, and the form's own label reads *"you chose: View > Zoom > Zoom In (a submenu, two levels
> deep)"*. Eight headless tests in `MenuPopupTests` pin the behaviour.
>
> **The snapshot half is closed too, in a follow-up.** `SnapshotComposer` renders the window and each open
> popup root separately and composes them at their real screen positions, so a dropped-down menu is now in
> the picture — verified live on `demo/bill-of-fare`: *Zoom ▶*, the separator as a rule, *Refresh  F5* with
> its shortcut, and the Zoom submenu open beside its parent in the right z-order.
>
> Three things that cost a build cycle each, worth knowing before touching this code:
>
> - **Render the popup's ROOT, not `Popup.Child`.** The root owns the border, padding and shadow the menu
>   is drawn with; the child alone came out short and shifted.
> - **`DrawingContext.DrawImage(image, destRect)` samples the source's *device-independent* extent.** On a
>   150% display that reads the top-left two-thirds of the piece and stretches it to fill — a correctly
>   placed, correctly sized menu box with magnified, clipped contents. The three-argument overload with an
>   explicit pixel source rect is what is wanted.
> - **The window still goes through `RenderTargetBitmap.Render`, not the context.** That path was already
>   right; routing it through `DrawImage` made the whole form `scaling` times too big.
>
> None of this is visible headlessly, because **headless hosts popups as overlays inside the window** — so
> the defect cannot occur there and a "the popup appears" test passes with the fix removed. The headless
> tests therefore cover the overlay path (the composer must not draw an already-rendered popup twice) and
> the desktop path is verified against the running IDE.



**Symptom.** Verifying that a running form's menus render correctly, `take_snapshot` shows the menu *bar*
but never a dropped-down menu, and `dump_visual_tree` reports every top-level `MenuItem` with
`"children": []`. `interact` refuses both `expand` and `invoke` on a top-level `MenuItem`
(`element does not support 'expand'`), and pressing Enter or Down on it via `press_key` highlights the item
without opening it.

The consequence is that the *inside* of a menu is unverifiable through MCP: separators, shortcut text drawn
right-aligned in the item, item enabled/checked state, and submenu nesting are all only reachable as data.

**Cause.** A menu's items are realised in a popup, which is its own top-level window. `take_snapshot`
captures the form's window, and the popup is not in it — the same reason
[#61](https://github.com/hexide-io/HexIDE/issues/61) reports runtime modal dialogs as invisible. The empty
`children` array is not a bug in the dump: sub-items genuinely do not exist in the tree until the menu is
opened.

**Workaround.** Assert the structure headlessly, where the objects the loader builds can be inspected
directly — `RuntimeMenuTests` covers the bar's contents, sub-item nesting, separators, `InputGesture` and
click dispatch that way. Then verify live only what the *form's own window* can show: the bar's captions,
and the effect of a shortcut, by having the handler write to a `Label` on the form. That combination did
verify #85 end to end, but it verified the dropdown's contents by proxy rather than by looking at them.

**Suggested fix.** Whatever fixes #61 for modal dialogs should cover this, since it is the same underlying
limitation — snapshot and tree-walk the topmost popup root rather than only the form's window. An
`interact` action that opens a menu (`open`/`expand` mapped to `MenuItem.IsSubMenuOpen`) would be the other
half, since a popup that cannot be opened cannot be captured either.

---

## 13. `shutdown_ide` reports success but the process survives a runtime modal — **CLOSED** (2026-09-01)

> **Fixed.** The probable cause below was right about the symptom and wrong about the mechanism. Nothing
> was blocking the message loop: HexIDE sets no `ShutdownMode`, so Avalonia's default
> **`OnLastWindowClose`** applies, and closing the main window while a running program's `VBFormRuntime`
> and its `MsgBox` were still open simply left windows open — so the app kept running, exactly as
> configured. Confirmed by the inverse: with no project running, the old `shutdown_ide` exited cleanly
> every time.
>
> `shutdown_ide` now stops a running project and closes any window still open before closing the main
> one — both gated on `force`, because `force=false` promises what a *user* closing the window sees, and
> a user does not have their program stopped or their dialogs shut from under them. There, the IDE
> staying up is a correct outcome rather than this bug.
>
> It also returns a result now: `{requested, projectStopped, dialogsClosed, note}`. `requested` is
> deliberately not `succeeded` — the reply has to be sent *before* the process exits, so no in-process
> result can honestly claim it did. The note says to poll `/health`, which is the only real confirmation.
>
> Verified live on both paths, so neither is dead code: a `Form_Load` `MsgBox` over a running program
> gives `projectStopped: true, dialogsClosed: 0` (ending the project takes its dialog with it), and an
> open Tools ▸ Options modal gives `projectStopped: false, dialogsClosed: 1`. `/health` stops answering
> within a second in both cases, the process is gone, and the build that used to fail on a lock succeeds.



**Symptom.** With a running VB6 program showing a `MsgBox`, `shutdown_ide` returned without error and the
process stayed alive. The next `dotnet build` then failed on a file lock:

```
error MSB3027: Could not copy "apphost.exe" to "bin\Debug\net10.0\HexIDE.Desktop.exe".
Exceeded retry count of 10. The file is locked by: "HexIDE.Desktop (13436)"
```

**How it bit.** It breaks step 1 of the documented rebuild cycle in `CLAUDE.md` precisely when a modal is on
screen — which, since [#61](https://github.com/hexide-io/HexIDE/issues/61), is exactly the state you are
most likely to be in while verifying dialog behaviour. The failure is silent at the tool boundary: the
success result says shutdown happened, and the lock error arrives one step later looking like a build
problem rather than a shutdown one.

**Cause (probable, not confirmed).** Shutdown runs the normal close path, and a modal dialog owning the
message loop keeps a window alive, so the lifetime never completes. The tool returns once it has *requested*
shutdown rather than waiting for the process to exit.

**Workaround.** The fallback already in `CLAUDE.md`: `Stop-Process -Name HexIDE.Desktop -Force`, then build.
Stopping the running project first (`stop_project`) should also clear it.

**Suggested fix.** Make `shutdown_ide` close any runtime dialogs and stop a running project before closing
the shell, and have it confirm the lifetime actually completed rather than returning on request. A result
that distinguished *requested* from *exited* would be enough on its own to make the failure legible.

---

## 14. `set_file_content` silently drops a form's `Attribute` header if the caller omits it

**Symptom.** Writing a fresh body to a form removes its attribute block:

```
-Attribute VB_Name = "frmBillOfFare"
-Attribute VB_GlobalNameSpace = False
-Attribute VB_Creatable = False
-Attribute VB_PredeclaredId = True
-Attribute VB_Exposed = False
```

No warning, no error, and the change is written straight to disk — `hasUnsavedChanges` reads `false`
afterwards, because as far as the IDE is concerned the save succeeded.

**How it bit.** Using `set_file_content` to drop a few probe lines into `demo/bill-of-fare`'s form for a
live check. It replaced the whole code section, taking the header and every event handler with it, and the
damage reached a commit before it was spotted in `git diff`.

**Not a defect in the tool.** `get_file_content` returns the attribute block too, so the pair is
self-consistent and a **get → modify → set** round-trip preserves everything. The trap is that "the VB6
source code of a form" *includes* the `Attribute` header, which is easy not to know: it is invisible in the
IDE's editor, VB6 hides it, and nothing in the tool description mentions it. `VB_Name` is load-bearing.

**Workarounds.** (a) Always `get_file_content` first and edit the returned text. (b) For a throwaway probe,
use a scratch project (`--newproject`) rather than a demo or a real one. (c) `git status` before committing
after any live verification — that is what caught it here, one commit late.

**Suggested fix.** Either preserve the `Attribute` block when the incoming content has none — the IDE knows
the form's name and can re-emit the header it just parsed — or refuse the write with "content is missing
the Attribute header; call get_file_content first". Silently accepting a body that destroys a form's
identity is the one behaviour that should not be available.

## 15. `dump_visual_tree` shows a hidden element as though it were on screen

**Symptom.** The read-only banner `TextBlock` appears in the tree for a form that is *not* read-only, with
`isOffscreen: false` and no other flag distinguishing it from a rendered element:

```
"path": ".../Custom[Root]/Text[Read-only — HexIDE cannot yet reproduce this form faithfully, ...]",
"isEnabled": true, "isOffscreen": false
```

The banner is bound to `IsReadOnly` and was collapsed. Nothing in the node says so.

**How it bit.** Verifying #152 against a brand-new UserControl. The tree said the read-only banner was
present and on screen, which would have meant a fresh, empty, perfectly reproducible `.ctl` was being held
unsaveable — a serious bug, and one entirely consistent with the change under test. It took a
`take_snapshot` to establish that nothing was rendered and the IDE was behaving correctly.

**Workaround.** Treat presence in the tree as "exists in the template", never as "visible". For anything
whose whole meaning is *whether it is showing* — banners, validation text, overlays, empty-state
placeholders — confirm with `take_snapshot`, or assert the bound view-model property via
`inspect_element` rather than reading the tree.

**Suggested fix.** Carry the real visibility on the node — `isVisible` from `Visual.IsVisible` (and ideally
`isEffectivelyVisible`, since an ancestor may be the one collapsed). `isOffscreen` is a UIA concept about
scroll position and clipping, and it does not answer this question. Without it the tree cannot be used to
assert the absence of a warning, which is exactly the assertion a fidelity gate needs.

## A collapsed ComboBox reports only its selected item, which reads as "the item is missing"

**Symptom.** `inspect_element` on a closed dropdown returns a `selectionItems` array containing just the
current selection:

```
"selectionItems": ["Form1"],
"value": "Form1"
```

The control had two items. `expand` then `dump_visual_tree` shows both:

```
ComboBoxItem[Sub Main]
ComboBoxItem[Form1]
```

**How it bit.** Verifying that `Sub Main` had been added to the Project Properties → Startup Object list
(#210). The inspection said the list held only `Form1`, which is exactly what a change that had failed to
take effect would look like — and the build had just been rebuilt and relaunched, so "the binary is stale"
was the obvious next hypothesis. The feature was working the whole time.

`interact(action: "select")` is honest about this — it fails with *"has no selectable items realized — if
it's a dropdown, 'expand' it first"* — and `dump_visual_tree`'s own description warns that virtualized
dropdown items are not addressable until realized. `inspect_element` carries no such warning and reports a
plausible-looking array instead of an empty one, which is the part that misleads.

**Workaround.** Never read `selectionItems` from a collapsed dropdown as the item list. `expand` first,
then `dump_visual_tree(root=<combo>)` and read the `ComboBoxItem` children. Note the popup closes between
calls, so expand again immediately before `select`.

**Suggested fix.** Either omit `selectionItems` when the items are unrealized, or mark it — an
`itemsRealized: false` alongside it would be enough. A partial list that looks complete is worse than no
list, because it supports a confident wrong conclusion; an empty array with a flag supports none.

## A Project Explorer node that is not a form or module cannot be selected or opened

**Symptom.** There is no way to drive "select this tree node, then open it" for any node kind beyond forms
and modules. Verified against a related-document node (a file the project carries but does not compile):

- `interact select` on its `TreeViewItem` → `element does not support 'select'`; the item exposes only a
  `scroll` provider, no `selectionItem`.
- `press_key` on the `TreeView` does nothing useful — `inspect_element` reports the tree as
  `isKeyboardFocusable: false`, so arrow keys never reach a selection model.
- `interact set_property` cannot help either: `ProjectToolViewModel.SelectedItem` is typed `Object`, and the
  reflection fallback coerces a **string** to the property type. There is no way to name an existing
  view-model instance as a value.
- `open_file(name)` opens a form or module by name only, so it cannot reach anything else.

**Consequence.** The double-click-to-open gesture is unverifiable through MCP for any new node kind. That
matters because CLAUDE.md requires a UI feature be confirmed against the running IDE, and here the *tree*
can be confirmed by snapshot while the *gesture* cannot be driven at all.

**Workaround.** Split the verification and say which half was driven. The node's presence, icon and caption
are visible in `take_snapshot` and assertable via `dump_visual_tree` (the node reports its
`dataContextType`, so its type is checkable). The routing — selection to the right editor — and the editor's
own behaviour are covered by tests instead. State plainly that the gesture itself was not driven.

**Suggested fix.** Either surface a `selectionItem` provider on tree items so `interact select` works, or
add a selection-by-path action (`interact select_node` taking the same `path` the tree dump already
returns). The second is probably better: paths are already the addressing scheme everywhere else in this
server, and it would work for every current and future node kind rather than needing a provider per
control. A narrower `open_file` that accepts any project member would help too, but would not fix
selection, which is what several context-menu commands key on.

## A MenuFlyout attached to a toolbar Button cannot be opened, and its items are invisible

**Symptom.** The Add Item toolbar button (`Standard.AddForm`) carries a `MenuFlyout` holding eight
Add commands. None of it is reachable:

- `interact invoke` on the button reports success and does nothing visible — it fires the button's own
  invoke, which is not what opens a flyout.
- `interact expand` fails: the button advertises only an `invoke` provider, no `expandCollapse`.
- `press_key Space` on the button reports success and does not open it either.
- `dump_visual_tree(root=<button>)` returns the button with `children: []`, both with
  `interactiveOnly: false` and at full depth — the flyout's items are simply not in that subtree.
- A synthetic Win32 click at the button's rect (DPI-corrected, see below) did not open it either.

**Consequence.** Eight toolbar commands cannot be verified through MCP at all — not driven, and not even
*observed* to be enabled or disabled. Note the contrast that makes this easy to misdiagnose: menu-bar
dropdowns work fine (`interact expand` on `MenuItem[Project]` opens it, and its items appear in the tree and
in `take_snapshot`), so the obvious inference is that "menus work" — they do, but only the menu bar's.

**Workaround.** Verify the command, not the menu item. `inspect_element` on the button lists the
DataContext's members, so the presence of the bound command (e.g. `AddFileCommand`) is confirmable there,
and the same command can be driven end-to-end through its menu-bar twin where one exists. Say explicitly
that the toolbar entry was verified structurally rather than driven.

**Suggested fix.** Give a control that owns a `FlyoutBase` an `expandCollapse` provider, so `interact
expand` opens it and the popup's contents then become dumpable; failing that, an `open_flyout(target)`
action. Whichever route, the flyout's items need to reach `dump_visual_tree` — a popup that opens but
cannot be walked only moves the problem.

## take_snapshot renders DIPs while Win32 coordinates are physical pixels

**Symptom.** Driving a synthetic mouse click from a `boundingRect` needs a scale conversion that nothing in
the tool output mentions. On the machine this was hit on, `GetClientRect` reported 987 × 560 physical pixels
while `take_snapshot` returned a 1481 × 840 image and `inspect_element` reported bounds in that same 1481-wide
space — a factor of 0.666. Clicking at the raw `boundingRect` coordinates lands roughly 50% off, far enough
to hit a different control and look like "the click did nothing".

**Consequence.** Any fallback that leaves the MCP surface for real input — the only route left when a
control has no usable provider — silently targets the wrong place, and the resulting no-op is easy to
misread as the feature being broken.

**Workaround.** Derive the factor before clicking: `GetClientRect` width ÷ snapshot image width, then
multiply the DIP coordinate by it and pass through `ClientToScreen`. Do not assume 1.0, and do not assume
the usual Windows 1.25/1.5 either — measure it.

**Suggested fix.** Report the scale explicitly. `take_snapshot` returning the render scale alongside the
path (and `inspect_element` naming the space its `boundingRect` is in) would remove the guesswork; the
values are already known to the server.

## get_project_info omits every project member that is not a form or module

**Symptom.** `get_project_info` returns `forms` and `modules` only. A project carrying a related document (a
file it does not compile) reports it nowhere, so after adding one the tool's output is byte-identical to
before.

**Consequence.** The obvious check after an add — call `get_project_info` and see the new member — silently
answers "nothing happened" for a whole member kind. It reads as a failed feature rather than a blind tool.

**Workaround.** Confirm through the Project Explorer instead: `take_snapshot` shows the node and its icon,
and `dump_visual_tree` reports the node's `dataContextType`, which distinguishes a related document from a
module.

**Suggested fix.** Add a `relatedDocuments` array, and prefer a shape that will not need this edit again the
next time a member kind is added — a single `members` array of `{name, kind, path}` would cover forms,
modules, related documents and whatever follows.

## A runtime error dialog can be missed entirely, and its text cannot be read

**Symptom.** Running a project that raises a runtime error opens a modal dialog over the running form.
`take_snapshot` does report it (`"activeDialog": "HexIDE"`) and captures it — but only while it is still up.
`stop_project` and `shutdown_ide` both close open dialogs, so the ordinary automation rhythm of run → stop →
snapshot destroys the evidence before it is ever seen. A run whose form silently did nothing looks identical
to a run whose error dialog was dismissed a moment earlier.

Separately, once the dialog IS captured, its text is only readable as pixels. `inspect_element` on the
`RuntimeErrorView` lists `ErrorText` among the DataContext members but returns no value for it, so the
message has to be read off a PNG.

**Consequence.** This is how a real defect stayed hidden: a runtime error raised inside a `.bas` module was
being swallowed (the handler threw before it could show the dialog), and the automated symptom — a form that
runs and does nothing — was indistinguishable from success. It was found only because a human happened to
see a dialog flash on screen.

**Workaround.** `take_snapshot` BEFORE `stop_project`, always, on any run that might raise. Treat
`activeDialog` in the snapshot result as the signal — it names the dialog even when the image is hard to
read. To confirm a swallowed error, check the IDE log at
`%LOCALAPPDATA%\HexIDE\logs\ide\ide-*.log`; an exception thrown inside the error handler lands there.

**Suggested fix.** Expose string property values in `inspect_element`'s `dataContextMembers` (at least for
simple scalars), so a dialog's message is assertable rather than only legible. And consider a
`get_last_runtime_error` that survives the dialog being dismissed — the interesting state currently exists
only for as long as a modal is on screen.

## inspect_element lists a property but never its value

**Symptom.** `dataContextMembers` gives each member's name, type and `canWrite`, but no current value. Two
cases hit in one session: `ImmediateToolViewModel.Document` (the Immediate window's contents) and
`RuntimeErrorViewModel.ErrorText` (a runtime error message). Both had to be read by screenshotting and
squinting, and the Immediate window's buffer scrolls, so output beyond the visible ~4 lines is unreachable
without resizing the pane.

**Consequence.** Any assertion about text the IDE produced — program output, an error message, a status
line — degrades from a structured check to reading a PNG. That is slower, and it silently caps at whatever
the pane happens to show.

**Workaround.** Keep program output to one line per run so it fits the visible strip, and restart the IDE
between runs when the buffer needs clearing (there is no clear-Immediate action). Note that setting the
containing `ToolDock`'s `Proportion` via `interact set_property` reports success but does not re-run the
layout, so it does not actually enlarge the pane.

**Suggested fix.** Return scalar property values (string/number/bool) alongside the member list. A
`get_immediate_output` returning the Immediate buffer as text would remove the whole class of workaround.

## A carried file cannot be opened at all

**Symptom.** A project's related documents (a `RelatedDoc=` in the `.vbp` — a README, a changelog, a
`.sql`) appear in the Project Explorer as `RelatedDocViewModel` nodes, and there is no MCP route to open
one. Every door is shut:

- The tree opens an item on **double-tap** (`ProjectToolView.axaml` → `TreeView_OnDoubleTapped` →
  `ProjectToolViewModel.OpenSelected()`), and `interact` has no double-click action.
- `interact select` on the `TreeViewItem` fails with *element does not support 'select'* — it reports only
  a `scroll` provider, so the row cannot even be **selected**, which `OpenSelected` reads.
- `OpenSelected()` is a plain method, not an `ICommand`, so `interact invoke_command` cannot reach it. The
  context menu's `ViewCodeCommand` handles forms and modules only.
- `open_file` and `add_file` are documented as forms and modules only, and reject a related document.
- `Project > Add File...` on an already-carried file does open it, but goes through
  `IStorageProvider.OpenFilePickerAsync` — a native Win32 dialog no MCP tool can drive.

**Consequence.** A whole editor type is unverifiable. This blocked the live check for #255: the point of
that change is attaching a language server for a file type HexIDE has no other support for, and a carried
`.md` is exactly that file. The configuration, the registry and the bundled server were all confirmable;
the foreign server's diagnostics rendering in an editor were not, and needed a human to double-click.

**Workaround.** None that stays inside the tool surface. Ask the user for the one gesture. (Declaring the
foreign server for `vb6://` documents instead reaches the routing, but proves it with a configuration no
user would write, and the file still cannot be opened.)

**Suggested fix.** Either a `double_click` action on `interact` (general, and the smaller change), or
teach `open_file` to accept a related document by name, since it is already the "open this project item"
tool and its current refusal is the surprising part. Note that selection is broken independently: the
`TreeViewItem` exposing no `selectionItem` provider is worth fixing on its own, because it blocks every
context-menu path in the Project Explorer, not just this one.
