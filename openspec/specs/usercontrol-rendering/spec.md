# usercontrol-rendering Specification

## Purpose
Define what a project's own UserControl looks like when it has been placed on a form: how it is drawn, how
it behaves as part of the host form, and what is drawn when it cannot be rendered properly.

The forcing requirement is that the form must open at all. A form referencing a UserControl the designer
does not understand used to fail to load, leaving the developer with a project they could not edit because
of a control they had not touched — the worst possible response, since it makes one unrecognised type take
out the whole form.
## Requirements
### Requirement: A form referencing a project UserControl SHALL open, with the control in place
Loading a form that hosts a UserControl defined in the same project SHALL succeed, and the control SHALL
occupy its saved position and size.

An unrecognised control type is a reason to draw something different, never a reason to refuse the form.
Keeping the geometry matters as much as loading: a control drawn at the wrong size silently misrepresents
the layout, and any arrangement the developer does around it is done against a false picture.

#### Scenario: Opening a form that hosts a UserControl
- **WHEN** a form referencing a project-defined UserControl is opened
- **THEN** the form loads and the control appears at its saved position and size

### Requirement: A hosted UserControl SHALL be drawn from its own definition
A hosted UserControl SHALL be drawn showing the child controls its definition contains, so it appears on the
host form as it will at run time. Where its definition holds those controls inside containers, they SHALL be
drawn inside those containers.

The point of placing a composite control is that it looks like the thing it is. A generic box conveys
position and size but nothing about whether the layout around it is right, which is the question the
developer is actually asking while arranging the form.

That argument applies with more force to a UserControl built out of containers, which is what a composite
control usually is. Drawing its contents flat would not merely lose the frames — it would pile controls that
belong in different containers on top of each other near the corner, which looks less like the control than
an empty box does.

#### Scenario: A UserControl containing child controls
- **WHEN** a UserControl containing child controls is hosted on a form
- **THEN** those children are visible within its bounds on the host form

#### Scenario: The UserControl's definition changes
- **WHEN** the UserControl's own definition is changed and the host form is opened again
- **THEN** the host form shows the updated appearance

#### Scenario: A UserControl whose children are held by containers
- **WHEN** a UserControl whose definition holds controls inside a container is hosted on a form
- **THEN** those controls appear inside that container, positioned as its definition places them

### Requirement: A hosted UserControl SHALL behave as one control on the host form
On the host form the UserControl SHALL be selectable, movable and resizable as a single control, and its
children SHALL NOT be individually selectable or movable there. Opening the control itself SHALL be
available from the host form.

The children belong to the UserControl's own definition, not to the form. Allowing them to be selected on
the host would invite edits with nowhere to be saved — the host form has no place to record a change to
another file's internals. Treating the control as opaque and offering a way into its own designer keeps
every edit in the file that owns it.

#### Scenario: Clicking a hosted UserControl
- **WHEN** the developer clicks a hosted UserControl on the form
- **THEN** the whole control is selected, not one of its children

#### Scenario: Editing the control itself
- **WHEN** the developer opens the hosted control from the host form
- **THEN** the UserControl's own designer opens, where its children can be edited

### Requirement: Where a UserControl cannot be drawn from its definition, a labelled placeholder SHALL be drawn
Where the definition cannot be resolved — including a UserControl hosted inside another and a circular
reference between definitions — a placeholder SHALL be drawn at the control's position and size, identifying
the type it stands for, and the form SHALL still open.

There will always be a case the renderer cannot resolve, and a circular reference has no correct rendering
at all. A placeholder answers the two questions that survive not knowing what the control looks like — how
much room it takes and what it is — while keeping the promise that the form opens. Labelling it is what
separates a deliberate fallback from a rendering bug.

#### Scenario: A UserControl hosted inside another UserControl
- **WHEN** a UserControl contains another project UserControl
- **THEN** the inner one is drawn as a labelled placeholder and the outer one still renders

#### Scenario: Definitions that reference each other
- **WHEN** two UserControl definitions reference each other
- **THEN** the cycle is detected, a placeholder is drawn, and no form fails to load

### Requirement: Properties the designer does not model SHALL survive a save
Properties on a hosted UserControl that the designer does not recognise SHALL be preserved and written back
unchanged when the form is saved.

A hosted control carries properties belonging to the control's own type, which the designer has no model
for. Dropping what it does not recognise would mean opening a form and saving it — with no edit at all —
silently stripped the control's configuration. Preserving them verbatim costs nothing and is the difference
between a designer that is incomplete and one that is destructive.

#### Scenario: Saving a form with a hosted UserControl
- **WHEN** a form hosting a UserControl is opened and saved without being edited
- **THEN** every property of the hosted control is written back unchanged, including those the designer does not model

