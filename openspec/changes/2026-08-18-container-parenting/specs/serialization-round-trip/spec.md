## ADDED Requirements

### Requirement: A container's children SHALL be written back inside their container
Where a file expresses a control as a child of a container, the IDE SHALL write it back inside that
container rather than as a sibling of it.

The existing structure requirement was written for menus and is satisfied by them; this states the harder
half. It matters more than menu nesting because the original product does not defend against getting it
wrong: a file that nests a control under a class which cannot contain one is accepted without complaint and
the control is silently re-parented somewhere else. A flattened file therefore does not fail loudly in VB6 —
it opens, and the form is quietly a different form.

#### Scenario: A control inside a container
- **WHEN** a form with a control inside a container is loaded and saved
- **THEN** the saved file expresses that control inside the same container

#### Scenario: Containers within containers
- **WHEN** a container is itself a child of another container
- **THEN** every level is written back at its original depth

### Requirement: A child's position SHALL be interpreted relative to its container
Where a control is a child of a container, the IDE SHALL treat its stored position as measured from that
container's own origin, in every layer that reads or writes it.

The file measures a child from its container, and a container is free to move — the tab-switching idiom in
the original product's own templates parks a page off-screen and brings it back by assigning the container's
position. A layer that reads a child's stored position as a distance from the form therefore draws it in the
wrong place, and a layer that writes one back as such corrupts it. The two failures are the same mistake in
opposite directions, which is why no layer may hold the assumption privately.

#### Scenario: A child of a container parked off-screen
- **WHEN** a container is positioned outside the visible form and holds children
- **THEN** its children are drawn with it rather than at the equivalent distance from the form

#### Scenario: Moving a child in the designer
- **WHEN** the developer moves a control that is inside a container
- **THEN** what is written back is its position relative to that container

### Requirement: Unreproducible binary content SHALL hold a file read-only
Where a file references binary content through a property the IDE does not model, the IDE SHALL treat that
file as one it cannot reproduce.

The reference is what is lost, not the bytes: the companion file is left alone by a separate guard, so the
image survives on disk while the property pointing at it does not. That is a save which looks successful and
silently strips a control's picture, which is the same class of failure as flattening and is currently
invisible to the gate. Recognising it widens the refusal before the container work narrows it — the count of
refused files gets worse before it gets better, and that is the correct direction, because a refusal is
recoverable and a silent strip is not.

#### Scenario: A property the IDE does not model referencing a companion blob
- **WHEN** such a file is loaded
- **THEN** it is presented read-only rather than saved with the reference dropped

#### Scenario: A modelled property referencing a companion blob
- **WHEN** the referencing property is one the IDE does model
- **THEN** the file is not held read-only on that account
