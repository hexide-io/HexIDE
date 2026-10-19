## ADDED Requirements

### Requirement: Explicit IDE synchronization action
Project Explorer SHALL offer source-folder synchronization for an archive project or any of its members. The action SHALL use the owning project's saved archive and SHALL report the physical destination on success. Merely opening a project SHALL NOT create the projection.

#### Scenario: Synchronize from a selected member
- **WHEN** the user invokes synchronization on a member of one of several open archive projects
- **THEN** only that member's owning project is synchronized
- **AND** the completion message identifies its physical folder

#### Scenario: Saved content differs from the preview
- **WHEN** the saved archive changes after it was opened and the user invokes synchronization
- **THEN** the physical folder reflects saved content while the existing read-only previews remain unchanged
- **AND** the completion message explains that explicit reload refreshes previews and folder edits are not imported

### Requirement: Responsive and serialized synchronization
The IDE SHALL run file synchronization off the UI thread, serialize it with archive lifecycle operations, prevent command reentry, and refuse to export a closed project from a stale selection. Result dialogs SHALL NOT hold the lifecycle operation gate.

#### Scenario: Repeated command invocation
- **WHEN** synchronization or its result dialog is active
- **THEN** another invocation through the same Project Explorer command does not queue another export or dialog

#### Scenario: Close while reading the result
- **WHEN** the synchronization result dialog is awaiting dismissal
- **THEN** the archive service can complete a close operation

### Requirement: Localized synchronization feedback
The IDE SHALL translate the synchronization action and result message templates in every shipped language. Expected filesystem failures SHALL display a warning containing the diagnostic instead of a success message.

#### Scenario: Edited physical source
- **WHEN** a user edits an exported file before synchronization
- **THEN** the IDE displays a warning identifying the conflict and preserves the edit
