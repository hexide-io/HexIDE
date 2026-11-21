## Closing the run-time error dialog without End or Debug (2026-11-21, #607, IDE, SP6)

**The question.** When a run-time error nothing handles stops a program in the IDE, VB6 shows its run-time error
dialog. HexIDE ends the run when that dialog's End is pressed. When the dialog is closed any other way, by its
close box or by Escape, HexIDE leaves the program running (#607). What does VB6 do?

**The probe:**
- **The IDE:** VB6.EXE 6.00.9782 (SP6), in the console session of a clone of the oracle guest. It was driven through
  the console agent, as "Reaching the VB6 *IDE*, not just the compiler" in the README describes.
- **The program:** a Standard EXE whose `Form1` holds a `Timer` at 300 ms. Each procedure logs to a file:
  ```vb
  Private Sub Form_Load()
      L "load start"
      Error 5
      L "load after error"
  End Sub

  Private Sub Timer1_Timer()
      ticks = ticks + 1
      If ticks <= 3 Then L "tick " & ticks
  End Sub

  Private Sub Form_Unload(Cancel As Integer)
      L "unload"
  End Sub
  ```
- **Each row was a fresh IDE:** F5, then one action on the dialog.
- **What was recorded:** at once and again 3 seconds later, the IDE's title, every window of the VB6 process with its
  texts and style, the log, and a screenshot.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.

**The dialog** is titled *Microsoft Visual Basic* and reads "Run-time error '5':" over "Invalid procedure call or
argument". It offers Continue (greyed), End, Debug (the default button) and Help. While it's up, the IDE's title reads
*[run]*, and the program's hidden `ThunderMain` window is disabled.

**It has no close box.** Its window style is `0x94C000C4`, a caption without `WS_SYSMENU`, so the title bar draws no
close box, and `WM_GETTITLEBARINFOEX` reports the close button as invisible (`STATE_SYSTEM_INVISIBLE`). The
screenshot shows the title bar with nothing at its right end.

| Action on the dialog | Right after | 3 s later |
|---|---|---|
| Escape, sent as a key | **The dialog stays up.** *[run]* | the same. Log: `load start` |
| A real click where the close box would be | not possible: there is no close box to click | |
| `WM_SYSCOMMAND` `SC_CLOSE`, what a close box sends | **The dialog stays up.** *[run]* | the same. Log: `load start` |
| Alt+F4, sent as a key | **The dialog stays up.** *[run]* | the same. Log: `load start` |
| **Control:** the End button | *[design]*. The dialog is gone | the same. Log: `load start` |
| **Control:** the Debug button | *[break]* at `Error 5` | the same. Log: `load start` |
| **Control:** E, sent as a key the way Escape was | *[design]* | the same. Log: `load start` |
| **Control:** Enter, sent as a key | *[break]* at `Error 5` | the same. Log: `load start` |

**Readings:**
- **VB6's dialog can't be closed without an answer.** Escape, `SC_CLOSE` and Alt+F4 each left it up, the run still
  halted. It has no close box to click.
- **Keys reached the dialog:** E ended the run and Enter chose Debug, sent the same way as Escape.
- **Nothing runs while it's up:** no tick in 3 seconds, and `Form_Load` didn't go on to log `load after error`. This
  agrees with [An unhandled run-time error](an-unhandled-run-time-error.md).
- **End raised no `Form_Unload`,** which also agrees with that page.

### In HexIDE

Per #607, HexIDE's dialog can be closed by its close box or by Escape, and the program then keeps running.
[An unhandled run-time error](an-unhandled-run-time-error.md) records HexIDE's later choice: "A dialog closed
without an answer takes Debug". Neither was checked again for this page.

### Not measured

- the same dialog for an error raised in an event procedure other than `Form_Load`, such as a click;
- the dialog of a compile error met during a run, which offers OK and Help only. That one is measured for #833;
- Help, which opens VB6's help viewer and was left alone.
