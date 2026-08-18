# user-control-designer Specification

## Purpose
Define authoring a UserControl: opening one on a design surface, editing it like a form, and saving it back
to the single file that carries both its layout and its code.

UserControls are first-class members of real VB6 projects, not an advanced extra — a project of any size
tends to have a few. That makes the failure mode expensive: a project containing one has to survive being
opened and saved before anything else about the IDE matters, because getting it wrong damages a file the
developer did not even edit.

How a UserControl appears once placed on a host form is a separate capability
(`usercontrol-rendering`); this one covers authoring the control itself.
## Requirements
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

### Requirement: Saving SHALL write the layout and the current code to one file
Saving a UserControl SHALL write its visual definition and its code into the single file that holds both,
and the code written SHALL be the code currently in the editor.

The file format carries both, so a save that writes one and not the other silently discards the developer's
work. The subtlety is which copy of the code is authoritative: the model holds a copy read when the file was
loaded, and the editor holds what has been typed since. Writing the loaded copy would compile, save
successfully, and lose the edit — a failure that looks like the editor never registered the typing.

#### Scenario: Editing code and saving
- **WHEN** the developer edits a UserControl's code and saves
- **THEN** the saved file contains the edited code together with the current layout

#### Scenario: Editing layout and saving
- **WHEN** the developer changes the layout and saves
- **THEN** the saved file contains the new layout together with the current code

### Requirement: Loading a project SHALL preserve what each member is
Loading a project SHALL identify a UserControl as a UserControl and SHALL NOT reclassify it as another kind
of module.

A misidentified member is not a display problem — the kind determines how the file is written back, so a
UserControl loaded as something else is a UserControl that gets saved as something else. The damage happens
on the next save of a project the developer only opened to look at.

#### Scenario: Opening a project containing a UserControl
- **WHEN** a project containing a UserControl is loaded
- **THEN** it is recognised as a UserControl and appears as one in the project

#### Scenario: Saving a project that was only opened
- **WHEN** such a project is saved without the UserControl being edited
- **THEN** the file is still a UserControl

### Requirement: The developer SHALL be able to add a UserControl to a project
The IDE SHALL offer adding a new UserControl to the current project, which SHALL create it with a default
name and open it for editing.

Adding one by hand means knowing the file format and the project file entry it needs — which is
reconstructible but is exactly the kind of knowledge an IDE exists to remove. Opening it afterwards is what
makes the command feel like it did something.

#### Scenario: Adding a UserControl
- **WHEN** the developer adds a UserControl to the project
- **THEN** it is created with a default name, added to the project, and opened on the design surface

