# Profile catalog

## Purpose

Record the standalone catalog foundation for #2621. The IDE still uses personalities. The approved
bundle design and remaining loader work are in
`openspec/changes/2026-10-25-profiles-supersede-personalities/`. This component does not apply profiles
or parse CLI selectors. It validates parsed manifest declarations and provides a contained byte reader,
bundle JSON assembly and name resolution, without multi-file atomicity or IDE consumers.

## Requirements

### Requirement: Immutable versioned declarations

The catalog SHALL represent a profile declaration with its schema version, id, source, optional pack
file references, language file map, ordered project type ids and visibility maps. Collections SHALL be
copied into immutable collections. Omitted project types SHALL differ from an explicitly empty list.
File references SHALL confer no permission to open a file.

#### Scenario: Caller changes an input collection

- **WHEN** a caller changes a collection used to construct a declaration
- **THEN** the declaration retains its original values

### Requirement: Explicit embedded index

The catalog SHALL read embedded JSON manifests through an explicit index containing `vb6`, `vbaode`
and `vba`, in that order. They SHALL declare schema version 1 and only the project types and menu
visibility specified by the approved bundle design. No IDE consumer SHALL be wired by this foundation.

#### Scenario: Built-in declarations are inspected

- **WHEN** the catalog is read
- **THEN** VB6 offers all eight approved project ids, VBA ODE offers Standard EXE, and VBA offers none
- **AND** Project References is visible only for VB6, with Tools References visible for the other two

### Requirement: Untrusted installed candidates

Discovery SHALL enumerate only immediate directories under the supplied user-data directory's
`profiles` child, ordered by ordinal basename.
Discovery SHALL return immutable candidates containing ids and absolute directory paths without opening
manifests or payloads.

#### Scenario: Installed folder uses a built-in name

- **WHEN** an eligible folder uses a built-in id
- **THEN** discovery returns it for later collision handling without certifying validity or containment

#### Scenario: Incomplete installation

- **WHEN** an eligible installed folder has a missing or half-written manifest
- **THEN** discovery returns an untrusted candidate without reading that manifest

#### Scenario: No installed directory

- **WHEN** the profiles directory does not exist
- **THEN** discovery returns an empty list

### Requirement: Candidate id grammar

Candidate ids SHALL start with an ASCII lower-case letter and contain only ASCII lower-case letters,
digits and hyphens. `none` and Windows device names SHALL be excluded on every platform.

#### Scenario: Reserved or malformed directory basename

- **WHEN** a folder is named `none`, starts with a digit or contains uppercase or non-ASCII letters
- **THEN** discovery excludes it

### Requirement: Manifest declaration validation

The standalone parser SHALL validate parsed JSON before returning a snapshot. Version 1 SHALL require
`schemaVersion` equal to 1 and a valid, non-reserved `id`. When given an installed folder id, the parser
SHALL require an ordinal match. Unknown and duplicate fields, incorrect JSON types and empty file
references SHALL be refused. Omitted optional members SHALL contribute no override.

#### Scenario: Invalid declaration

- **WHEN** a manifest has an unknown field, unsupported schema, invalid id or folder-id mismatch
- **THEN** validation refuses the declaration without returning a snapshot

### Requirement: Closed manifest vocabulary

Project types SHALL use the eight approved ids and retain order, with duplicates refused. Menu
visibility SHALL accept only `project.references` and `tools.references`; toolbar visibility SHALL
accept only `standard`, `edit`, `debug` and `form-editor`. Language overrides SHALL name a shipped
language or a region in the shipped language manifest. Visibility values SHALL be JSON booleans.
Duplicate map entries SHALL be refused.

#### Scenario: Unknown surface

- **WHEN** a declaration uses a menu or toolbar id outside the published catalogue
- **THEN** validation refuses the declaration

### Requirement: Internal attributed diagnostics

Validation failures SHALL carry machine-readable `file`, `field` and `code` data. These diagnostics
SHALL remain internal until a localized presentation adapter exists. Validation SHALL NOT open any
referenced file or certify a file reference's containment.

#### Scenario: Invalid declaration is attributed

- **WHEN** declaration validation fails
- **THEN** the error identifies the source and offending field

#### Scenario: Optional payload declaration

- **WHEN** a manifest declares pack file references and valid optional surface overrides
- **THEN** the snapshot retains the declarations after the input JSON document is disposed
- **AND** the parser does not open the referenced files

### Requirement: Portable file reference spelling

The manifest parser SHALL refuse rooted paths, drive-qualified paths, UNC paths, alternate data streams
and parent traversal using either separator. It SHALL refuse empty and dot components, Windows-invalid
characters, trailing spaces/dots and reserved device names. File references SHALL remain relative
original declarations; both separator styles SHALL denote directory boundaries during containment checks.

#### Scenario: Cross-platform escape spelling

- **WHEN** any pack, settings or language reference names an unsafe path on either supported filesystem
- **THEN** parsing refuses it with source file, manifest field and `UnsafePath` code

### Requirement: Directory manifest containment checks

The directory-manifest parser SHALL check the root and existing reference components for reparse
points, refusing symlinks and junctions even with internal targets. It SHALL NOT open payloads. Missing
files SHALL remain declarations pending loader validation. Preflight SHALL NOT authorize later opens.

