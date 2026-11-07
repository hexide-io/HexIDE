## One handler at a time (2026-10-14, #766)

Can an event handler start while another is still running, and what does `DoEvents` change? Two `/make` probes, each
built native and p-code, all four builds agreeing on every line. A Timer at 50 ms drives each run, and handlers keep
themselves busy with `GetTickCount` loops, with `DoEvents` in the loop or without. GetTickCount's resolution is
15.6 ms, which is why a 50 ms Timer ticks every 62 ms below.

**1. A nested raise, posted input, and DoEvents.** Timer1's first tick posts `WM_LBUTTONDOWN` and `WM_LBUTTONUP` to
Command1, sets `Text1.Text = "x"` (its Change handler is busy for 100 ms), then is busy for 300 ms itself. Each handler
logs a depth: 1 inside the tick, 0 outside every handler.

| | Without DoEvents | With DoEvents in both busy loops |
|---|---|---|
| Change | runs inside the setter: `Change begins` and `Change ends` before `after the Text set` | the same |
| The posted press and release | after the tick ends, at depth 0: MouseDown, Click, MouseUp | inside Change's DoEvents, at depth 1: MouseDown, Click, MouseUp |
| Timer1 inside its own handler | no | no, not even under DoEvents |

**2. How a Timer catches up, and a second Timer.** Timer1 and Timer2 both run. Timer1's second tick is busy for 400 ms.

| | Without DoEvents | With DoEvents |
|---|---|---|
| Timer2 during the 400 ms | silent | ticks inside Timer1's handler, at depth 2, every 62 ms (125, 188, 250 … 500) |
| Timer2 after it | **one** tick at once (busy done at 531, `T2 tick 2 at 531`), then its 62 ms cadence | its cadence |
| Timer1's own next tick | one interval after its handler returns (`T1 tick 3 at 593`) | the same (`T1 tick 3 at 610`) |

What these show:
- **Nothing runs inside a handler unless it calls DoEvents**, neither input nor a Timer. An event that comes while a
  handler runs waits for it, in the order it came.
- **A handler a statement raises runs to its end inside that statement.** The raiser's next statement comes after it.
- **DoEvents runs what is waiting inside the caller**, one level deeper, in the order it came.
- **A Timer delivers one tick, not several,** however long a handler kept it waiting, and it delivers it at once.
- **A Timer never runs inside its own handler**, even under DoEvents, while another Timer can. Its next tick comes an
  interval after the handler returns.

The obvious expectation for the last two rows, a queue that replays every missed tick, and a Timer that re-enters
itself under DoEvents as any other event does, are both wrong.

Not measured, and why: whether a Timer fires while a MsgBox is up in a compiled program, which needs the interactive
desktop; and MouseMove coalescing, which synthetic messages cannot drive. Windows synthesises WM_MOUSEMOVE when
nothing else is queued, so only the latest move is ever delivered. That rule is inferred, not measured here.

### HexIDE

Before #766, `VBWindowContext` started each handler and did not wait for it. While every walk ran synchronously
that was harmless. Under the IDE's debugger, though, a walk gives up the UI thread whenever 15 ms have passed, so
Ctrl+Break can land, and at that yield any other event could start and finish first. A raiser ran on past the
handler it had raised. A CommandButton printed MouseDown, MouseUp, Click, because Click and MouseUp come from one
release and the first of them yielded. A `Do: DoEvents: Loop Until clicked` loop only worked because the yield let
the click in.

Since #766 an `EventPump`, one per run, runs handlers one at a time:
- **A top-level event** (input, a Timer, a window event) runs at once when nothing runs, and otherwise waits its turn.
- **An event the program's own code raises** runs at once. The raiser waits for it: before its next statement, before
  the rest of a single-line `If`, at the end of a block (so a loop's condition sees what it set), and before a call
  returns.
- **`DoEvents`** gives the dispatcher one turn, then runs what is waiting inside the caller.
- **A Timer** keeps at most one tick waiting, and none while its own handler runs. A move updates only a move at the
  back of the queue, of any control, and never reaches past a press, by the inferred rule.
- **The end of a run** closes the pump: what it left waiting is forgotten, and nothing more is taken.
- **The time slice stays,** for Ctrl+Break and rendering, and no longer lets an event in.
- **A MsgBox or InputBox** keeps the gate held, until a Timer under a MsgBox is measured.

Pinned in `EventOrderTests`, with the debugger's time slice off and at -1, where every statement yields. `RunFaultTests`
now runs at -1 too.

Where HexIDE still differs: VB6 runs a nested handler inside the very call that raised it, so the rest of that
expression comes after it. HexIDE waits at the next statement, the next part of a single-line `If`, the end of a block,
or the return of a call. A raise in the middle of an expression, `x = SetIt() + Text1.Text` with SetIt raising Change, can therefore
see the rest of the expression evaluated first. And `DoEvents` returns 0, not VB6's count of open forms.

