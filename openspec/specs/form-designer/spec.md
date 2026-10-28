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

> **Known divergence.** "Copying a container SHALL copy what it holds" is not met for a control HexIDE does not
> model: a copy is pasted without it, since its name would repeat the original's, and a container cut into another
> form is pasted without it unless that form declares the libraries the first one did. Tracked in
> [#1458](https://github.com/hexide-io/HexIDE/issues/1458). Cut and paste within a form keeps it (#1453).

## Requirements
### Requirement: The designer SHALL draw a form's client area at the size it is saved and run with
The area a form's controls are designed in SHALL be its client area, `ClientWidth` by `ClientHeight`, the size its
window has at run time. The frame and caption bar SHALL be drawn outside it, and dragging the frame's handles SHALL
save the client size the drag leaves. Drawing the whole window at the client size put a margin around every running
form (#924).

#### Scenario: A form saved at 640 by 480 pixels
- **GIVEN** a form with `ClientWidth = 9600` and `ClientHeight = 7200`
- **WHEN** it is opened in the designer
- **THEN** the area its controls are placed in is 640 by 480 pixels, and the frame is drawn around it

#### Scenario: Resizing the form by its frame
- **WHEN** the frame's corner handle is dragged 40 pixels right and 20 down from a 640 by 480 client
- **THEN** the form is saved with a client of 680 by 500 pixels

### Requirement: The designer SHALL support selecting several controls with one as the reference
The designer SHALL allow more than one control to be selected at once, and SHALL designate exactly one of
them as the reference for operations that need a target to align or size against. The reference SHALL be
visually distinguishable from the rest of the selection.

Every alignment and sizing command answers "align to what?", and the answer has to be visible before the
command runs — otherwise the developer finds out which control was the reference by looking at the result.
Making it the same control whose properties are being shown means one visual cue answers both questions.
This is a HexIDE presentation choice: VB6 uses the last selected control with solid handles as the reference,
but its toolbar keeps showing the first selected and its Properties caption can change after reselection
(measured 2026-10-22, #928/#929).

#### Scenario: Selecting a second control
- **WHEN** the developer adds a second control to the selection
- **THEN** both show selection handles
- **AND** the most recently clicked is the reference and is drawn distinctly

#### Scenario: Properties while several are selected
- **WHEN** several controls are selected
- **THEN** the properties window shows the reference control's properties

### Requirement: Controls SHALL be selectable by adding to the selection or by dragging a region
The designer SHALL support extending the selection with a modifier, and selecting by dragging a region over
the canvas. A modifier click on a control in a different container from the reference SHALL be ignored,
leaving the selection and reference unchanged (VB6 SP6 measurement, #2181). A plain click SHALL select
that control alone. Controls sharing a container, including a Frame and a form-level control, SHALL
remain selectable together. A dragged region SHALL select every control it intersects, not only those it
fully encloses. The region SHALL span the whole form rather than being confined to one container, and this is a stated
divergence from the original product.

Intersection rather than containment is what VB6 did, and it is the more useful rule: selecting a row of
controls by dragging across them is a natural gesture, whereas requiring full enclosure means starting the
drag outside the outermost control every time.

VB6 confines the region to whichever container the drag began in, so it never returns a container together
with what that container holds. HexIDE's region does return such a selection, and the drag rule above is
what makes that harmless rather than a defect. Confining the region needs the IDE to decide which container
a point belongs to — the same judgement that moving a control between containers by dragging needs — and
both are deferred together rather than half-built.

#### Scenario: Modifier click across containers
- **WHEN** a control is selected and the developer Ctrl-clicks or Shift-clicks a control in another container
- **THEN** the selection and reference remain unchanged

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

#### Scenario: Centring where the exact position is half a twip
- **WHEN** a control is aligned by its centre, or centred in the form, and the exact position is half a twip
- **THEN** it goes where VB6 puts it, in whole twips, each size halved on its own and truncated: a 1215-twip control
  centred across a 4680-twip form goes to 2340 - 607 = 1733

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
- **THEN** the gaps between them become equal to the whole twip, as VB6 truncates them
- **AND** the outermost controls do not move, the last gap taking any twip left over

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

#### Scenario: Cutting a container that holds a control HexIDE does not model
- **WHEN** a container holding a control HexIDE does not model is cut and pasted on the same form
- **THEN** the pasted container holds that control, which is written back as it was read, with the companion records
  it cites

#### Scenario: A control's own properties HexIDE does not model
- **WHEN** a control with properties HexIDE does not model is copied or cut, and pasted
- **THEN** the pasted control has them, with the companion records they cite

#### Scenario: Pasting a control back into the container it was copied from
- **WHEN** a control copied out of a container is pasted while that container is still on the form
- **THEN** the copy is placed inside that container, offset from the original

#### Scenario: Pasting a control where its container is not present
- **WHEN** a control copied out of a container is pasted on a form that does not have that container
- **THEN** the copy is placed on the form at the position it occupied on screen, not at the raw stored number

### Requirement: A control HexIDE does not model SHALL be shown where VB6 shows it, and SHALL NOT be editable
The designer SHALL draw a box for each control it does not model, at the position and size VB6's IDE gives the box it
puts in that control's place, labelled with the control's class. The box SHALL NOT be selectable or movable, and the
control SHALL be written back once, from the block it was read as.

Unseen, it is edited around blindly. VB6's box is an empty PictureBox; labelling it is HexIDE's one divergence.

#### Scenario: A control recording only its extents
- **WHEN** a form holds an ImageList, which records `Left`, `Top`, `_ExtentX` and `_ExtentY` and no `Width` or `Height`
- **THEN** its box is at `Left` and `Top`, 1200 by 480 twips, as VB6 draws it, whatever the extents say

#### Scenario: A control inside a Frame
- **WHEN** a Frame the designer models holds a control it does not, and the Frame is moved, deleted or restored by undo
- **THEN** the box is drawn on the Frame, moves with it, goes with it, and comes back with it

#### Scenario: Pressing on the box
- **WHEN** the developer presses on the box
- **THEN** nothing is selected and no region drag begins


### Requirement: Export a picture property's payload
The Properties window SHALL offer Export picture on modeled Picture, Icon, MouseIcon and DragIcon properties
holding a nonempty, length-consistent VB6-framed StdPicture record. Export SHALL write only the image payload
chosen by a save picker, without changing the property, designer history, form or companion file. It SHALL NOT
export opaque OCX property bags, unframed bytes or malformed records. Cancellation SHALL write nothing, and a
write failure SHALL be reported to the user.

#### Scenario: Export a form's icon
- **GIVEN** a form's Icon holds a VB6-framed StdPicture record
- **WHEN** Export picture is chosen from its property row's context menu and a destination is selected
- **THEN** the destination contains the original image bytes without either framing layer
- **AND** the property record remains unchanged

#### Scenario: A picture belongs to an OCX property bag
- **GIVEN** an opaque OCX block contains an image
- **THEN** the Properties window offers no export for that property bag

### Requirement: A tool drag preview SHALL match the control placed on release
The designer SHALL use the same rectangle and container calculation for a tool drag's preview and placement.
The calculation SHALL snap the corners to the nearest lines of the grid measured from the client origin of
the innermost container under the press, hold the rectangle to that container, and include the extra pixel
past the far corner. With the grid off, the extra pixel SHALL still be included. Placement with an explicit
size (`add_control`) SHALL keep its own container hold and SHALL NOT add that pixel.

#### Scenario: Drawing inside a bordered PictureBox
- **WHEN** a tool drag begins inside a PictureBox nested in a Frame and moves beyond the PictureBox
- **THEN** the preview uses the PictureBox's grid and container limits
- **AND** releasing at the same point places a control with that rectangle in the PictureBox

#### Scenario: Drawing with the grid off
- **WHEN** a tool drag spans 45 by 17 pixels with snapping disabled
- **THEN** the preview and the placed rectangle both measure 46 by 18 pixels

### Requirement: The designer SHALL offer a read-only tab-order overlay
The designer SHALL offer a toggle, initially off, that draws each modelled control's existing `TabIndex` over its
canvas position. Controls without that property SHALL have no badge. A badge SHALL be crossed out with a diagonal slash when the control
has `TabStop` set to false, so the mark remains distinct from every digit’s strokes. Badges SHALL follow property and position changes and SHALL NOT intercept pointer input
or change the form's properties. This is a HexIDE presentation feature, not a VB6 designer gesture.

#### Scenario: Inspecting tab order
- **WHEN** the developer enables the tab-order toggle
- **THEN** tab-index badges appear over the controls, with non-tab-stops crossed out
- **AND** disabling the toggle hides the badges

### Requirement: A form SHALL be previewable without running its code
The form designer SHALL offer a Preview Form command that opens a live window using the runtime loader's
layout, menus and tab settings, without starting the project or loading an interpreter. Control interaction,
resizing and closing SHALL execute no VB event code and SHALL leave the designer's properties, selection,
modified state and undo history unchanged. A UserControl or PropertyPage designer SHALL not offer this
command. A form the runtime loader cannot create SHALL report its existing unsupported-control error.

#### Scenario: Check a form's layout and interaction
- **WHEN** Preview Form is invoked in a form designer
- **THEN** a live form window opens with the current design, including unsaved edits
- **AND** its controls accept interaction without executing the form's code or starting the project

#### Scenario: Close after editing the preview
- **WHEN** the developer edits a text box, resizes the preview and closes it
- **THEN** those edits are discarded and the designer remains unchanged
- **AND** no Load, Resize, Click, QueryUnload or other VB event procedure runs

### Requirement: ImageList designer icon
The designer SHALL show a modelled MSComctlLib.ImageList as a 38 × 38 box with a two-pixel raised edge.
The outer top/left SHALL use 3D light, inner top/left highlight, inner bottom/right shadow, and outer
bottom/right dark shadow, with the face brush inside. These roles SHALL follow the theme's system brushes.
HexIDE SHALL draw its own vector filmstrip glyph in a 32 × 32 area at (2, 2), rather than reproduce VB6's picture.
Runtime invisibility and the measured unsnapped placement SHALL remain unchanged.

#### Scenario: ImageList in the designer
- **WHEN** an ImageList is placed on a form
- **THEN** its raised box contains the original vector drawing rather than a text stand-in
- **AND** changing theme updates its brushes
