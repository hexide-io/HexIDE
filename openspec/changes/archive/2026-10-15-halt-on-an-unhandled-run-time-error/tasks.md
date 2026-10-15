# Tasks

## 1. The halt

- [x] 1.1 Decide "unhandled" where the error is caught, before anything unwinds. Walk the activation stack from the
  faulting activation down to the program's entry, stopping at the first that would trap it.
- [x] 1.2 Mark the entries: an event's procedure, a `RaiseEvent` sink, `Sub Main`.
- [x] 1.3 Halt in a block's statement, in a condition and in a single-line `If`'s tail, and run again only what raised.
- [x] 1.4 `IDebugController.HaltAsync`, with the IDE as its host. End ends the run, and Debug breaks with reason `RuntimeError`.
- [x] 1.5 Bracket the wait for the event pump, so that nothing runs behind it.
- [x] 1.6 The dialog enables Debug, which is its default button, while the program is halted.

## 2. Tests

- [x] 2.1 Nothing runs behind a nested MsgBox, a nested breakpoint, or a nested halt, each with a Timer ticking.
- [x] 2.2 Debug breaks at the faulting statement, and Continue runs it again. The retry runs all of the statement, and
  a second failure halts again.
- [x] 2.3 Only what raised runs again: a statement in a block, a statement in a single-line `If`'s tail, a `Loop Until`
  condition, and an `ElseIf` condition.
- [x] 2.4 A handler in the code that raised an event doesn't see its error (b1). The same holds around `RaiseEvent`,
  whether the handler is in the form or inside the method that raises the event (b2, b3).
- [x] 2.5 A caller's handler catches an error from a class method or a standard module's Sub (c1, c2). An error in a
  called Sub halts inside that Sub.
- [x] 2.6 An error in `Form_Load` halts the start, and one in `Sub Main` halts it.

## 3. Records

- [x] 3.1 D23 fixed, D27 renumbered from the branch's D24, `get_debug_state`'s description names `RuntimeError`, and
  CHANGELOG.
