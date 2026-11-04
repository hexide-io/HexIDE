# contributed-projects Specification

## Purpose
Let an add-in contribute a kind of project file it reads, such as an RDCore `.rdproj`, which the IDE then opens as a
project of its own model: modules and their kinds, references and folders, with code editing. The project file stays
the add-in's program's to write, and its membership the program's to change.

## Requirements

### Requirement: An add-in SHALL be able to contribute a project type
The host surface SHALL let an add-in register a project type: a title, the file names or `*.ext` patterns it reads, and
what reads a file of it. A type SHALL NOT read a file HexIDE's own project formats or another type already read.
Disposing the registration, or a failed initialisation of its add-in, SHALL take the type out; a project of it that is
open stays open.

#### Scenario: A contributed type is offered
- **WHEN** an add-in contributes a type for `.rdproj`
- **THEN** Open Project offers those files, and opening one asks the add-in to read it

#### Scenario: A type claims HexIDE's own files
- **WHEN** an add-in contributes a type for `*.vbp`
- **THEN** the registration is refused

### Requirement: A contributed project SHALL open as a project of the IDE's model
Opening a file of a contributed type SHALL ask its add-in to read it, only then, and SHALL load what it returns as a
project: its standard and class modules from their files, each named by its `Attribute VB_Name`; its other files as
related documents; its references; and the folders it lists, which the Project Explorer SHALL show even when they hold
nothing. A host document's code module SHALL be loaded as a class module with its header kept, for reading and
editing; the IDE SHALL NOT run or instantiate it as a document. A read that fails SHALL open nothing and say why, with
the add-in's message.

#### Scenario: An .rdproj opens
- **WHEN** the user opens an `.rdproj` an add-in reads
- **THEN** its modules, references and folders are in the Project Explorer, and its code opens in code windows

### Requirement: A contributed project's file SHALL NOT be written by the IDE
Saving a contributed project SHALL write its edited modules and SHALL NOT write the project file, which SHALL NOT count
as edited. Adding or removing its members from the IDE SHALL NOT be offered, and a removal that reaches the project
service SHALL be refused with a reason.

#### Scenario: Saving
- **WHEN** the user edits a module of a contributed project and saves
- **THEN** the module's file is written and the project file is left as it was

### Requirement: The add-in SHALL be able to report changes below the level of the project
While a contributed project is open, its add-in SHALL be able to report that a member was added, removed or moved, or
that its references changed. Each report SHALL be applied, on the UI thread, as the same change to a `.vbp` project
would be, so the Project Explorer, the language-server workspace and the file watcher follow it. Reports for a project
since closed SHALL be dropped.

#### Scenario: The add-in's program adds a module
- **WHEN** the add-in reports a module added to an open contributed project
- **THEN** the module joins the project as one added from the IDE would
