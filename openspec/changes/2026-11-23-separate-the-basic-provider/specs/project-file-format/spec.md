## ADDED Requirements

### Requirement: A project's format answers the VB family's code-text rules
The IDE SHALL ask a project's format for three VB-specific rules: the leading `Attribute` block of a code section, the
module name a preserved header gives, and whether a file is a designer file. A format with no VB headers SHALL answer
an empty block, the file's own name and false. The IDE SHALL name none of the Basic provider's text helpers (#5637).

#### Scenario: A format with no VB headers
- **WHEN** a format that implements none of the three rules is asked for them
- **THEN** it answers an empty block, the fallback name and false

#### Scenario: A VB6 module is added
- **WHEN** a `.bas` whose header names `VB_Name = "Utilities"` is added to a VB6 project
- **THEN** the module is named `Utilities`, as VB6's format reads the header

#### Scenario: An add-in project's member with no member format
- **WHEN** an add-in's project lists a code module and no registered format reads its members
- **THEN** the module's whole text is kept as its code, with no header split

### Requirement: A new form, designer root or menu item takes its class from the project's format
The IDE SHALL ask the project's format for the class of a form, UserControl or PropertyPage it adds, and of a menu
item the Menu Editor adds. Where the format gives none, or gives a class in another role, the IDE SHALL refuse the add,
say why, and change nothing. The IDE SHALL name none of the Basic provider's form or menu classes (#5723).

#### Scenario: A format that makes no UserControls
- **WHEN** a UserControl is added to a project whose format gives no class for `VB.UserControl`
- **THEN** the add is refused with a message naming the project and `VB.UserControl`
- **AND** the project is unchanged

### Requirement: A project template is told the format its project is made for
The IDE SHALL make a new project from a template by telling it the format the project will have, because the IDE gives
the project that format only after the template returns it. A template that ignores the format SHALL make the project
it makes by name alone (#5723).

#### Scenario: The Standard EXE template
- **WHEN** a Standard EXE project is made
- **THEN** its Form1 has the class VB6's format gives for `VB.Form`

#### Scenario: A template written before formats were passed
- **WHEN** a template that implements only `Create(name)` makes a new project
- **THEN** the new project is the one it makes by name
