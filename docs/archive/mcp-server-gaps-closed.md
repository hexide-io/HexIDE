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
