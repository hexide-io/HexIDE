## ADDED Requirements

### Requirement: A sidecar written before lines counted from the top of the file SHALL be carried forward
A sidecar recorded before the code window held each file's header SHALL be migrated when its project is opened,
so that every breakpoint and bookmark in it stays on the line it was set on. Each entry SHALL be re-keyed to
the document it names within the project, and each line moved down by the number of lines the code window now
shows above the line that used to be its first. An entry whose document's shift cannot be determined SHALL be
kept unchanged rather than dropped or moved, and SHALL be migrated on a later opening once it can be. Content
the migration does not understand SHALL be kept. The sidecar SHALL record which format it is written in, and
that record SHALL be read. A migrated sidecar SHALL be rewritten only when migration changed something, and
never after a read that failed.

Lines used to be counted from the first line the code window showed; they are now counted from the top of the
file. A sidecar left as it was would put every mark on a different statement, or inside the header. The shift
is not the same for every kind, because the code window did not show the same thing for every kind: for a
form it already began at that file's leading attribute lines, so shifting by the whole header would overshoot
by their number. It is whatever is now shown above the old first line.

A shift cannot be determined when the project has no such document, or when the document's file is missing or
unreadable — and a module whose file is missing is still one of the project's documents, so the rule turns on
whether the shift is known rather than on whether the document exists. Guessing would misplace a mark, and
dropping the entry would lose it; keeping it untouched does neither, and a later opening can still migrate
it.

An earlier version of the IDE cannot be changed. Given a migrated sidecar it will not find its marks, and a
version predating the fix for the unrecognised-content defect (hexide-io/HexIDE#466) will remove them when it
next saves — that requirement being broken by builds that cannot be changed, rather than behaviour this
capability permits. It is recorded as a known limit, and it is the lesser harm: a migrated file that reused
the old keys would instead have the earlier version show every mark on the wrong line, silently.

Entries SHALL be keyed within a sidecar by the document's name. The sidecar belongs to one project already, so
the project needs no naming inside it.

#### Scenario: Opening a project with an earlier sidecar
- **GIVEN** a sidecar with a breakpoint on the third line of a class's code, recorded before this change
- **WHEN** the project is opened
- **THEN** the breakpoint is shown on that same statement, now numbered from the top of the file

#### Scenario: An entry for a file that is missing
- **GIVEN** a sidecar entry for a module whose file cannot be found
- **WHEN** the project is opened and later saved
- **THEN** the entry is still in the sidecar, unchanged

#### Scenario: Opening an already migrated sidecar again
- **WHEN** a migrated sidecar is read again
- **THEN** nothing in it moves
