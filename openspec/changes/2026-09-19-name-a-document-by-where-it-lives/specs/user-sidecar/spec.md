## ADDED Requirements

### Requirement: A sidecar written before lines counted from the top of the file SHALL be carried forward
A sidecar recorded before the code window held each file's header SHALL be migrated when its project is opened,
so that every breakpoint and bookmark in it stays on the line it was set on. Each entry SHALL be re-keyed to
the document it names within the project, and each line moved down by that document's header length. An entry
naming no document the project has SHALL be kept unchanged rather than dropped or moved, and content the
migration does not understand SHALL be kept. The sidecar SHALL record which format it is written in, and
that record SHALL be read. A migrated sidecar SHALL be rewritten only when migration changed something, and
never after a read that failed.

Lines used to be counted from the first line after the header; they are now counted from the top of the file.
A sidecar left as it was would put every mark a header's length too high — on a different statement, or inside
the header. An entry for a file that is only temporarily missing cannot be measured, and guessing its offset
would misplace it where dropping it would lose it; keeping it untouched does neither.

An earlier version of the IDE cannot be changed. Given a migrated sidecar it will not find its marks, and may
remove them when it next saves. That is recorded as a known limit, and it is the lesser harm: a migrated file
that reused the old keys would instead have the earlier version show every mark on the wrong line.

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
