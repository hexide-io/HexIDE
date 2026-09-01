## MODIFIED Requirements

### Requirement: A save SHALL NOT be able to destroy the previous version
Writing a file SHALL be arranged so that the previous content survives intact until the new content is
complete. Where a designer file has a binary companion, the two SHALL be treated as one artifact: a save
SHALL write both halves or neither, and SHALL decide which before either is written.

Interrupted writes happen — a crash, a full disk, a killed process — and the file being written at that
moment is the one the developer cares about most. Writing in place means the interruption lands in the
middle of their source file; completing the write elsewhere first means the worst outcome is a leftover
temporary file next to an intact original.

A companion is not a separate file that happens to sit alongside. Each record's offset is simply where it
landed, and the designer file cites those offsets, so the citations are meaningful only against the exact
companion produced with them. Writing one half without the other leaves citations addressing a partition
that no longer exists — and because a file's faithfulness is re-derived from its own citations, the result
reopens as reproducible. The markers that would have flagged it are the ones that were overwritten.

Two halves cannot be committed to disk in one indivisible step, so the order SHALL be chosen for what an
interruption leaves behind. Writing the companion first leaves the previous designer file, whose citations
may overrun the new companion and be refused on the next load. Writing the text first leaves citations that
resolve inside the larger stale companion — to the wrong records, with nothing to indicate it. An
interrupted save SHALL fail towards the outcome the developer is told about.

#### Scenario: An interrupted save
- **WHEN** a save is interrupted before it completes
- **THEN** the file on disk is still the previous version, unmodified

#### Scenario: A file the IDE will not write
- **WHEN** a designer file cannot be written faithfully
- **THEN** neither it nor its companion is modified, and the developer is told which file and why

#### Scenario: An interrupted save of a designer file and its companion
- **WHEN** a save is interrupted between the two halves
- **THEN** what remains on disk is a pair whose mismatch is detected on the next load, rather than one that
  reads as correct

#### Scenario: A companion the designer file does not reference
- **WHEN** a save produces no binary content for a designer file that cited none
- **THEN** any companion beside it is left alone, because it holds content this file never referenced

#### Scenario: One record referenced by two properties
- **WHEN** more than one property cites the same companion content
- **THEN** it is written once, and both citations address the copy that was written
