## ADDED Requirements

### Requirement: Breakpoints SHALL be toggleable from every VB6-familiar entry point
The IDE SHALL let the developer toggle a breakpoint from the keyboard, the Debug menu, and the breakpoint gutter, and SHALL clear every breakpoint on request.

A developer sets and clears breakpoints on a code line without leaving the keyboard or the editor. The breakpoint store is a set of line numbers per document; a line that never executes simply never breaks.

#### Scenario: Toggling a breakpoint on the caret line
- **WHEN** the developer presses F9 in a code window, or chooses Debug ▸ Toggle Breakpoint, or clicks the breakpoint gutter beside a line
- **THEN** a breakpoint is added on that line, or removed if one was already present
- **AND** the gutter shows a solid dark-red dot on every line carrying a breakpoint

#### Scenario: Clearing every breakpoint in the project
- **WHEN** the developer presses Ctrl+Shift+F9, or chooses Debug ▸ Clear All Breakpoints
- **THEN** all breakpoints across all documents are removed

### Requirement: Breakpoints SHALL persist across sessions per user
Breakpoints SHALL be stored per user beside the project and restored when it is reopened.

VB6 discarded breakpoints when the project closed. HexIDE keeps them, stored per user beside the project so the developer may commit or ignore them at their discretion.

#### Scenario: Reopening a project
- **WHEN** the developer sets breakpoints, closes the project, and reopens it
- **THEN** the breakpoints are restored on the same lines of the same documents

### Requirement: Breakpoints SHALL only take effect in code loaded for the run
A breakpoint SHALL cause a break only when it sits in code loaded into the interpreter for the current run.

A run loads the startup form's code into the interpreter. Breakpoints in other modules are stored, shown and persisted, but the code they sit in is not executing, so they cannot be reached.

#### Scenario: A breakpoint in a standard or class module
- **WHEN** the developer sets a breakpoint in a `.bas` or `.cls` module and runs the project
- **THEN** the breakpoint is retained and displayed
- **AND** execution does not break there, because that module's code is not loaded for the run

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
The debugger SHALL support stepping one statement at a time, descending into any procedure called by that statement.

Stepping follows VB6's gestures and granularity.

#### Scenario: Stepping into a call
- **WHEN** the developer presses F8 while paused
- **THEN** one statement executes and the program breaks again, descending into any procedure called by that statement

#### Scenario: Starting a run with F8
- **WHEN** the developer presses F8 while the project is idle
- **THEN** the project starts and breaks at its first executed statement

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
The Immediate window SHALL evaluate expressions against the paused frame and SHALL reject calls to user procedures.

The Immediate window is VB6's primary interactive debugging surface. It is a single editable buffer that also receives `Debug.Print` output.

#### Scenario: Evaluating an expression
- **WHEN** the developer types `?expr`, `Print expr`, or a bare expression and presses Enter while paused
- **THEN** the expression is evaluated against the paused frame and the result is printed on the next line

#### Scenario: Calling a user procedure
- **WHEN** the entered expression calls a user-defined `Sub` or `Function`
- **THEN** it is rejected, because running user code on the suspended execution path is not supported

### Requirement: Editing code during a run SHALL offer VB6's reset prompt
The IDE SHALL prompt to reset the project when code is edited during a run, because edits cannot be applied to a suspended execution.

The interpreter cannot hot-patch a running program: the execution position is a live host call stack, not a re-pointable program counter. Rather than silently diverging, HexIDE surfaces the same prompt VB6 showed whenever it could not apply an edit live — which is both honest and familiar.

#### Scenario: Editing while the project is running or paused
- **WHEN** the developer edits code while the project is running or in break mode
- **THEN** a prompt asks whether to reset the project
- **AND** choosing yes ends the run and keeps the edit, while choosing no reverts the edit and lets the program continue

### Requirement: The debugger SHALL expose the VB6 default keyboard gestures
The default keymap SHALL bind the VB6 debugging gestures without requiring an opt-in keymap pack.

The debug keymap is the deepest layer of VB6 muscle memory.

#### Scenario: Using the VB6 debug keys
- **WHEN** the developer uses the default keymap
- **THEN** F5 starts or continues, Ctrl+Break breaks, F8 steps into, F9 toggles a breakpoint, Ctrl+Shift+F9 clears all breakpoints, and Ctrl+G opens the Immediate window

### Requirement: The debugger SHALL be drivable by automation
The debugger SHALL expose an automation surface mirroring the IDE debug commands.

A stepping session cannot be verified by screenshot, so the automation surface is how the debugger is tested as well as scripted.

#### Scenario: Driving a debug session without the UI
- **WHEN** an automation client is connected
- **THEN** it can set and clear breakpoints, break, continue, step into, read the debug state, read locals, and evaluate an expression

#### Scenario: Reading state that requires a paused program
- **WHEN** locals or an evaluation are requested while the program is not paused
- **THEN** the request returns no result rather than an error
