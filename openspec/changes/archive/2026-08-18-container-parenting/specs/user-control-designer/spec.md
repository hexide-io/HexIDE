## MODIFIED Requirements

### Requirement: A UserControl SHALL be editable on the same design surface as a form
Opening a UserControl SHALL present the same design surface, selection model and arrangement commands used
for forms, including where its own controls are held by containers.

A UserControl is a container of controls with code behind it — the same thing a form is, with a different
root. Giving it a separate, lesser editor would mean every designer improvement had to be built twice and
would drift, and the developer would have to learn which subset of the designer works where.

Containment is a case where that could quietly stop being true. A UserControl's root behaves as a form's
does, and its controls may be held by containers exactly as a form's may, so a UserControl holding a
populated container is in scope here rather than being the one shape that opens as a flat list.

#### Scenario: Opening a UserControl
- **WHEN** the developer opens a UserControl from the project
- **THEN** it opens on the design surface with its child controls laid out as saved
- **AND** selection, arrangement and property editing behave as they do for a form

#### Scenario: Opening a UserControl whose controls are held by a container
- **WHEN** the developer opens a UserControl that holds a container with controls inside it
- **THEN** those controls are drawn inside that container, and are selectable and editable there as they
  would be on a form
