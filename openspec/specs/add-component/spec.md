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

### Requirement: The commands SHALL be unavailable without a project
The add commands SHALL be unavailable when no project is open.

There is nothing to add to. Leaving them enabled would mean either failing when invoked or silently
inventing a project, and both are worse than a greyed-out menu item that says the prerequisite is missing.

#### Scenario: No project open
- **WHEN** no project is open
- **THEN** the add commands are unavailable
