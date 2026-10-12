# A run that stops says why

## Why

`interpreter-core` already requires a program that uses something the engine does not implement to stop "with
an error identifying the unimplemented feature". That held in the test suite and nowhere else
(hexide-io/HexIDE#754). In a running IDE, every event procedure goes through a catch at the run-window boundary
that only logged an exception which was not a VB6 error, and the Sub Main runner caught only VB6's own two
exception types. So a click handler that reached an unimplemented statement ended part way while the program
carried on, and F5 on the shipped Battleship demo, a Sub Main project, opened nothing, said nothing and wrote one
log line. Seen from the keyboard, that is a statement that silently did nothing, which is the outcome the
requirement exists to forbid.

The requirement was also silent on error handlers. An unimplemented feature has never been trappable, but nothing
said it must not be, and the obvious way to make it "reach the developer" is to make it a VB6 error, which
`On Error Resume Next` would then step over.

## What changes

- **A fault ends the run and says why.** An exception that is not a VB6 error, raised in any event procedure or
  in Sub Main, ends the run, and the runtime-error dialog shows it. A construct HexIDE does not implement reads as
  that, with its name and no VB6 error number. Anything else reads as VB6's own "Internal error" (51) with the
  exception's type and message, which is what a bug report needs.
- **Sub Main's errors are reported like everyone else's.** A compile or run-time error that unwinds Sub Main used
  to go to a message box that nothing recorded. It now goes through the same dialog and record as a form's,
  with the statement that raised it.
- **A Sub Main project whose code cannot be loaded is a start that never became a run**, reported the way a
  startup form that cannot be built already is (#590). It used to leave the debugger Running with nothing running.
- **The requirement gains two scenarios**: an error handler does not see an unimplemented feature, and in a
  running IDE the run ends and the developer is shown which feature it was.

## Not changed

- **A form's ordinary VB6 run-time error.** It leaves the run going until End is pressed on its dialog, as
  before. What closing that dialog any other way should do is #607's question, unmeasured.
- **Where in the program a fault happened.** The dialog names the construct but not the statement. That needs
  the failing statement carried out of the walk, and is filed separately.
- **`HexIDE.Standalone`**, the runner without an IDE, still only logs. It has no dialog to report into, and is
  filed separately.
