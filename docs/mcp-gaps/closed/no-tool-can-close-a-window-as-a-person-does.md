# No tool can close a window as a person does — **CLOSED** (#829, 2026-10-20)

**Symptom.** Verifying #820, a program's Yes/No message box had to be closed the way a person closes it, to show
that it refuses. Nothing on the surface does that:

```
dump_visual_tree {}
→ {"window":"YesNoBox","root":{"path":"Window", ... "children":[{"path":"Window/Custom", ... Button[Yes], Button[No]}]}}
interact {"target":"Window","action":"close"}      no such action
press_key {"target":"Window/Custom/Button[No]","key":"F4","modifiers":"Alt"}
```

The title bar is the platform's chrome, outside the control tree, and Alt+F4 is handled by Windows before
Avalonia raises a key, so a key event on a control closes nothing. `stop_project` and `shutdown_ide` close a dialog
programmatically, which is the close a guard deliberately lets through, so they cannot stand in for a person's.

**Workaround.** From PowerShell, find the window by title with `FindWindow` (passing `[NullString]::Value` as the
class: PowerShell turns `$null` into `""`, which matches nothing), check with `GetWindowThreadProcessId` that it
belongs to the IDE's process, and post `WM_SYSCOMMAND` with `SC_CLOSE`, which is what the title-bar button sends.
Windows only, and outside the surface a caller has.

**Suggested fix.** An `interact` action, `close`, on a top-level window, raising the platform's close for a person
(`WindowCloseReason.WindowClosing`, not programmatic), and replying whether the window is still open.
[#829](https://github.com/hexide-io/HexIDE/issues/829).

> **Fixed.** `interact` has a `close` action, which closes the target's window, or the window holding the target, as its
> close box does: through the window's own handling of its platform's close, a close no program asked for, which a
> Yes/No or Abort/Retry/Ignore MsgBox refuses and a form's QueryUnload (UnloadMode 0, vbFormControlMenu) may refuse.
> The reply says what became of the window, read once the program has run what the close set off, since a form's
> close box closes it only once the program
> has unloaded it: closed; hidden, not closed, when QueryUnload hid it; still open while the program decides, when it is
> still running (a QueryUnload asking a MsgBox); or still open because it refused. A window with a modal dialog open over
> it is refused, as its close box does nothing for a person until the dialog is answered. The IDE's main window is
> refused, since closing it takes this server down with it; `shutdown_ide` closes the IDE. The handling is internal to
> Avalonia and is reached by reflection, so an Avalonia that hides it fails `CloseWindowTests` rather than closing from
> code. Guarded by `CloseWindowTests` and `ProgramWaitTests`.
