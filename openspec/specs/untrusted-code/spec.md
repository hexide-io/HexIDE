# Untrusted code

## Purpose
HexIDE runs a project's code with the developer's own rights and none of Office's macro security. This capability is
the question asked before code that came from outside the computer runs: a file the operating system marks as
downloaded, or a project read from an Office document (#1121). It covers what marks a file on each platform, which
routes ask, and what an answer does. The add-in boundary is `addin-trust`'s. The user-facing account, with the gaps,
is `docs/untrusted-code.md`.

## Requirements

### Requirement: Opening a project SHALL run none of its code
Opening a project, a group or a file of one SHALL NOT run any of its code: no `Form_Load`, `Sub Main`,
`Class_Initialize` or `UserControl_Initialize`, and nothing like Office's `Workbook_Open` or `AutoOpen`. Drawing a form
or a UserControl in the designer SHALL NOT run its code either.

#### Scenario: A project whose code writes a file
- **WHEN** a project whose `Form_Load` and `Sub Main` each write a file is opened
- **THEN** no file is written

### Requirement: Code from outside this computer SHALL run only once the developer says so
Before a project's first run in a session, HexIDE SHALL ask whether to run it when any file of its code came from
outside this computer. The question SHALL name the project and each such file with where it came from, SHALL say the
code runs with the developer's rights, and SHALL have No as its default button. It SHALL be asked before the run is
claimed or the program built.

#### Scenario: One downloaded module
- **WHEN** F5 starts a project of which only `Module1.bas` is marked as downloaded
- **THEN** HexIDE asks, naming `Module1.bas`, before anything runs

#### Scenario: A project nothing marks
- **WHEN** F5 starts a project none of whose files is marked and which no Office document holds
- **THEN** it runs without asking

### Requirement: Every route that runs the code SHALL ask
The question SHALL be asked by every route that runs a project's code: F5, F8, Run To Cursor, Start With Full Compile,
Restart, an add-in's launch and Run with VB6. Starting an external debug session SHALL ask when its launch file is
itself marked as downloaded, because a launch file names the program it starts.

#### Scenario: Run with VB6
- **WHEN** Run with VB6 is chosen for a project with a downloaded module
- **THEN** HexIDE asks before VB6 makes or runs it

#### Scenario: A downloaded launch file
- **WHEN** an external debug session is started from a launch file marked as downloaded
- **THEN** HexIDE asks before the session starts

### Requirement: An answer SHALL hold for the session and no longer
A Yes SHALL let those files run for the rest of the session without asking again, in any project that includes them. A
No SHALL start nothing, claim nothing and leave no idle Run To Cursor armed, and the next start SHALL ask again. A
start while the question is up SHALL NOT ask a second time. Nothing SHALL be remembered past the session.

#### Scenario: No
- **WHEN** the developer answers No
- **THEN** nothing runs, and the next start asks again

#### Scenario: A file marked after a Yes
- **WHEN** a downloaded module is added to a project the developer has already allowed to run
- **THEN** the next start asks about that module alone

### Requirement: A file's mark SHALL be read when HexIDE reads the file
HexIDE SHALL read a file's mark when it reads the file, on open, reload and Add File, and again before a run. A mark
seen once SHALL stand for the session, through a save and a Save As, because a save writes a new file that carries no
mark. A later session SHALL take each file as it then finds it.

#### Scenario: Saved before running
- **WHEN** a downloaded module is edited and saved, and F5 then starts its project in the same session
- **THEN** HexIDE asks about the module

### Requirement: What marks a file SHALL follow the platform
On Windows, a file SHALL be marked when its `Zone.Identifier` stream names zone 3 or 4, names no zone that can be read,
or is there but can't be read. On macOS, the `com.apple.quarantine` attribute SHALL mark a file. Linux has no mark, so
no file SHALL be marked there. A project SHALL be taken as read from an Office document when its own file has a Word,
Excel, PowerPoint, Access or Visio document's extension.

#### Scenario: A stream naming the Internet zone
- **WHEN** a file's `Zone.Identifier` stream reads `ZoneId=3`
- **THEN** the file is marked as downloaded

#### Scenario: A stream naming this computer
- **WHEN** a file's `Zone.Identifier` stream reads `ZoneId=0`
- **THEN** the file is not marked

### Requirement: The question SHALL lead with HexIDE's own words
The question SHALL open with HexIDE's own sentence, that the code came from the internet or another computer, and SHALL
give the project's or launch file's name only after it, quoted, on a line of its own. A project names itself, so its name
SHALL NOT be able to read as the question's opening. Every name shown SHALL be on one line, with a newline or other
control character read as a space, formatting characters dropped, and the project's name cut at 60 characters.

#### Scenario: A project named as reassurance
- **WHEN** a downloaded project's name is `Invoices`, a newline, and "Verified safe by Microsoft."
- **THEN** the question opens with HexIDE's sentence, and the name follows it on one quoted line, cut at 60 characters

### Requirement: The question SHALL show only names, made safe to show
The question SHALL NOT show what a mark names as the file's source, such as its `HostUrl`, which whoever supplied the
file wrote. It SHALL drop formatting characters from every name it shows, so that no name can reverse or hide part of
itself.

#### Scenario: A file name with a right-to-left override
- **WHEN** a downloaded file's name holds a right-to-left override character
- **THEN** the question shows the name without it

### Requirement: Automation SHALL be told a start is waiting on the person
A tool that starts a run SHALL reply, while the question is up, that the run has not started, that a security prompt is
open, which files it names, and that the answer is the person's: the run starts by itself if they allow it, and nothing
runs if they decline. The reply SHALL NOT say how to answer the prompt, so that it never coaches an automated caller
towards allowing downloaded code on the person's behalf.

#### Scenario: run_project on a downloaded project
- **WHEN** `run_project` starts a project with a downloaded module
- **THEN** the reply names the module, says the decision is the person's, and doesn't say how to answer the prompt
