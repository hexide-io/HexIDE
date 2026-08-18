# form-designer-undo-redo Specification

## Purpose
Define the undo history behind every designer edit: what is undoable, what one step means, and how far back
it goes.

Without it the designer is unsafe to use. Every operation on the design surface is immediate and permanent,
so one mis-drag, one accidental delete, or one bulk alignment applied to the wrong selection is
unrecoverable — and the operations most worth having are exactly the ones with the widest blast radius.
Undo is what makes the rest of the designer usable rather than nerve-wracking.

VB6 had an undo history with an arbitrary and undocumented step limit. That is treated here as a defect of
the original product rather than a behaviour to reproduce, which is the distinction the project draws
generally: reproduce what VB6 intended, not what it got wrong.
## Requirements
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

### Requirement: Each step SHALL be described in the interface
Each undoable operation SHALL carry a short human-readable description naming what it did and what it
affected, and the undo and redo commands SHALL show the description of the step they would apply.

An undo command that says only "Undo" asks the developer to remember what they last did, which after a few
minutes of layout work they will not. Naming the step — the operation and its subject — turns undo from a
guess into a decision, and makes it safe to undo several steps in a row.

#### Scenario: Reading what undo will do
- **WHEN** the developer opens the menu with an undoable operation available
- **THEN** the undo command names that operation and what it affected

#### Scenario: Nothing to undo
- **WHEN** there is nothing to undo
- **THEN** the command is unavailable rather than showing a stale description

### Requirement: History SHALL be per-designer and SHALL NOT be limited in depth
Each open form or user control SHALL have its own independent history, and the number of steps retained
SHALL NOT be capped.

The original product's step limit was arbitrary and undocumented, which made it useless to reason about — a
developer could not know how far back they could rely on going. Keeping history per designer is what makes
that safe: an unlimited global history would accumulate steps from forms the developer is no longer looking
at, and undoing would reach into a form they had not touched in an hour.

#### Scenario: Undoing far back
- **WHEN** many operations have been performed on one form
- **THEN** every one of them can be undone in reverse order

#### Scenario: Two forms open
- **WHEN** operations are performed on two different forms
- **THEN** undoing in one has no effect on the other

#### Scenario: Closing a designer
- **WHEN** a designer is closed
- **THEN** its history is discarded rather than persisting into the next session

### Requirement: A new operation SHALL discard the redo history
Performing an operation after undoing SHALL clear anything that was available to redo.

Keeping it would leave the developer able to redo a step that no longer follows from the current state,
producing a form neither branch of the history describes. Discarding at the point of divergence is the
conventional behaviour and the only one that stays coherent.

#### Scenario: Editing after undoing
- **WHEN** an operation is undone and then a different operation is performed
- **THEN** the undone operation can no longer be redone

### Requirement: Undo SHALL apply to whichever editor has focus
The undo and redo commands SHALL act on the designer when a designer has focus and on the text editor when
the code editor has focus, and the two histories SHALL NOT interfere.

The same shortcut serves both, and a developer moving between a form and its code does not re-learn which
key to press. Sharing one history instead would mean undoing a text edit reached back into the layout, which
is both surprising and destructive.

#### Scenario: Undoing with the designer focused
- **WHEN** the designer has focus and undo is invoked
- **THEN** the last designer operation is reversed and the code is untouched

#### Scenario: Undoing with the code editor focused
- **WHEN** the code editor has focus and undo is invoked
- **THEN** the last text edit is reversed and the layout is untouched

### Requirement: Menu editing SHALL be committed rather than tracked
Changes made through the menu editor SHALL NOT enter the undo history.

The menu editor is a modal dialog the developer works in and then confirms or cancels, so it already has its
own commit point — cancelling is the undo. Recording its result as a step as well would offer two different
ways to reverse the same thing, and tracking each edit inside the dialog would fill the history with steps
the developer never sees as separate actions.

#### Scenario: Completing the menu editor
- **WHEN** the developer confirms changes in the menu editor
- **THEN** the undo history is unchanged

