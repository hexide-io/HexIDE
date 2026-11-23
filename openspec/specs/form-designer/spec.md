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

### Requirement: Changing a form's BorderStyle applies the measured designer consequences

A form's Properties window SHALL offer BorderStyle values 0 through 5, defaulting to 2 (Sizable).
Changing to a different style SHALL apply the table below as one undoable edit, leaving ControlBox
unchanged. Choosing the existing style SHALL preserve hand-set values and add no undo step.
A VB.Form root SHALL save MinButton, MaxButton and ShowInTaskbar only when False.

#### Scenario: A form changes to a different style

- **WHEN** a form's BorderStyle changes in the Properties window
- **THEN** these consequences apply, as [measured on VB6 SP6](../../../docs/vb6-fidelity-oracle/a-forms-window-styles-borderstyle-controlbox-minbutton-and-maxbutton.md) (#1596, oracle PR #5216):

| BorderStyle | MinButton | MaxButton | ShowInTaskbar |
|---|---|---|---|
| 0 None | unchanged | unchanged | False |
| 1 Fixed Single | False | False | True |
| 2 Sizable | True | True | True |
| 3 Fixed Dialog | False | False | False |
| 4 Fixed ToolWindow | False | False | False |
| 5 Sizable ToolWindow | False | False | False |

- **AND** one undo restores BorderStyle and all three consequences to their earlier values
- **AND** a save omits Sizable's BorderStyle line and writes other styles with their VB6 names in property order

#### Scenario: A form changes from Sizable to another style and back

- **WHEN** a Sizable form changes to each other style and then back to Sizable
- **THEN** each intermediate save writes only the False consequences from the table
- **AND** the save back at Sizable omits BorderStyle, MinButton, MaxButton and ShowInTaskbar
- **AND** undoing a change from Sizable to Fixed Dialog also saves none of the three boolean lines

#### Scenario: A form holds an explicit True

- **WHEN** MinButton, MaxButton or ShowInTaskbar is True, whether loaded from a file or set by hand
- **THEN** a save omits that property line

Client-origin movement is not specified here: the oracle measured only one starting position.

### Requirement: Windowless controls draw below windowed siblings regardless of file order

Within each container, the designer SHALL draw Label, Shape, Line and Image below windowed siblings,
regardless of their block order in the file. The existing first-block-on-top rule SHALL remain within
each plane. An unedited form SHALL remain clean and SHALL NOT be rewritten. A save after an edit SHALL
write windowed blocks before windowless blocks, preserving each plane's order and every property,
including TabIndex, as measured in [#1517](https://github.com/hexide-io/HexIDE/issues/1517) and
[oracle PR #4518](https://github.com/hexide-io/HexIDE/pull/4518).

#### Scenario: A Label precedes a button in a hand-edited form

- **WHEN** the form opens with a Label block before a CommandButton block in the same container
- **THEN** the Label draws underneath the button and the form has no unsaved changes
- **WHEN** the developer edits the code and saves
- **THEN** the button block precedes the Label block, with their property lines unchanged
- **AND** the same rule applies independently to nested containers

### Requirement: A control designer double-click writes its measured default event

The designer SHALL use the declared default event independently of event-list order, matching the
[VB6 SP6 measurements in #4411](https://github.com/hexide-io/HexIDE/issues/4411) from
[oracle PR #4460](https://github.com/hexide-io/HexIDE/pull/4460).

#### Scenario: Double-click a measured in-box control

- **WHEN** a control in this table is double-clicked
- **THEN** the code window opens its named handler, creating it if absent

| Controls | Event |
|---|---|
| PictureBox, Label, CommandButton, CheckBox, OptionButton, ListBox, Image | Click |
| TextBox, ComboBox, HScrollBar, VScrollBar | Change |
| Frame | DragDrop(Source As Control, X As Single, Y As Single) |
| Timer | Timer |
| Shape, Line | None: open code without creating a handler |

#### Scenario: An unmeasured control or control-array element

- **WHEN** a control outside the measured table is double-clicked
- **THEN** its first-event behavior is retained pending measurement
- **WHEN** a control-array element's handler is created
- **THEN** its existing `Index As Integer` argument is retained pending measurement
- **AND** runtime DragDrop delivery is unchanged

#### Scenario: Double-click a TextBox

- **WHEN** the user double-clicks Text1 in the designer
- **THEN** the code window opens Text1_Change, creating its stub if absent, even if KeyDown is listed first

#### Scenario: Double-click a Frame or Shape

- **WHEN** the user double-clicks Frame1
- **THEN** the code window creates or navigates to Frame1_DragDrop with the measured arguments
- **WHEN** the user double-clicks Shape1
- **THEN** the code window opens without creating an event handler

### Requirement: Refuse negative designer control sizes

The Properties window and `add_control`, `move_control` and `set_control_property` SHALL refuse a
negative Width or Height before changing any control, recording undo or saving. Zero and positive
sizes SHALL retain existing behaviour. Negative Left and Top SHALL remain allowed.

#### Scenario: Refuse a negative size atomically
- **WHEN** a designer edit supplies a negative Width or Height
- **THEN** the control collection, properties, undo history and saved file remain unchanged
- **AND** the Properties window shows a localized "Invalid property value" refusal
- **AND** an automation reply explains that VB6 cannot load a form holding a negative size, naming
  #589 where that was measured, and reports the control's unchanged state, or that no control was created


### Requirement: Properties window filter

The Properties window SHALL offer a localized live filter above its Alphabetic and Categorized tabs.
Matching SHALL use a case-insensitive substring of the property name, independently of category collapse.

#### Scenario: Narrow and restore the property list
- **WHEN** the user types a property-name fragment
- **THEN** both tabs show only matching properties, and categories without matching children are omitted
- **WHEN** the filter is cleared
- **THEN** the full property lists return, preserving category expansion state


### Requirement: Unmodelled properties remain visible and read-only

The Properties window SHALL append every raw property carried by the selected modelled component
that has no editable design-time adapter after its modelled properties in both tabs, as greyed
read-only rows included in name filtering. Text values
SHALL retain the file's textual representation; a property with a binary record or companion citation
SHALL show a localized `(binary)` marker instead. Displaying these rows SHALL NOT alter the saved form
or companion bytes. This is the presentation contract decided in [#158](https://github.com/hexide-io/HexIDE/issues/158).

#### Scenario: Inspect a carried property

- **WHEN** a selected component carries an unmodelled textual property
- **THEN** both tabs show its name and saved value after the modelled rows, without an editor
- **AND** saving after inspecting or filtering the row preserves the form and companion byte for byte

#### Scenario: Inspect a binary record

- **WHEN** the carried property cites a companion record
- **THEN** its value is the localized binary marker, never the citation or binary bytes

### Requirement: PictureBox designer bitmap assignment honors AutoSize
When a bitmap is assigned to a PictureBox with AutoSize True, the designer SHALL set Width and Height to the bitmap's pixel dimensions plus two pixels on each side for the default 3-D Fixed Single border, or no extra pixels with BorderStyle None. It SHALL record those dimensions with the picture edit as one undo step. This implements the measured bitmap cases in [#922's oracle](../../../docs/vb6-fidelity-oracle/autosize-on-a-label-and-a-picturebox.md); Label AutoSize remains pending at design time; runtime Label sizing is specified in [label-autosize](../label-autosize/spec.md); runtime bitmap sizing is specified in [container-controls](../container-controls/spec.md).

#### Scenario: A 40 by 30 bitmap is assigned
- **WHEN** the designer assigns a 40 by 30 pixel bitmap to an AutoSize PictureBox
- **THEN** the model holds 44 by 34 pixels with the default border, or 40 by 30 with none
- **AND** saving writes 660 by 510 twips with the default border, or 600 by 450 with none
- **AND** undo restores the picture and both previous dimensions together

#### Scenario: Stored dimensions and explicit assignments hold
- **WHEN** a form loads, the picture is cleared, or Width is assigned explicitly
- **THEN** AutoSize does not recompute the dimensions

#### Scenario: An unmeasured picture or border is used
- **WHEN** AutoSize would fit a non-bitmap picture or a flat Fixed Single border
- **THEN** the designer refuses sizing once with the not-implemented message explaining that the size is unmeasured
- **AND** it restores the picture without reporting a file-load or invalid-property error

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

### Requirement: Dragging SHALL preserve the control's declared size
A move SHALL leave Width and Height unchanged, including when layout rounds the arranged bounds at display
scaling above 100%. A resize SHALL start from the declared dimensions where they are set, falling back to
arranged dimensions for automatically sized elements.

#### Scenario: Move a control at 150% display scaling
- **GIVEN** a control whose declared size is 81 by 33 DIP (1215 by 495 twips)
- **WHEN** the developer drags it to a new position at 150% display scaling
- **THEN** its declared size remains 81 by 33 DIP
- **AND** its saved Width and Height remain 1215 and 495 twips

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
- **WHEN** a form holds a control HexIDE does not model that records `Left`, `Top`, `_ExtentX` and `_ExtentY` and no `Width` or `Height`
- **THEN** its box is at `Left` and `Top`, 1200 by 480 twips, as VB6 draws it, whatever the extents say

#### Scenario: A control inside a Frame
- **WHEN** a Frame the designer models holds a control it does not, and the Frame is moved, deleted or restored by undo
- **THEN** the box is drawn on the Frame, moves with it, goes with it, and comes back with it

#### Scenario: Pressing on the box
- **WHEN** the developer presses on the box
- **THEN** nothing is selected and no region drag begins

### Requirement: Control preview captions use grayscale antialiasing
The designer SHALL render control text with grayscale antialiasing before composing its bitmap preview.
Running CheckBox and OptionButton captions SHALL also use grayscale antialiasing, independently of their
bullet's aliased edges (#917).

#### Scenario: An outline font supplies a control caption
- **WHEN** a CommandButton is previewed in the designer, or a CheckBox or OptionButton is rendered at run time
- **THEN** its caption contains intermediate shades at glyph edges rather than only foreground and background pixels

### Requirement: Export a picture property's payload
The Properties window SHALL offer Export picture on modeled Picture, Icon, MouseIcon and DragIcon properties
holding a nonempty, length-consistent VB6-framed StdPicture record. Export SHALL write only the image payload
chosen by a save picker, without changing the property, designer history, form or companion file. It SHALL NOT
export opaque OCX property bags, unframed bytes or malformed records. Cancellation SHALL write nothing, and a
write failure SHALL be reported to the user. Before replacing an existing file, export SHALL ask
"{0} already exists. Do you want to replace it?", with the full destination path substituted for {0},
under the title "Export Picture" without an ellipsis. Both strings SHALL use dedicated localized keys.

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

### Requirement: Saved TabIndexes normalize when a form loads
The form reader SHALL normalize saved TabIndexes in depth-first file order across the whole form,
including container children, as measured in [#4843](https://github.com/hexide-io/HexIDE/issues/4843)
(oracle PR #4873). For N saved indices, the first occurrence of each value within 0..N−1 SHALL retain
its value; duplicates and out-of-range values SHALL take the smallest free numbers in file order.
The designer, runtime and save SHALL use those same values, including a modelled control's raw
TabIndex property. Carried controls SHALL count in N and SHALL take their number by the same rule.
A missing TabIndex
line SHALL retain the class default pending measurement of mixed omitted and explicit values.

#### Scenario: A duplicate precedes a valid zero
- **GIVEN** three controls saved in file order with TabIndexes 1, 1 and 0
- **WHEN** the form loads
- **THEN** the designer, runtime and next save use 1, 2 and 0

#### Scenario: A carried control would change
- **GIVEN** those same saved indices with the second control carried as text
- **WHEN** the form loads
- **THEN** the designer, runtime and next save use 1, 2 and 0, the carried control's included

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

### Requirement: Designer instantiation failures are contained per control

The designer SHALL catch an exception from a control's designer instantiation, layout or drawing, log the exception, and show a disabled placeholder at that control's bounds with its name and the exception message. The view-only label SHALL be localized. The failed preview SHALL NOT prevent the form from remaining open. The designer SHALL exclude a failed preview from the Properties object selector and multi-selection. If a selected preview fails, the designer SHALL select the form instead and remove the failed preview from multi-selection.

#### Scenario: A registered component refuses its designer instance

- **WHEN** a form containing a registered component whose designer instantiation throws is opened
- **THEN** the designer shows the failed control's name and reason in a disabled placeholder at its bounds
- **AND** its canvas item is disabled and the form stays open
- **AND** opening the preview does not change the serialized form

#### Scenario: A control refuses while it is drawn

- **WHEN** a form is opened whose StatusBar has a spring panel beside a Contents panel with text, which the StatusBar refuses to lay out
- **THEN** the designer shows that StatusBar's name and reason in a disabled placeholder at its bounds, and the form stays open
- **AND** opening the preview does not change the serialized form

#### Scenario: A selected control preview fails

- **WHEN** a selected control fails to produce its designer preview
- **THEN** Properties shows the form and its object selector excludes the failed control
- **AND** multi-selection excludes the failed control, including subsequent attempts to select it

#### Scenario: Save edits elsewhere after a preview fails

- **GIVEN** a form read from writer-normalized text containing a control whose preview fails
- **WHEN** another control is moved and the form's caption is changed, and the full writer saves the form
- **THEN** the edits appear in the output and the untouched control's block is byte-identical to its input block
- **AND** that block is identical to the output with a working preview, including modelled children, companion citations and unmodelled properties

`DesignerInstantiationFailureTests` also opens VB6's own save of a TabStrip form (`corpus/inbox-controls/saved/tabstrip`), whose preview refuses. After the form's caption is edited, the TabStrip's block is saved as VB6's bytes, identical to a save that never built the preview (#2405). A failed preview therefore changes nothing in what is written. Whether every control's block matches VB6's bytes after an edit is the writer's round-trip fidelity, not a property of the preview.

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

### Requirement: Load a picture from the Properties window
The Properties window SHALL offer an ellipsis button on modeled Picture, Icon, DragIcon and MouseIcon rows.
It SHALL open the IDE file-dialog service's single-file picker and validate the selected file with the existing
companion picture decoder. An accepted picture SHALL replace the property's record as one undoable designer
change and save through the existing companion writer. Cancelling SHALL change nothing. A file the reader cannot
read SHALL be refused with a localized message naming the file, retaining the old picture.
This is the scoped decision in [#2653](https://github.com/hexide-io/HexIDE/issues/2653); clearing and file-type
filters remain unchanged. Record framing is measured in
[the serialization oracle](../../../docs/vb6-fidelity-oracle/serialization--file-format-fidelity.md).

#### Scenario: Replace a picture and undo
- **WHEN** a picture file the decoder accepts is chosen from a picture row's ellipsis button
- **THEN** the row and control hold that picture and saving and reopening retains it
- **AND** one undo restores the previous picture

#### Scenario: Cancel or refuse a file
- **WHEN** the picker is cancelled or the chosen file cannot be read as a picture
- **THEN** the property and undo history remain unchanged
- **AND** a refused file produces a message naming it

### Requirement: Confirm picture export replacement
The Properties window's picture export SHALL ask an explicit Yes/No question naming an existing destination
before replacing it, including when a scripted file-picker answer chose the path. Only Yes SHALL permit
replacement, staged and flushed through the atomic writer. A new destination SHALL be created exclusively.

#### Scenario: Decline replacement
- **WHEN** picture export chooses an existing file and the replacement question is declined or dismissed
- **THEN** the existing bytes remain unchanged

#### Scenario: Accept replacement
- **WHEN** the replacement question is answered Yes
- **THEN** the destination contains the exported picture payload

### Requirement: Missing symbol fonts use drawable Unicode equivalents
Label, CommandButton, CheckBox and OptionButton captions SHALL use the same symbol-font mapping in the designer
and at run time. Missing Charset 2 fonts SHALL use the equivalents chosen in
[#1133](https://github.com/hexide-io/HexIDE/issues/1133), keeping original text for unmapped or undrawable glyphs.
This rendering substitution SHALL NOT change Caption/Text readback or permit setting a missing font at run time.

#### Scenario: A form names a symbol font
- **WHEN** a Charset 2 font is installed
- **THEN** its caption retains its GDI private-use mapping
- **WHEN** Wingdings, Wingdings 2, Wingdings 3 or Webdings is missing (family names compared without regard to case)
- **THEN** its caption uses the byte-to-scalar facts in WG2 N4384 (UTC L2/12-368), section 7
- **WHEN** Marlett is missing
- **THEN** its caption uses the short table chosen in #1133

#### Scenario: A symbol precedes an access key
- **WHEN** a missing Wingdings caption is `M&J` and the host can draw both equivalents
- **THEN** the displayed text contains U+1F4A3 followed by U+263A
- **AND** the access-key underline follows the two-unit U+1F4A3 and marks U+263A
- **AND** Caption still reads `M&J`

### Requirement: Edit a control array's design-time Index

The Properties window and `set_control_property` SHALL share an editable Index for each modelled
control. Edits SHALL support undo and form save/load. Invalid edits SHALL be refused before mutation
or saving. Automation replies SHALL name the resulting element or plain control.

#### Scenario: Create and move a one-element array
- **WHEN** a plain control's initially empty Index is set to an integer from 0 through 32767
- **THEN** it becomes that element of the array bearing its name
- **AND WHEN** Index is changed to another free value in that range
- **THEN** the element moves and the saved form reloads with its new Index
- **AND** new and moved Index lines use the serializer’s normal property indentation and value columns;
  untouched VB6-written array lines are preserved verbatim

#### Scenario: Refuse an invalid array edit
- **WHEN** Index is set to -1, 32768, non-integer text, or an index a same-name sibling holds
- **THEN** the edit is refused without changing the model or saving
- **AND** the Properties window gives localized instructions without measurement notes, distinguishing
  a sibling holding the same Index from a same-name sibling with no Index
- **AND** automation refusals explain the VB6 file-load rule measured in
  [#2701 / measuring PR #4074](https://github.com/hexide-io/HexIDE/issues/2701), for the
  [control-array oracle](../../../docs/vb6-fidelity-oracle/control-arrays.md)

#### Scenario: Clear an element
- **WHEN** a lone element's Index is cleared
- **THEN** it becomes a plain control and undo restores its Index
- **BUT WHEN** same-name siblings remain
- **THEN** clearing is refused with a localized instruction naming the sibling
- **AND** this rule is reasoned from the indexed-first mixed form measurement (missing-Index log, plain
  control outside the array, Index raises 343), not measured VB6 IDE behaviour; reverse file order and
  IDE prompts remain unmeasured
- **AND** existing event signatures and new handler stubs remain unchanged, tracked by
  [#4205](https://github.com/hexide-io/HexIDE/issues/4205); renaming to join an array follows the requirement below

### Requirement: Renaming can create or join a control array

The Properties window SHALL ask before renaming an unindexed control to the name of exactly one other
unindexed modelled control of the same type, following the [VB6 SP6 measurement](../../../docs/vb6-fidelity-oracle/the-tabindex-a-new-or-pasted-control-takes.md)
(#1397, measuring PR #5234). The localized question SHALL name the holder and use a Warning icon,
the product display name as title, and Yes (default) then No. HexIDE offers no Help button.

#### Scenario: Accept or decline array creation

- **WHEN** Command2 is renamed to Command1 and neither control has an Index
- **THEN** the question precedes the rename
- **AND** Yes gives the original Index 0 and the renamed control Index 1, preserving Caption, position and TabIndex
- **AND** one Undo restores both controls; Redo reapplies the change
- **BUT** No changes nothing and shows no further message

#### Scenario: Join an existing array

- **WHEN** an unindexed control is renamed to the name of an indexed array of the same type
- **THEN** every rename route joins it without a question, at one more than the highest Index
- **AND** the Properties window records the Index and name together for undo
- **AND** this allocation rule is the explicit #1397 decision; VB6 was measured only for 0..1 becoming 0..2
- **BUT** unindexed cross-type renames, preserved holders, multiple unindexed holders and unavailable Index values remain refused

#### Scenario: Automation cannot confirm

- **WHEN** set_control_property would create an array from two unindexed controls
- **THEN** it refuses before mutation or save and explains in English that the caller can set the holder's
  Index to 0 first, then rename the other control to join it

### Requirement: Assign TabIndex when adding or pasting a control

For a control that models `TabIndex`, the designer SHALL assign the next index after the highest on the
form when adding or pasting it, starting at 0 for a form with no indexed controls. A paste assigns a
container's index before its children's. A Timer takes no index and uses none up, as measured in
[#925](../../../docs/vb6-fidelity-oracle/the-tabindex-a-new-or-pasted-control-takes.md) on a densely numbered form.
VB6 renumbers a form's indexes to 0..N−1 when it loads it, which HexIDE does not yet do
([#4843](https://github.com/hexide-io/HexIDE/issues/4843)), so on a form saved with gaps the two can differ.

#### Scenario: A ListView holds an index as property text

- **GIVEN** a form with a TextBox whose `TabIndex` is 0 and, after it, a ListView whose saved `TabIndex` is 1
- **WHEN** a CommandButton is added or the TextBox is copied and pasted
- **THEN** the designer assigns the new control `TabIndex` 2, counting the ListView's preserved property
  ([#4825](https://github.com/hexide-io/HexIDE/issues/4825))

### Requirement: Designer keyboard selection follows measured tab order

Tab and Shift+Tab in the designer SHALL move the selection through every control with a TabIndex in
pure TabIndex order across the whole form, wrapping without including the form. TabStop, Visible, Enabled
and containment SHALL NOT exclude a control or group it with its container; Labels and containers are
stops too, as measured in [#4211](https://github.com/hexide-io/HexIDE/issues/4211). With several controls
selected, traversal SHALL start from the primary (last selected) control and produce a single selection.
Escape SHALL select the form, and focus SHALL stay
in the designer, except while a mouse button is held: then Escape SHALL leave the selection alone, so a drag
that is abandoned and let go where it began leaves its control where it was.

#### Scenario: Creation order differs from TabIndex

- **GIVEN** Command1, Text1 and Check1 created in that order with TabIndex 2, 0 and 1
- **WHEN** Tab is pressed from the form and then repeatedly
- **THEN** selection visits Text1, Check1, Command1 and Text1, with focus in the designer
- **AND** Shift+Tab reverses that cycle, and Shift+Tab from the form selects Command1

#### Scenario: Children precede their Frame by TabIndex

- **GIVEN** Command1 (0), Text2 inside Frame1 (1), Text1 (2), Frame1 (3), Command3 (4), Command2 inside Frame1 (5), Label1 (6), hidden Check1 (7) and disabled Option1 (8), with Text1's TabStop False
- **WHEN** Tab is pressed repeatedly from the form
- **THEN** each control is selected in that order and the cycle wraps to Command1
- **AND** Shift+Tab traverses the reverse order

#### Scenario: Tab replaces a multiple selection

- **GIVEN** Command3 (8) then Text1 (1) selected, with Text1 primary
- **WHEN** Tab is pressed
- **THEN** Check1 (2) alone is selected

#### Scenario: Escape on a form with TabStop False

- **GIVEN** a form where one control has TabStop False
- **WHEN** a control is selected and Escape is pressed
- **THEN** the form is selected

#### Scenario: Escape during a held move

- **WHEN** a control is being moved with the button held, and Escape is pressed
- **THEN** the selection does not change, and letting go where the move began leaves the control where it was

#### Scenario: The design-time caption buttons

- **WHEN** focus moves through the designer
- **THEN** the form's minimize, maximize and close buttons never take it
