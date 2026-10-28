# No reply mentions a modal dialog, whether it was already open or the action opened it — **CLOSED** (#681, 2026-10-20)

> **Fixed.** A tool's reply names each modal dialog open once the call is done, in a `modalDialog` field (a block of
> its own when the reply is not a JSON object). The call still acts; refusing would also stop a caller answering the
> dialog through the same tools. Measured with a cold agent on a Debug build, in the wording since tightened at its
> request:
>
> - `invoke_menu_item Tools/Add Procedure...` → `"modalDialog":"'Add Procedure', a modal dialog over 'Ask - HexIDE
>   [design]', opened during this call and waits for an answer. dump_visual_tree with 'window' set to its title shows
>   it, and interact answers it."`
> - `get_file_content`, `set_breakpoints` and `dump_visual_tree {"window":"ide"}` under it → `"… is still open and
>   waits for an answer; this call reached past it, as a person could not. …"`
> - `continue_program` into a division by zero → `"note":"Running; no pause within 2 s. It raised Run-time error '11'
>   …","modalDialog":"'HexIDE', a modal dialog over the running form 'Ask', opened during this call …"`, and
>   `get_debug_state` after it, still `Running`, names the same dialog.
>
> Which dialogs are named follows the window the call acts in, resolved as the tools resolve it: the IDE's, or for a
> window tool the one its `window` argument picks. The dialog the call acted in, and any under it, are not named, so
> `dump_visual_tree {}` and `interact` left to the frontmost window, which is the dialog, get no note. "Reached past
> it" is said only when the dialog blocks that window; a program's MsgBox over its form is named as open, since it
> does not stop a person using the IDE. A dialog the wait for a program (#1232) named in the reply is not named again;
> every other is, the program's included (#1253). A program's MsgBox or InputBox over a hidden form, as from
> `Form_Load`, waits in a window of its own that is not shown as a dialog, and is counted too; a tool window such as
> Find is not. Each look at the windows is bounded at 500 ms, and one that fails, as after `shutdown_ide`, leaves the
> reply as the tool gave it. A tool that throws gets no note. The `take_snapshot` half of the suggested fix, preferring
> a modal with `window: "ide"`, is not done: the note now names it, and `"ide"` asks for the IDE's own window. Guarded
> by `OpenDialogsTests` and `OpenDialogNoteTests`.


**Symptom.** (Windows 11, Debug build of #838's branch, merged with main at 2047857, 2026-10-13.) Paused at a
breakpoint on line 3 of a module, with the caret moved to line 2, an `Attribute` line:

```
invoke_menu_item {"path":"Debug/Set Next Statement"}
→ {"success":true,"note":"Invoked 'Set Next Statement'."}
take_snapshot {"window":"ide"}
→ {"path":"…_3.png","width":1920,"height":974,"scale":1.5}
dump_visual_tree {"maxDepth":3,"interactiveOnly":false}
→ {"window":"HexIDE","root":{… "className":"MessageBox" … "Can't set the next statement here — only a
   top-level statement of the currently paused procedure is supported …" … "Button[OK]"}}
take_snapshot {}
→ {"path":"…_4.png","activeDialog":"HexIDE","width":642,"height":172,"scale":1.5}
```

The command refused the move and opened a message box saying so. The first reply reads as if the move had
happened. The second is the main window with no `activeDialog`, and the box is not in the image, although
`window: "ide"` is the documented way to reach the IDE while a program is paused, and the tool's description
says an open modal is captured in preference to the main window. Only the default window finds it. #681
measured the first half with Tools/Add Procedure left open; this is a modal raised by the action itself, which
[its Linux comment](https://github.com/hexide-io/HexIDE/issues/681#issuecomment-6146516046) describes.

**Workaround.** After any action that might open a dialog, `dump_visual_tree {}` and read its `window`. A
`take_snapshot {}` with no `window` captures the dialog.

**Suggested fix.** #681's decision, for the replies. For `take_snapshot` with `window: "ide"`, prefer a modal
the IDE owns, as `auto` does, or at least name it in `activeDialog`.
[#681](https://github.com/hexide-io/HexIDE/issues/681).

**Seen again with #1011's halt** (2026-10-15, Debug build of `fix/1011-halt-on-error`). A click divides by zero, and the
program halts at the run-time error dialog. After Debug, a Continue without a fix retries the statement, which halts again:

```
continue_program {}
→ {"success":true,"note":"Running; no pause within 2 s. It raised Run-time error '11': Division by zero at 10 \ d;
   get_last_runtime_error has it."}
get_debug_state {}
→ {"running":true,"state":"Running"}
```

Both replies say Running. The program is held at a dialog that waits for End or Debug, and nothing of it runs. Naming
the error helps, but neither reply says the program is waiting for an answer. `dump_visual_tree {}` finds the dialog, as
above.
