# bookmarks Specification

## Purpose
Define line bookmarks in the code editor: setting them, moving between them, and how long they last.

Bookmarks are how a developer holds a place while working somewhere else — the call site they need to come
back to, the three procedures involved in one change. In a language where a module can run to thousands of
lines and there is no cross-file navigation to lean on, that is more load-bearing than it sounds.

## Requirements
### Requirement: A bookmark SHALL be settable on any line and visible in the margin
The developer SHALL be able to toggle a bookmark on the current line, and a bookmarked line SHALL be marked
in the editor margin. A bookmark and a breakpoint on the same line SHALL both remain visible.

A bookmark that is not visible is just a hidden cursor position — the mark is the feature. Breakpoints share
the same margin and the same line can carry both, so neither may hide the other: a developer who cannot see
that a line is bookmarked because it also has a breakpoint has lost the bookmark.

#### Scenario: Toggling a bookmark
- **WHEN** the developer toggles a bookmark on a line
- **THEN** the line is marked in the margin, and toggling again removes it

#### Scenario: A line with both a bookmark and a breakpoint
- **WHEN** a line carries both
- **THEN** both are distinguishable in the margin

### Requirement: Navigation SHALL move between bookmarks and wrap around
The developer SHALL be able to move to the next and previous bookmark in the document, and navigation SHALL
wrap from the last to the first and from the first to the last.

Wrapping is what makes bookmarks usable as a set rather than a list: the developer cycles between the two or
three places they are working on by pressing the same key, without tracking where they are in the sequence
or which direction to go.

#### Scenario: Moving past the last bookmark
- **WHEN** the developer moves to the next bookmark from the last one
- **THEN** the caret moves to the first bookmark in the document

#### Scenario: No bookmarks set
- **WHEN** the developer navigates with no bookmarks in the document
- **THEN** nothing happens and no error is raised

### Requirement: Bookmarks SHALL be per-document and clearable in one action
Bookmarks SHALL belong to the document they were set in, and the developer SHALL be able to clear all of a
document's bookmarks at once.

Navigation that crossed documents would open files the developer was not working in, which is a surprise
rather than a convenience. Clearing individually is fine for two bookmarks and tedious for a dozen — and a
dozen is what accumulates during a long session, which is exactly when the developer wants a clean start.

#### Scenario: Bookmarks in another document
- **WHEN** bookmarks exist in more than one document
- **THEN** navigation moves only within the document being edited

#### Scenario: Clearing
- **WHEN** the developer clears all bookmarks
- **THEN** the current document's bookmarks are removed and other documents are unaffected

### Requirement: Bookmarks SHALL survive closing the project
Bookmarks SHALL be stored per user alongside the project and restored when it is reopened, and SHALL NOT be
written into the source files themselves.

This is a deliberate divergence: VB6 discarded bookmarks when the IDE closed, which made them useless for
anything spanning more than one sitting — and the work they mark usually does. Keeping them is the
improvement; keeping them *out of the source files* is what makes it safe, since a bookmark is one
developer's working state and has no business appearing in a colleague's diff.

#### Scenario: Reopening a project
- **WHEN** a project with bookmarks is closed and reopened
- **THEN** the bookmarks are still set on the same lines

#### Scenario: The effect on source files
- **WHEN** bookmarks are set and the project is saved
- **THEN** no source file changes as a result
