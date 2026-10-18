## Purpose

Provide immutable archive-backed project documents with explicit lifecycle and text access, independently
of VB6 project models. This capability describes the model API; IDE views and commands are separate work.

## ADDED Requirements

### Requirement: Preserve a complete project tree

The project snapshot MUST expose the container's original entry order, empty directories, names, metadata
and opaque file bytes without coercing source into VB6 forms or modules. Projects and documents MUST report
read-only status and expose no content mutation or persistence operation.

#### Scenario: Project contains unsupported resources
- **WHEN** a supported container includes unknown categories, hidden entries or binary resources
- **THEN** all those entries and their original content and metadata remain available in the snapshot

### Requirement: Qualify identity by project and exact archive segments

An entry identity MUST include its normalized container path and exact ordinal archive-name segments.
It MUST distinguish identical member names in different containers, different folders and different case.
It MUST NOT resolve member names through the host filesystem. Duplicate sibling names MUST be refused
with parent context before publishing a project. Root display name MUST NOT determine root identity.

#### Scenario: Reopen or reload a member
- **WHEN** the same normalized container path still contains the same entry-name segments
- **THEN** its new entry identity equals the previous identity, even when its content has changed
- **AND** the new snapshot can resolve that identity without reading from the previous document object

#### Scenario: A name contains a separator
- **WHEN** one file's name contains a slash and another is nested beneath a directory
- **THEN** their identities remain distinct even if their display paths look alike

### Requirement: Read text explicitly and without loss of raw content

Known text types MUST offer a strict UTF-8 preview bounded by a configurable byte limit. The preview MUST
preserve line endings and omit an initial UTF-8 BOM while original bytes remain unchanged. Unsupported
types, invalid encoding and exceeded limits MUST return distinct unavailable states with no substitute
text. Empty valid text MUST remain distinguishable from an unavailable preview.

#### Scenario: A source has invalid UTF-8
- **WHEN** its preview is requested
- **THEN** the result reports invalid encoding with no replacement text
- **AND** the original bytes remain available for inspection

### Requirement: Publish only complete snapshots

Open and explicit reload MUST read and validate the whole project before registering or replacing it.
Failure or observed cancellation MUST leave the existing collection and snapshots unchanged. Opening an
already-open normalized path MUST return its current snapshot until explicit reload. Close MUST remove
the project without modifying the container or creating extraction directories, sidecars or compiler sessions.
The owner MUST serialize lifecycle operations.

#### Scenario: Reload fails after an external change
- **WHEN** the new container is corrupt, ambiguous, missing or exceeds a policy limit
- **THEN** reload fails and the previous project/document objects and bytes remain available

#### Scenario: Reload succeeds
- **WHEN** a complete replacement has been read and validated
- **THEN** it replaces the registered snapshot as a whole
- **AND** previously returned snapshots retain their original content
