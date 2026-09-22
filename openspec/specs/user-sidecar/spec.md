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
project file, and SHALL NOT be written into the project file.

The project file is shared and versioned. Putting personal state in it means one developer's breakpoints
arrive in everyone else's checkout and show up in every diff — noise that trains people to stop reading
changes to that file. Keeping it beside the project rather than in a global settings store is what keeps it
attached to the thing it describes: bookmarks for a project the developer has not opened in a year should
still be there when they return.

#### Scenario: Setting personal state
- **WHEN** a developer sets a bookmark or a breakpoint
- **THEN** it is recorded in the sidecar and the project file is unchanged

#### Scenario: Sharing a project
- **WHEN** a project is committed and checked out by someone else
- **THEN** they get the project without the first developer's personal state

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
to record.

A fresh checkout has no sidecar, which is the common case rather than an error — treating it as one would
mean every clone starts with a warning. Not creating the file until there is something to put in it also
keeps projects clean for developers who never set a bookmark.

#### Scenario: Opening a project with no sidecar
- **WHEN** a project has no sidecar
- **THEN** it opens with no personal state and nothing is reported

#### Scenario: Deleting the sidecar
- **WHEN** the sidecar is deleted
- **THEN** the project still opens, having lost only the personal state

### Requirement: Unrecognised content SHALL survive a round-trip
Content in the sidecar that this version does not understand SHALL be preserved when it is rewritten.

Two versions of the IDE will read the same sidecar — a developer moving between machines, or a team not all
on the same build. A newer version recording something the older one has no concept of should not have it
silently deleted by the older one, because the loss is invisible and only noticed later.

#### Scenario: A sidecar written by a newer version
- **WHEN** a sidecar containing unrecognised content is read and later rewritten
- **THEN** that content is still present afterwards

### Requirement: A mark on a line its document does not have SHALL be dropped when the sidecar is read
When the sidecar is applied to a project, a bookmark or breakpoint on a line its document does not have SHALL
be dropped, the drop SHALL be logged with the document and the lines, and the sidecar SHALL next be written
without it.

Such a mark is drawn nowhere and never hit, so keeping it helps nobody. Yet it is reported by every query and
written back on every save. It arises from a document shortened outside the IDE, from a hand-edited sidecar,
or from a version that did not check lines before storing them. Lines are counted as the editor numbers them:
bookmarks from 0 and breakpoints from 1, in the code without its Attribute header. A mark is dropped rather
than moved, because nothing records which line it was meant for.

#### Scenario: A sidecar naming lines past the end of its document
- **WHEN** a project is opened whose sidecar holds a breakpoint on line 99 of a two-line module
- **THEN** the module has no breakpoint on line 99, the log says it was dropped, and the next save does not write it

#### Scenario: Marks inside the document are kept
- **WHEN** the same sidecar also holds a breakpoint on line 2
- **THEN** that breakpoint is restored
