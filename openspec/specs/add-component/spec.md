# add-component Specification

## Purpose
Define adding a new component — a module, class, form, user control or property page — to the open project.

Creating one by hand means knowing the file format, the naming convention, and the entry the project file
needs. All of that is reconstructible and none of it is interesting, which is precisely the kind of work an
IDE exists to absorb. It is also the first thing a developer does after opening a project, so it is an early
and unforgiving impression.

## Requirements
### Requirement: The IDE SHALL offer adding each supported component kind
The IDE SHALL provide commands to add a standard module, a class module, a form, a user control and a
property page to the current project.

These five are the component kinds a HexIDE project can contain, so the menu covers the model exactly. VB6's
menu also offered kinds that depended on technologies HexIDE deliberately does not carry — an MDI parent
form, browser-hosted documents, and third-party designer plug-ins. Those are absent rather than disabled:
offering a command that cannot work is worse than not offering it.

#### Scenario: Adding each kind
- **WHEN** the developer adds any of the five supported kinds
- **THEN** a component of that kind is created and added to the project

### Requirement: A new component SHALL be named by the convention VB6 used
A new component SHALL receive a unique default name formed from its kind and the next unused number in that
kind's sequence.

`Form1`, `Module2`, `Class3` is the sequence a VB6 developer expects, and expecting it is the point — the
name appears immediately in the project tree and in code that refers to it. Numbering per kind rather than
globally is what keeps the sequence predictable when several kinds are added in one session.

#### Scenario: Adding a second module
- **WHEN** a project already contains a standard module named for the first in the sequence
- **THEN** the next standard module added takes the following number

#### Scenario: Adding different kinds
- **WHEN** components of different kinds are added
- **THEN** each is numbered within its own kind's sequence

### Requirement: A new component SHALL be written to disk as it is created
Creating a component SHALL write its file immediately, alongside the project, and add it to the project.

Holding a new component only in memory leaves it without a location, which everything downstream needs —
saving the project, referring to it by path, and any tool acting on the file. Writing it at creation means
there is never a state where the project references something that does not exist yet.

#### Scenario: Adding a component to a saved project
- **WHEN** a component is added to a project that has been saved
- **THEN** its file exists alongside the project and the project references it

### Requirement: A new component SHALL NOT replace a file already where its own would be written
A new component's default name SHALL pass over any number whose file is already in the project's folder, or
is named by the project file for a member that did not load. For a form, user control or property page, the
`.frx`, `.ctx` or `.pgx` beside it counts too. A name the caller chose SHALL instead be refused, with nothing
written.

Writing at creation makes the name a path, and VB6 projects often share a folder. Writing over another
project's file destroys it silently (#738).

#### Scenario: Another project's module in the folder
- **GIVEN** a saved project whose folder already holds `Module1.bas`, which the project does not reference
- **WHEN** a standard module is added
- **THEN** it is named `Module2`, and `Module1.bas` is unchanged

#### Scenario: A binary file alone in the folder
- **GIVEN** a saved project whose folder holds `Form1.frx` but no `Form1.frm`
- **WHEN** a form is added
- **THEN** it is named `Form2`

#### Scenario: A name chosen by the caller
- **WHEN** automation adds a module named `Module1` to a project whose folder already holds `Module1.bas`
- **THEN** the add is refused, the reply names the file, and nothing is written or added

### Requirement: A new component SHALL open for editing in the surface that suits it
After creation the component SHALL open for editing — code-only kinds in the code editor, and kinds with a
visual definition on the design surface.

Adding a component is never the goal in itself; the developer added it in order to put something in it.
Opening it removes a step, and opening it in the right surface is what makes the command feel like it
understood the request rather than merely creating a file.

#### Scenario: Adding a standard module
- **WHEN** a standard module is added
- **THEN** it opens in the code editor

#### Scenario: Adding a form or user control
- **WHEN** a form or user control is added
- **THEN** it opens on the design surface

### Requirement: A new form SHALL be written as VB6 writes one
A new form's file SHALL be CRLF throughout and end with a line break. Its code SHALL open with the five
`Attribute` lines VB6 writes for a form, naming the form, and its designer block SHALL say
`StartUpPosition = 3`. A new user control or property page SHALL take neither the form's `Attribute` block nor a
`StartUpPosition` line.

VB6 reads a missing `StartUpPosition` as Manual, which opens the form at the screen's origin (#932). The
`Attribute` block is what VB6 writes for a form (#516).

#### Scenario: Adding a form
- **WHEN** a form named `Form2` is added
- **THEN** its file has no bare line feed, ends with CRLF, says `StartUpPosition = 3 'Windows Default`, and its
  code opens with `Attribute VB_Name = "Form2"` and the other four lines VB6 writes

#### Scenario: Adding a user control
- **WHEN** a user control is added
- **THEN** its file has no `StartUpPosition` line

### Requirement: The commands SHALL be unavailable without a project
The add commands SHALL be unavailable when no project is open.

There is nothing to add to. Leaving them enabled would mean either failing when invoked or silently
inventing a project, and both are worse than a greyed-out menu item that says the prerequisite is missing.

#### Scenario: No project open
- **WHEN** no project is open
- **THEN** the add commands are unavailable
