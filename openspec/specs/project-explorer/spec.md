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
Activating a loaded member in the tree SHALL open it — code-only members in the code editor, and members with a
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

The carried-file editor SHALL decode strictly as UTF-8, falling back to Latin-1 when UTF-8 cannot read
its bytes, and SHALL save in the encoding used to read it, preserving any UTF-8 byte-order mark.
It SHALL skip writing when the buffer matches the text last read or saved. An edit the selected encoding
cannot represent SHALL fail before replacing the original file, log a warning, and show the localized
source-encoding warning naming the document, code page, character, code point and offending line.
The buffer SHALL remain unsaved.

#### Scenario: A carried file is opened from the tree
- **WHEN** a carried file is opened from the tree
- **THEN** it opens in a plain text editor, with highlighting chosen from its extension

#### Scenario: A carried file contains invalid UTF-8
- **WHEN** a carried file contains a byte that is not valid UTF-8 and is saved after an edit elsewhere
- **THEN** that byte survives unchanged in the saved file

#### Scenario: A carried file is saved without a change
- **WHEN** the buffer matches the text last read or saved
- **THEN** saving does not write the file

#### Scenario: An edit cannot be saved in the carried file's encoding
- **WHEN** a carried Latin-1 file is edited to contain ✓ and saved
- **THEN** a warning notice names the file, code page 28591, ✓, U+2713 and the offending line
- **AND** the original bytes remain unchanged and the buffer remains unsaved

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

### Requirement: The tree's Add Form SHALL be the same operation as Project > Add Form
Adding a form from the tree's menu SHALL give it the next free name, write its file, add it to the project and open
its designer, exactly as the Project menu's Add Form does.

