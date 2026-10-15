## MODIFIED Requirements

### Requirement: A mark on a line its document does not have SHALL be dropped when the sidecar is read
When the sidecar is applied to a project, a bookmark or breakpoint on a line its document does not have SHALL
be dropped, the drop SHALL be logged with the document and the lines, and the sidecar SHALL next be written
without it. A bookmark or breakpoint on a read-only line of its document — a line of the file's header, or an
`Attribute` line describing a member — SHALL be dropped, logged and left out of the next write in the same way.

Such a mark is drawn nowhere and never hit, so keeping it helps nobody. Yet it is reported by every query and
written back on every save. It arises from a document shortened outside the IDE, from a hand-edited sidecar,
or from a version that did not check lines before storing them. Lines are counted as the editor numbers them:
bookmarks from 0 and breakpoints from 1, from the top of the file, its header included. A mark in a sidecar
written before lines were counted that way is checked in the numbering it was written in, over the document's
code, before it is moved: a line before the first would otherwise be moved into the header, and judged there
as a line the document has. A mark is dropped rather than moved, because nothing records which line it was
meant for.

A read-only line never runs, so a breakpoint there is never hit, and a bookmark there marks a line the IDE
writes rather than the developer. Nothing in the IDE can set one there, so one arrives only from a sidecar: from
a version that did not refuse such lines, or as an earlier sidecar's mark moved onto one, because the code window
used to show a form's leading `Attribute` lines, and every `Attribute` line describing a member, as code that
could be marked.

#### Scenario: A sidecar naming lines past the end of its document
- **WHEN** a project is opened whose sidecar holds a breakpoint on line 99 of a two-line module
- **THEN** the module has no breakpoint on line 99, the log says it was dropped, and the next save does not write it

#### Scenario: Marks inside the document are kept
- **WHEN** the same sidecar also holds a breakpoint on line 2
- **THEN** that breakpoint is restored

#### Scenario: An earlier sidecar's mark that lands on a read-only line
- **WHEN** a project is opened whose sidecar, written before lines counted from the top of the file, holds a
  breakpoint on a form's `Attribute VB_Name` line, which the code window used to show as its first line
- **THEN** the form has no breakpoint there, the log says it was dropped, and the next save does not write it

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

Marks in the earlier format SHALL be recognised by where the sidecar records them, and SHALL be migrated whatever
format the sidecar says it is in. A version that keeps content it does not understand, and has no migration,
keeps a migrated sidecar's format and new marks and writes its own beside them, in the earlier format; read by
the format alone, those would be taken as already migrated, and every one would be shown too high by the header's
length.

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

#### Scenario: Earlier marks written into a migrated sidecar
- **GIVEN** a migrated sidecar into which an earlier version has since written marks of its own
- **WHEN** the project is opened
- **THEN** the marks of both versions are shown, each on the statement it was set on
