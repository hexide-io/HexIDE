## ADDED Requirements

### Requirement: Structure the IDE does model SHALL survive a round-trip
Where a file expresses a hierarchy among the things the IDE models, the IDE SHALL write that hierarchy
back as a hierarchy. Reading a nested structure and writing a flat one SHALL be treated as data loss, not
as formatting.

The existing requirements cover content the IDE does *not* understand, which it carries verbatim. This is
the opposite case and the more dangerous one: the IDE understood the components perfectly well and lost
the relationship between them. A menu tree written back flat is not a cosmetic difference — every item
becomes a top-level menu, which is a different program.

#### Scenario: A nested menu
- **WHEN** a form whose menus are nested is loaded and saved
- **THEN** the saved file expresses the same nesting

#### Scenario: Depth beyond one level
- **WHEN** a menu contains a submenu which itself contains items
- **THEN** every level is written back at its original depth

### Requirement: The refusal gate SHALL narrow as reproduction improves
Where the IDE becomes able to reproduce a structure it previously could not, the refusal gate SHALL stop
firing for that structure, and SHALL continue to fire for the structures still unreproducible.

A gate that stays shut after the reason for it is fixed is as wrong as one that never closed — it holds
files read-only for a defect that no longer exists, and it teaches developers that the read-only state is
arbitrary. Narrowing it in the same change that fixes the underlying defect is what keeps the gate
meaningful.

#### Scenario: A form whose only nesting is menus
- **WHEN** such a form is loaded, once menu nesting round-trips
- **THEN** it is editable and saveable

#### Scenario: A form with a populated container
- **WHEN** such a form is loaded, while container nesting does not round-trip
- **THEN** it is still presented read-only
