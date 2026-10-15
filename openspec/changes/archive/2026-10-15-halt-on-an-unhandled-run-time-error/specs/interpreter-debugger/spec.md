## ADDED Requirements

### Requirement: A run-time error nothing handles SHALL halt the program at the statement that raised it
Under the debugger, a run-time error that no active error handler traps SHALL stop the program at the statement that
raised it, before any procedure unwinds. The developer SHALL be offered End and Debug, and nothing else in the program
SHALL run until they choose. Debug SHALL enter break mode at that statement with its procedure still active, and
continuing SHALL run that statement again, all of it. End SHALL end the program as the End command does.

The error is handled if the procedure that raised it, or any procedure below it, would trap it. The search SHALL stop
at the procedure where the program was entered: an event procedure, a procedure called to handle a `RaiseEvent`, or
`Sub Main`. A handler in the code that raised an event SHALL NOT see that event's error. A class's error that its caller
traps SHALL go to the caller, as a compiled program does, rather than stopping in the class as VB6's IDE does under its
default Error Trapping setting.

VB6's IDE halts this way, and leaving the procedure abandoned while the program carried on let timers and clicks run
on whatever state it had left half-updated. Measured on vb6.exe: the dialog, what runs behind it, the break's location,
the retry, and the event boundary (`docs/vb6-fidelity-oracle.md`, "An unhandled run-time error"). The exception for a
class's error is a recorded decision (hexide-io/HexIDE#1011, divergence D27).

#### Scenario: An event procedure divides by zero
- **WHEN** an event procedure with no error handler raises a run-time error under the debugger
- **THEN** the program stops at that statement, and the run-time error dialog offers End and Debug
- **AND** no timer tick or other event runs until the developer chooses

#### Scenario: Debug, then continue
- **WHEN** the developer chooses Debug
- **THEN** the program enters break mode at the statement that raised the error, with that procedure's variables readable
- **AND** continuing runs the statement again, and the dialog returns if it fails again

#### Scenario: A condition that raised
- **WHEN** the error was raised by the condition of an `ElseIf` or of a `Loop Until` or `Loop While`
- **THEN** continuing asks that condition again, without the conditions before it and without another pass through the loop

#### Scenario: End
- **WHEN** the developer chooses End
- **THEN** the program ends with no `QueryUnload`, `Unload` or `Terminate` event, as the End command ends it

#### Scenario: A handler in the code that raised the event
- **WHEN** code with an active error handler raises an event, by setting a property or with `RaiseEvent`, and the
  event's procedure raises an error it does not handle
- **THEN** the program halts in the event's procedure, and the handler in the raising code does not run

#### Scenario: A caller's handler
- **WHEN** a procedure with no handler of its own, in a standard module or a class, raises an error, and a caller with an
  active handler called it from the same event
- **THEN** the caller's handler runs and the program does not halt
