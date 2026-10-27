# container-controls Specification

## Purpose
Define what it means for one control to be held inside another: how a contained control is positioned, what
operations on a container do to what it holds, what remains true of the contained control in its own right,
and which classes can hold controls at all.

Containment is not a feature of any one layer, which is why it is a capability rather than a note in three
others. The file expresses it, the designer draws it, the running form hosts it and the automation surface
reports it — and a layer that quietly disagrees with the others produces a defect nobody sees. The position
of a contained control is the sharpest case: read as a distance from the form it draws in the wrong place,
written back as one it corrupts the file, and because a container may sit anywhere — including off-screen,
which is how the original product's own templates switch between pages — the two numbers are unrelated
rather than merely different.

The consequences of getting it wrong are not symmetrical with the consequences of getting it slightly wrong.
The original product accepts a file that nests a control under a class which cannot hold one, and silently
relocates the control rather than complaining. So a file with the structure flattened out of it opens
cleanly, and is quietly a different form — a failure the developer discovers weeks later, in the other
product, with no way back. That asymmetry is why the last requirement here refuses rather than repairs: a
refused save is recoverable, and a plausible-looking wrong file is not.
## Requirements
### Requirement: A control's position SHALL be measured from its container
Where a control is held by a container, the IDE SHALL treat its position as measured from that container's
own client origin, and SHALL do so identically at rest, at run time, on the design surface and through the
automation surface.

Position is the property most likely to be read by a layer that does not know containment exists, and the
failure is silent in both directions: a layer that reads a child's stored position as a distance from the
form draws it in the wrong place, and a layer that writes one back as such corrupts the file. Because a
container may sit anywhere — including off-screen, which is the tab-switching idiom in the original
product's own templates — the two numbers are unrelated, and no layer may hold the assumption privately.

#### Scenario: A contained control is drawn where its container puts it
- **WHEN** a form holding a control inside a container is shown
- **THEN** the control appears at its stored offset from that container, not from the form

#### Scenario: A container moves
- **WHEN** a container's position changes
- **THEN** everything it holds moves with it, and each stored position is unchanged

#### Scenario: A container off the visible area
- **WHEN** a container is positioned outside the form's visible bounds
- **THEN** its contents are not drawn on the form, at the offset they would have had against the form's own
  origin or anywhere else

