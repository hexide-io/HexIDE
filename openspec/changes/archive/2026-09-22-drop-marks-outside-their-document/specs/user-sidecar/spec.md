## ADDED Requirements

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
