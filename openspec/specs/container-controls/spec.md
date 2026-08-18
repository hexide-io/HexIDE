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

