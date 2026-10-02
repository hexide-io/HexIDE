## MODIFIED Requirements

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
