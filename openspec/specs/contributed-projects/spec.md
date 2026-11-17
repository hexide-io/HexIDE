# contributed-projects Specification

## Purpose
Let an add-in contribute a kind of project file it reads, such as an RDCore `.rdproj`, which the IDE then opens as a
project of its own model: modules and their kinds, references and folders, with code editing. The project file stays
the add-in's program's to write, and its membership the program's to change.

## Requirements

### Requirement: The file name SHALL choose a project's format
The IDE SHALL choose a project format by the file name or extension, never by sniffing the content.
The chosen format SHALL check the content and refuse content it cannot read, giving its reason;
it SHALL NOT fall through to a different format based on those bytes.

#### Scenario: Content does not match the selected file's format
- **WHEN** the chosen format cannot read the file's content
- **THEN** opening is refused with that format's reason rather than trying another format

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

### Requirement: Command-line project paths SHALL wait for add-in discovery and consent
The command-line parser SHALL accept any plain, non-option argument naming an existing file as a project path,
relative to the invocation directory. Consumed option values and arguments beginning with `-` SHALL retain their
existing parsing behaviour. Startup SHALL wait for add-in discovery before choosing a reader. When no type claims
the path, startup SHALL wait for the first-load consent pass after the main window opens, then use the shared open
route again. A file still unclaimed SHALL receive the localized no-reader message naming the file, without showing
the New Project dialog.

#### Scenario: Consent supplies the reader
- **WHEN** a command-line project file is unclaimed before first-load consent
- **AND** allowing the add-in contributes a type for the file
- **THEN** the shared open route asks that type to read the file after consent completes

#### Scenario: No reader after consent
- **WHEN** no type claims the command-line file after the consent pass
- **THEN** a localized message names the file and the New Project dialog is not shown

