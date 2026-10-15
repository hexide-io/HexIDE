# Refuse a form holding a control HexIDE cannot create

## Why

A form holding a control HexIDE doesn't model ran with that control, and everything nested inside it, left out. That
means a `VB.Data`, a `VB.OLE`, a `VB.Line`, the file-system list boxes, and every ActiveX control. Nothing at run time
said so (hexide-io/HexIDE#857):
- **Code naming the control** read Empty, or was told the name wasn't defined.
- **A call on it under `On Error Resume Next`** was skipped with `Err.Number` 0.
- **A form whose code never names it** ran as a different program. A TextBox bound to a `VB.Data` kept its design-time
  text, where VB6 shows the first record.

That is a wrong result, not a late error, and `interpreter-core` already forbids a silent substitution. The in-box
ActiveX controls epic (hexide-io/HexIDE#1081) makes this pressing, because a ProgressBar that runs beside a ListView
that silently doesn't would be worse than today.

## What changes

- **The form is refused before any control is placed,** wherever a form is built to run: the startup form, a form the
  program loads, and a form in a made program. The refusal names every control the form can't create, nested ones
  included. A control nested in one is named too, even one HexIDE models, because nothing inside an unmodelled block is
  read. One whose name is shared across a control array is named once.
- **It is an engine limit,** so the program can't trap it:
  - at the start, the IDE says the startup form couldn't be loaded and why, and a made program shows the reason in
    its error box;
  - mid-run, loading such a form ends the run.
- **The designer is unchanged.** It still opens the form and carries the control through a save, and what it draws for
  the control is hexide-io/HexIDE#157.
- **Two requirements are narrowed:**
  - `serialization-round-trip`'s read-only requirement promised that such a file still runs. It now runs unless it
    holds a control HexIDE can't create.
  - `interpreter-core`'s unimplemented-surface requirement gains a scenario for this refusal.

## Not in this change

- **A project UserControl whose `.ctl` holds an unmodelled control.** The designer draws a UserControl through the
  path that also builds it at run time, so refusing there would stop the designer opening any form that holds one. That
  needs a check of its own, as #857 records.
- **The placeholder the designer draws** (#157), and the in-box controls themselves (#1081).
