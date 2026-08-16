# form-designer Specification

## Purpose
Define how controls are selected, arranged and edited on the design surface — the multi-select model, the
alignment and spacing commands that act on it, locking, and clipboard operations.

This is the surface a VB6 developer spends most of their time on, and it is judged by muscle memory rather
than by feature list. Laying out a form without alignment tools means dragging controls a pixel at a time,
which is the difference between a designer someone can work in and one they try once. Almost everything here
depends on being able to select more than one control, which is why the selection model is specified first
and everything else is expressed in terms of it.

Undoing these operations is a separate capability (`form-designer-undo-redo`); this one covers what the
operations do.

## Requirements
### Requirement: The designer SHALL support selecting several controls with one as the reference
The designer SHALL allow more than one control to be selected at once, and SHALL designate exactly one of
them as the reference for operations that need a target to align or size against. The reference SHALL be
visually distinguishable from the rest of the selection.

Every alignment and sizing command answers "align to what?", and the answer has to be visible before the
command runs — otherwise the developer finds out which control was the reference by looking at the result.
Making it the same control whose properties are being shown means one visual cue answers both questions.

#### Scenario: Selecting a second control
- **WHEN** the developer adds a second control to the selection
- **THEN** both show selection handles
- **AND** the most recently clicked is the reference and is drawn distinctly

#### Scenario: Properties while several are selected
- **WHEN** several controls are selected
- **THEN** the properties window shows the reference control's properties

### Requirement: Controls SHALL be selectable by adding to the selection or by dragging a region
The designer SHALL support extending the selection with a modifier, and selecting by dragging a region over
the canvas. A dragged region SHALL select every control it intersects, not only those it fully encloses.

Intersection rather than containment is what VB6 did, and it is the more useful rule: selecting a row of
controls by dragging across them is a natural gesture, whereas requiring full enclosure means starting the
drag outside the outermost control every time.

#### Scenario: Dragging a region across several controls
- **WHEN** the developer drags a region that partially crosses several controls
- **THEN** all of the crossed controls are selected

#### Scenario: Dragging a region with the modifier held
- **WHEN** a region is dragged with the extend modifier held
- **THEN** the controls it crosses are added to the existing selection rather than replacing it

#### Scenario: Clicking empty canvas
- **WHEN** the developer clicks the canvas away from any control
- **THEN** the form itself becomes selected and no control remains selected

### Requirement: Dragging one selected control SHALL move the whole selection
Dragging any control that is part of the selection SHALL move every selected control by the same
displacement, and where snapping applies it SHALL be resolved once so that the relative positions within the
selection are preserved.

Snapping each control independently is the obvious implementation and it is wrong: controls that were
aligned before the drag come to rest on different grid points and the arrangement quietly deforms. Snapping
the drag once and applying the same displacement to everything keeps the group rigid, which is what the
developer is asking for by selecting it.

#### Scenario: Dragging a group
- **WHEN** the developer drags one control of a multiple selection
- **THEN** every selected control moves by the same displacement

#### Scenario: Dragging a group with snapping active
- **WHEN** a group is dragged and positions snap to the grid
- **THEN** the relative offsets within the group are unchanged after the drag

### Requirement: Alignment and sizing commands SHALL act relative to the reference control
The designer SHALL provide commands aligning selected controls by left, right, top, bottom, horizontal
centre and vertical centre, and commands making them the same width, the same height, or both, each taking
its target value from the reference control. These commands SHALL be unavailable when fewer than two
controls are selected.

Taking the target from the reference is what makes the result predictable — the developer chooses the
control that is already correct and brings the others to it. Disabling below two selections is not merely
tidiness: a command that silently does nothing is indistinguishable from one that is broken.

#### Scenario: Aligning to the reference
- **WHEN** several controls are selected and an alignment command is invoked
- **THEN** each moves to match the reference control on that axis, and the reference does not move

#### Scenario: Fewer than two controls selected
- **WHEN** one control or none is selected
- **THEN** the alignment and sizing commands are unavailable

### Requirement: The designer SHALL provide spacing commands across a selection
The designer SHALL provide commands to equalise, increase, decrease and remove the spacing between selected
controls, on both the horizontal and vertical axes. Equalising SHALL preserve the overall span occupied by
the selection.

Even spacing is tedious to achieve by hand and trivially checkable by eye, so it is one of the first things
a developer notices the absence of. Preserving the span matters because the alternative — anchoring the
first control and pushing the rest outward — moves controls the developer had already positioned, often off
the form.

#### Scenario: Equalising spacing
- **WHEN** three or more controls are selected and spacing is equalised on an axis
- **THEN** the gaps between them become equal
- **AND** the outermost controls do not move

#### Scenario: Removing spacing
- **WHEN** spacing is removed on an axis
- **THEN** the controls are packed against each other from the first one

### Requirement: The designer SHALL align control geometry to the grid on request
The designer SHALL provide a command that rounds each selected control's position and size to the nearest
grid increment.

Controls acquire fractional positions — pasted, nudged, or arriving from a file another tool wrote — and
once off-grid they never quite line up with anything placed afterwards. A single command that re-seats a
selection on the grid is the cheap fix for a whole class of "almost right" layouts.

#### Scenario: A control at a fractional position
- **WHEN** a control not on a grid increment is aligned to the grid
- **THEN** its position and size round to the nearest increment

### Requirement: Controls SHALL be lockable against accidental movement, and the state SHALL persist
The designer SHALL provide a toggle that prevents controls being moved or resized on the canvas, and that
state SHALL be saved with the form and restored when it is reopened.

A finished layout is easy to disturb — one stray drag while reaching for a control's properties. Locking is
the VB6 answer, and it only works if it survives closing the form: a lock that resets on reload protects the
developer for exactly one session, which is not when the accident happens.

#### Scenario: Dragging while locked
- **WHEN** controls are locked and the developer drags one
- **THEN** it does not move or resize

#### Scenario: Reopening a form saved with controls locked
- **WHEN** a form saved with controls locked is reopened
- **THEN** the controls are still locked

### Requirement: Controls SHALL be copyable within the IDE, pasting offset and renamed
The designer SHALL support copying, cutting and pasting the selected controls, retaining their properties.
Pasted controls SHALL be offset from the originals and SHALL take the next available name in their series.
The clipboard SHALL be private to the IDE.

Pasting exactly on top of the original produces a control the developer cannot see and did not obviously
create — the offset is what makes the paste visible. Names have to be generated because two controls in one
container cannot share one. Keeping the clipboard private matches VB6 and sidesteps the question of what a
control means to any other application that might read the system clipboard.

#### Scenario: Copying and pasting a control
- **WHEN** a control is copied and pasted
- **THEN** a copy appears offset from the original, with its properties and a newly generated name

#### Scenario: Pasting twice
- **WHEN** the same copied control is pasted a second time
- **THEN** the second copy is offset again rather than landing on the first

#### Scenario: Cutting controls
- **WHEN** controls are cut and then pasted
- **THEN** they are removed from the form and reappear at the offset position
