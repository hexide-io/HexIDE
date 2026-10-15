## MODIFIED Requirements

### Requirement: A file the IDE will not save SHALL be presented as read-only
Where the IDE would refuse to save a file, its editing surfaces SHALL be read-only and SHALL state the
reason, while remaining open for viewing. It SHALL remain runnable unless it holds a control the engine
cannot create, which is refused when it is run (see `interpreter-core`).

Discovering the refusal at save time means discovering it after the work is done. A developer who has spent
an hour on a form and is then told it cannot be written has lost the hour — and the refusal, which exists to
protect them, is what took it. Saying so up front turns the same limitation into something they can plan
around, and keeping the file viewable and runnable preserves the reason they opened the IDE at all.

The exception is a control the engine cannot create. Running a form without one is running a different program:
code naming it finds nothing, and a form whose code never names it behaves as if it had never held it. That is a
wrong result rather than a late error, so such a form is refused when it is run, and stays viewable
(hexide-io/HexIDE#857).

#### Scenario: Opening such a file
- **WHEN** a file the IDE would refuse to save is opened
- **THEN** its editors are read-only and state why

#### Scenario: Running a project containing one
- **WHEN** a project containing such a file is run, and the file holds no control the engine cannot create
- **THEN** it runs

#### Scenario: Running a form holding a control the engine cannot create
- **WHEN** a project is run whose form holds a control the engine cannot create
- **THEN** that form is refused when it is built to run, naming the control, and it can still be opened and viewed
