# external-project-container Specification

## Purpose
Read and write supported external backend project containers through streams. The IDE's project
open and save workflows are outside this capability.

## Requirements

### Requirement: Read a supported container without side effects

The reader MUST return a detached immutable tree for a supported version 1 container, preserving entry
names, order, empty directories, opaque file bytes, revisions, revision trailers, flags and categories.
It MUST interpret the root field as a version and other entry fields as kinds. It MUST NOT write files,
extract members, start a backend or close the caller's stream, whether reading succeeds or fails.

#### Scenario: Read a project or package
- **WHEN** a caller supplies a valid supported container stream
- **THEN** the reader returns its complete tree, reading from the current position to EOF
- **AND** unknown non-structural metadata and duplicate names remain available without normalization
- **AND** the caller may close or modify its input after the read without changing the returned snapshot

### Requirement: Refuse unsupported or malformed input with context

The reader MUST refuse invalid magic, unsupported versions, unknown entry kinds, invalid UTF-8 names,
truncated fields and unexplained trailing bytes. Format errors MUST identify an offset relative to the
read's starting position and the containing entry path known when the error occurred.

#### Scenario: An entry's declared content does not fit
- **WHEN** the declared byte count exceeds the remaining container data
- **THEN** reading fails with a format error before allocating that content
- **AND** no partial snapshot is returned

### Requirement: Bound parsing work and support cancellation

The reader MUST enforce caller-configurable input-byte, entry-count, nesting-depth and name-byte limits
before allocating based on untrusted counts. It MUST check cancellation between input reads and parsed
entries and during revision trailers. Invalid policy values MUST be refused before input is read.

#### Scenario: Input exceeds a policy limit
- **WHEN** a container exceeds a configured resource limit
- **THEN** reading fails with an explanatory format error and returns no snapshot

#### Scenario: A caller cancels
- **WHEN** cancellation is observed before or between read/parse steps
- **THEN** reading ends with a cancellation exception and leaves the caller's stream open

### Requirement: Write a supported container losslessly

The writer MUST emit a version 1 container from its immutable tree, preserving entry order, names,
opaque content and metadata. It MUST write from the caller's stream position, support non-seekable
output and leave the stream open. It MUST validate the entire tree against the reader's format and
the supplied or default reader limits before emitting any bytes.

#### Scenario: Write an unchanged project or package
- **WHEN** the caller writes a snapshot returned by the reader
- **THEN** the container bytes match the input byte for byte

#### Scenario: Replace one file's content
- **WHEN** the caller writes a snapshot with only one file's content replaced
- **THEN** only that file's content and its encoded content length can change

#### Scenario: Refuse a snapshot before writing
- **WHEN** the snapshot has an unsupported version or entry type, an uninitialised array,
  an invalid UTF-16 name, or exceeds a reader limit
- **THEN** a format error identifies the entry path
- **AND** the writer leaves the output open and untouched
