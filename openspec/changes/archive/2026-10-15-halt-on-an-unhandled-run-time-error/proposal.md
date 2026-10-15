# Halt on an unhandled run-time error

## Why

An unhandled run-time error in an event procedure was reported in the run-time error dialog, where only End worked. The
procedure that met it was abandoned, and the program carried on. Timer ticks and clicks still ran their handlers, on
whatever state the abandoned procedure had left half-updated. Closing the dialog any other way left the run going
(divergence D23, hexide-io/HexIDE#1011).

VB6's IDE stops the program at the statement instead. Its dialog offers End and Debug, nothing runs behind it, and
Debug breaks at the faulting line with the procedure still live. This was measured on vb6.exe before the design was
written, along with the retry and the event boundary: `docs/vb6-fidelity-oracle.md`, "An unhandled run-time error".

## What changes

- **Unhandled is decided where the error is raised, before anything unwinds.** The error is handled if the activation
  that raised it, or one below it, would trap it. The search stops at the activation where the program was entered:
  an event's procedure, a `RaiseEvent` sink, or `Sub Main`. So a handler in the code that raised an event never sees
  that event's error, `RaiseEvent` included, as in VB6.
- **The halt shows the dialog from inside the faulting statement.** The interpreter asks the debugger, and the debugger
  asks the IDE, which shows the dialog with Debug enabled. End ends the run as the End command does. Debug breaks
  there with reason `RuntimeError`, and continuing runs the statement again.
- **What runs again:**
  - **A statement in a block** runs again whole, as VB6 retries it.
  - **A condition** runs again on its own, whether an `If`'s, an `ElseIf`'s, a `Do` loop's or a `While`'s. An
    `ElseIf` is asked again without the conditions before it, and a `Loop Until` without another pass through the body.
  - **A statement in a single-line `If`'s tail** runs again alone.
- **Nothing runs behind the dialog or the break.** The wait is bracketed for the event pump, as any wait is, so an event
  that comes meanwhile queues. The review's three cases are pinned, each with a Timer ticking: a nested MsgBox, a
  nested breakpoint and a nested halt. All three held with the existing bracket, so no second one was added.
- **A class's error that its caller traps goes to the caller,** as a compiled program does. This is a recorded
  exception to following the IDE (D27).

## Not changed

- **The Standalone runner** has no host to halt, and its run-time errors are #757. Without a host, an error unwinds to the
  event's dispatch and is reported there as before.
- **A form's `Terminate`, and a class's `Initialize` and `Terminate`,** are not entries.
- **A compile error met while running** (D22, #833) is the same decision with a different raise site.
- **Set Next Statement from the halted break** behaves as at any other break: allowed at a top-level statement and
  refused inside a block.
- **The Help button.**
- **Two things are unmeasured,** and HexIDE takes a side on each: a dialog closed without an answer takes Debug, and
  `Err` is left as it was at the break.
