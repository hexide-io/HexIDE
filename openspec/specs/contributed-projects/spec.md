# contributed-projects Specification

## Purpose
Let an add-in contribute a kind of project file it reads, such as an RDCore `.rdproj`, which the IDE then opens as a
project of its own model: modules and their kinds, references and folders, with code editing. The project file stays
the add-in's program's to write, and its membership the program's to change.

## Requirements

### Requirement: An add-in SHALL be able to contribute a project type
The host surface SHALL let an add-in register a project type: a title, the file names or `*.ext` patterns it reads, and
what reads a file of it. The names SHALL be copied at registration. A type SHALL NOT read a file HexIDE's own project
formats or another type already read. Disposing the registration, or a failed initialisation of its add-in, SHALL take
the type out and call off an open of it in progress; a project of it that is open stays open. Open Project SHALL offer
each type's names as contributed, a whole file name as that name.

#### Scenario: A contributed type is offered
- **WHEN** an add-in contributes a type for `.rdproj`
- **THEN** Open Project offers files named `.rdproj`, and opening one asks the add-in to read it

#### Scenario: A type claims HexIDE's own files
- **WHEN** an add-in contributes a type for `*.vbp`
- **THEN** the registration is refused

### Requirement: Every project file SHALL be read through one path
Open Project, the recent projects list and a project group SHALL read a project file the same way: by a contributed
type that reads it, then as a `.vbp`. Any other file SHALL be refused with a reason, never read as a `.vbp`.

#### Scenario: A group lists a contributed project
- **WHEN** a `.vbg` names an `.rdproj` an add-in reads
- **THEN** the add-in reads it, as Open Project would have

#### Scenario: The add-in is not loaded
- **WHEN** the recent list names an `.rdproj` and no loaded add-in reads it
- **THEN** it is refused with a reason, and nothing opens

### Requirement: A contributed project SHALL open as a project of the IDE's model
Opening a file of a contributed type SHALL ask its add-in to read it, only then, and SHALL load what it returns as a
project: its standard and class modules from their files, each named by its `Attribute VB_Name`; its other files as
related documents; its references; and the folders it lists, which the Project Explorer SHALL show even when they hold
nothing. A file listed twice SHALL be loaded once, and a module whose name another member has SHALL be left out and
named. A host document's code module SHALL be loaded as a class module with its header kept, for reading and editing.
A read that fails SHALL open nothing and say why, with the add-in's message. The project SHALL record the add-in's
manifest id, and show its title.

#### Scenario: An .rdproj opens
- **WHEN** the user opens an `.rdproj` an add-in reads
- **THEN** its modules, references and folders are in the Project Explorer, and its code opens in code windows

### Requirement: The IDE SHALL NOT write, make or run a contributed project
Saving a contributed project, Save As included, SHALL write its edited modules and SHALL NOT write the project file,
ask where it goes, or add it to the recent list; the file SHALL NOT count as edited. Adding or removing its members,
Project Properties, References and Components SHALL NOT be offered, and SHALL be refused with a reason from any other
route. The IDE's Make SHALL refuse it with a reason, and the IDE's interpreter SHALL NOT start it: its program builds and
runs it, through its add-in.

#### Scenario: Saving
- **WHEN** the user edits a module of a contributed project and saves
- **THEN** the module's file is written and the project file is left as it was

#### Scenario: Make
- **WHEN** Make reaches a contributed project
- **THEN** it is refused with a reason before the IDE asks where to put the program

### Requirement: The add-in SHALL be able to report changes below the level of the project
While a contributed project is open, its add-in SHALL be able to report that a member was added, removed or moved, or
that its references changed. Each report SHALL be applied on the UI thread, once the one reported before it is, as the
same change to a `.vbp` project would be, so the Project Explorer, the language-server workspace and the file watcher
follow it:
- a member removed SHALL be unloaded as Remove unloads one, its window closed and the language server told, after an
  offer to save its unsaved edits;
- a member moved SHALL keep its unsaved edits unsaved, and its window and the language server SHALL take its new name.

The changes handle SHALL say when the project is closed or its open failed; reports after that SHALL be dropped.

#### Scenario: The add-in's program adds a module
- **WHEN** the add-in reports a module added to an open contributed project
- **THEN** the module joins the project as one added from the IDE would

#### Scenario: Added then removed
- **WHEN** the add-in reports a module added and then the same module removed
- **THEN** the module is not in the project
