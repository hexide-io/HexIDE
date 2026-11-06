# An action on a running form replies before the program's handler has finished — **CLOSED** (#1232, 2026-10-20)

> **Fixed.** `interact`, `press_key` and `type_text` on a running program's form, on a window it owns, or on its
> MsgBox or InputBox, now wait until the program has finished what the action set off and is waiting for input again,
> up to 3 seconds. A handler parked in `Show vbModal`, or at a `DoEvents` in a loop that was already running before
> the action (or in a Timer's), counts as waiting for input; a `DoEvents` loop the action itself started is waited for,
> up to the same limit (corrected by #1254, below). The program is found before the action, as a MsgBox that the
> action answers has closed and lost its owner by the time the action returns. A reply that comes back sooner ends
> with a note saying why:
>
> - the program is paused in the debugger: `(the program is paused at Form1 line 23 (Breakpoint), with Command1_Click
>   not finished; it runs on, and the form may change, once the program continues)`;
> - it has a window that is not one of its forms open over the form, a MsgBox most often: `(the program is still
>   running Command1_Click, and has a window open over its form, 'Question'; a MsgBox holds its handler until it is
>   answered, so the form may change after that)`;
> - it was still running at the limit: `(the program was still running Command1_Click 3s later, so the form may still
>   change)`.
>
> A form the program opened is named: `(the program opened 'Form2')`. Guarded by `ProgramWaitTests`.

**Symptom.** Found during #947's live check (Windows, Debug, 2026-10-20). On the running Minesweeper demo:

```
interact {"window":"frmMines","target":"Window/Menu/MenuItem[&Game]/Pane/MenuItem[&Intermediate]","action":"invoke"}
→ invoked MenuItem '&Intermediate' and closed the menu, as a click does
take_snapshot {"window":"frmMines"}
→ the 16×16 field, with the mine counter still reading 010
```

A later snapshot read `040`. `mnuLevel_Click` sets the counter last, after rebuilding the field, and the reply had
gone out while it was part way through. Nothing in the reply said so.

**Cause.** A handler is a walk on the run's `EventPump`. `ExecuteSub` starts it and returns, and the walk gives up
the UI thread at the debugger's 15 ms time slice (#766), so the tool's reply was sent mid-walk.
