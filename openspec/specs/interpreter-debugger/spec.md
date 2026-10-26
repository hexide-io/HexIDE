# interpreter-debugger Specification

## Purpose
Define the VB6-style step-through debugging experience over HexIDE's in-box tree-walking interpreter:
breakpoints, break mode, stepping, and the inspection windows a VB6 developer expects to find behind
F5, F8 and F9.

The debugger is deliberately built on the interpreter rather than deferred to an external compiled-execution
backend, because the interpreter is a permanent part of HexIDE and a developer who cannot stop a program on a
line cannot evaluate the IDE at all. Its limits follow from that choice and are stated as behaviour, not
hidden: execution is a live host call stack rather than a re-pointable program counter, so edits during a run
prompt for a reset and the execution point moves only between top-level statements of the paused procedure.

## Requirements

### Requirement: Breakpoints SHALL be toggleable from every VB6-familiar entry point
The IDE SHALL let the developer toggle a breakpoint from the keyboard, the Debug menu, and the breakpoint gutter, and SHALL clear every breakpoint on request.

A developer sets and clears breakpoints on a code line without leaving the keyboard or the editor. The breakpoint store is a set of line numbers per document, keyed by the document's identity rather than by any of its names, and counted from the top of the file; a line that never executes simply never breaks. A line in a read-only region of the code window SHALL NOT take a breakpoint.

#### Scenario: Toggling a breakpoint on the caret line
- **WHEN** the developer presses F9 in a code window, or chooses Debug ▸ Toggle Breakpoint, or clicks the breakpoint gutter beside a line
- **THEN** a breakpoint is added on that line, or removed if one was already present
- **AND** the gutter shows a solid dark-red dot on every line carrying a breakpoint

#### Scenario: Clearing every breakpoint in the project
- **WHEN** the developer presses Ctrl+Shift+F9, or chooses Debug ▸ Clear All Breakpoints
- **THEN** all breakpoints across all documents are removed

#### Scenario: Pressing F9 on a header line
- **WHEN** the developer presses F9 on a line of a form's designer block
- **THEN** no breakpoint is set

### Requirement: Breakpoints SHALL persist across sessions per user
Breakpoints SHALL be stored per user beside the project and restored when it is reopened.

VB6 discarded breakpoints when the project closed. HexIDE keeps them, stored per user beside the project so the developer may commit or ignore them at their discretion.

#### Scenario: Reopening a project
- **WHEN** the developer sets breakpoints, closes the project, and reopens it
- **THEN** the breakpoints are restored on the same lines of the same documents

### Requirement: Breakpoints SHALL only take effect in code loaded for the run
A breakpoint SHALL cause a break only when it sits in code loaded into the interpreter for the current run.

A form's run loads its code and the project's standard (`.bas`) and class (`.cls`) modules into the interpreter. UserControl (`.ctl`) and PropertyPage (`.pag`) modules are not loaded as free-standing modules by this path.

#### Scenario: A breakpoint in a standard or class module
- **WHEN** the developer sets a breakpoint on an executable line in a `.bas` or `.cls` module and the running form calls that code
- **THEN** execution breaks at that line under the module's name
- **AND** the stop reason is `Breakpoint`, with the line counted from the top of the module's file

#### Scenario: A breakpoint in a UserControl or PropertyPage module
- **WHEN** the developer sets a breakpoint in a `.ctl` or `.pag` module and runs the startup form
- **THEN** the breakpoint is retained and displayed
- **AND** the form's module-loading path does not load that module as a free-standing module

### Requirement: The developer SHALL be able to suspend and resume a running program
The IDE SHALL let the developer break into a running program, continue from break mode, and end the run from either state.

Break mode is entered from a breakpoint, a step, an explicit pause, or a `Stop` statement, and left by continuing or ending the run.

#### Scenario: Breaking into a running program
- **WHEN** the developer presses Ctrl+Break, or chooses Run ▸ Break, while the project is running
- **THEN** the program suspends at the next executed statement and enters break mode

#### Scenario: Continuing from break mode
- **WHEN** the developer presses F5 while paused
- **THEN** the program resumes, the current-statement bar clears, the inspection windows blank, and any frozen event handlers are released

#### Scenario: Ending the run from break mode
- **WHEN** the developer chooses End while paused
- **THEN** the suspended execution is unwound and the project is reset
- **AND** no `Class_Terminate` handlers run, matching VB6's `End`

