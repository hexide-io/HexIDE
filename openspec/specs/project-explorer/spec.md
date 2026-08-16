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
