## MODIFIED Requirements

### Requirement: The IDE SHALL offer menu, tool window, command, control and template contribution
The host surface SHALL let an add-in add menu items at a named location, register a dockable tool window
from a factory, register a named command, register a custom VB6 control into the toolbox, and register a
project template. A contributed control SHALL NOT be a container, and a form nesting a control under one
SHALL be held read-only rather than saved.

These are the extension points the original add-in ecosystem actually used. Controls and project templates
matter disproportionately: a custom control that appears in the toolbox and a project type that appears in
the New Project dialog are what make an add-in feel like part of the product rather than a bolted-on panel.

The container exclusion is not a policy about add-ins; it is a statement of what the IDE can actually do.
Holding controls means drawing them inside the container's own client area, positioning them against its
origin and clipping them to it — none of which the IDE can do inside a control whose drawing it does not
own. Treating a contributed class as a container would therefore produce a file the IDE cannot reproduce
while reporting that it can, which is the one outcome the refusal gate exists to prevent. Refusing to save
such a form is recoverable; writing it is not.

#### Scenario: Contributing a menu item
- **WHEN** an add-in adds a menu item at a named parent location
- **THEN** it appears there, visually separated from the IDE's own items
- **AND** it can supply a predicate that greys the item out when unavailable

#### Scenario: Contributing a tool window
- **WHEN** an add-in registers a tool window with a factory
- **THEN** the window docks, floats and hides like a built-in one
- **AND** its content is constructed on first display rather than at registration

#### Scenario: Contributing a control
- **WHEN** an add-in registers a custom VB6 control
- **THEN** the control appears in the toolbox and can be placed on a form

#### Scenario: A control nested under a contributed class
- **WHEN** a form nests a control under a class an add-in contributed
- **THEN** the form opens read-only and the save is refused, with the reason given