### Requirement: A fresh run SHALL be isolated from prior execution
A walk suspended at a time-slice yield SHALL abort before executing its statement if the run ended or the debug
session changed. The runner SHALL accept static fault and `End` notifications only from interpreters tagged with
its controller and session when attached to the run.

#### Scenario: Restart while a running walk has yielded
- **WHEN** End and a fresh run's Reset happen before the old time-slice continuation resumes
- **THEN** the old walk unwinds without executing its parked statement

#### Scenario: An old or unrelated interpreter reports a fault or End
- **WHEN** a startup-form or Sub Main run is active and another run's interpreter reports a fault or `End`
- **THEN** the current run stays active and reports no error from that interpreter

### Requirement: A `Stop` statement SHALL enter break mode under the debugger
A `Stop` statement SHALL enter break mode when the debugger is attached, and SHALL terminate the program when it is not.

`Stop` is the source-level equivalent of a breakpoint. Its behaviour differs between a debugged run and a standalone one, matching VB6.

#### Scenario: `Stop` reached under the IDE debugger
- **WHEN** execution reaches a `Stop` statement while the debugger is attached
- **THEN** the program enters break mode at that line, exactly as a breakpoint would

#### Scenario: `Stop` reached in a standalone run
- **WHEN** execution reaches a `Stop` statement with no debugger attached
- **THEN** the program terminates, matching a compiled executable

### Requirement: The paused location SHALL be shown in the editor
The IDE SHALL reveal and highlight the statement at which execution is suspended.

The developer must be able to see where execution stopped without hunting for it.

#### Scenario: Breaking at a line
- **WHEN** the program enters break mode
- **THEN** the IDE opens or activates the tab for that module, scrolls the line into view, moves the caret to it, and paints a full-width amber bar across it

### Requirement: The developer SHALL be able to step through execution
The debugger SHALL support stepping into, over and out of calls, and running to the cursor, one statement at a time.

Stepping follows VB6's gestures and granularity: one statement at a time, with control over whether calls are entered.

#### Scenario: Stepping into a call
- **WHEN** the developer presses F8 while paused
- **THEN** one statement executes and the program breaks again, descending into any procedure called by that statement

#### Scenario: Starting a run with F8
- **WHEN** the developer presses F8 while the project is idle
- **THEN** the project starts and breaks at its first executed statement

#### Scenario: Stepping over a call
- **WHEN** the developer presses Shift+F8 while paused on a line that calls a procedure
- **THEN** that procedure runs to completion and the program breaks at the next statement in the same or a shallower frame

#### Scenario: Stepping out of a procedure
- **WHEN** the developer presses Ctrl+Shift+F8 while paused
- **THEN** the rest of the current procedure runs and the program breaks in its caller after it returns

#### Scenario: Running to the cursor
- **WHEN** the developer presses Ctrl+F8 with the caret on a line
- **THEN** the program runs until that line is reached and breaks there
- **AND** a breakpoint encountered on the way still breaks first

### Requirement: Break mode SHALL freeze the whole program
While paused, the runtime SHALL prevent any new event handler from beginning execution.

VB6 suspends the entire program at a break, not just the frame that stopped. Without this, timers and control events would run while the developer inspects state.

#### Scenario: An event fires while paused
- **WHEN** a timer tick or control event would begin while the program is paused
- **THEN** that handler is held before its first statement and does not execute
- **AND** it resumes only when the developer continues

### Requirement: The Locals window SHALL show the paused frame's variables
The Locals window SHALL present the paused frame's parameters, locals and module scope as an expandable tree.

Locals is the primary inspection surface, showing what is in scope where execution stopped.

#### Scenario: Inspecting locals while paused
- **WHEN** the program is paused and the Locals window is open
- **THEN** it shows the module and procedure context, and an expandable tree of the frame's parameters and locals plus a `Me`/module root, with expression, value and type columns

#### Scenario: Expanding a structured value
- **WHEN** the developer expands an array, a user-defined type, or an object instance
- **THEN** its elements or fields are shown, guarded against cycles and bounded in depth

### Requirement: The Immediate window SHALL evaluate expressions against the paused frame
The Immediate window SHALL evaluate expressions and execute assignments against the paused frame, and SHALL reject calls to user procedures.