### Requirement: Every project file SHALL be read through one path
Open Project, a command-line project path, the recent projects list and a project group SHALL read a project file the same way: by a contributed
type that reads it (including VB6's registered type), or the archive service for its reserved files.
Any other file SHALL be refused with a localized reason, never read as a `.vbp`.

#### Scenario: A group lists a contributed project
- **WHEN** a `.vbg` names an `.rdproj` an add-in reads
- **THEN** the add-in reads it, as Open Project would have

#### Scenario: The add-in is not loaded
- **WHEN** the recent list names an `.rdproj` and no loaded add-in reads it
- **THEN** it is refused with a reason, and nothing opens

### Requirement: An absent first-party reader SHALL be named
A refusal SHALL name the file. When a first-party reservation covers it and its reader is absent, it SHALL
name that reservation's title and say that reader is not loaded. VB6's member patterns SHALL be kept as data
beside its project reservation, using VB6's unambiguous source extensions: `.cls` SHALL NOT name VB6, since a
LaTeX class file uses it too (#279). Only first-party readers
SHALL be named; HexIDE SHALL NOT claim that an unknown file is opened by an add-in.

#### Scenario: VB6 is not registered
- **WHEN** a `.vbp`, `.vbg` or VB6 member file is opened with no VB6 type registered
- **THEN** the refusal names "Visual Basic 6.0" and says it is not loaded

#### Scenario: A .cls with VB6 not registered
- **WHEN** a `.cls` is opened with no VB6 type registered
- **THEN** the refusal does not name Visual Basic 6.0, and reads as for any other unread file

### Requirement: An unknown project file SHALL explain the available readers
For any other unread file, the refusal SHALL name the file and list every registered type's project and group patterns in registration order,
then the archive reservation's patterns while the archive service is present. A `*.ext` pattern SHALL display
as `.ext`, a plain file name as itself, joined with `, `. With no registered type and no archive service, the
refusal SHALL say that no project type is loaded.

#### Scenario: An unknown file with readers loaded
- **WHEN** `notes.txt` is opened with VB6's type and the archive service present
- **THEN** the refusal names `notes.txt` and lists `.vbp, .vbg, .twinproj`
- **AND** registering another type adds its patterns without changing a translation

#### Scenario: No reader is loaded
- **WHEN** an unknown file is opened with no registered type and no archive service
- **THEN** the refusal names the file and says no project type is loaded

### Requirement: A contributed project SHALL open as a project of the IDE's model
Opening a file of a contributed type SHALL ask its add-in to read it, only then, and SHALL load what it returns as a
project: its standard and class modules from their files, each named by its `Attribute VB_Name`; its other files as
related documents; its references; and the folders it lists, which the Project Explorer SHALL show even when they hold
nothing. A file listed twice SHALL be loaded once, and a member whose name another member has SHALL be left out and
named. A host document's code module SHALL be loaded as a class module with its header kept, for reading and editing.
A read that fails SHALL open nothing and say why, with the add-in's message. The project SHALL record the add-in's
manifest id, and show its title.

#### Scenario: An .rdproj opens
- **WHEN** the user opens an `.rdproj` an add-in reads
- **THEN** its modules, references and folders are in the Project Explorer, and its code opens in code windows

### Requirement: An add-in's project type SHALL be able to carry forms
A project type SHALL be able to list a member as a form and supply what reads one, from the file's text and companion. The
IDE SHALL read the file and its companion, and SHALL ask the type's reader for the form, at open, at a reload of a changed
file and when restoring crash-recovery data alike. A form SHALL be named by what the reader gives it. A form that a type
with no reader lists, that its reader cannot read, or whose reader throws, SHALL be left out and named, with what the
reader said, and the project SHALL still open. A form read with problems SHALL join the project and the problems SHALL be
shown. Once the type has been taken out, a reload SHALL ask nothing and SHALL leave the form as it is.

A UserControl or a PropertyPage SHALL be listed and read the same way, and SHALL join the project as a module with its
designer half. UserControls SHALL be read before the other members, because a form can hold them.

A type MAY supply a format in place of a reader: a reader that also renders a form, a UserControl's or PropertyPage's file,
and the header over its code. Where it does, the IDE SHALL write the forms it read through that format, as it writes a
`.vbp`'s, and every render of one SHALL go through it: to save, to tell whether it was edited, to check that its file's code
page can hold it, to keep a copy for crash recovery and to refresh the header over its code. The IDE SHALL write the
companion before the text. It SHALL refuse a render that doesn't end with the code it was given, because the code window
takes the header to be the render less the code, and a companion the format names outside its file's folder, which it
SHALL NOT read either. A form that the format refuses as it reads it, or can't render once read, SHALL open read-only, with
the add-in's words where it gave any. A render that throws, or is refused, at a save SHALL end the save before anything is
written and say why.

Before an add-in is turned off, or its consent withdrawn, while a form its format writes has unsaved changes, the IDE SHALL
ask, as closing a project does. Save SHALL write them while the add-in can still write them, Don't Save SHALL let them go,
and Cancel SHALL leave the add-in as it was, as SHALL a save that fails. Once the type has been taken out, the IDE SHALL NOT
read or render through it again, and each of its forms SHALL be read-only. A form whose unsaved changes nobody was asked
about SHALL keep the copy crash recovery last took of it, and the user SHALL be told that what changed after that copy
wasn't kept.

Where the type only reads, a form it read SHALL open read-only in the designer and the code window, and a save SHALL refuse
it, say that the add-in that read it can't write it back, and leave its file as it was. The IDE's own writers are for its
own files, so it SHALL NOT render such a form as a `.frm` for any purpose, and the form SHALL NOT count as edited. Rendering
it through VB6's writer would change the file silently, which `docs/serialization-outcomes.md` forbids. VB6's own format is
a format like any other here, so a type MAY supply it, and what it writes then SHALL be byte for byte what the same
project writes when opened from its `.vbp`.

#### Scenario: A provider's form opens
- **WHEN** a project type lists a form and supplies only a reader
- **THEN** the form is in the Project Explorer and opens in the designer, read-only

#### Scenario: Saving a form an add-in read
- **WHEN** the user saves such a form
- **THEN** the save is refused, the user is told why, and its file is unchanged

#### Scenario: Saving a form a provider's format renders
- **WHEN** a project type supplies a format, and the user edits one of its forms and saves
- **THEN** the format renders the form, and the IDE writes its companion and then its text

#### Scenario: VB6's own format through a project type
- **WHEN** a project type supplies VB6's own format and lists a `.vbp` project's members, and the user saves its forms,
  UserControls and PropertyPages, edited or not
- **THEN** each file and its companion are byte for byte what the project's `.vbp` route writes for the same edit

#### Scenario: A render the IDE can't write
- **WHEN** at a save the format throws, or renders text that doesn't end with the form's code
- **THEN** nothing is written, and the user is told which form and which add-in, with what the add-in said

#### Scenario: The add-in is turned off with a form edited
- **WHEN** the user turns the add-in off, or withdraws its consent, while one of its forms has an unsaved edit
- **THEN** the IDE asks first: Save writes the form, Don't Save lets the edit go, and Cancel leaves the add-in on

#### Scenario: The add-in goes with nobody asked
- **WHEN** the type is taken out while one of its forms has an unsaved edit that nobody was asked about
- **THEN** its format isn't asked again, the form is read-only, crash recovery keeps the copy it last took, and the user is
  told that later changes weren't kept

### Requirement: The IDE SHALL write a contributed project's file only through its type's writer, and SHALL NOT make or run it
Saving a contributed project, Save As included, SHALL write its edited modules, and SHALL write its edited forms only
through its type's format. Where the type has no project-file writer, the save SHALL NOT write the project file, ask where
it goes, or add it to the recent list, and the file SHALL NOT count as edited. Where it has one, the IDE SHALL render the
project file through that writer and nothing else, and SHALL write it, in the encoding the writer names, only at the path
the project was opened from. Save Project As SHALL be unavailable when no loaded project offers moving its project file.
A save that would write a `.vbg` listing a contributed project's non-`.vbp` file SHALL refuse before any write,
as specified by [serialization-round-trip](../serialization-round-trip/spec.md). Standalone saves retain the writer
contract above. No member SHALL be written at the project's own path, whichever route names it.

Adding and removing members SHALL be offered only where the type's writer carries members. References SHALL be offered only
where the writer carries references. Project Properties SHALL offer name and description only where the writer carries
properties; Project Type and startup object SHALL be disabled and left unchanged when the dialog is applied. Components
SHALL NOT be offered. Whatever isn't offered SHALL be refused with a reason naming only the refused dialog's settings
from any other route, automation and crash recovery included.

A writer that throws SHALL end a save before any file is written, and the user SHALL be told which project and which
add-in, with what the add-in said. Elsewhere the IDE SHALL keep the last answer and the last crash-recovery copy the writer
gave. A save that finds the project file changed outside the IDE since the IDE last read or wrote it SHALL leave it as it
is where the IDE has no change of its own to write there, and otherwise SHALL ask whether to reload it, overwrite it or
cancel, writing nothing until the user chooses. The writer SHALL NOT be called once its type is withdrawn, and turning
its add-in off SHALL ask first about an unsaved project file, as it asks about forms.

The IDE's Make SHALL refuse a contributed project with a reason, and the IDE's interpreter SHALL NOT start it: its program
builds and runs it, through its add-in. The IDE SHALL NOT render it as a `.vbp`, for a language server's project import
or otherwise.

#### Scenario: Saving
- **WHEN** the user edits a module of a contributed project whose type writes no project file, and saves
- **THEN** the module's file is written and the project file is left as it was

#### Scenario: A member added through the type's writer
- **WHEN** a type's writer carries members, and the user adds a module and saves
- **THEN** the module's file is written, and then the project file as the writer renders it, at the path the project was
  opened from

#### Scenario: References carried by the writer
- **WHEN** the type's writer carries references, and the user changes References and saves
- **THEN** the project file includes those references as its writer renders them
- **AND** a writer without references leaves References refused with a reason

#### Scenario: Properties carried by the writer
- **WHEN** the type's writer carries properties, and the user changes the project's name or description and saves
- **THEN** the project file includes those properties as its writer renders them
- **AND** Project Type and startup object are disabled, and applying the dialog leaves both unchanged
- **AND** a writer without properties leaves Project Properties refused with a reason about properties alone

#### Scenario: Save Project As with contributed projects
- **GIVEN** contributed project files whose extensions are not `.vbp`
- **WHEN** only contributed projects are loaded
- **THEN** Save Project As is unavailable
- **WHEN** a VB6 project is loaded alongside a contributed project, and the user chooses Save Project As
- **THEN** only the VB6 project's file and the group ask for destinations
- **AND** writing the `.vbg` refuses before any file is written, naming non-`.vbp` members and explaining that VB6
  would open them as code modules ([oracle PR #4308](https://github.com/hexide-io/HexIDE/pull/4308))
- **AND** no contributed project is reported as saved in place
- **WHEN** a destination picker is dismissed
- **THEN** neither project file is written and no saved-in-place message is shown

#### Scenario: The project file changed outside the IDE
- **WHEN** the add-in's own program changed the project file since the IDE opened it, and the user saves changes of the
  IDE's own to it
- **THEN** the IDE asks whether to reload it, overwrite it or cancel, and writes nothing until the user chooses

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

The changes handle SHALL say when the project is closed or its open failed, without running the add-in's handlers on
the thread that closed it; reports after that SHALL be dropped. An exit that closes no project, as one forced without the
save prompt, ends the add-in with the process and SHALL NOT be signalled.

#### Scenario: The add-in's program adds a module
- **WHEN** the add-in reports a module added to an open contributed project
- **THEN** the module joins the project as one added from the IDE would

#### Scenario: Added then removed
- **WHEN** the add-in reports a module added and then the same module removed
- **THEN** the module is not in the project
