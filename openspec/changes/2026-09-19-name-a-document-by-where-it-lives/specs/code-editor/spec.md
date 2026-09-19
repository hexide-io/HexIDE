## Purpose
Define what the code window holds and which parts of it the developer may change.

A VB6 source file is more than its code. A class opens with a block that says what kind of class it is; a form
carries its entire layout above its first line of code; attribute lines bind members to descriptions, help
topics and dispatch ids. VB6 hid all of it. HexIDE shows it — so that the text the editor holds is the text of
the file, and a position means the same thing to the editor, the debugger, the compiler and every language
server — and protects it, because every line of it is load-bearing and none of it is the developer's to type
over.

That is a deliberate divergence from VB6, and it follows the project's fidelity principle rather than
departing from it: what VB6 intended was that the developer not edit these lines, and that is reproduced;
that the developer could not see them was a limitation of its code window, and that is not.

## ADDED Requirements

### Requirement: The code window SHALL hold the whole file
The code window for a form, module, class, UserControl or PropertyPage SHALL hold the file's complete text:
its header followed by its code. The header SHALL be the file's own text until the document's model changes,
and thereafter the text the next save will write.

The header is the class `VERSION` and `BEGIN … END` block, or the `VERSION` line, object references and
designer block of a form, UserControl or PropertyPage — followed, for every kind, by the run of `Attribute`
lines that opens the code.

Holding anything less means a server that reads the file, and the compiler, count lines differently from the
text the editor sent, and every position crossing between them has to be translated — in both directions, for
every message, forever. Holding the file means there is nothing to translate.

A document with no file yet SHALL hold the header its first save will write.

#### Scenario: Opening a class module
- **WHEN** a class module is opened
- **THEN** the code window's text is the text of its file, header included

#### Scenario: Opening a form and saving it unchanged
- **WHEN** a form is opened and saved without any change
- **THEN** the file written is the text the code window held

### Requirement: The header SHALL be folded, greyed out and read-only
The header SHALL be shown folded when a code window opens, and SHALL be folded again whenever its fold is
re-created — unless the developer has expanded it in that window. Its text SHALL be shown in a colour that
marks it as not the developer's to edit, meeting the same contrast requirement as other editor text in every
theme. It SHALL NOT be editable.

The fold is what keeps the window recognisable to somebody who never saw the header in VB6: a form's code
still starts at the top of what they see. A fold that reopened whenever the document was reformatted or
reloaded would put a designer block of hundreds of lines back in front of them at unpredictable moments.

The fold SHALL NOT depend on any language server, and SHALL be present when none is attached.

#### Scenario: Opening a form
- **WHEN** a form's code window opens
- **THEN** its designer block and attribute lines are folded into one line, shown greyed out

#### Scenario: Reformatting after expanding the header
- **GIVEN** a developer who has expanded a class's header
- **WHEN** the document is formatted
- **THEN** the header stays expanded

#### Scenario: Reformatting a document whose header is folded
- **WHEN** the document is formatted, or reloaded after an external change
- **THEN** the header is still folded afterwards

### Requirement: An attribute line that describes a member SHALL be read-only and folded into that member's line
An `Attribute` line naming a procedure, property or module-level variable SHALL be greyed out, read-only, and
folded — by default — into the end of the line it describes: the procedure's declaration, or the variable
declaration it follows.

These lines give a member its description, help topic, dispatch id or default-member status. A developer who
edits one by hand breaks what it binds without being told, and VB6's only way to change them was a dialog. A
fold that starts at the end of the line it describes hides them the way VB6 did while keeping them in the file.

#### Scenario: A procedure with a description
- **WHEN** a module containing a procedure with an `Attribute <Name>.VB_Description` line is opened
- **THEN** the attribute is folded into the end of the procedure's declaration line, and cannot be edited

### Requirement: Only the IDE SHALL change a read-only region
No edit the developer makes — by typing, pasting, deleting, replacing, completing, inserting a file, or
running an add-in or automation command — SHALL change a read-only region of the code window. The IDE SHALL
change such a region only from its own model: a designer change, a rename, a reload after an external change.

Protecting the text against typing alone is not protection. Most writes into a code window are programmatic —
formatting, rename, replace-all, completion, event stubs, add-ins, automation — and none of them passes
through the typing path. A region that one of them can overwrite is only read-only until somebody formats the
document.

Where a server's formatting answer would change a read-only region, the part that would SHALL be discarded and
the rest applied. Where a server's rename would change the header, the rename SHALL be refused as a whole and
the developer told why. A procedure's own attribute lines SHALL follow the procedure's rename.

A mark SHALL NOT be set on a line in a read-only region: such a line never executes, and a folded header is
one visible line, so a mark set there would land on a line the developer cannot see.

#### Scenario: Formatting a class
- **WHEN** a class is formatted by a server whose answer replaces the whole document
- **THEN** the code is formatted and the header is unchanged

#### Scenario: Replacing text that also appears in the header
- **GIVEN** a form with a control named `Command1` and code that refers to it
- **WHEN** the developer replaces every `Command1` with `cmdOK`
- **THEN** the code is changed and the designer block is not

#### Scenario: Renaming a procedure that has a description
- **WHEN** a procedure with an `Attribute` line is renamed
- **THEN** the attribute line names the procedure's new name

#### Scenario: An add-in replaces a document's content
- **WHEN** an add-in replaces a document's content with text whose header differs
- **THEN** the replacement is refused and the add-in is told why

### Requirement: A designer change SHALL reach the header without becoming a code edit
A committed change in a form's designer — a property, a control added or removed, a move, the menu editor, the
colour palette, the same change made through automation — SHALL be reflected in the header of the form's open
code window. It SHALL NOT be undoable from the code window, SHALL NOT make an earlier code edit undo the wrong
text, and SHALL NOT raise the prompt that editing code during a run raises.

The designer and the code window keep separate undo histories, and the developer expects each to undo what was
done in it. A change the developer made by dragging a control is not one they made in the code window.

The header SHALL be refreshed once per committed change, not per intermediate state of a drag.

#### Scenario: Moving a control with the code window open
- **WHEN** the developer moves a control in the designer
- **THEN** the code window's header shows the new position, and its code is untouched
- **AND** undo in the code window does not undo the move

#### Scenario: Changing a form's layout while paused
- **WHEN** a designer change is made while a run is paused
- **THEN** no prompt to reset the run is shown on its account

### Requirement: Lines SHALL be numbered from the top of the file
Every line number the IDE shows or reports for a code window — the line-number margin, the status bar,
breakpoints, bookmarks, the current statement, the Call Stack, the automation surface and the add-in surface —
SHALL count from the first line of the file, header included.

One numbering is the point of holding the whole file. A second, code-relative numbering shown anywhere would
reintroduce exactly the translation this capability exists to remove, and a developer comparing a line number
from one place with another would be wrong by the length of the header.

#### Scenario: A class with the standard header
- **WHEN** the caret is on the first line of code after a class's header
- **THEN** the status bar shows that line's number counted from the top of the file

### Requirement: Find SHALL NOT match inside a read-only region
Find, Find Next and Replace SHALL search only text outside read-only regions.

VB6's Find never searched the header, because the header was not in its code window. A search that now stopped
inside a folded designer block would land the developer on text they cannot change, in a region they cannot
see.

#### Scenario: Searching for a control's name
- **WHEN** the developer searches a form's code for the name of one of its controls
- **THEN** only matches in the code are found