#### Scenario: Link in a file reference

- **WHEN** a directory manifest references a file through a symlink, dangling link or junction
- **THEN** parsing refuses it with the manifest file and field attributed

### Requirement: Handle-relative Linux containment

On Linux the standalone byte reader SHALL open each directory and file relative to the preceding
directory handle with no-follow semantics, independently of preflight checks.

#### Scenario: Path becomes a link after preflight

- **WHEN** a checked file, intermediate directory or bundle root becomes an escaping link before open
- **THEN** the reader refuses to return the target's bytes

### Requirement: Locked Windows containment

On Windows the reader SHALL hold ancestor handles without write/delete sharing and reject reparse
points using the opened handles.

#### Scenario: Windows path replacement

- **WHEN** a checked path becomes a junction before open
- **THEN** the reader refuses it

### Requirement: Contained handle reads

The reader SHALL validate portable reference spelling and read from the opened handle without reopening
its pathname. Other platforms SHALL refuse reads until a contained implementation exists. This
primitive SHALL NOT certify a multi-file snapshot.

#### Scenario: Ordinary contained file

- **WHEN** an existing regular file has no links in its path
- **THEN** the reader returns that file's bytes

### Requirement: Bounded file reads

The reader SHALL refuse files exceeding 1 MiB with `TooLarge`, reading at most the limit plus one byte,
including when a file grows during reading.

#### Scenario: Growing file

- **WHEN** a file grows beyond 1 MiB during a read
- **THEN** the reader refuses it without reading beyond 1 MiB plus one byte

### Requirement: Regular files only

The reader SHALL refuse non-regular Linux files using fstat on the opened handle where the entry point
and x64 or arm64 stat layout are available. Windows SHALL refuse non-disk handles using their file
type. Refusals SHALL use `UnsafePath`.

#### Scenario: FIFO or device

- **WHEN** an opened Linux handle names a FIFO or device
- **THEN** the reader refuses it without consuming its contents

### Requirement: Linux handle fallback

When fstat is unavailable or the architecture has no supported stat layout, the reader SHALL refuse
non-seekable handles with `UnsafePath` and allow seekable handles through the existing bounded read.
It SHALL NOT close the caller's handle during fallback validation.

#### Scenario: Older glibc or unsupported stat layout

- **WHEN** fstat cannot be resolved or the process architecture is outside x64 and arm64
- **THEN** a regular file remains readable and a pipe is refused without reading it

### Requirement: Detached bundle assembly

The standalone assembler SHALL read the manifest and every declared payload through the contained
reader before returning an immutable map of detached JSON objects. It SHALL NOT be treated as an atomic
multi-file snapshot or connected to IDE consumers.

#### Scenario: Payload changes after assembly

- **WHEN** a referenced file changes after assembly returns
- **THEN** the captured JSON retains its original values

### Requirement: Bundle payload validation

The assembler SHALL validate packs against existing record formats and reject malformed JSON, missing
files and non-object payloads. Failures SHALL identify the file, field and diagnostic code. Pack entry
filtering and effective settings value validation SHALL remain the adapters' responsibility.

#### Scenario: Invalid referenced file

- **WHEN** a referenced JSON file is missing, malformed or incompatible with its record format
- **THEN** assembly refuses the bundle with the source file and manifest field attributed

### Requirement: Explicit allowed settings defaults

The assembler SHALL retain only explicitly present settings defaults and refuse disallowed settings,
duplicate settings and values of the wrong JSON type.

#### Scenario: Unsafe settings defaults

- **WHEN** defaults contain an unapproved setting, duplicate setting or incorrect JSON value type
- **THEN** assembly refuses the bundle with the settings file and offending setting attributed

### Requirement: Reserved built-in name resolution

Name resolution SHALL use ordinal lower-case bundle ids and only the embedded index or the supplied
user-data directory's `profiles/<id>` directory. Reserved built-in ids SHALL always resolve through the
embedded index, regardless of installed contents.

#### Scenario: Installed bundle shadows a built-in name

- **WHEN** an installed folder uses `vb6`, `vbaode` or `vba`, even with an invalid manifest
- **THEN** name resolution returns the embedded declaration without reading that folder
- **AND** explicit directory loading can select the folder's own valid declaration

### Requirement: Installed name resolution

Other names SHALL load their exact installed folder with folder/id validation and no fallback. Invalid
names, including `none`, SHALL be refused; safe-mode selection remains CLI work.

#### Scenario: Broken installed profile

- **WHEN** an installed name selects an invalid manifest
- **THEN** resolution refuses the bundle without fallback

### Requirement: Explicit directory selection

Explicit directory loading SHALL select that directory even when its manifest id collides with a
built-in; its source SHALL remain the absolute manifest path.

#### Scenario: Directory has a built-in id

- **WHEN** an explicit directory declares `vb6`
- **THEN** loading returns that directory's valid declaration

## Out of Scope

Effective pack entry and settings value validation, attributed localized presentation,
pack/default adapters, atomic payload loading, CLI selection and application remain pending in #2621 and its companion
split-outs. The directory-manifest preflight alone provides no protection against concurrent filesystem
replacement; the standalone byte reader does not yet assemble atomic snapshots.