#### Scenario: A Timer's designer position is read by a running program
- **WHEN** a running program reads a Timer's Left or Top through an Object reference
- **THEN** it raises run-time error 393 rather than returning the designer position (#2069)

### Requirement: A container SHALL carry what it holds
Every operation that acts on a container SHALL act on its contents with it — moving, hiding, disabling,
deleting, copying and saving alike — as one thing rather than as a container and a set of unrelated
controls that happen to overlap it.

This is what "container" means to the developer, and it is the expectation they bring from the original
product. Any operation that quietly treats the two separately produces a result the developer did not ask
for and did not see: contents left behind at the old position, a deleted container's contents surviving
with nothing to hold them, a copy that arrives empty.

#### Scenario: Deleting a container
- **WHEN** a container is deleted
- **THEN** its contents are deleted with it, and one undo restores the container and its contents together

#### Scenario: Copying a container
- **WHEN** a container is copied and pasted
- **THEN** the copy holds its own copies of the contents, and the original keeps its own

#### Scenario: Hiding a container
- **WHEN** a container is hidden
- **THEN** nothing it holds is drawn, and code attached to what it holds keeps running

#### Scenario: Disabling a container
- **WHEN** a container is disabled
- **THEN** nothing it holds responds to the user
- **AND** a contained control's Enabled reads False, including through nested containers and Me.Controls

#### Scenario: Assigning Enabled inside a disabled container
- **WHEN** a program assigns a contained control's Enabled while a container above it is disabled
- **THEN** its Enabled still reads False
- **AND** once its containers are enabled it reads its own assigned value

### Requirement: A contained control SHALL remain editable in its own right
A control inside a container SHALL be selectable, movable, resizable, renameable and deletable on its own,
without being taken to be part of its container.

A container that swallowed its contents would be worse than no container at all: the developer would have to
empty it to change anything inside it. The distinction the developer expects is between acting on the
container — which carries its contents — and acting on something it holds, which does not disturb the
container.

#### Scenario: Selecting something inside a container
- **WHEN** the developer selects a control inside a container
- **THEN** the container is not selected, and the reported position is the one measured from the container

#### Scenario: Moving something inside a container
- **WHEN** the developer moves a control inside a container
- **THEN** only that control moves, and its position remains measured from the same container

### Requirement: Ordering SHALL be resolved within the container
Where controls overlap, the IDE SHALL resolve which is drawn in front among the controls sharing a
container, and SHALL NOT move a control out of its container to change its order.

Ordering is a statement about siblings. Applying it across the whole form would let "bring to front" move a
control out of the container holding it — a change to the structure of the form, made by a command the
developer expects to change only what is painted over what.

#### Scenario: Bringing a contained control to the front
- **WHEN** the developer brings a control inside a container to the front
- **THEN** it is drawn in front of the others in that container, and it is still held by that container

#### Scenario: Ordering across containers
- **WHEN** two controls are in different containers
- **THEN** neither command changes which is drawn in front of the other

### Requirement: There SHALL be no fixed limit on how deeply containers nest
The IDE SHALL support a container inside a container to any depth a file expresses, and SHALL NOT impose a
cap of its own.

The original product's own templates nest a container two levels deep, and nothing in the file format
suggests a limit. A cap would be an artificial constraint of the kind this project does not reintroduce: a
real limit is one the machine imposes, not one an implementation found convenient.

#### Scenario: A container within a container
- **WHEN** a file expresses a container holding a container holding a control
- **THEN** every level is loaded, drawn, run and saved at its own depth

### Requirement: Tab order SHALL remain a single form-wide sequence
Keyboard traversal SHALL follow the form's own tab-order sequence across every control it holds, regardless
of which container each is in.

The original product's tab order is one flat sequence per form, not one per container, and its own templates
depend on that: a form whose fields are inside a container but whose buttons are not is traversed in the
order the developer assigned, not container by container. Traversing containers as units would reorder every
such form.

#### Scenario: Tabbing across a container boundary
- **WHEN** the developer tabs through a form whose tab order interleaves contained and uncontained controls
- **THEN** focus follows the assigned sequence, crossing into and out of containers as that sequence requires

### Requirement: The automation surface SHALL speak the same coordinates and identify the container
Every automation operation that reports or accepts a control's position SHALL use the position measured from
its container, and every report of a control SHALL identify the container holding it.

An automation surface with a private coordinate convention is a trap: the caller reads a number from one
operation and passes it to another that means something else by it, and the control lands somewhere
unintended. Naming the container makes the convention self-describing rather than something the caller has
to know.

#### Scenario: Reading a contained control
- **WHEN** a caller lists a form's controls
- **THEN** each contained control's reported position is measured from its container, and the container is
  named alongside it

#### Scenario: Moving a contained control
- **WHEN** a caller moves a contained control to a given position
- **THEN** the position is interpreted as measured from its container, matching what listing the control
  reported

### Requirement: Only a class that can hold controls SHALL be treated as a container
The IDE SHALL treat as containers only those classes it can genuinely draw and host controls inside, and
SHALL refuse to save a file that nests a control under any other class rather than relocating it.

The file format permits nesting a control under a class that cannot hold one, and the original product
accepts such a file without complaint while quietly relocating the control. That makes it corrupt input
rather than an exotic container, and it is exactly the shape a save must not propagate: relocating the
control produces a file that opens cleanly and is quietly a different form. Refusing is recoverable.

#### Scenario: A control nested under a class that cannot hold one
- **WHEN** a file nests a control under a class that is not a container
- **THEN** the form opens read-only and the save is refused, with the reason given

#### Scenario: A class contributed by an extension
- **WHEN** a component class contributed by an extension has a control nested under it
- **THEN** it is treated the same way, because the IDE cannot host controls inside a class it did not build


### Requirement: TabStrip top-row drawing

An MSComctlLib.TabStrip SHALL paint its single top row with raised selected tabs (Style 0),
pressed selected buttons (Style 1), or flat buttons with optional separators (Style 2).
Caption mnemonic letters SHALL be underlined. Disabled painting SHALL match enabled painting.
The strip SHALL remain excluded from the designer's container classes.

#### Scenario: Selection changes from code or input

- **WHEN** a supported top-row strip changes its selection, tabs or captions
- **THEN** it repaints the row with the current selection and captions

#### Scenario: Drawing outside the supported slice

- **WHEN** drawing encounters TabStyle 1, nondefault Placement, MultiRow, TabWidthStyle, fixed sizes or row overflow
- **THEN** a running form reports the not-implemented error through its event queue
- **AND** it does not substitute a guessed layout

### Requirement: TabStrip saved property bags

An MSComctlLib.TabStrip SHALL write its measured own-property order, with `_Version` after the
scalar properties, then a Tabs block, MousePointer, Font and OLEDropMode. Every tab SHALL write
ImageVarType, using ImageIndex for type 2 and ImageKey for type 8. Saved selection SHALL be ignored.
TabFixedWidth, TabFixedHeight and TabMinWidth SHALL load from HIMETRIC. Unsupported property bags
and loaded blocks lacking measured extents SHALL keep the designer read-only.

#### Scenario: VB6's measured save

- **WHEN** the measured TabStrip form and its ImageList companion are loaded and serialized
- **THEN** the output matches VB6's save byte for byte, with line endings normalized for the checkout
- **AND** a minimal strip writes one empty tab with ImageVarType 2

#### Scenario: A missing image key

- **WHEN** a tab declares ImageVarType 8 without ImageKey
- **THEN** loading refuses the entire form with Type mismatch

#### Scenario: Runtime loading outside the file slice

- **WHEN** a saved TabStrip requires ImageList, Font or tab-image runtime loading
- **THEN** it reports the not-implemented error rather than silently dropping those properties

### Requirement: SSTab page bookkeeping preserves observable positions
The SSTab runtime container SHALL move children by 75000 twips rather than changing their Visible or
Enabled values. Interpreter Tab assignments SHALL use this page engine (#1090).
Initial page membership SHALL be supplied explicitly, rather than inferred from hidden coordinates.

#### Scenario: Leaving and showing a page
- **WHEN** a page is left
- **THEN** every current child whose right and bottom edges are at or past zero is recorded for that page,
  moved left by 75000 twips, and given TabStop False
- **AND** showing a page moves its recorded children right by exactly 75000 twips at their current positions
  and restores their recorded TabStop values before notifying the caller with the previous page

#### Scenario: Removing a page with recorded children
- **WHEN** the requested tab count removes the showing page
- **THEN** the engine selects page zero first
- **AND** if a removed page still holds recorded children it raises 380 and keeps the old count, even if a
  recorded child has since been reparented

#### Scenario: An unmeasured empty tab count
- **WHEN** zero or a negative tab count is requested with no recorded children on page zero
- **THEN** the engine explicitly refuses it as not implemented pending a measurement separating that range
  from refusal for recorded children

### Requirement: SSTab exposes its runtime pages
A minimal SSTab block SHALL run as a container with three tabs, tab zero showing, three tabs per row,
and captions Tab 0 through Tab 2. TabCaption, TabEnabled and TabVisible SHALL accept indexes, with
381 for an invalid index. Tab SHALL reject out-of-range or hidden pages with 380, accept disabled pages,
and raise Click with the previous tab after movement. Rows SHALL count visible tabs and refuse writes
with 383. Its implicit default member SHALL read zero and ignore assignments.

Saved own properties and page lists SHALL initialize the runtime pages as described below.
Unimplemented members and unmeasured property coercions SHALL refuse explicitly.
Drawing and input are separate slices; this requirement does not define tab-strip rendering.

#### Scenario: Programmatic page selection
- **WHEN** a program changes SSTab.Tab
- **THEN** its children move through the position-based page engine and Click receives PreviousTab
- **AND** assigning the tab already showing raises no Click

#### Scenario: Disabled container reads
- **WHEN** a program disables an SSTab or Frame
- **THEN** a child's Enabled reads False while retaining its own enabled setting for when the container is enabled again

### Requirement: SSTab loads saved page metadata
The runtime SHALL load SSTab page lists independently of child positions, suppress listed TabStops
according to ControlEnabled, and interpret recorded HIMETRIC dimensions. Saved runtime-only flags SHALL
be ignored. Unmeasured conversions and child references SHALL refuse explicitly. SSTab forms SHALL
remain read-only in the designer until edits can maintain the saved page membership.

#### Scenario: Saved pages switch without position repair
- **GIVEN** a saved SSTab with tab one showing and tab zero's ControlEnabled False
- **WHEN** the form loads
- **THEN** children keep their written positions, and listed children with ControlEnabled False lose TabStop
- **AND** saved TabEnabled, TabVisible and per-control Enabled suffixes are ignored
- **AND** selecting tab zero brings back its listed controls by exactly 75000 twips

#### Scenario: Missing or true tab-zero flag leaves tab zero on load
- **GIVEN** a saved SSTab showing a nonzero tab with tab zero's ControlEnabled missing or True
- **WHEN** the form loads
- **THEN** on-page children move left by 75000 twips and are recorded for tab zero
- **AND** the showing tab brings no controls back, so those children show on tab zero instead

### Requirement: SSTab writes saved page metadata
The writer SHALL emit extenders, extents and version, then SSTab own properties and page metadata in
measured order. It SHALL retain saved HIMETRIC lengths, page lists, ControlEnabled flags and optional
companion pictures. It SHALL always write TabHeight (520 when absent) and SHALL omit runtime-only
TabEnabled and TabVisible.

#### Scenario: VB6-designed forms round-trip
- **WHEN** either measured SSTab corpus form is loaded and serialized without edits
- **THEN** its form text and companion records match the saved source exactly

#### Scenario: Extender edit preserves the saved pages
- **WHEN** an SSTab extender property changes in the model
- **THEN** the writer emits the changed extender before its own-property run
- **AND** it preserves page lists, flags and HIMETRIC lengths without inferring membership from positions
- **AND** each page writes caption, optional picture, ControlEnabled, control references with Enabled suffixes, then ControlCount
- **AND** even flags triggering the tab-zero load quirk remain as read
- **AND** the designer remains read-only because membership-changing edits are not yet writable


### Requirement: SSTab draws its runtime tab strip
The runtime SSTab SHALL draw a page and tab captions without insetting its child host. Style 0 SHALL
share the strip length by TabsPerRow and bold the active caption; Style 1 SHALL size captions individually
without active bolding. TabMaxWidth SHALL cap tab lengths. Visible captions SHALL pack from the start,
retaining their share when another tab is hidden. The active row SHALL be nearest the page, with other
rows behind and offset. Side captions SHALL remain horizontal and clipped to the strip thickness.
These requirements prescribe relationships, not exact pixel metrics. Appearance/picture member binding
and the saved-state designer preview remain later slices of #1090.

#### Scenario: Selecting a tab in a back row
- **GIVEN** five tabs with three tabs per row
- **WHEN** a program selects tab four
- **THEN** tabs three and four are in the row nearest the page
- **AND** Style 0 bolds tab four's caption while Style 1 does not

#### Scenario: Drawing does not change page coordinates
- **WHEN** TabHeight, TabsPerRow, Style, TabMaxWidth or a tab's visibility changes
- **THEN** the strip is redrawn without moving children or insetting the child host
- **AND** side orientations draw horizontal captions clipped to each tab

#### Scenario: Caption height and focus indication
- **GIVEN** WordWrap is True and a caption exceeds its tab's width
- **THEN** its text wraps and is clipped to the tab's height
- **AND** the active caption has a dotted focus rectangle only while the SSTab has focus and ShowFocusRect is True

### Requirement: SSTab switches pages through runtime input
The SSTab SHALL switch tabs on a left caption press after MouseDown and before MouseUp, raising
Click(PreviousTab) only when the tab changes. Caption presses SHALL focus the SSTab even for disabled
captions. A double-click SHALL raise DblClick in place of its second MouseDown. Page clicks SHALL
not raise Click. Mouse coordinates SHALL be in twips.

#### Scenario: Caption press changes the page
- **WHEN** a user clicks an enabled caption for another tab
- **THEN** MouseDown runs to completion while Tab still identifies the old page
- **AND** the tab changes through the same event dispatch before Click(PreviousTab), followed by MouseUp
- **AND** the SSTab has focus and its page engine has switched the controls

#### Scenario: Disabled caption or page press
- **WHEN** a user clicks a disabled caption or the page
- **THEN** MouseDown and MouseUp occur without Click or a tab change
- **AND** a disabled caption press gives the SSTab focus

### Requirement: SSTab keyboard navigation respects focus
While the SSTab itself has focus, Left/Right and Ctrl+Tab/Ctrl+Shift+Tab SHALL select the adjacent tab,
wrapping at the ends, and SHALL stop at a disabled tab without skipping it. Home, End, Up, Down and
plain letters SHALL not select tabs. Ctrl+Tab SHALL not switch pages from a child control. An Alt
caption mnemonic from a child SHALL switch to its enabled visible tab and then focus the SSTab.

#### Scenario: Keyboard selects an adjacent tab
- **GIVEN** the SSTab itself has focus
- **WHEN** Right or Ctrl+Tab is pressed on the last tab
- **THEN** tab zero is selected if enabled and visible

#### Scenario: Mnemonic from a child
- **GIVEN** a child control has focus and tab two has caption &Third
- **WHEN** Alt+T is pressed
- **THEN** tab two is selected and the SSTab receives focus
