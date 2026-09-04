# project-explorer Specification

## Purpose
Define the project tree: what it shows, where the structure comes from, and the one place it deliberately
departs from VB6.

The project file is permissive about where members live — a form in a subdirectory and a module reached by
traversing out of the project directory are both legal, and both occur in real projects. A tree that hides
that arrangement leaves the developer unable to see the shape of their own project from inside the IDE.

## Requirements

### Requirement: The tree SHALL show members in their filesystem hierarchy
The tree SHALL present the project's members beneath the project file, arranged by their location on disk.

VB6 grouped members into virtual folders by component type instead, which told the developer something they
could already see from each item's icon while hiding something they could not see at all. Showing the real
arrangement means the tree answers "where does this live", which is the question a project file's
flexibility creates.

#### Scenario: Members in subdirectories
- **WHEN** a project has members in subdirectories
- **THEN** the tree shows them nested under those directories

#### Scenario: A flat project
- **WHEN** every member sits beside the project file
- **THEN** the tree is flat, because that is the arrangement

### Requirement: The tree SHALL be derived from project membership, never from scanning disk
Every node SHALL come from a member the project declares. Files that exist beside the project but are not
members SHALL NOT appear, and no node SHALL exist without a member behind it.

Membership is what the project *is* — a file sitting in the directory is not part of the build unless the
project says so, and showing it implies otherwise. Deriving strictly from membership also means the tree
cannot show an empty directory or drift from the project file, and it avoids needing to watch the
filesystem recursively to stay correct.

#### Scenario: A non-member file beside the project
- **WHEN** a file that the project does not reference sits in the project directory
- **THEN** it does not appear in the tree

#### Scenario: A member removed from the project
- **WHEN** a member is removed from the project
- **THEN** it disappears from the tree whether or not its file still exists

### Requirement: Members outside the project's directory SHALL be shown at the root with their path
Where a member lives outside the project file's directory, it SHALL appear at the root of the tree with its
relative location visible in its caption rather than as a chain of parent folders.

Rendering a traversal out of the project as ascending folder nodes would put the tree's root somewhere above
the project, which misrepresents what the project is and grows the tree with directories the developer does
not think of as theirs. Placing such members at the root, labelled with where they actually are, keeps the
project as the root while still telling the truth about the file.

#### Scenario: A member reached by traversing out of the project directory
- **WHEN** a member lives outside the project file's directory
- **THEN** it appears at the tree's root, with its relative location shown in the caption

### Requirement: Opening a member from the tree SHALL open it appropriately
Activating a member in the tree SHALL open it — code-only members in the code editor, and members with a
visual definition on the design surface.

The tree is the primary way into a project's files, so the gesture has to land somewhere useful. Choosing
the surface by kind is what makes a single gesture correct for every member instead of opening a form's
source when the developer wanted its layout.

#### Scenario: Opening a form from the tree
- **WHEN** the developer activates a form in the tree
- **THEN** it opens on the design surface

#### Scenario: Opening a module from the tree
- **WHEN** the developer activates a standard or class module
- **THEN** it opens in the code editor

### Requirement: A project member MAY be a file the project does not compile
The tree SHALL show files a project carries without compiling them, alongside its forms and modules, and
SHALL derive them from project membership like any other member rather than by scanning disk.

Such a member SHALL be distinguishable from a code member in the tree, and SHALL take part in the filesystem
hierarchy and the out-of-cone location caption on the same terms as the others. A carried file is the member
kind most likely to live outside the project's directory — notes and specs commonly sit a level up from the
source — so it must not be a special case there.

#### Scenario: A carried file appears in the tree
- **WHEN** a project holds a file it carries but does not compile
- **THEN** that file appears in the tree as a member, shown as a carried file rather than as code

#### Scenario: A carried file outside the project directory
- **WHEN** a carried file's location is outside the project's own directory
- **THEN** it appears with its location shown, exactly as an out-of-cone form or module does

### Requirement: Opening a member SHALL route by what the member is
The IDE SHALL open a member in the editor that suits its kind: a form in its designer, a code module in the
code editor, and a carried file in a plain text editor.

The text editor SHALL choose highlighting from the file's own extension, and SHALL apply none when the
extension is unrecognised — an unhighlighted text file is the correct outcome, not a failure. It SHALL NOT
present a designer, procedure navigation, breakpoints, a faithfulness gate or companion-binary handling,
because none of those describe a text file.

Routing a carried file to the VB6 code editor is the failure this forbids: that editor assumes a form or
module model and a VB6 language server, and a document rendered with VB6 colouring looks broken in a way a
reader attributes to the file.

#### Scenario: A carried file is opened from the tree
- **WHEN** a carried file is opened from the tree
- **THEN** it opens in a plain text editor, with highlighting chosen from its extension

#### Scenario: A carried file that cannot be read
- **WHEN** a carried file cannot be read from disk
- **THEN** the editor opens showing the reason and does not present an empty editable buffer

An editor that silently opens empty on a failed read invites the developer to type into it and save over
whatever was actually there.

### Requirement: An existing file SHALL be addable to a project
The IDE SHALL provide a gesture that adds a file already on disk to the project, accepting more than one file
at a time, and SHALL open each added file in the editor its kind calls for.

The kind SHALL be decided from the file's extension. A file recognised as VB6 source SHALL join as the
corresponding form or module; **every other file SHALL join as a carried file**. The default SHALL be the
carried outcome, because a carried file is read and written verbatim: mis-filing VB6 source as carried costs
a designer the developer adds again, while mis-filing a document as source hands it to the header writer.

The file SHALL join where it lies. It SHALL NOT be copied, moved or rewritten, so a file from outside the
project's directory is a supported outcome rather than an error.

A file the project already carries SHALL NOT be added a second time; the IDE SHALL open the existing member
instead. Path comparison SHALL follow the host filesystem's own case rule rather than assuming one, since
assuming either answer everywhere causes a file to be carried twice or refused wrongly.

#### Scenario: A document is added
- **WHEN** a file that is not VB6 source is added to a project
- **THEN** it joins as a carried file and opens in the plain text editor

#### Scenario: Source is added
- **WHEN** a file recognised as VB6 source is added to a project
- **THEN** it joins as the form or module its extension names, and opens in the editor for that kind

#### Scenario: A file the project already carries is added again
- **WHEN** a file already held by the project is chosen again
- **THEN** no second member is created, and the existing one is opened

#### Scenario: Source that cannot be parsed
- **WHEN** a file offered as a form cannot be parsed
- **THEN** nothing is added to the project

A member whose file was never understood is worse than a refused add: the tree shows a node and the project
file gains a line for something the IDE cannot open.