The Immediate window is VB6's primary interactive debugging surface. It is a single editable buffer that also receives `Debug.Print` output.

#### Scenario: Evaluating an expression
- **WHEN** the developer types `?expr`, `Print expr`, or a bare expression and presses Enter while paused
- **THEN** the expression is evaluated against the paused frame and the result is printed on the next line

#### Scenario: Assigning to a variable
- **WHEN** the developer types an assignment or a `Set` statement and presses Enter while paused
- **THEN** it is executed and the paused program's state is changed

#### Scenario: Calling a user procedure
- **WHEN** the entered expression calls a user-defined `Sub` or `Function`
- **THEN** it is rejected, because running user code on the suspended execution path is not supported

#### Scenario: Typing while not paused
- **WHEN** the developer presses Enter in the Immediate window while the program is not in break mode
- **THEN** a message explains that evaluation is only available in break mode

### Requirement: Editing code during a run SHALL offer VB6's reset prompt
The IDE SHALL prompt to reset the project when code is edited during a run, because edits cannot be applied to a suspended execution.

The interpreter cannot hot-patch a running program: the execution position is a live host call stack, not a re-pointable program counter. Rather than silently diverging, HexIDE surfaces the same prompt VB6 showed whenever it could not apply an edit live — which is both honest and familiar.

#### Scenario: Editing while the project is running or paused
- **WHEN** the developer edits code while the project is running or in break mode
- **THEN** a localized prompt asks "This action will reset your project, proceed anyway?" with OK and Cancel
- **AND** choosing OK ends the run and keeps the edit, while choosing Cancel reverts the edit and lets the program continue

#### Scenario: The window the reset prompt is shown over
- **WHEN** the reset prompt is asked while the code window's own window is visible and not minimized
- **THEN** the prompt is modal over that window, which it disables until answered
- **AND** when that window is minimized, the prompt is modal over the top window instead, which may be the running form
- **AND** in VB6 the running form owns the prompt and every window of the IDE and the program is disabled; the
  difference is recorded as D2 in `docs/debugger-vb6-divergences.md`

### Requirement: The debugger SHALL expose the VB6 default keyboard gestures
The default keymap SHALL bind the VB6 debugging gestures without requiring an opt-in keymap pack.

The debug keymap is the deepest layer of VB6 muscle memory and is bound by default.

#### Scenario: Using the VB6 debug keys
- **WHEN** the developer uses the default keymap
- **THEN** F5 starts or continues, Shift+F5 restarts, Ctrl+Break breaks, F8 steps into, Shift+F8 steps over, Ctrl+Shift+F8 steps out, Ctrl+F8 runs to cursor, F9 toggles a breakpoint, Ctrl+Shift+F9 clears all breakpoints, Ctrl+F9 sets the next statement, Shift+F9 opens Quick Watch, Ctrl+W edits a watch, Ctrl+G opens the Immediate window, and Ctrl+L opens the Call Stack

### Requirement: The debugger SHALL be drivable by automation
The debugger SHALL expose an automation surface mirroring the IDE debug commands.

A stepping session cannot be verified by screenshot, so the automation surface is how the debugger is tested as well as scripted. It mirrors the IDE commands rather than offering a separate model.

#### Scenario: Driving a debug session without the UI
- **WHEN** an automation client is connected
- **THEN** it can set and clear breakpoints, break, continue, step into, over and out, run to cursor, set the next statement, read the debug state, read locals, read the call stack, add and read watches, and evaluate an expression

#### Scenario: Reading state that requires a paused program
- **WHEN** locals, the call stack, or an evaluation are requested while the program is not paused
- **THEN** the request returns no result rather than an error

### Requirement: The developer SHALL be able to move the execution point within the paused procedure
The debugger SHALL let the developer move the execution point to another top-level statement of the paused procedure, and SHALL refuse targets it cannot address.

Setting the next statement lets a developer re-run or skip code without restarting. The tree-walking interpreter can address top-level statements of the paused procedure; targets inside nested blocks are refused rather than silently mishandled.

#### Scenario: Moving to another top-level statement
- **WHEN** the developer places the caret on a top-level statement of the paused procedure and presses Ctrl+F9
- **THEN** the execution point moves to that line without running the statements in between, and the amber bar follows

