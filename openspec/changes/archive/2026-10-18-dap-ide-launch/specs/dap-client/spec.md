## ADDED Requirements

### Requirement: Explicit IDE debug launch
The IDE SHALL offer an explicit JSON-file launch command and SHALL NOT execute launch files merely because
a project opens. Executable, working-directory and source paths SHALL resolve from the selected file's
directory; command arguments and adapter launch fields SHALL pass unchanged without shell interpretation.

#### Scenario: Launch an existing workspace
- **WHEN** the user selects a valid launch file from the Run menu
- **THEN** the IDE owns one external session and prevents concurrent interpreter runs
- **AND** F5, Break, End and stepping route to that session according to its state

### Requirement: Remote debugger presentation
The IDE SHALL populate Call Stack and Locals asynchronously from the current stop, reject stale inspection
results, and fetch aggregate children without blocking the UI. It SHALL expose output and failures and SHALL
refuse unsupported remote operations rather than applying them to the interpreter.

#### Scenario: Resume during inspection
- **WHEN** the user continues while a locals request is pending
- **THEN** the old locals are cleared and the late response does not repaint them

### Requirement: External session visibility and cleanup
The IDE SHALL expose its current or most recent external debug connection in the shared servers view and
capture its traffic using the shared conversation log. Program completion, End and IDE closure SHALL release
the owned adapter and return run controls to their idle route.

#### Scenario: End while starting
- **WHEN** the user ends a session while its adapter is initializing
- **THEN** startup is cancelled and the adapter is cleaned up without starting an interpreter run
