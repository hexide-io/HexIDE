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

## A MenuFlyout on a toolbar Button opened blind, and its items were invisible — **CLOSED** (#343, 2026-09-08)


> **Fixed — and the entry was wrong about why, in the direction that sends the next reader looking for the
> wrong fix.** Corrections first, because they were measured:
>
> - *"`interact invoke` ... does nothing visible — it fires the button's own invoke, which is not what opens
>   a flyout."* **False.** `Button.OnClick` opens the flyout; invoke has always opened it. What failed was
>   *seeing* the result.
> - *"`press_key Space` ... does not open it either"* and the heading *"cannot be opened"* — false for the
>   same reason.
>
> The single real cause: a `ContextMenu` and a `Button.Flyout` are attached with
> `ISetLogicalParent.SetParent`, so their `Popup` is never a **visual** child of anything and a visual walk
> cannot reach it however deep it goes. A menu-bar dropdown's popup *is* a visual child, which is why menus
> looked like they worked and these did not.
>
> `AttachedPopupOf` now finds a popup from its owner by all three routes — `Button.Flyout`,
> `FlyoutBase.GetAttachedFlyout`, and `Control.ContextFlyout` — and feeds it to the existing popup walk, so
> the items appear as children of the owner and their paths round-trip. `SnapshotComposer` shares the same
> definition, so a popup you can address is one you can also see. A control owning either now advertises
> `expandCollapse`, and `expand`/`collapse` open and shut it: the verb existing while nothing said so is how
> these eight commands came to be recorded as unreachable.
>
> Measured live, all the way through:
>
> ```
> interact invoke  Button[Standard.AddForm]        → success
> dump_visual_tree root=<that button>
>   → Pane/MenuItem[Form], MenuItem[MDI Form] (isEnabled:false), MenuItem[Module], … MenuItem[Add File...]
> interact invoke  …/Pane/MenuItem[Module]         → success
> get_project_info                                 → modules: ["Module1"]
> take_snapshot                                    → the flyout is in the picture
> ```
>
> Not merely observable: driven end-to-end. And `MDI Form` reporting `isEnabled:false` answers the
> Consequence's "not even *observed* to be enabled or disabled".


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

## A context menu opened but was invisible to both `take_snapshot` and `dump_visual_tree` — **CLOSED** (#343, 2026-09-08)


> **Fixed, same cause as the MenuFlyout entry above — one parenting problem wearing two faces, exactly as
> this entry predicted.** The Project Explorer's menu turned out to be a `MenuFlyout` on
> **`TreeView.ContextFlyout`** (Tools/Projects/ProjectToolView.axaml:125) — not a `ContextMenu`, and not on
> the `TreeViewItem`. Handling only `ContextMenu` and `Button.Flyout` left an open, plainly visible menu
> reporting nothing, which reads exactly like a menu that never opened, and was twice diagnosed that way
> before the AXAML was read.
>
> **The second half was lifetime, not visibility.** A popup opened over an UNFOCUSED owner light-dismisses
> almost immediately: `press_key Apps` does open this menu — watched on screen — but it is gone before the
> next MCP call can walk it, so every later look reported nothing. `interact expand` now focuses the owner
> before opening, and the menu stays up across calls. The same trap as the transient tooltip, one layer up.
>
> Measured live:
>
> ```
> interact expand  …/Custom/Tree                   → "opened flyout on 'Tree'"
> dump_visual_tree root=…/Custom/Tree
>   → MenuItem[Properties], MenuItem[Add] (expandCollapse), MenuItem[Print]
>     plus Set as Start Up / View Object / View Code, each isHidden:true
> take_snapshot                                    → Properties / Add ▸ / Print, in the picture
> ```
>
> The visible three match a photograph the maintainer took of this menu earlier the same day, and the
> `isHidden` flags — added the day before — are what separate them from the items that are merely present.
>
> **Still transient across calls.** Snapshot it in the call straight after opening: an intervening
> `dump_visual_tree` was enough for it to dismiss before the picture was taken.


**Symptom.** A context menu can be opened — and then neither tool can see it. `take_snapshot` returns the
window with no menu in it, and `dump_visual_tree` returns the ordinary window tree with no popup root. The
menu is plainly on screen the whole time.

**This is not a missing verb, and I recorded it as one until it was pointed out.** `interact` has no
right-click action, so the first conclusion was "a context menu cannot be opened at all". It can:

```
press_key(…/Custom/Tree/Pane/TreeItem, "Apps")
  → {"success":true,"detail":"pressed Apps on Border[SelectionBorder]"}
```

