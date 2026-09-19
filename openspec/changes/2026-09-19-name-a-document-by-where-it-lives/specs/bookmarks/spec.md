## REMOVED Requirements

### Requirement: A bookmark SHALL be settable on any line and visible in the margin
**Reason**: The code window now holds each file's header and member attribute lines, read-only. "Any line" would
include lines the developer cannot edit and, folded, cannot see; a mark there lands on a line other than the
one clicked. The requirement is restated below, limited to editable lines, with its other content unchanged.
**Migration**: None for developers. Bookmarks set before this change were counted from the first line after the
header, and the sidecar migration moves each one down by that document's header length, so every bookmark
stays on the line it was set on and none lands in the header.

## ADDED Requirements

### Requirement: A bookmark SHALL be settable on any editable line and visible in the margin
The developer SHALL be able to toggle a bookmark on the current line, provided it is not in a read-only region
of the code window, and a bookmarked line SHALL be marked in the editor margin. A bookmark and a breakpoint on
the same line SHALL both remain visible.

A bookmark that is not visible is just a hidden cursor position — the mark is the feature. Breakpoints share
the same margin and the same line can carry both, so neither may hide the other: a developer who cannot see
that a line is bookmarked because it also has a breakpoint has lost the bookmark.

A read-only region is excluded because, folded, it is one visible line standing for many: a mark set by
clicking it would belong to whichever of those lines the click happened to map to.

#### Scenario: Toggling a bookmark
- **WHEN** the developer toggles a bookmark on a line
- **THEN** the line is marked in the margin, and toggling again removes it

#### Scenario: A line with both a bookmark and a breakpoint
- **WHEN** a line carries both
- **THEN** both are distinguishable in the margin

#### Scenario: Toggling a bookmark on a folded header
- **WHEN** the developer toggles a bookmark on a form's folded header
- **THEN** no bookmark is set
