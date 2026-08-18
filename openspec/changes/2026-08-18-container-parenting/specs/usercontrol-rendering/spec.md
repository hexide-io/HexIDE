## MODIFIED Requirements

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
