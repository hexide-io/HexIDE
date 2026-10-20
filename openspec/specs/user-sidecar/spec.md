# user-sidecar Specification

## Purpose
Define where per-user, per-project state is kept: the things one developer wants remembered about a project
that nobody else should inherit.

Bookmarks and breakpoints are the clear cases. They belong to a project — they mean nothing without the file
they point into — but they belong to one developer working on it. Somewhere between "settings" and "the
project" there is a third thing, and this is where it goes.

## Requirements

### Requirement: Per-user project state SHALL live beside the project, not inside it
State that is specific to one developer's work on a project SHALL be stored in a separate file alongside the
project file, and SHALL NOT be written into the project file. Every change to that state SHALL reach the file,
including a change made just before the project or the IDE is closed, however it is closed, and changes made in
several open projects at once. A change that cannot be written when it is made SHALL be written at the next
opportunity, and at the latest when its project is closed.

The project file is shared and versioned. Putting personal state in it means one developer's breakpoints
arrive in everyone else's checkout and show up in every diff — noise that trains people to stop reading
changes to that file. Keeping it beside the project rather than in a global settings store is what keeps it
attached to the thing it describes: bookmarks for a project the developer has not opened in a year should
still be there when they return.

A change that never reaches the file is lost without anyone seeing it go. Nothing prompts for it on closing,
because personal state is not part of what makes a project need saving, so the loss surfaces only when the
mark is missing on the next opening.

#### Scenario: Setting personal state
- **WHEN** a developer sets a bookmark or a breakpoint
- **THEN** it is recorded in the sidecar and the project file is unchanged

#### Scenario: Sharing a project
- **WHEN** a project is committed and checked out by someone else
- **THEN** they get the project without the first developer's personal state

#### Scenario: Closing a project straight after setting a mark
- **WHEN** a developer sets a bookmark and at once closes the project, or the IDE
- **THEN** the bookmark is there when the project is next opened

#### Scenario: Two projects open at once
- **WHEN** a developer sets a bookmark in one project of a group and straight afterwards a breakpoint in another,
  including one of the same name
- **THEN** each project's sidecar records its own

#### Scenario: A sidecar that cannot be written for a moment
- **WHEN** another program holds the sidecar as a change is written, and the project is closed afterwards
- **THEN** the change is in the sidecar when the project is next opened

### Requirement: The sidecar SHALL be named predictably from the project
The sidecar SHALL be named from the project file's name with a distinct extension, in the same directory.

Predictability is what makes it possible to ignore it in version control with one pattern, and to find it
without the IDE. Deriving it from the project name rather than an identifier also means moving or renaming a
project keeps the relationship visible, and a stray sidecar is self-explanatory rather than an orphan with
an opaque name.

#### Scenario: Locating the sidecar
- **WHEN** a project is opened
- **THEN** its sidecar is the file beside it named from the project's own name

### Requirement: The sidecar SHALL be optional and its absence SHALL be normal
A project without a sidecar SHALL open normally, and the sidecar SHALL be created only when there is state
to record. Saving a project that has nothing to record SHALL NOT create one. Content a sidecar carries that this
version does not understand counts as state to record. A sidecar that exists SHALL NOT be deleted when the last
of its state is removed; it SHALL be rewritten without it.

A fresh checkout has no sidecar, which is the common case rather than an error — treating it as one would
mean every clone starts with a warning. Not creating the file until there is something to put in it also
keeps projects clean for developers who never set a bookmark.

Once a sidecar exists it is kept. It may hold content this version does not understand, which deleting it would
lose, and it is a file the developer may have chosen to commit.

#### Scenario: Opening a project with no sidecar
- **WHEN** a project has no sidecar
- **THEN** it opens with no personal state and nothing is reported

#### Scenario: Deleting the sidecar
- **WHEN** the sidecar is deleted
- **THEN** the project still opens, having lost only the personal state

#### Scenario: Saving a project with nothing to record
- **WHEN** a project with no bookmarks, no breakpoints and no sidecar is saved
- **THEN** no sidecar is created

#### Scenario: Removing the last mark
- **WHEN** the last bookmark or breakpoint a sidecar records is removed
- **THEN** the sidecar remains, without it, and with everything else it held

### Requirement: Unrecognised content SHALL survive a round-trip
Content in the sidecar that this version does not understand SHALL be preserved when it is rewritten: the
content the sidecar holds at the time it is rewritten, so that what another version wrote after this one read it
is kept as well, and what was deleted with the file is not restored. The format the sidecar records SHALL NOT be
lowered when this version rewrites it. A project saved elsewhere SHALL take its sidecar's content with it, and a
sidecar already at the new place SHALL be replaced, as the project file beside it is. A sidecar that cannot be
read, or whose state cannot be applied, SHALL NOT be rewritten while its project is open, and the log SHALL say
so.

Two versions of the IDE will read the same sidecar — a developer moving between machines, or a team not all
on the same build. A newer version recording something the older one has no concept of should not have it
silently deleted by the older one, because the loss is invisible and only noticed later.

The format is kept for the same reason: the content is still the newer version's, and labelling it with the
older one's format would have the newer version read it as something else. And a sidecar that cannot be read is
content this version does not understand in its entirety, so rewriting it would delete all of it. Its marks are
not saved until it is repaired or removed and the project opened again, which the log says.

#### Scenario: A sidecar written by a newer version
- **WHEN** a sidecar containing unrecognised content is read and later rewritten
- **THEN** that content is still present afterwards, and so is the format the sidecar records

#### Scenario: A sidecar changed by another version while the project is open
- **WHEN** another version adds content to the sidecar while this version has the project open, and this version
  then rewrites it
- **THEN** the content the other version added is still present

#### Scenario: A sidecar deleted while the project is open
- **WHEN** a developer deletes the sidecar while its project is open, and then sets a bookmark
- **THEN** the sidecar written holds the project's marks and nothing else the deleted one held

#### Scenario: A project saved elsewhere
- **WHEN** a project whose sidecar holds content this version does not understand is saved to another folder
- **THEN** the sidecar beside the new project file holds that content, even when the project has no marks

#### Scenario: A sidecar that cannot be read
- **WHEN** a project is opened whose sidecar cannot be read, and a developer then sets a bookmark
- **THEN** the sidecar is left exactly as it was, and the log says it was not rewritten

### Requirement: A mark on a line its document does not have SHALL be dropped when the sidecar is read
When the sidecar is applied to a project, a bookmark or breakpoint on a line its document does not have SHALL
be dropped, the drop SHALL be logged with the document and the lines, and the sidecar SHALL next be written
without it. A bookmark or breakpoint on a read-only line of its document — a line of the file's header, or an
`Attribute` line describing a member — SHALL be dropped, logged and left out of the next write in the same way.

Such a mark is drawn nowhere and never hit, so keeping it helps nobody. Yet it is reported by every query and
written back on every save. It arises from a document shortened outside the IDE, from a hand-edited sidecar,
or from a version that did not check lines before storing them. Lines are counted as the stores hold them:
bookmarks from 0 and breakpoints from 1 (the editor and the tools show both from 1), from the top of the file, its header included. A mark in a sidecar
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
