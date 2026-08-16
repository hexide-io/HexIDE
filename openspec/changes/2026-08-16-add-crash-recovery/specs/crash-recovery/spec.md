## ADDED Requirements

### Requirement: Unsaved work SHALL be backed up without touching project files
The IDE SHALL periodically copy unsaved editor content to a private store, and SHALL NOT write to the
project's own files other than by explicit save.

The explicit-save model is deliberate and is what a VB6 developer expects: the file on disk is what they
last saved, and nothing else writes it. A backup elsewhere adds a safety net without weakening that
guarantee — nobody has to wonder whether the IDE has silently altered their source.

#### Scenario: Editing without saving
- **WHEN** the developer edits and does not save
- **THEN** the content is backed up to the private store and the project's files are untouched

#### Scenario: Closing an editor with unsaved edits
- **WHEN** an editor with unsaved edits is closed without saving
- **THEN** those edits are still backed up

### Requirement: An unclean exit SHALL be distinguished from a normal one
The IDE SHALL detect that a previous session ended abnormally, and SHALL offer recovery only in that case.

Offering to restore after a normal close would be offering back work the developer already decided about —
they were asked to save and answered. Recovery has to be rare enough that its appearance means something,
or it becomes another dialog to dismiss.

#### Scenario: A normal exit
- **WHEN** the IDE is closed normally and reopened
- **THEN** nothing is offered for recovery

#### Scenario: An abnormal exit
- **WHEN** the IDE is killed or the machine loses power, and it is reopened
- **THEN** the unsaved work from that session is available to recover

### Requirement: Recovery SHALL be offered when the affected project is opened
Recovered work SHALL be offered at the point the project it belongs to is opened, rather than at startup.

Offered at startup, the developer is asked about work before there is any context for it — possibly for a
project they are not about to open. Waiting until the project is loaded puts the question next to the files
it concerns.

#### Scenario: Reopening the affected project
- **WHEN** the project that was open during the crash is opened again
- **THEN** the developer is offered the unsaved work from that session

### Requirement: Recovery SHALL match on a stable project identity, not a path
A project SHALL carry an identity that survives being moved or renamed, and recovery SHALL match on it.

A path is not an identity. A project moved between the crash and the reopen would lose its recovered work,
and — worse — a different project that happened to occupy the old path could be offered somebody else's
edits.

#### Scenario: The project has moved
- **WHEN** the project directory is renamed or moved before reopening
- **THEN** the recovered work is still matched to it

### Requirement: Restoration SHALL warn where the file on disk has since changed
Where a file has been modified on disk since the backup was taken, restoring SHALL say so before replacing
the developer's current content.

Restoring silently over a newer version is the one way this feature could itself destroy work — the
developer recovers a crashed session and unknowingly discards a change they made afterwards in another
editor. The baseline needed to detect it already exists for external-change detection.

#### Scenario: The file changed after the crash
- **WHEN** recovered content is offered for a file that has since changed on disk
- **THEN** the developer is told before anything is replaced