…opens the Project Explorer's menu (Properties / Add ▸ / Print). The keypress works. What fails is
*seeing* it, and the two failures look identical from the tool output — which is exactly how a capture
problem gets written up as an interaction problem.

**Cause.** Both tools discover popups by walking the **owner window's visual tree** for `Popup` children —
`SnapshotComposer.CollectOpenPopupRoots` (`:105-124`) and `UiAutomationDriver` (`:643`, `:683`). That
reaches a **menu-bar** popup, because `Menu` → `MenuItem` → `Popup` really are visual children, which is
why the fix for the closed gap 12 works. A `ContextMenu` is not: it is set as a *property* on the control
that owns it, so it is only logically parented and the walk never arrives.

Predicted, as it turns out: `artifacts/design/259-language-server-visibility.md` noted that these two call
sites "cross `Popup`s found via `GetVisualChildren()` and a flyout's popup may be only logically parented".
This is that, measured.

**A second oddity, recorded rather than explained.** A keyboard-invoked context menu is placed at the
pointer, and a synthetic key press carries no pointer position — so it opened at the last real pointer
location, which was over a *different application* entirely. Whether the placement contributes to the
capture failure or is merely untidy is not established: the composer never finds the popup at all, so it
never gets as far as caring where it is.

**Workaround.** `press_key` with `Apps` opens the menu, and its items can be invoked blind through
`interact invoke_command` on the owning DataContext if the command is known. So a context-menu *action*
can be driven; its *presentation* — that the right items appear, enabled, in the right order — cannot be
asserted at all.

**Suggested fix.** Enumerate open popups from the application's own top-level list rather than by walking
the owner window's visual tree; Avalonia tracks popup roots globally, and that reaches logically-parented
popups as well as visual ones. Both call sites want the same change, and closing it would also make the
`MenuFlyout` gap above (a flyout attached to a toolbar `Button`) tractable, since that is the same
parenting problem wearing different clothes.

A `context_menu(target)` action raising `ContextRequested` is still worth adding — `Apps` depends on the
target being focusable and on Avalonia's own key handling — but it is a convenience, not the blocker.

## 11. `add_control` mutated the designer but never persisted — **CLOSED** (#344, 2026-09-09)


