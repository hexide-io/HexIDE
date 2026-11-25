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
