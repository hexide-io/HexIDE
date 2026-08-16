## ADDED Requirements

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

#### Scenario: Breaking when a watch condition is met
- **WHEN** a watch is configured to break when its expression becomes true, or when its value changes
- **THEN** the program breaks at the statement where that occurs

### Requirement: The Call Stack window SHALL show the chain of active procedures
The Call Stack window SHALL list the active procedure activations, anchored at the paused frame.

The call stack answers "how did execution get here", which Locals alone cannot.

#### Scenario: Viewing the call stack while paused
- **WHEN** the developer presses Ctrl+L while paused
- **THEN** the Call Stack window lists the active procedure activations, deepest first, each with its name and current line

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

## MODIFIED Requirements

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