> **Fixed.** `add_control` now saves the form, like its sibling `set_control_property` always did. The pair
> disagreed about whether a designer edit was durable, and the one that did **not** save is the one that
> creates things — which is why nine controls could exist, be reported as added, and not survive a crash.
>
> The save runs **on the UI thread**: a save first publishes `ApplyAllUnsavedChangesEvent`, whose handler
> reads AvaloniaEdit's `Document.Text` and throws off it, leaving the previous content to be written and
> reported as success (#334, closed the same week). And a refusal is **not** reported as success (#147): an
> unfaithful form comes back `success:false`, naming the control that is in the designer but not on disk,
> because a caller told "saved" would not find out otherwise until something else read that file.
>
> Measured live: `add_control(Form1, CommandButton, 40, 40, 100, 30)` → `Command0`, and the `.frm` on disk
> carried `Begin VB.CommandButton Command0` immediately, with no further call.


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

## 14. `set_file_content` silently dropped a form's `Attribute` header — **CLOSED** (#344, 2026-09-09)


> **Fixed, in both directions — the entry's case and its mirror (#338).**
>
> A form's leading `Attribute VB_*` block is now **kept** when the incoming content omits it, and the result
> says so rather than adjusting silently. Preserving beats refusing here: omitting the block is what a caller
> does when they mean exactly what they said — *replace the code* — and the block is not code.
>
> The mirror case is **refused**: a whole `.frm`, opening with a `VERSION` / `Begin` designer block, comes
> back as an error rather than being written into the code buffer where it is compiled as VB (#338).
>
> **The asymmetry with the module branch is deliberate.** A module's header carries nothing the model does
> not already own, so stripping it loses nothing. A form's designer block describes its **controls**, and
> this tool does not apply them — so accepting the file would either bury the header in the code or silently
> discard the controls the caller supplied. Strip where nothing is lost; refuse where something would be.
>
> Measured live, all three paths, checking the bytes on disk each time:
>
> ```
> set …  "Attribute VB_Name … / Private Sub Form_Load() … one"   → success
> set …  "Private Sub Form_Load() … two"        (no header)      → success + note
>        disk: Attribute VB_Name = "Form1" survives, body is "two"
> set …  "VERSION 5.00 / Begin VB.Form … "                       → refused; disk unchanged
> ```


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

---

## The Project Explorer could be read in full and not driven at all — **CLOSED** (2026-09-14)

> **Fixed in the same change that found it.** Two independent holes that only bite together, and the
> reason a whole navigation surface was unreachable.
>
> - **Tree nodes advertised no selection.** Every `TreeViewItem` reported `providers: ["scroll"]` — no
>   `selectionItem`, no `invoke` — so `interact(<node>, "select")` failed with *"element does not support
>   'select'"*. `DescribeProviders` now advertises `selectionItem` for a `TreeViewItem`, and `DoSelect`
>   sets `IsSelected`, which the owning `TreeView` reflects into `SelectedItem` — the property a view
>   model is actually bound to. Exactly the shape of the `DataGridRow` fix directly above it.
> - **Nothing could double-click.** UI Automation has no pattern for a double-click — it is a gesture, not
>   a control contract — so `interact` had invoke / select / set_value / toggle / expand / collapse and no
>   way to express the one that opens a document. Added as its own verb, `double_click`, which raises
>   `DoubleTapped` on the target and lets it bubble to whichever container handles it. It **selects the
>   target first**, because a real double-click does, and a verb that skipped that would fire the handler
>   against whatever was selected before — the wrong document, silently. The reply says whether the
>   selection moved.

**The measurement worth keeping.** The two gaps were invisible separately and fatal together, and the
reflection fallback did not cover either: `invoke_command` needs an `ICommand` and the Project Explorer's
`OpenSelected()` is a plain method (reasonably — it is a gesture handler, not a bindable command), while
`set_property` coerces from a string and `SelectedItem` is an `Object` holding a live node no string can
express. So the surface read perfectly through `dump_visual_tree` and refused every attempt to act on it,
which is the worst shape a tool can have: a caller sees a tree it appears able to address and finds out
only on the failing call.

**How long it hid, and behind what.** `open_file` already carried a branch routing carried files straight
to `EditorService`, with a comment naming all three halves of this — no double-click, no selection
provider, `OpenSelected` not a command — as the reason it existed. That workaround made one editor type
reachable and left the root cause in place, so the next feature to depend on the gesture (the read-only
project document) hit the same wall. A bypass that resolves one symptom is worth writing down as a gap
even when it unblocks the task, which is what that comment did and why this was quick to place.

---

## A scrolled tool window can only be verified down to its first screenful — **CLOSED** (#361, 2026-09-22)

> **Fixed, both halves, each measured on the running IDE.** `interact` now has a `scroll` action (`value`
> = `up`/`down`/`left`/`right` for a page, `line_up`/`line_down`/`line_left`/`line_right`, `home`/`end`).
> It moves the target, or the nearest control containing it that can scroll along the requested axis, so a
> caller can aim at the content it wants more of rather than at a `ScrollViewer` that is often a template
> part. Driven against the Properties list: `scroll down` aimed at a property *row* paged the list to
> 28.8%, and the snapshot showed rows below the fold. The reply says where the scroller now is, and a
> scroll that cannot move (`nothing to scroll vertically`, `already at that end`) fails rather than
> reporting a success that changed nothing.
>
> **The "cannot read the tab's content" half was never real, and neither was "cannot scroll" (#362).**
> The entry's attempt ran `dump_visual_tree` with its defaults, and `interactiveOnly: true` filters out
> plain text. With `interactiveOnly: false, maxDepth: 14` the same document returned every field of every
> server card; `inspect_element` on one card returned its whole `LanguageServerRowViewModel`, including a
> card below the fold; and `interact invoke` on the tab's own `PART_PageDownButton` already scrolled it,
> moving that card from y = 870 to 607. So before #361 scrolling was possible but roundabout, and
> reading was possible all along. What #361 added is a direct `scroll` action.
>
> An earlier version of this note said the reading half "had already closed" before #361. That was wrong
> in the same way as the entry: it read a working result as a fix, when nothing had been broken. The entry
> below is kept as written, because the mistake in it is the useful part.
>
> **What the entry did not notice.** `dump_visual_tree` was already *advertising* a `scroll` token, on 42
> nodes, while `interact` had no such action, so the capability this entry asked for was being promised
> and refused at the same time. Those phantom tokens also kept 40 otherwise-inert nodes in the default
> tree. With the action added and the token reported only where something would actually scroll, the
> default tree of a fresh Standard EXE project went from 256 nodes to 198.


**Symptom.** The Language Servers window (#259) lists every attached server, one card each, so its content
is routinely taller than the pane. `take_snapshot` captures what is painted, and there is no way to scroll
the content, so every server below the fold is unverifiable. Hiding a bottom-docked tool via
`set_tool_window_visible` buys one more card and no more.

**What was tried.** `press_key` with `End`/`PageDown` needs a `target` path, and the only addressable node
in that region is the `TabItem` — pressing a key there does not reach the `ScrollViewer` inside the tab's
content. `dump_visual_tree` returns the tab chrome (the `TabItem`, its close `Button`, its header `Text`)
but not the realised card content beneath it, so the values could not be asserted structurally either.
Two shapes of the same limit: the content of a document tab is neither drivable nor readable.

**Consequence.** Verification stopped at "the first two groups render correctly, with real servers and the
right fields". The case that most wanted checking — a server further down the list whose row shows
`Running` with nothing advertised — was covered by a view-model test instead. That is a reasonable place
for it, but it means the *rendering* of the most diagnostic row in the window is unverified, which is
exactly the substitution the visual-verification rule exists to prevent.

**Workaround used.** Assert the projection in `LanguageServersToolViewModelTests` and snapshot only what
fits. Note this is weaker than it sounds: a binding typo renders an empty row and passes every view-model
test.

**Suggested fix, cheapest first.** A `scroll` action on `interact` (`value` = `up`/`down`/`home`/`end`,
resolving to the nearest ancestor `ScrollViewer`) would close it for every scrolled surface at once, not
just this one — the Object Browser, the Translation Editor and the Locals tree have the same shape. Failing
that, `take_snapshot` could accept an optional element `path` and capture that element at its full desired
size rather than clipped to the viewport, which would also make long content diffable.

---

## 6. Can't open a Debug-menu dialog (e.g. Add Watch) via MCP to snapshot it — **CLOSED** (#362, 2026-09-22)

> **Closed, measured on the running IDE (#362).** `invoke_menu_item(path: "Debug/Add Watch...")` opened the
> dialog, `take_snapshot()` captured it (`activeDialog: "Add Watch"`), and `dump_visual_tree()` listed its
> expression box, three radio buttons and OK/Cancel, each drivable. The row half is closed by the tree
> fallback in `interact select`: a `TreeViewItem` is now reported with `selectionItem`, and selecting one
> sets the owning tree's `SelectedItem`. That path was driven on a Project Explorer node in the same pass,
> not on a Watches row.

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

## A Project Explorer node that is not a form or module cannot be selected or opened — **CLOSED** (#362, 2026-09-22)

> **Closed (#362).** A Project Explorer node is now reported as `TreeItem` with `selectionItem`, and
> `interact(target: ".../Custom/Tree/Pane/TreeItem/TreeItem", action: "double_click", window: "ide")` on
> Form1's node answered `double-clicked 'TreeItem' (selected it first, as a real double-click does)` and
> activated its designer. `open_file` also resolves carried files by name or filename
> (`HexIdeTools.OpenFileAsync`; the filename half only from #548, which found it matched the name alone). **Not driven in this pass:** a carried-file node specifically. The project
> had none, and `add_file` cannot create one. The route is the same one that worked on the form node.

**Symptom.** There is no way to drive "select this tree node, then open it" for any node kind beyond forms
and modules. Verified against a related-document node (a file the project carries but does not compile):

- `interact select` on its `TreeViewItem` → `element does not support 'select'`; the item exposes only a
  `scroll` provider, no `selectionItem`. (Since #361 a tree node reports no `scroll` either: nothing
  behind its peer scrolls, so the token was a lead that went nowhere.)
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

---

## get_project_info omits every project member that is not a form or module — **CLOSED** (#362, 2026-09-22)

> **Closed (#362).** `get_project_info` returns `relatedDocuments` beside `forms` and `modules`
> (`ProjectInfoResult`), seen live on 2026-09-22 as `"relatedDocuments":[]`. The suggested single
> `members` array was not adopted, so the next member kind will need this edit again.

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

---

## `list_lsp_messages` described a vocabulary it does not use, and promised data that was not there — **CLOSED** (#400, 2026-09-22)

> **Closed by the guard this entry asked for.** A tool whose reply renders an enum now carries
> `[DescribesEnum(typeof(T))]`, and `ToolDescriptionEnumTests` fails the build when its description leaves
> a member out. Opting in the four tools that render one found sixteen more members no description named:
> `get_debug_state`'s stop reasons, `get_watches`' watch types, `get_diagnostics`' severities (which it
> never mentioned at all), and `list_lsp_messages`' outcomes. All are now described. The check is for
> omission only: a stale name left in prose cannot be told from an ordinary capitalised word, but a
> renamed member is still caught, because its new name is missing.

**Symptom.** The tool's own description enumerates what a `kind` can be, because a caller who reads
`Unconsumed` in a reply has no other way to learn what it means. `ConversationEntryKind` has **nine**
members; the description named **seven** of them, and got one of those seven wrong:

- **`StandardError` and `Note` were absent entirely.** A caller shown `"kind": "StandardError"` had been
  told the set and it was not in the set, which reads as a bug in the tool rather than a gap in the
  sentence.
- **`Lifecycle` was described as "a process starting, stopping, its standard error and exit code".** It
  covers none of standard error and, at the time the sentence was written, no exit code either — nothing
  in the tree read one. So the description was the only place in the repository claiming the record held
  data it did not hold, and a caller who trusted it would have concluded the *server* was silent.
- **`direction` said "Sent, Received, or Local for the entries that are not messages at all"**, which
  puts every non-message under `Local`. A standard error line is `Received`, and the vocabulary's own
  definition says so.

**Why it survived.** The two omissions were kinds the enum had and the wire never produced — `StandardError`
because nothing raised it, `Note` because the only path to it was an undecodable frame that the recorder
never saw. A description drifts exactly where the code is unreachable, so the sentence agreed with the
observable behaviour and disagreed with the design. Nothing checks a `[Description]` string against the
enum it enumerates; the coverage guard that would have caught it is the one this repository applies to
`docs/lsp-client.md` and to the language packs, and tool descriptions have no equivalent.

**Fixed** by rewriting both sentences to the full set of nine — four for wire traffic, five that are not
messages — and to what each kind actually carries. The
underlying data gaps were closed in the same change: standard error and the exit code now reach the record
(`ILspTransport.Notice`), and an undecodable frame is recorded as a `Note` with its bytes rather than
dropped. Filed as hexide-io/HexIDE#400 for the general problem — a tool description that enumerates
a C# enum should be guarded against it, the way the LSP coverage table is guarded against the
specification.

---

## `invoke_menu_item` misses items with `_` in their text, and describes menus it cannot read (#544) — **CLOSED** (#560, 2026-09-22)

> **Closed (#560).** `MenuPath.Resolve` matches a segment as typed first and with its access key
> stripped second, so `File/Remove My_App` reaches the item showing that text. An `ItemsSource`-backed
> submenu such as Recent Projects is resolved through each entry's view model (`Header`, `Command`,
> `CommandParameter`), so its entries are listed and run without the submenu being opened first. A hidden
> item is neither listed nor run, at any level of the path, and the reply names it as hidden instead.
> Pinned by three `MenuPathTests`, each failing with its fix undone. Still open: a trailing `...` has to be
> typed (#564), and an entry built from a view model is always treated as visible.

**Symptom.** Three, all from #519's resolver (`MenuPath`). (1) An item whose displayed text contains an
underscore cannot be invoked: with a project named `My_App`, `invoke_menu_item(path: "File/Remove My_App")`
misses, because the caller's segment is stripped of an access key as if it were a header and becomes
`Remove MyApp`. The miss then lists `Remove My_App` among the items the menu holds. This worked before #519.
(2) `invoke_menu_item(path: "File/Recent Projects/anything")` answers `No item 'anything' in menu 'Recent
Projects'. It has no items.` while Recent Projects is visible, which it only is when it has entries: its
items come from `ItemsSource` and are not `MenuItem`s. (3) `invoke_menu_item(path: "Tolls/Options")` lists
`Go to github repo`, which is hidden on desktop, and invoking that path opens the browser.

**Workaround.** For (1) and (2), open the menu with `interact` `expand`, then `dump_visual_tree` scoped to
it, then `interact` `invoke` on the item: the realised containers carry the displayed text. For (3), check
`isHidden` in a dump before trusting a listing.

**Fix.** In the issue: match the segment as given, resolve item containers as well as `MenuItem` children,
and skip hidden items.

---

## `interact scroll` misses a target's own scroller, and `set_range_value` detaches a scroll bar (#545) — **CLOSED** (#566, 2026-09-22)

> **Closed (#566).** `scroll` now looks in the target's own template before walking upward, so aiming at
> an editor or a `DataGrid` scrolls it. `set_range_value` on a `ScrollViewer`'s bar moves the viewer's
> `Offset`, and any other range control goes through `SetCurrentValue`, so the template binding survives
> and a later `scroll` keeps the bar in step. The `DataGrid` case read from the code was real: setting its
> bar moved the bar and not the rows. A grid is now moved with the public `DataGrid.ScrollIntoView`, a
> whole row at a time, and the reply gives the offset it landed on beside the one asked for. A grid's
> horizontal bar has no such route and is refused. `NaN` and infinities are refused. Known limit: rows are
> located through `ItemsSource`, so a grid the user has sorted by a column can land on a different row
> from the one asked for; the reply still reports where it actually landed.

**Symptom.** (1) `interact(target: "…/Custom[Root]/None[TextEditor]", action: "scroll", value: "down")` on a
141-line code window answers `nothing to scroll vertically: neither 'TextEditor[TextEditor]' nor anything
containing it has content taller than its viewport`. The same call on `…/None[TextEditor]/Pane[PART_ScrollViewer]`
scrolls. The scroller is a template part inside the target, and the search only walks upwards. (2)
`interact(target: "…/Pane[PART_ScrollViewer]/ScrollBar[PART_VerticalScrollBar]", action: "set_range_value",
value: "2391")` moves the view and the thumb together. Afterwards,
`interact(target: "…/Pane[PART_ScrollViewer]", action: "scroll", value: "home")` puts the text back at line 1
and leaves the thumb at the bottom for the rest of the session: the local value outranks the template
binding. From reading, not yet reproduced: a `DataGrid`'s scroll bars take `set_range_value` without
scrolling, and `NaN` is accepted.

**Workaround.** Aim `scroll` at the `PART_ScrollViewer` inside the control, never at the control itself. Do
not use `set_range_value` on a `ScrollViewer`'s scroll bars; `scroll` with `home`/`end`/a page does the same
without breaking the binding.

**Fix.** In the issue.

---

## `inspect_element` does not show a range control's value (#550) — **CLOSED** (#561, 2026-09-22)

> **Closed (#561).** `inspect_element` reports a `range` object for any control with a range provider:
> `value`, `minimum`, `maximum` and `isReadOnly`. Seen live on a code window's vertical scroll bar as
> `{"value":0,"minimum":0,"maximum":905.39,"isReadOnly":false}`, and as `"value":400` after
> `set_range_value` with `400`.

**Symptom.** `inspect_element(target: "…/Pane[PART_ScrollViewer]/ScrollBar[PART_VerticalScrollBar]")`
reports `"providers":["rangeValue"]` and no value, minimum, maximum or read-only flag, though its description
promises the "current selection/value/toggle state". The bounds could only be learned from a refusal
(`set_range_value` with `999999` answers `… outside 'PART_VerticalScrollBar''s range 0..2391.59375`), and
whether a value took only from a snapshot.

**Workaround.** Probe with an out-of-range `set_range_value` for the bounds, and `take_snapshot` to see the
result.

**Fix.** Add the range provider's `Value`, `Minimum`, `Maximum` and `IsReadOnly` to the inspection.

---

## `add_watch` turns an unrecognised `watchType` into `Expression` without saying so (#546) — **CLOSED** (#556, 2026-09-22)

> **Closed (#556).** An unrecognised non-empty `watchType` is refused and nothing is added. The error names
> the accepted values: `'BreakWhenTru' is not a watch type, so no watch was added. Use Expression (the
> default), BreakWhenTrue or BreakWhenChanged.` Null, empty and whitespace still mean `Expression`. The
> tool now carries `[DescribesEnum(typeof(WatchType))]`, so its description is guarded against the enum.

**Symptom.** From reading `HexIdeTools.AddWatchAsync`: `add_watch(expression: "x > 5", watchType:
"BreakWhenTru")` falls through the `switch` to `WatchType.Expression`, adds a watch that will never break,
and replies with the watch list as if the call had done what was asked.

**Workaround.** Read `watchType` back from the reply's list after adding.

**Fix.** Refuse an unrecognised non-empty value and name the accepted spellings.

---

## A taken `--server-port` exits without running the shutdown handlers (#547) — **CLOSED** (#559, 2026-09-22)

> **Closed (#559).** The forced exit now runs, by hand and in order, the cleanup `ShutdownRequested` would
> have run. The add-in loader is disposed first, so a consent dialog closed by the exit records nothing;
> then the server context is cancelled and disposed. A throw from any of that is logged and the exit still
> happens with its code, and the log is closed after the windows. `ConsentAtShutdownTests` pins both
> directions at the registry: a late refusal records nothing, and a late Allow neither records nor loads.
>
> **The entry's premise was partly wrong.** Avalonia's forced `Shutdown` still closes every window, so the
> main window's `Closing` handler runs and saves window state and layout; it only loses its veto. A failed
> launch therefore still writes the profile it shares, which is part of #557. No test pins the order in
> `DesktopStartup` itself; that is #563.

**Symptom.** From reading `DesktopStartup.cs`: exit code 3 (#525) goes through Avalonia's forced
`desktop.Shutdown(exitCode)`, which does not raise `ShutdownRequested`. The add-in loader is never disposed,
and a third-party add-in's consent prompt, opened from the same `MainWindow.Opened` as the server start, can
then be recorded as **Block** when the closing dialog returns. The IDE's own shutdown work and the server
context's disposal are skipped too.

**Workaround.** None needed for automation itself: the exit code is right. Don't launch into a taken port:
check that `/health` stops answering before relaunching, as the rebuild cycle says. A fresh `--user-data-dir`
does not help. A new profile asks about every third-party add-in again, which makes an open consent prompt
more likely, not less.

**Fix.** Exit through `TryShutdown(exitCode)`, or run the cleanup before the forced shutdown.

---

## The name-taking tools saw one project, could not read a carried file, and refused without saying why — **CLOSED** (#572, #573, #575, 2026-09-22)

> **Fixed.** `open_file`, `view_designer`, `get_form_controls`, `get_file_content` and `set_file_content`
> resolve a name across every loaded project, as the mark tools already did, and take the same optional
> `project`. `get_file_content` reads a carried file, from its editor or else from disk, and
> `set_file_content` refuses one by what it is. A refusal says what the name is when it is something else,
> and lists what each project holds.

**Symptoms**, all measured on a group of two projects, `Carried` (`Module1`, carried `Notes`) and `Second`
(`Form1`, `Module1`, `Module2`), with `project` left at null unless shown:

- `open_file {"name":"Module2"}` refused while `get_breakpoints {"name":"Module2"}` answered for
  `Second/Module2`. The five tools looked only in the startup project (#575).
- `get_file_content {"name":"Notes"}` answered "No form or module named 'Notes' found" straight after
  `open_file` had opened it in a tab (#572).
- `view_designer {"name":"Module1"}` answered "No form or UserControl named 'Module1' found in the project".
  That reads as a wrong name, when the name was right and the tool was not, and like every other miss it
  never said what the project did hold (#573).

**Now:**

```
view_designer {"name":"Module2"}              → 'Module2' is a module, which has no designer. open_file opens its code.
view_designer {"name":"Notes"}                → 'Notes' is a carried file, which has no designer. open_file opens it.
get_form_controls {"formName":"Module1"}      → 'Module1' names more than one document: Carried/Module1, Second/Module1. Pass `project` to say which.
open_file {"name":"Module1","project":"Second"} → success; the tab is "Second - Module1 (Code)"
get_file_content {"name":"Notes"}             → {"content":"# Notes\r\n\r\nA carried file.\r\n","hasUnsavedChanges":false}
open_file {"name":"Nope"}                     → No form, module or carried file named 'Nope' in any loaded project. Carried holds no forms and UserControls; modules Module1; carried files Notes. Second holds forms and UserControls Form1; modules Module1, Module2; no carried files.
```

**Since closed.** `get_project_info` reported only the startup project, which left a refusal as the only
reply that showed the whole group; it now lists every loaded project in `projects` (#581).

---

## A mark tool accepted lines the document does not have, and persisted them — **CLOSED** (#569, 2026-09-22)

> **Fixed.** `set_breakpoints` and `set_bookmarks` now refuse the whole call when any line is outside the
> document, name the lines and the valid range, and change nothing:
> `set_breakpoints {"name":"Module1","lines":[1,3]}` on a two-line module answers `3 is not a line of
> Carried/Module1, which has 2 lines: breakpoints are numbered 1..2. Nothing was changed.` The bookmark
> refusal adds "counting from 0", because the two tools number lines differently.

**Symptom.** `set_breakpoints {"name":"Module1","lines":[-1,0,99]}` (`project` left at null) on a two-line
module answered `{"success":true,"note":"Carried/Module1 now has breakpoints on -1, 0, 99."}`, and
`get_breakpoints` read the same three back. `set_bookmarks` did the same. The gutter showed none of the
breakpoints, because none is a line (breakpoints count from 1). The marks survived a relaunch too: they had
been written to the project's `*.user.hexproj` sidecar.

**Why it mattered.** The reply and the readback agreed with each other, and both were wrong. A caller who
gave bookmark numbering to the breakpoint tool, or the other way round, got a confident success and then a
run that never broke.

**Still open.** A sidecar that already holds such lines is loaded as-is, and a breakpoint on a line with no
executable statement is still accepted. Both are noted on #569.

---

<<<<<<< HEAD
## `add_file` created a document under a name VB6 does not accept, and could write outside the project — **CLOSED** (#596, 2026-09-22)

> **Fixed.** `add_file` now applies `ProjectNaming`, the rule every other way of naming a document uses:
> `add_file {"name":"My Module","type":"module"}` answers `'My Module' is not a valid name. A name starts
> with a letter and continues with letters, digits and underscores. Nothing was added.`, and a name already
> used by any form, module or class, in any case, is refused the same way. A successful reply now carries a
> `note` saying that the project file does not list the new file until the project is saved, and when the
> file went to the project's temporary folder.

**Symptom.** `add_file {"name":"My Module","type":"module"}` on a saved project answered
`{"success":true,"path":"…\\My Module.bas"}` and wrote `Attribute VB_Name = "My Module"`. It was the one route
to such a document: Project → Add names new documents with `NextFreeName`, and adopting a file refuses an
invalid name. The tool also kept its own copy of the collision check.

**Security, and the larger half.** The name also reached a file path unchecked: `AddNew*` builds
`Path.Join(dir, name + ".bas")`, so `add_file {"type":"Module","name":"..\\..\\somewhere\\X"}` wrote a
VB6 source file outside the project folder, anywhere the user can write, and `set_file_content` could then
overwrite it. The extension is fixed (`.bas`, `.cls`, `.frm`, `.ctl`, `.pag`), and the server is DEBUG-only and
loopback-only, but it has no authentication (#352). It failed open, with success and the escaped path in
the reply. `IsValidName` admits no separator, dot or colon, which closes it: `..\x`, `../x` and `C:x` are
each refused, and nothing is written. The reviewer found this, not the audit.

**Why it mattered.** Nothing refused the name until something downstream tried to use it as an identifier.
The reply said "saves it to disk" while the `.vbp` did not list the file, so a caller checking the project
file concluded the add had failed.
=======
## A run that failed to start was reported as started, and left the debugger saying `Running` — **CLOSED** (#590, 2026-09-22)

> **Fixed.** When the startup form cannot be built, the start now tears down what it had claimed (the
> controller is stopped and no project is left running) and reports the failure through the existing
> runtime-error path: the person gets the runtime-error dialog and `get_last_runtime_error` returns the
> text. `run_project`, `run_to_cursor`, `step_into`, `step_over` and `step_out` answer the failure instead
> of success: `run_project {}` on a form whose button has `Width = -300` answers `Form 'Form1' could not
> be loaded, so the project did not start. -20 is not a valid value for 'Width'. Nothing is running. …`,
> and `get_debug_state` then reads `{"running":false,"state":"Stopped"}`. `get_debug_state` also reports
> `Stopped` on a freshly launched IDE, where it used to report the controller's pre-session `Running`.
> Whether a negative size should be accepted in the first place is #589, still open.

**Symptom.** With the startup form holding a control whose `Width` is -20 (the entry above):

```
run_project {}          → {"success":true}
get_debug_state {}      → {"running":false,"state":"Running"}
get_last_runtime_error  → {"raised":false,"sequence":0}
```

The form never appears. The `state` half is not only this failure's doing: a freshly launched IDE that has
run nothing answers `{"running":false,"state":"Running"}` too, and only reads `Stopped` after a run has
been stopped. The only record is an `ArgumentException` from `VBLoader.PlaceComponentTree`
in the IDE log. `run_to_cursor` takes the same path. A person pressing F5 sees nothing either.

**Workaround.** After a start, confirm with `get_debug_state` that `running` is true, and read the IDE log
(`%LOCALAPPDATA%/HexIDE/logs/ide/`) when it is not.

**Suggested fix.** Route a failed form load through the existing runtime-error path, so the person gets the
runtime-error dialog and `get_last_runtime_error` reports it, and reset the controller to `Stopped`:
[#590](https://github.com/hexide-io/HexIDE/issues/590). Any exception while the startup form loads
takes this path, not only #589's.
>>>>>>> upstream/main
