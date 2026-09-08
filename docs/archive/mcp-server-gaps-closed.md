# MCP dev-server gaps — closed

Entries retired from [`../mcp-server-gaps.md`](../mcp-server-gaps.md). They are kept, rather than
deleted, because each records a *measurement*: what the symptom looked like, what was tried, and which
reading of it turned out to be wrong. That is the part which stops a fixed gap being rediscovered as a
new one, and it is not recoverable from the commit that closed it.

The live document holds only what is still open, so its length means something again.

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

## A carried file cannot be opened at all — FIXED

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

**Fixed** in the change that needed it. `open_file` now accepts a carried file by name, and
`get_project_info` lists them, so the whole editor type is drivable. That route was chosen over a
`double_click` action on `interact` for a reason worth recording: **it changes no tool's parameters**, so
it needed no schema change and therefore no session restart — the fix was usable in the session that
found the gap. `open_file` was already the "open this project item" tool; its refusing one kind of item
was the surprising part.

**Still open, and independent:** the `TreeViewItem` in the Project Explorer exposes no `selectionItem`
provider, so a row cannot be selected through `interact` at all. That blocks every context-menu path in
that tree, not just this one, and `open_file` does not help there. `interact` also still has no
double-click action, which is the general form of the problem.

---

## 9. `press_key` on a non-focusable container silently does nothing — **CLOSED** (2026-09-08)

> **CLOSED (2026-09-08).** Both of these were one root cause: `press_key` raised the event on a control
> the caller did not mean, and reported success either way. `type_text` and `press_key` now use separate
> resolvers — `press_key` takes the DEEPEST input surface (AvaloniaEdit's `TextArea`, not the `TextEditor`
> that wraps it), falls back to the first focusable descendant, and FAILS rather than succeeding when
> nothing under the target can take keyboard focus. The reply names the receiving control whenever it is
> not the one addressed, so a no-op is attributable. Pinned by `UiAutomationDriverTests.PressKey_*`.


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

---

## press_key cannot reach a handler attached to TextArea, which is where the code editor's handlers live — **CLOSED** (2026-09-08)

> **CLOSED (2026-09-08).** Both of these were one root cause: `press_key` raised the event on a control
> the caller did not mean, and reported success either way. `type_text` and `press_key` now use separate
> resolvers — `press_key` takes the DEEPEST input surface (AvaloniaEdit's `TextArea`, not the `TextEditor`
> that wraps it), falls back to the first focusable descendant, and FAILS rather than succeeding when
> nothing under the target can take keyboard focus. The reply names the receiving control whenever it is
> not the one addressed, so a no-op is attributable. Pinned by `UiAutomationDriverTests.PressKey_*`.