#### Scenario: Targeting a statement inside a nested block
- **WHEN** the target line is inside an `If`, `For`, `Do` or `Select` block, or the program is paused inside one
- **THEN** the move is refused with an explanation of the limit

### Requirement: The developer SHALL be able to watch expressions
The debugger SHALL evaluate watched expressions at each break, and SHALL support watches that themselves cause a break.

Watches persist an expression across breaks, so the developer does not re-type it, and can turn a value change into a breakpoint.

#### Scenario: Adding a watch
- **WHEN** the developer chooses Debug ▸ Add Watch, or presses Shift+F9 with the caret on an identifier
- **THEN** the Add Watch dialog opens, pre-filled with that identifier where one was under the caret

#### Scenario: Watching a value across breaks
- **WHEN** an expression watch exists and the program breaks
- **THEN** its current value is displayed, expandable in the same way as Locals

#### Scenario: Selecting an expanded watch child
- **WHEN** the developer selects a child of an expanded watch after selecting another watch
- **THEN** Edit Watch and Delete SHALL do nothing, including the context menu and Delete key
- **AND** rebuilding the watch rows SHALL clear selection

#### Scenario: Breaking when a watch condition is met
- **WHEN** a watch is configured to break when its expression becomes true, or when its value changes
- **THEN** the program breaks at the statement where that occurs

### Requirement: The Call Stack window SHALL show the chain of active procedures
The Call Stack window SHALL list the active procedure activations, anchored at the paused frame.

The call stack answers "how did execution get here", which Locals alone cannot.

#### Scenario: Viewing the call stack while paused
- **WHEN** the developer presses Ctrl+L while paused
- **THEN** the Call Stack window lists the active procedure activations, deepest first, each with its name and current line

#### Scenario: Showing a caller
- **WHEN** the developer selects or double-clicks a caller in the built-in Call Stack window
- **THEN** the code window opens that module at the calling statement with a green call-site triangle
- **AND** the current execution statement stays unchanged
- **AND** the call-site marker clears on continuing or the next break

#### Scenario: A concurrent event frozen above the paused frame
- **WHEN** an event handler was frozen by break mode after the paused frame began
- **THEN** it is excluded from the call stack, which is anchored at the paused frame

### Requirement: Hovering an identifier while paused SHALL show its value
The editor SHALL show the live value of an identifier hovered while the program is paused.

Data tips let a developer read state without opening a window or typing an expression.

#### Scenario: Hovering a variable in break mode
- **WHEN** the developer hovers an identifier in a code window while the program is paused
- **THEN** a tip shows the identifier and its live value, taking precedence over the ordinary quick-info tooltip

#### Scenario: Hovering something that is not an expression
- **WHEN** the hovered word does not resolve to an evaluable expression
- **THEN** no data tip is shown

### Requirement: The debugger's line numbers SHALL be the code window's
Each document a run loads SHALL be given to the interpreter as its whole text, as the code window holds it,
header included, so that the line a statement starts on in the interpreter is the line it is shown on in the
code window, with no conversion anywhere between them. Which documents a run loads is a separate question,
governed by its own requirement and unchanged here.

Breakpoints, the current-statement bar, Run To Cursor, Set Next Statement and the Call Stack all exchange line
numbers with the interpreter. Giving it only the code after the header would mean correcting by the header's
length at every one of those, and a single place that forgot would stop on, or show, the wrong line.

#### Scenario: Stopping on a breakpoint in a form
- **WHEN** a run reaches a breakpoint in a form's code
- **THEN** the current-statement bar is on the line the breakpoint was set on, counted from the top of the file

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
the retry, and the event boundary (`docs/vb6-fidelity-oracle/an-unhandled-run-time-error.md`, "An unhandled run-time error"). The exception for a
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

### Requirement: Run state in the IDE chrome
The IDE SHALL show localized design, run and break title tags, with break selected while the interpreter debugger is paused. The no-project title SHALL be localized. Title and OS window title SHALL refresh on debugger stop, continue and language changes. The running status message SHALL be cleared while paused and restored on continuing a running program.

#### Scenario: Pausing and continuing
- **WHEN** a running program pauses at a breakpoint, a Stop statement, a step or a requested break
- **THEN** the title shows the break tag and the running status message is cleared
- **WHEN** execution continues
- **THEN** the title shows the run tag and the running status message returns
