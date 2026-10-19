## Purpose
Provide a persistent, byte-preserving physical projection of a saved archive project for source control, while protecting external edits. This specification covers the synchronization engine portion of #1190; IDE command integration is a subsequent slice.

## ADDED Requirements

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
