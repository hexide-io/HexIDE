## ADDED Requirements

### Requirement: Opening a project from the tree SHALL show what it contains
Activating a project in the tree SHALL open a read-only document describing that project: its name, type,
startup object, location, the members it holds with the kind of each, and the references it declares. A
description SHALL be shown only where the project has one.

The document SHALL describe the project as currently loaded rather than the contents of its file on disk,
and SHALL follow the project as it changes without re-reading that file. A project that has not been saved
has no file, and SHALL still be describable — its location SHALL say so rather than being left empty.

The document SHALL NOT offer to edit the project. A project has two authorities already — the file and the
loaded model — and an editable third view would have to reconcile them.

The project node is the only node in the tree with nothing to open, while being the node every other member
hangs from. What a developer wants from it is the answer VB6 never gave directly: what is in this thing.

#### Scenario: Opening a saved project
- **WHEN** the developer activates a project in the tree
- **THEN** a read-only document opens showing its members, references, startup object and file location

#### Scenario: Opening a project that has never been saved
- **WHEN** the developer activates a project with no file on disk
- **THEN** the document opens and reports that the project is not saved, rather than showing an empty location

#### Scenario: A member added while the document is open
- **WHEN** a member is added to a project whose document is open
- **THEN** the document shows it, without the project file being re-read

### Requirement: Activating a project SHALL NOT toggle its expansion
Activating a project in the tree SHALL open it and SHALL leave its expanded state unchanged. Expansion
SHALL remain available from the node's chevron.

VB6 spent the double-click on expand/collapse here. This diverges deliberately: the chevron already offers
expansion, and a gesture that leads somewhere is worth more than a second way to do what a control beside
it does. This is the Fidelity Principle's "intended behaviour, not the accident" applied to a gesture —
VB6's binding reflected the node having nothing to open, which is no longer true.

A project **group** node is not a project and keeps expand/collapse, because there is nothing for it to
open into yet.

#### Scenario: Expansion survives activation
- **WHEN** the developer activates an expanded project in the tree
- **THEN** the project opens and remains expanded
