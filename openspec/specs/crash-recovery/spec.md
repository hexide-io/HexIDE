# crash-recovery Specification

## Purpose
Define how unsaved work survives an IDE that ends without a clean exit: a crash, a kill, a power loss, or a forced
close that discards edits without asking. The IDE keeps a private copy of what has not been saved, separate from the
project's own files, and offers it back the next time it can be placed: at start for a project that was never saved,
and when the project is opened for one that was.

## Requirements

### Requirement: Unsaved work SHALL be backed up without touching project files
The IDE SHALL copy what a save would write for every unsaved document to a private store, within a few seconds of a
change: a form's layout as well as its code. It SHALL do so whether or not the document has a file, and whether or not
its project has ever been saved. It SHALL NOT write to the project's own files other than by explicit save.

A VB6 developer expects the file on disk to be what they last saved. A backup elsewhere keeps that true.

#### Scenario: Editing without saving
- **WHEN** the developer edits and does not save
- **THEN** the content is backed up to the private store and the project's files are untouched

#### Scenario: Closing an editor with unsaved edits
- **WHEN** an editor with unsaved edits is closed without saving
- **THEN** those edits are still backed up

#### Scenario: A project that has never been saved
- **WHEN** the developer works in a new project and saves nothing
- **THEN** the text of each of its documents is backed up

### Requirement: The backup SHALL stay private to the machine and the user
The private store SHALL live in the user's local, non-roaming application data, or under the user-data directory
named at start-up when one is. It SHALL NOT be written in the project's folder or in its user sidecar. Each run's
backup SHALL be deleted after a clean exit, and after it has been restored or discarded.

Kept beside the project, the backup could be committed and pushed. In a roaming profile, it could follow the developer
to a machine where it means nothing.

#### Scenario: The sidecar and the project folder stay as they were
- **WHEN** unsaved work is backed up
- **THEN** nothing is written beside the project, and its sidecar is unchanged

#### Scenario: A clean exit
- **WHEN** the IDE closes after the save prompt has been answered, or with nothing unsaved
- **THEN** the run's backup is deleted

### Requirement: An unclean exit SHALL be distinguished from a normal one
The IDE SHALL detect that a previous run ended without a clean exit, and SHALL offer recovery only in that case. An exit
that discards unsaved work without anyone deciding to, such as on a terminate signal, SHALL count as unclean. A close
that automation asks for SHALL count as clean. A run still going in another IDE window SHALL NOT be offered.

After a normal close the developer answered the save prompt, so nothing is owed. After a terminate signal nobody
decided, so the work is.

#### Scenario: A normal exit
- **WHEN** the IDE is closed normally and reopened
- **THEN** nothing is offered for recovery

#### Scenario: An abnormal exit
- **WHEN** the IDE is killed or the machine loses power, and it is reopened
- **THEN** the unsaved work from that run is available to recover

#### Scenario: A terminate signal with unsaved work
- **WHEN** the IDE is sent a terminate signal while edits are unsaved, and it is reopened
- **THEN** those edits are available to recover

#### Scenario: Automation closes the IDE and discards
- **WHEN** an automation client shuts the IDE down without saving, and it is reopened
- **THEN** nothing is offered for recovery

#### Scenario: Two IDEs at once
- **WHEN** one IDE is open with unsaved work and a second is started
- **THEN** the second offers nothing from the first

### Requirement: Recovery SHALL be offered where the work can be placed
Recovered work for a saved project SHALL be offered when that project is opened. Recovered work for a project that was
never saved SHALL be offered at start, and SHALL restore as an unsaved project holding its documents.

Asked at start, the developer has no context for a saved project's work; asked on opening, the question sits next to
its files. A never-saved project has nothing to open, so start is its only chance.

#### Scenario: Reopening the affected project
- **WHEN** the project that was open during the crash is opened again
- **THEN** the developer is offered the unsaved work from that run

#### Scenario: A never-saved project
- **WHEN** the IDE ended uncleanly with work in a project that had never been saved, and it is started again
- **THEN** the developer is offered that project, and restoring it opens an unsaved project holding its documents

#### Scenario: A project in the IDE's own scratch folder
- **WHEN** the IDE ended uncleanly with work in a project it had saved into a scratch folder of its own
- **THEN** that project is offered at start, as a never-saved one is, and restored into that folder while it is still there

#### Scenario: A scratch project its files already hold
- **WHEN** such a project's files on disk already hold, byte for byte, everything its backup does
- **THEN** nothing is offered, and the backup is deleted

### Requirement: Restoring a saved project SHALL bring back its documents
Restoring a saved project's work SHALL put each backed-up document's content in place of what was opened, unsaved, and
SHALL add back any document the project no longer holds. Its other settings, such as its startup object, SHALL NOT be
restored. A backup holding nothing the opened project's files don't SHALL NOT be offered, and SHALL be deleted.

Such a backup is what a crash in the moment a project loads leaves, and every answer to it changes nothing.

#### Scenario: A backup the files already hold
- **WHEN** a project is opened whose backup matches its files on disk
- **THEN** nothing is offered, and the backup is deleted

#### Scenario: Restored work is unsaved
- **WHEN** a saved project's work is restored
- **THEN** its files on disk are unchanged, and the restored documents are saved only when the developer saves them

### Requirement: Recovery SHALL follow a moved project without matching a copy
Recovered work SHALL be matched to a saved project by its path. Where nothing is at that path any more, it SHALL be
matched by the project file's content. The offer SHALL say where the work came from.

A path alone loses work when a project moves between the crash and the reopen. Content alone would offer one copy's
edits in another, and VB6 developers copy project folders often. Falling back to content only when the old path is
empty gives the first and refuses the second.

#### Scenario: The project has moved
- **WHEN** the project directory is renamed or moved before reopening
- **THEN** the recovered work is still offered, naming the folder it came from

#### Scenario: A copy of the project
- **WHEN** a copy of the project is opened while the original is still at its old path
- **THEN** the copy is not offered the original's work

### Requirement: Restoration SHALL warn where the file on disk has since changed
Where a file has been modified on disk since the backup was taken, restoring SHALL say so before replacing the
developer's current content.

Restoring silently over a newer version is the one way this feature could itself destroy work: the developer recovers a
crashed run and unknowingly discards a change they made afterwards in another editor. The baseline needed to detect it
already exists for external-change detection.

#### Scenario: The file changed after the crash
- **WHEN** recovered content is offered for a file that has since changed on disk
- **THEN** the developer is told before anything is replaced

### Requirement: Pending recovery SHALL stay reachable
The IDE SHALL list every run whose work is waiting to be recovered, from the File menu, and SHALL let the developer
restore or discard each one.

A project that is never reopened would otherwise keep its recovered work out of sight indefinitely. Deleting it
unasked would destroy the developer's work, which is the one thing this capability must never do.

#### Scenario: A project that isn't reopened
- **WHEN** the IDE ended uncleanly with work in a saved project, and that project isn't opened again
- **THEN** the work is listed under the File menu until it is restored or discarded
