## MODIFIED Requirements

### Requirement: Dragging one selected control SHALL move the whole selection
Dragging any control that is part of the selection SHALL move every selected control by the same
displacement, and where snapping applies it SHALL be resolved once so that the relative positions within the
selection are preserved. Where the selection holds both a container and something that container holds, the
displacement SHALL be applied to the container only, so that what it holds moves exactly once.

Snapping each control independently is the obvious implementation and it is wrong: controls that were
aligned before the drag come to rest on different grid points and the arrangement quietly deforms. Snapping
the drag once and applying the same displacement to everything keeps the group rigid, which is what the
developer is asking for by selecting it.

The container rule is the same principle one level up. A container already carries what it holds, so
displacing both moves the contents twice — and because a selection that holds a container and its contents
is one the region gesture produces routinely, this is the common case rather than the exotic one.

Snapping is resolved in the space the control lives in. A container's origin is not aligned to the grid, so
snapping a contained control against the form's grid puts it off its own container's — and because each drag
snaps from wherever the last one left it, the control never settles.

#### Scenario: Dragging a group
- **WHEN** the developer drags one control of a multiple selection
- **THEN** every selected control moves by the same displacement

#### Scenario: Dragging a group with snapping active
- **WHEN** a group is dragged and positions snap to the grid
- **THEN** the relative offsets within the group are unchanged after the drag

#### Scenario: Dragging a selection that holds a container and its contents
- **WHEN** the selection holds a container and a control that container holds, and the group is dragged
- **THEN** the contained control ends up displaced once, at the same position within its container as before

#### Scenario: Dragging a contained control on its own
- **WHEN** the developer drags a control inside a container
- **THEN** it moves within that container, snapping to the grid as drawn inside the container

#### Scenario: Resizing a control inside a container positioned off-screen
- **WHEN** the developer resizes such a control by its leading edge
- **THEN** the resize is bounded by the container's own edge, and the designer does not fail

### Requirement: Controls SHALL be selectable by adding to the selection or by dragging a region
The designer SHALL support extending the selection with a modifier, and selecting by dragging a region over
the canvas. A dragged region SHALL select every control it intersects, not only those it fully encloses. The
region SHALL span the whole form rather than being confined to one container, and this is a stated
divergence from the original product.

Intersection rather than containment is what VB6 did, and it is the more useful rule: selecting a row of
controls by dragging across them is a natural gesture, whereas requiring full enclosure means starting the
drag outside the outermost control every time.

VB6 confines the region to whichever container the drag began in, so it never returns a container together
with what that container holds. HexIDE's region does return such a selection, and the drag rule above is
what makes that harmless rather than a defect. Confining the region needs the IDE to decide which container
a point belongs to — the same judgement that moving a control between containers by dragging needs — and
both are deferred together rather than half-built.

#### Scenario: Dragging a region across several controls
- **WHEN** the developer drags a region that partially crosses several controls
- **THEN** all of the crossed controls are selected

#### Scenario: Dragging a region with the modifier held
- **WHEN** a region is dragged with the extend modifier held
- **THEN** the controls it crosses are added to the existing selection rather than replacing it

#### Scenario: Clicking empty canvas
- **WHEN** the developer clicks the canvas away from any control
- **THEN** the form itself becomes selected and no control remains selected

#### Scenario: Dragging a region across a container
- **WHEN** a dragged region crosses a container and the controls it holds
- **THEN** both the container and those controls are selected

### Requirement: Controls SHALL be copyable within the IDE, pasting offset and renamed
The designer SHALL support copying, cutting and pasting the selected controls, retaining their properties.
Pasted controls SHALL be offset from the originals and SHALL take the next available name in their series.
The clipboard SHALL be private to the IDE. Copying a container SHALL copy what it holds, and pasting SHALL
place the copy where its position still means what it meant when it was copied.

Pasting exactly on top of the original produces a control the developer cannot see and did not obviously
create — the offset is what makes the paste visible. Names have to be generated because two controls in one
container cannot share one. Keeping the clipboard private matches VB6 and sidesteps the question of what a
control means to any other application that might read the system clipboard.

Copying is where a position can silently change meaning. A control copied out of a container carries a
position measured from that container; pasted onto the form, the same number means a distance from the form
and the copy lands somewhere the developer did not point at. So the clipboard remembers which container the
control came from: pasted back into it, the number still holds; pasted anywhere else, it is converted first.

#### Scenario: Copying and pasting a control
- **WHEN** a control is copied and pasted
- **THEN** a copy appears offset from the original, with its properties and a newly generated name

#### Scenario: Pasting twice
- **WHEN** the same copied control is pasted a second time
- **THEN** the second copy is offset again rather than landing on the first

#### Scenario: Cutting controls
- **WHEN** controls are cut and then pasted
- **THEN** they are removed from the form and reappear at the offset position

#### Scenario: Copying a container
- **WHEN** a container is copied and pasted
- **THEN** the copy holds copies of the contents, and changing one copy's contents does not change the other's

#### Scenario: Pasting a control back into the container it was copied from
- **WHEN** a control copied out of a container is pasted while that container is still on the form
- **THEN** the copy is placed inside that container, offset from the original

#### Scenario: Pasting a control where its container is not present
- **WHEN** a control copied out of a container is pasted on a form that does not have that container
- **THEN** the copy is placed on the form at the position it occupied on screen, not at the raw stored number
