## ADDED Requirements

### Requirement: A carried file SHALL round-trip without becoming source
The deserializer SHALL recognise the project-file key for a carried file, and SHALL place such an entry in a
collection separate from the compiled modules. A carried file SHALL NOT appear among the modules under any
circumstance.

This is a structural guarantee rather than a defensive one. Every operation that could damage a non-code
file — execution, extension-based rename on save, the module header writer — iterates the module collection.
Absence from that collection is what makes the damage unreachable; a guard at each site is something a later
change forgets to add.

Because the key is now recognised rather than falling through as an unknown line, it SHALL be counted on read
and re-emitted through the same counter on write. A key promoted from unknown to known that is not counted on
both sides shifts the position of every unknown line after it.

#### Scenario: A project carrying non-compiled files is re-saved untouched
- **WHEN** a project holding carried files is read and written back with no change
- **THEN** the output is byte-identical to the input, including any unknown lines that followed a carried entry

#### Scenario: A carried entry names no member name
- **WHEN** an entry uses the key that carries a path but no name field
- **THEN** the member's name is derived from the filename

### Requirement: A non-source file on a code line SHALL be reclassified, and its line preserved
Where an entry on a module or class line names a file that is not VB6 source, the deserializer SHALL treat it
as a carried file, and SHALL preserve that entry's original line to be re-emitted verbatim.

Both halves are required and they answer different risks. Reclassifying prevents the file being treated as
source and rewritten with a module header on the next save. Preserving the line prevents the opposite fault:
reclassification is an inference about intent, and silently editing a developer's project file on the
strength of an inference is a larger liberty than declining to. The line SHALL change only when the project's
membership actually changes.

A file with no extension SHALL NOT be reclassified on read. The project already claims it is source, and
reclassifying something that cannot be classified is a guess stacked on a guess.

#### Scenario: A document on a module line
- **WHEN** a module or class entry names a file that is not VB6 source
- **THEN** it becomes a carried file, and re-saving emits its original line unchanged

#### Scenario: Source on a module line
- **WHEN** a module or class entry names VB6 source, or a file with no extension
- **THEN** it remains a module

#### Scenario: A file the developer adds
- **WHEN** a carried file joins the project through the IDE rather than by being read
- **THEN** it is written using the project file's own key for a carried file

### Requirement: A path inside a project file SHALL be a Windows path on every host
Paths held in a project file SHALL be read and written as backslash-separated regardless of the host the IDE
is running on. The serializer SHALL emit backslashes for every member path; the deserializer SHALL preserve
the value verbatim, and SHALL derive a member's name from it using backslash and forward slash as separators.
Conversion to the host's separator SHALL happen only where a path is resolved against the filesystem, and
SHALL NOT affect what is written back.

A project file is a Windows-native format. The host path API answers about the *host* filesystem, so it is
the wrong tool for these strings: where a backslash is an ordinary filename character, deriving a name yields
the whole path and a carried file is named after its own directory, while relativising on write yields
forward slashes that go into a file which must contain backslashes. Both faults are silent — each produces a
value that still looks like a path — and neither is reachable on a Windows development machine.

Tests covering this SHALL express their expectations about project-file content as literal backslashed
strings, never by composing them with a host path API. An expectation built from the host follows whichever
machine runs it, and so certifies the defect on the platform where it matters rather than catching it.

#### Scenario: A member in a subdirectory is written
- **WHEN** a project holding a member in a subdirectory is saved
- **THEN** the emitted path uses backslashes, whatever host performed the save

#### Scenario: A member in a subdirectory is read back
- **WHEN** a project file naming a member in a subdirectory is read
- **THEN** the stored relative path is the backslashed value exactly as it appeared in the file