It was a second Add Form of its own, which added a form with no file and opened nothing (#468). A form with no file
was then left out of the project file when the IDE closed (#1359).

#### Scenario: Adding a form from the tree
- **WHEN** the developer chooses Add > Form on a project in the tree
- **THEN** the new form's file exists in the project's folder and its designer is open

### Requirement: Opening a project from the tree SHALL show what it contains
Activating a project in the tree SHALL open a read-only document describing that project: its name, type,
startup object, location, the members it holds with the kind of each, and the references it declares. A
description SHALL be shown only where the project has one.

The document SHALL describe the project as currently loaded rather than the contents of its file on disk,
and SHALL follow the project as it changes without re-reading that file. A project that has not been saved
has no file, and SHALL still be describable — its location SHALL say so rather than being left empty.

The document SHALL NOT offer to edit the project. A project has two authorities already — the file and the
loaded model — and an editable third view would have to reconcile them.

The project node is the only node in the tree with nothing to open, while being the node every other member
hangs from. What a developer wants from it is the answer VB6 never gave directly: what is in this thing.

#### Scenario: Opening a saved project
- **WHEN** the developer activates a project in the tree
- **THEN** a read-only document opens showing its members, references, startup object and file location

#### Scenario: Opening a project that has never been saved
- **WHEN** the developer activates a project with no file on disk
- **THEN** the document opens and reports that the project is not saved, rather than showing an empty location

#### Scenario: A member added while the document is open
- **WHEN** a member is added to a project whose document is open
- **THEN** the document shows it, without the project file being re-read

### Requirement: Activating a project SHALL NOT toggle its expansion
Activating a project in the tree SHALL open it and SHALL leave its expanded state unchanged. Expansion
SHALL remain available from the node's chevron.

VB6 spent the double-click on expand/collapse here. This diverges deliberately: the chevron already offers
expansion, and a gesture that leads somewhere is worth more than a second way to do what a control beside
it does. This is the Fidelity Principle's "intended behaviour, not the accident" applied to a gesture —
VB6's binding reflected the node having nothing to open, which is no longer true.

A project **group** node is not a project and keeps expand/collapse, because there is nothing for it to
open into yet.

#### Scenario: Expansion survives activation
- **WHEN** the developer activates an expanded project in the tree
- **THEN** the project opens and remains expanded

### Requirement: Preserved items SHALL remain visible
The tree SHALL include the project's preserved item lines alongside loaded members, using the directory
in which those lines were read to resolve their paths. Missing files SHALL have a missing-file overlay
and localized label; unsupported UserDocuments SHALL have a distinct unsupported overlay and localized
label. Both markers SHALL appear when an unsupported item's file is also missing. The original line
SHALL remain visible, and showing or activating it SHALL NOT load, edit or discard the preserved item.

#### Scenario: A missing form or module
- **WHEN** a declared form, standard module, class, UserControl or PropertyPage has no file on disk
- **THEN** its line is preserved without creating a live member, and its entry appears in the tree with the missing-file marker
- **AND** saving the project SHALL NOT create an empty member file

#### Scenario: An unsupported UserDocument
- **WHEN** a UserDocument entry is preserved rather than loaded
- **THEN** its entry appears with the unsupported marker, and also the missing-file marker if absent

#### Scenario: Save As leaves a preserved member at its original location
- **WHEN** a project is saved in a different directory
- **THEN** the preserved node still resolves against its original directory and is placed relative to the new project anchor

### Requirement: Selecting a module SHALL offer its name in the Properties window
Selecting a standard module, class module, UserControl or PropertyPage SHALL show a single `(Name)` row.
A rename SHALL apply the existing VB6 identifier and per-project document-name checks, excluding the document
itself. A refused edit SHALL report the reason and restore the row. A successful rename SHALL update the
module name and its `VB_Name`, including an open code window, without changing its file path. For a UserControl
or PropertyPage, the designer root SHALL follow the module name, and a committed designer root rename or undo
SHALL keep the module name in step.

#### Scenario: Rename a module selected in the tree
- **WHEN** the developer selects a module and commits a free valid name in Properties
- **THEN** the module and its code header use that name and its file path stays unchanged

#### Scenario: Refuse a name another document holds
- **WHEN** a module is renamed to a name another document in its project holds
- **THEN** the rename is refused with the existing naming message and the row shows its original name

#### Scenario: Rename a designer root
- **WHEN** a UserControl or PropertyPage root is renamed in its designer
- **THEN** its module name and `VB_Name` follow the root, including on designer undo

### Requirement: The form context menu SHALL save the selected form
The Project Explorer's Save and Save As commands SHALL call the same form-save service as the designer,
with Save As requesting a new path. The service flushes open buffers and applies its existing save gates.
These commands SHALL be available only for a selected form and SHALL NOT format its code on save.

#### Scenario: Save a selected form
- **WHEN** Save is chosen on a form in the Project Explorer
- **THEN** that form is passed to the form-save service with Save As disabled

#### Scenario: Save a selected form at a new path
- **WHEN** Save As is chosen on a form in the Project Explorer
- **THEN** that form is passed to the form-save service with Save As enabled

### Requirement: Loaded forms and modules SHALL be removable from the Explorer
The Explorer SHALL offer removal for a loaded form, standard module, class module, UserControl or property
page. Removal SHALL flush editor buffers and offer to save an unsaved member before removing it. Cancelling
the prompt or its save picker, or refusing the save, SHALL retain the member. Choosing No SHALL discard
unsaved edits from project membership without writing the file.

Successful removal SHALL close the member's code and designer editors, remove its tree node, and leave its
file on disk. Removing a saved member SHALL make the project changed, and its next save SHALL omit that
member's entry.
The Project menu SHALL offer the same removal for the selected Explorer member, falling back to the focused
editor member when the Explorer has no removable selection. Its header SHALL name that member and its
command SHALL be disabled when neither identifies a loaded member. Designers and related documents remain
outstanding under #835.

#### Scenario: Removing an unchanged saved module
- **WHEN** the developer removes an unchanged saved module through the Explorer
- **THEN** its tree node and code editor close without a save prompt
- **AND** the module file remains on disk and the next project save omits its entry

#### Scenario: Removing an unchanged adopted member
- **WHEN** a form, standard module, class, UserControl or property page is added from an existing file and removed without edits
- **THEN** it SHALL compare as unchanged and remove without a save prompt, just as a project-loaded member does
- **AND** its file remains on disk

This is HexIDE consistency (#2644); VB6's removal behavior for an adopted member is unmeasured.

#### Scenario: Cancelling an unsaved member's removal
- **WHEN** the developer cancels the save prompt or the save picker while removing an unsaved form or module
- **THEN** the member remains in the project and its editors remain open

#### Scenario: Removing the member named by the Project menu
- **WHEN** the developer chooses Project → Remove for a selected Explorer member or focused editor member
- **THEN** the member named in the header goes through the same save/cancel flow as Explorer removal
- **AND** the menu item is disabled when neither selection nor focus identifies a loaded removable member
