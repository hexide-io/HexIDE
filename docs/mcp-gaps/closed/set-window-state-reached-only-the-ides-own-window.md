# `set_window_state` reached only the IDE's own window — **CLOSED** (#2357, 2026-10-27)

**Symptom.** `set_window_state` and `get_window_state` took no `window` and acted on the IDE's main window only, and no
tool minimized, restored, moved or resized any other window. A running form's `Form_Resize`, a `WindowState` read in a
Timer, and a layout that depends on the form's size couldn't be driven through the tools, and neither could a tool's own
handling of a minimized window. Found twice. #1925's live check couldn't try `includeFrame`'s refusal for a minimized
form. #2182's needed a form moved partly off the desktop and did it with `SetWindowPos` from a harness, which bypasses
the surface being checked.

**Fixed (#2357).** Both tools take the tree tools' `window`, and act on the IDE's own window when it is omitted, as
before. The reply's `name` is what reaches the window again: `ide`, a running form's name, or a window's title.

- **As a person does it.** Another window changes through the same Avalonia properties its caption buttons and frame
  set, so a running form's Resize and Activate run as they do for a click. The reply waits for the program, as
  `interact`'s does, then reads the window again.
- **What a person couldn't do is refused**, with nothing changed:
  - a state the window has no box for (a form's MinButton, MaxButton, ControlBox and BorderStyle decide);
  - a size for a fixed border, where `x` and `y` alone still move it;
  - any change to a disabled window or one under a modal dialog, through the refusal `interact` uses.
- **An unknown name** is refused, listing the open windows. The message says the IDE's window is the default here, where
  the tree tools' says `auto` is.

Guarded by `WindowStateOtherWindowTests` and `ForegroundWindowTests`.

**Live (2026-10-27, Windows at 150%, a Debug build of the fix, headless `claude -p` through the hexide tools).** Form1
(Sizable) counted its Resize and Activate into a label. Form2 was a Fixed Dialog with no minimize or maximize box.

- **Omitted `window`.** `get_window_state {}` gave `{"state":"Normal","x":-11,"y":-11,"width":1280,"height":649,"name":"ide"}`,
  as before, apart from `name`.
- **Form1, from `resize 1, activate 1`.**
  - Minimize answered `"state":"Minimized","x":-32000,"y":-32000`.
  - Restore: `resize 3, activate 2, state 0, width 4500`.
  - Maximize: `resize 6, activate 2, state 2, width 19200`.
  - Normal at `x 200, y 150, width 500, height 300`: `resize 11, activate 2, state 0, width 7500`, at the position and
    size asked for.
- **Form2.**
  - Minimize was refused: `'Form2' has no minimize box, so a person can't minimize it. …`.
  - `width: 400` was refused: `'Form2' has a fixed border, so a person can't resize it; …`.
  - `x: 700, y: 200` moved it.
- **Form1 under the program's MsgBox.** Minimize was refused: `'Form1' is in a window a modal dialog has disabled:
  'WinState' (a message box: "Hold", OK). …`. Once the MsgBox was answered, the same call minimized the form, a second
  one found it minimized by name, and Normal restored it. The first run named the form by its class, `VBFormRuntime`,
  which is fixed.
- **An unknown name.** `get_window_state {"window":"Nope"}` answered `Unknown window 'Nope'. Use "ide" (the default
  here: …), "auto" (…), or the name or title of an open window: Form2 ("Fixed"), Form1 ("Resize probe"). …`.
- **The same changes from a person's caption buttons**, sent as the `WM_SYSCOMMAND` a click sends, gave the same
  Resize counts as the tool's:
  - maximize and restore from maximized, three each;
  - minimize and restore from minimized, one each, with one Activate on the restore.

  Three for a maximize is the runtime's own behaviour whichever route it comes by: #2389.
