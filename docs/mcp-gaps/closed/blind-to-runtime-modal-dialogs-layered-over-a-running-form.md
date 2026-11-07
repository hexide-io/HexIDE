# 1. Blind to runtime modal dialogs layered over a running form — **CLOSED** (#61, 2026-09-01)

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
