# archive-source-folder Specification

## Purpose
Provide a persistent, byte-preserving source-control projection as a capability of any compound project file, with twinBASIC as the first format, protecting external edits through explicit synchronization in Project Explorer (#1190, #5148).

## Requirements

### Requirement: Stable complete physical projection
The synchronizer SHALL create a stable project-qualified physical hierarchy beside the saved archive, preserving every stored file's bytes and every directory, with an ordered deterministic metadata manifest outside that hierarchy. The projection and manifest SHALL be eligible for source control, with transient synchronization state ignored separately.

#### Scenario: Export a saved project
- **WHEN** a valid saved archive is synchronized into an unused destination
- **THEN** its virtual paths map directly to physical paths, including hidden entries and empty directories
- **AND** original metadata and file hashes are recorded without host-specific absolute paths or timestamps
- **AND** the saved archive remains unchanged

#### Scenario: Synchronize unchanged content
- **WHEN** the saved archive, managed hierarchy and manifest still match the synchronization baseline
- **THEN** synchronization reports no content change and preserves source and manifest timestamps

#### Scenario: Track bytes through Git
- **WHEN** the projection is added to a repository with automatic line-ending conversion enabled and no overriding user attributes
- **THEN** projection attributes preserve the original file bytes in the index
- **AND** transaction state and recovery copies are excluded from the default untracked-file listing

### Requirement: External edits stop refresh
The synchronizer SHALL refuse refresh when managed files, directories or the tracked manifest differ from the previous baseline. It SHALL preserve those changes and report the affected path without importing them into the archive.

#### Scenario: A user edits the physical projection
- **WHEN** a user edits, adds, deletes or renames a managed entry before refresh
- **THEN** refresh fails without replacing the user's changed tree

#### Scenario: A clean saved archive changes
- **WHEN** only the saved archive changes and the existing projection still matches its baseline
- **THEN** refresh installs the complete new hierarchy, including archive additions and removals
- **AND** retains the previous tree separately for recovery

#### Scenario: Adopt a clone
- **WHEN** a projection has no local baseline after cloning
- **THEN** adoption succeeds only if its tracked manifest and entire hierarchy match the current saved archive

### Requirement: Bounded portable destinations
The synchronizer SHALL reject unrepresentable or colliding portable paths, active repository-control names, symbolic links and reparse points in managed paths, and unrelated existing destinations. It SHALL bound input and inventory work and SHALL leave unrelated sibling files and repository metadata intact.

#### Scenario: Unsafe archive member
- **WHEN** an archive contains traversal, reserved names or case/normalization collisions
- **THEN** synchronization fails before creating any projection content rather than silently renaming or skipping entries

#### Scenario: Linked destination
- **WHEN** a managed member or synchronization lock is a symbolic link
- **THEN** synchronization fails without writing through that link

### Requirement: Recover interrupted synchronization
The synchronizer SHALL retain enough recovery state to resume or restore an interrupted installation on a later invocation. It SHALL refuse recovery over externally changed data and SHALL serialize simultaneous synchronizations of the same project.

#### Scenario: Interruption at commit boundaries
- **WHEN** initial creation or refresh is interrupted after staging, journaling, moving the previous tree, installing the new tree, or publishing the manifest
- **THEN** a subsequent synchronization recovers a consistent projection or reports a conflict while retaining the available trees

#### Scenario: Cancellation before commit
- **WHEN** cancellation occurs before journaling the installation
- **THEN** the previously published tree remains unchanged

#### Scenario: Late edit to recovery data
- **WHEN** an external edit is detected in the retained previous tree during commit or recovery
- **THEN** the edit is retained and synchronization fails rather than discarding it

### Requirement: Explicit IDE synchronization action
Project Explorer SHALL offer source-folder synchronization for a compound project with a synchronization format, or any of its members. Office browser projects SHALL NOT offer this capability. The action SHALL use the owning project's saved archive and SHALL report the physical destination on success. Merely opening a project SHALL NOT create the projection.

#### Scenario: Synchronize from a selected member
- **WHEN** the user invokes synchronization on a member of one of several open archive projects
- **THEN** only that member's owning project is synchronized
- **AND** the completion message identifies its physical folder

#### Scenario: Saved content differs from the preview
- **WHEN** the saved archive changes after it was opened and the user invokes synchronization
- **THEN** the physical folder reflects saved content while the existing document buffers remain unchanged
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

### Requirement: Shared project-local projection layout
The synchronizer SHALL honor the discovered project-local root, session choice and distribution identity. It SHALL place the tree at `{name}/`, the tracked manifest at `.manifests/{name}.json` and ignored state at `.sync/{name}/`. It SHALL preserve bytes through root Git attributes and each recovery folder's ignore rule, retain the exact folder marker bytes, and refuse compound filenames colliding with root controls before writing.

#### Scenario: A format supplies its saved contents
- **WHEN** a compound file with a synchronization format is synchronized
- **THEN** its ordered entries and format metadata are projected through the shared engine at the same layout
- **AND** the twinBASIC manifest retains Format 1 and its existing field order and bytes
- **AND** root `.gitattributes` contains `* -text` and each recovery folder's `.gitignore` contains `*`, each with a trailing LF
- **AND** `project-state.json` remains exactly `{"kind":"hexide.project-state","version":1}` as UTF-8 without a byte-order mark or trailing newline

#### Scenario: Shared branded root
- **WHEN** a branded distribution synchronizes a file beside an existing stock state root
- **THEN** the projection uses that root without creating a second distribution-specific folder
- **AND** without an existing root it uses the distribution's configured folder

#### Scenario: Reserved compound filename
- **WHEN** the compound filename is `project-state.json`, `.manifests`, `.sync`, `.gitattributes`, `.twinproj` or `.twinproj-sync`, compared case-insensitively
- **THEN** synchronization refuses before writing anything and names the conflicting filename

### Requirement: Move verified legacy projections
The synchronizer SHALL recover interrupted legacy synchronization before moving a verified projection to the shared layout. Its manifest and tree MUST match the baseline, or the current saved file for a clone. It SHALL retain manifest bytes, then sync normally. Conflicts SHALL retain existing data. Old directories SHALL be removed only when no unrelated entries remain. Every write, move and removal SHALL check managed paths for links before use.

#### Scenario: A verified old projection
- **WHEN** a legacy projection matches its baseline, or an adopted clone matches its current saved file
- **THEN** its tree, manifest and recovery state move from `.twinproj/{name}`, `.twinproj/.manifests/{name}.json` and `.twinproj-sync/{name}` to the new layout
- **AND** the old layout directories are removed only when no unrelated entries remain

#### Scenario: An interrupted legacy transaction
- **WHEN** a legacy sync was interrupted during installation
- **THEN** existing recovery completes before the projection is verified and moved

#### Scenario: A legacy conflict
- **WHEN** the old tree or tracked manifest differs from its baseline, or both layouts exist for the file without a migration record
- **THEN** synchronization refuses, naming the path and reason, and retains existing data without migrating it

#### Scenario: Another legacy project remains
- **WHEN** one of two projects in the same state root moves to the new layout
- **THEN** the other project's legacy directories remain usable until its own synchronization

#### Scenario: An interrupted layout move
- **WHEN** a verified migration stops after recording the move and before completing it
- **THEN** the next synchronization verifies ownership, ignore rules, and the tree against its manifest and baseline (or current saved file for a clone), then finishes the recorded move
- **AND** tree, manifest and baseline bytes are preserved, and empty legacy directories are removed
- **AND** both layouts without a record of a move are still refused
- **AND** neither layout can be synchronized concurrently during the move, and no directory containing an engine-held handle is moved

### Requirement: Bounded verified recovery retention
After a changing sync deletes its journal, the synchronizer SHALL prune, under its lease, only direct directories in that file's `.sync` folder named as lower-case GUIDs in N format, and MUST NOT delete through a link. It SHALL keep the ten newest candidates and all candidates at most seven days old, ordered and dated by `new.json` UTC last-write time using its supplied clock. It SHALL verify retained trees before deletion and preserve recovery evidence and external edits.

#### Scenario: Expired recovery trees
- **WHEN** a changing sync completes with more than ten retained transactions
- **THEN** only verified transactions outside the newest ten and older than seven days are removed
- **AND** unrelated directories and loose files remain untouched

#### Scenario: Protected recovery evidence
- **WHEN** an old transaction contains a link, an external tree edit, or missing or unreadable new metadata
- **THEN** that transaction remains and synchronization still succeeds
- **AND** previous and staged trees must match their own old and new manifests before deletion
- **AND** the just-completed transaction is always kept
- **AND** an I/O or access failure leaves the affected transaction and continues with later candidates without failing sync
- **AND** each transaction kept that way is reported for the IDE to log

#### Scenario: Removing a pruned transaction
- **WHEN** a transaction is pruned
- **THEN** it is renamed to `<id>.pruning` first and then deleted bottom-up, one entry at a time, stopping at any link
- **AND** a renamed transaction left by an interrupted deletion is finished by the next prune

#### Scenario: No pruning before successful commit
- **WHEN** synchronization is unchanged, recovering, fails, or still has a journal
- **THEN** it does not prune recovery transactions
- **AND** legacy synchronization folders are not pruned
