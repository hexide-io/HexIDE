## MODIFIED Requirements

### Requirement: Every designer mutation SHALL be undoable and redoable
Adding, deleting, moving, resizing, pasting, reordering, locking, editing a property, changing which
container holds a control, and every bulk arrangement command SHALL each be reversible, and reversing SHALL
be repeatable in the forward direction.

A history with gaps is worse than none, because the developer learns they cannot rely on it only at the
moment they need it. The operations easiest to omit are the bulk ones — precisely the operations that change
the most at once and are least reconstructible by hand.

Changing which container holds a control is listed separately because it cannot be recorded as a move.
A move records the position before and after, and a position means something different once the container
has changed — replaying it would restore a number measured from a container the control has left. Reversing
one therefore has to restore the container, the position and the order among siblings together.

#### Scenario: Undoing a deletion
- **WHEN** a control is deleted and undo is invoked
- **THEN** the control returns with its previous properties and position

#### Scenario: Undoing a bulk arrangement
- **WHEN** an alignment or spacing command is applied to several controls and undo is invoked
- **THEN** every affected control returns to where it was

#### Scenario: Redoing
- **WHEN** an operation is undone and redo is invoked
- **THEN** the operation is applied again

#### Scenario: Undoing a change of container
- **WHEN** a control is moved from one container to another and undo is invoked
- **THEN** it returns to the first container, at the position and among the siblings it had there

### Requirement: One user gesture SHALL be one undo step
A single continuous gesture SHALL produce a single undo step, and a bulk command applied across a selection
SHALL produce a single step covering all of it. Deleting or pasting a container SHALL likewise be one step
covering the container and everything it holds.

This is the requirement that decides whether undo is usable. A drag reports its position continuously as the
pointer moves, so recording each report would put hundreds of entries on the stack for one drag and make
undo useless — the developer would hold the shortcut and watch the control creep backwards. The same
reasoning applies to a command that moves ten controls: the developer performed one action and expects one
reversal.

A container is the same case again, and the worse version of it: deleting one is a single gesture that can
remove a dozen controls, and restoring them one undo at a time would be both tedious and — because the
container and its contents would exist separately in between — briefly incoherent.

#### Scenario: Undoing a drag
- **WHEN** a control is dragged to a new position and undo is invoked once
- **THEN** it returns to where it started, in one step

#### Scenario: Undoing an alignment across several controls
- **WHEN** several controls are aligned and undo is invoked once
- **THEN** all of them return to their previous positions together

#### Scenario: Separate property edits
- **WHEN** two different properties are changed in sequence
- **THEN** each is its own step, undone independently

#### Scenario: Undoing the deletion of a container
- **WHEN** a container holding several controls is deleted and undo is invoked once
- **THEN** the container and all of its contents return together, still held by it