**Symptom.** `press_key` reports `{"success":true,"mechanism":"keyboard","detail":"pressed F12"}` and nothing
happens. No exception, no log line, no visible effect — indistinguishable from a feature that is bound and
broken. It cost a wrongly-filed issue (hexide-io/HexIDE#325) and a round of diagnosis into the wrong layer.

**Mechanism.** `UiAutomationDriver.PressKey` resolves its target through `FindTextSurface`, which returns
the first **`TextEditor`** descendant, then raises `KeyDownEvent` on it. But AvaloniaEdit nests
`TextEditor` → `TextArea` → `TextView`, and `CodeEditorView` attaches its key handling to **`TextArea`**
(`CodeEditorView.axaml.cs:229`, `RoutingStrategies.Tunnel`).

A routed event raised on `TextEditor` tunnels *down to* it and bubbles *up from* it. `TextArea` is beneath
it in the tree, so it is on neither route. The handler cannot fire, however correct it is — and the tool
reports success, because raising the event did succeed.

So **every keyboard command in the code editor is undrivable at the obvious target**: F12 (go to
definition), Enter auto-indent, Shift+Alt+F (format), and the whole edit-while-running reset prompt.

**Workaround.** Address the `TextArea` explicitly. It is in `dump_visual_tree` as the `None` node with
`className: "TextArea"` under the editor's `PART_ScrollViewer`:

```
…/Pane[#0]/Custom/Pane[PART_ScrollViewer]/None
```

`FindTextSurface` returns a `TextArea` unchanged when handed one, so the event raises on the right element
and tunnel handlers fire. Verified: F12 at a call site then moves the caret to the declaration.

**Suggested fix.** `FindTextSurface` prefers `TextEditor` over `TextArea`
(`descendants.FirstOrDefault(c => c is TextEditor) ?? descendants.FirstOrDefault(c => c is TextArea)`),
which is right for `type_text` — inserting at the caret wants the editor's own API — and wrong for
`press_key`, which wants the innermost element so the route covers everything above it. The two tools want
opposite ends of the same chain, so the preference belongs at the call site rather than in a shared helper:
`press_key` should prefer the *deepest* text surface, `type_text` the outermost.

**The general lesson, which is the reason this is written down.** A synthetic `RaiseEvent` reproduces a real
keypress only for handlers on the target or its ancestors. Anything attached below the element the harness
picked is unreachable and reports success. When a keyboard-driven feature appears to do nothing, establish
that the handler is on the event's route **before** concluding anything about the feature.

## 4. Can't snapshot or drive the IDE while a form is running — **CLOSED** (#340, 2026-09-08)
> **Fixed.** Every window-addressing tool — `take_snapshot`, `dump_visual_tree`, `inspect_element`,
> `interact`, `type_text`, `press_key`, `hover` — takes an optional `window`: `"auto"` (the default,
> unchanged) or `"ide"`. The policy lives in `ForegroundWindow.Pick(scope, …)` beside the rule it
> qualifies, and `take_snapshot` now shares the resolver instead of repeating the selection inline.
>
> The diagnosis below was right, and its most useful part is the *negative* result: activation is not the
> lever. `set_window_state`, breaking before the form is shown, and `activate_document_tab` all succeed and
> change nothing, because the preference is in target *selection*. Three failed workarounds are why the fix
> is a parameter and not a focus call.
>
> **Measured while paused at a breakpoint**, which is the state the whole entry is about:
>
> ```
> dump_visual_tree()              → window: "Form1"   (VBFormRuntime — the old behaviour)
> dump_visual_tree(window:"ide")  → window: "MainWindow", "Project1 - HexIDE [run]"
> hover(<code editor>, window:"ide")
>                                 → tip: total = 42
> ```
>
> That last line is the **debugger's Auto Data Tip**, read as text — the thing gap 7 was filed for in P6c
> and could not verify. Confirmed on screen by the maintainer at the same moment.
>
> A `"form"` scope was considered and left out: `"auto"` already resolves to the running form, and an enum
> cannot say *which* form once a project shows more than one. An unrecognised value is named rather than
> quietly treated as `"auto"` — a caller passing this at all wants a window the default would not give them.


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

**And now `hover`, which is what makes this the blocker it is (2026-09-08).** The `hover` action closed the
editor half of gap 7, so **Auto Data Tips are the one paused-state feature that a tool could otherwise verify
outright** — not a pixel, but the tip's actual text. It cannot: `hover` resolves a path against the active
window like everything else, so with a form up there is no path to the code editor to aim at.
`activate_document_tab` was tried as an escape hatch and **also fails** — it succeeds, and the tools still
resolve against `VBFormRuntime` — which is a third confirmation, after `set_window_state` and the
break-before-`Show` attempt, that the preference is in target selection and not in activation.

So the "highest-value dev-server fix" note below is now understating it: this no longer blocks only *pixel*
verification of paused-state panes. It blocks a **textual, assertable** one.

**Workarounds used.** (a) `get_debug_state` for the pause fact; (b) a post-`stop_project` IDE snapshot to confirm
the caret-reveal + Immediate output + tool-pane chrome; (c) the user confirming live paused-state pixels
(amber bar, populated Locals/Call Stack rows).

**Fix consideration.** A `take_snapshot` **and** `dump_visual_tree` target/scope parameter
(e.g. `window: "ide" | "form" | "auto"`), so debugger/IDE-chrome verification can force the main window even while a
form runs. This is now the single highest-value dev-server fix — it blocks pixel-verifying *every* paused-state tool
pane (Locals, Call Stack, and future Watches/data-tips).

---

## 15. `dump_visual_tree` showed a hidden element as though it were on screen — **CLOSED** (#341, 2026-09-08)


> **Fixed.** A node carries `"isHidden": true` when it is in the tree but not on screen; the field is
> absent when it is showing. `inspect_element` reports the same on a single control.
>
> **Effective visibility, not the local flag.** A control can be `IsVisible` itself and still be invisible
> because an ancestor is collapsed, so reporting `Visual.IsVisible` would have moved the same trap up one
> level instead of closing it. `IsEffectivelyVisible` answers the question a caller is actually asking.
>
> **Null rather than false**, so the field is absent from the overwhelming majority of nodes: a dump runs to
> hundreds, and the hidden one is the exception worth spelling out.
>
> `isOffscreen` is left alone and still means what UIA means by it — clipping and scroll position. The two
> are genuinely different: a control scrolled out of view is *offscreen and visible*; a collapsed one is
> *visible to UIA and not showing*. Neither implies the other, which is why one more flag was the fix
> rather than a correction to the existing one.
>
> Verified against the case in the report below — the read-only banner on a form that is **not** read-only:
>
> ```
> "path": ".../Custom[Root]/Text[Read-only — HexIDE cannot yet reproduce this form faithfully, ...]",
> "isEnabled": true, "isOffscreen": false, "isHidden": true
> ```
>
> Its visible siblings in the same dump carry no `isHidden` at all. The entry had gone on misleading after
> it was written, too: the same banner appeared in every dump taken while investigating #334 and was read,
> again, as a form being held unsaveable.


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

## A runtime error dialog could be missed entirely, and its text could not be read — **CLOSED** (#342, 2026-09-08)


> **Fixed, in both halves.**
>
> *Its text* is readable because `inspect_element` now reports property values (see the entry below):
> `ErrorText` comes back as a string instead of pixels.
>
> *Missed entirely* is fixed by `get_last_runtime_error`, backed by a `RuntimeErrorLog` recorded **before**
> the dialog is shown and deliberately not tied to its lifetime. `run_project` clears the message, so a
> result belongs to the most recent run; the `sequence` is **not** cleared and only increases, because a
> caller needs to tell "no error this run" from "the same error again" — comparing message text cannot,
> since a loop can raise the identical error twice.
>
> Measured live, in the exact rhythm that used to destroy the evidence — raise, dismiss, stop, then look:
>
> ```
> run → End → stop_project → get_last_runtime_error
>   {"raised":true,"message":"Run-time error '11': Division by zero, at 1 / 0",
>    "at":"2026-09-08T04:12:13+01:00","sequence":1}
>
> (clean program) run → get_last_runtime_error
>   {"raised":false,"sequence":0}
> ```
>

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

## inspect_element listed a property but never its value — **CLOSED** (#342, 2026-09-08)


> **Fixed.** `dataContextMembers` now carries each property's current `value` where it can be read as text.
>
> **Only recognised types are called at all** — strings, primitives, enums, `decimal`, the date/time types,
> `Guid`, and AvaloniaEdit's `TextDocument`. Reading a property invokes a getter, which is arbitrary code;
> restricting it keeps the blast radius to reads that are ordinary. Anything else is listed exactly as
> before, with no value.
>
> **`TextDocument` earns its special case**, and is why this closes rather than half-closes: the Immediate
> window's contents are a `TextDocument`, not a string, so a scalars-only reader would have left the most
> useful text in the IDE unreadable.
>
> **Null means "not read", `""` means "empty".** Collapsing the two would make *no output yet* and *cannot
> see the output* identical, which is the failure this entry is about. A throwing getter yields null rather
> than failing the whole inspection, and a value over 4000 characters is truncated with a marker saying so —
> `Debug.Print` output has no natural limit.
>
> Measured live, with the Immediate pane only ~4 lines tall:
>
> ```
> ImmediateToolViewModel.Document  → "before
clean run
"   (both runs, not the visible strip)
> RuntimeErrorViewModel.ErrorText  → "Run-time error '11': Division by zero, at 1 / 0"
> ```
>

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
