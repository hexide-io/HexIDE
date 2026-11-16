# Project file format

## Purpose
How the IDE reaches what a project's files say: reading and rendering them, what a project's type offers, the read-only
text in a document, what the code window asks about a module's text, and the form designer's control classes. Each is
reached through one interface, bound once where the IDE is composed. These interfaces are among the seams by which the
interpreter and the run-time forms engine can move out to an optional provider (#3411), and they let another
implementation stand behind any of them. Which parts move is decided on #3411, not here. An add-in's own project type is
specified in `contributed-projects`, and what the code window does with read-only text in `code-editor`.

## Requirements

### Requirement: A project's files SHALL be read and rendered only through the project file format
The IDE SHALL read a project's file, its group's file and its members, and SHALL render each of them, only through the
project file format (`IProjectFileFormat`), bound once where the IDE is composed (#3564). No other part of the IDE SHALL
name a built-in reader or writer.

#### Scenario: Opening a project
- **WHEN** the user opens a `.vbp`
- **THEN** the project, and every member its file names, is read through the format

### Requirement: A format SHALL only render, and the IDE SHALL write
A format SHALL render a file's text and companion and SHALL write nothing: the IDE SHALL do the writing itself, atomically,
with the companion before the text (#3564). A render SHALL change nothing, and the same model SHALL render the same text,
because a document counts as edited when a fresh render differs from the last.

#### Scenario: Saving a form
- **WHEN** the user saves a form
- **THEN** the format renders its text and companion, and the IDE writes the companion first and then the text, each
  atomically

#### Scenario: Checking whether a project is edited
- **WHEN** the IDE checks whether a document has unsaved changes, captures it for crash recovery, or hands a project's
  text to a language server
- **THEN** it renders through the same format, and nothing is written

### Requirement: The IDE SHALL ask a project's type what it offers
The IDE SHALL decide what it can do with a project from what the project's type offers (`ProjectOffers`), and SHALL NOT ask
which add-in opened it (#3598). The offers SHALL cover saving and moving the project's own file, rendering its forms,
making a program and running it, changing its members, and Project Properties, References and Components. A project the
IDE read itself SHALL offer all of them; an add-in's type SHALL offer what its writer carries (`contributed-projects`).

#### Scenario: A project whose type offers no run
- **WHEN** a project's type doesn't offer running it
- **THEN** F5 doesn't start it through the IDE's run engine, whoever opened the project

#### Scenario: A `.vbp`
- **WHEN** the IDE reads a `.vbp` itself
- **THEN** every offer is available

### Requirement: The code window SHALL read a document's read-only text through the format
The code window SHALL compose a form's or module's text as the format's prefix followed by its code, asking the format for
that prefix (#3731). It SHALL read the regions only the IDE may change through the region reader (`IReadOnlyRegionReader`),
as positions alone, with no knowledge of what their text says (#3624, #3668). What it does with those regions is specified
in `code-editor`.

#### Scenario: A form's code window
- **WHEN** a form's code window opens
- **THEN** its text is the format's prefix for that form followed by the form's code, and its read-only regions are the
  positions the reader gives

### Requirement: Writes to read-only text SHALL follow the format's rules and writer
The IDE SHALL decide writes into read-only regions through the format's rules (`IReadOnlyRegionRules`), and SHALL let the
format name its read-only text in refusals and save notes (#3680, #3716). Where the format decides nothing, the IDE SHALL
accept incoming text only when the regions keep their text and order (#3732). The IDE's own changes to that text, what a
save rewrites and the name line a rename writes, SHALL come from the region writer (`IReadOnlyRegionWriter`) (#3694).

#### Scenario: A save rewrites a member's attributes
- **WHEN** a save brings a member's attribute lines into step with its code
- **THEN** the region writer supplies the change, inside a region the reader marks for the same text, and Undo in the code
  window skips it

#### Scenario: A replacement keeps the header
- **WHEN** an add-in replaces a document's whole text, and the format's rules decide nothing
- **THEN** the IDE accepts it only if every read-only region keeps its text and order

### Requirement: The code window SHALL ask about a module's text without running it
The code window SHALL colour parentheses from the bracket reader (`IBracketReader`), which SHALL leave out any in a string
or a comment (#3572). The code window and the archive view SHALL grey the lines conditional compilation leaves out, as the
conditional-code reader (`IConditionalCodeReader`) gives them, and that reader SHALL NOT run or change the module (#3559).
A function call in an `#If` SHALL read as 0, and a block whose branch can't be decided SHALL NOT be greyed.

#### Scenario: A parenthesis in a string
- **WHEN** a line holds `MsgBox "(" & Len(x)`
- **THEN** only the parentheses of `Len(x)` are coloured

#### Scenario: An `#If` that can't be decided
- **WHEN** an `#If` condition comes to Null
- **THEN** neither of its branches is greyed

### Requirement: The form designer SHALL reach a control class and its picture through the designer's interfaces
The form designer SHALL reach what it asks of a control class through `IDesignerComponentClass`, and SHALL NOT reach the
class that also runs the control (#3482). It SHALL ask for a control's picture through one call (`IDesignerPictures.Draw`)
and SHALL show the pixels that come back. A control that refuses to draw SHALL be shown with its message in its place
(#3505).

#### Scenario: A control that can't draw
- **WHEN** a control's class throws while drawing its picture
- **THEN** the designer shows the message where the control would be

### Requirement: The in-box controls SHALL come from the component registry
The toolbox, the Components dialog and `add_control` SHALL take the in-box control classes and libraries from the
component registry (`IComponentRegistry`) (#3601). A class an add-in registers SHALL join the toolbox after them, and
leave it when its registration is disposed.

#### Scenario: An add-in registers a control
- **WHEN** an add-in registers a control class
- **THEN** the toolbox lists it after the in-box controls, and it leaves the toolbox when the add-in's registration is
  disposed
