## MODIFIED Requirements

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

## ADDED Requirements

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
