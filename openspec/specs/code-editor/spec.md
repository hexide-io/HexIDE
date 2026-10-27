# code-editor Specification

## Purpose
Define what the code window holds and which parts of it the developer may change.

A VB6 source file is more than its code. A class opens with a block that says what kind of class it is; a form
carries its entire layout above its first line of code; attribute lines bind members to descriptions, help
topics and dispatch ids. VB6 hid all of it. HexIDE shows it — so that the text the editor holds is the text of
the file, and a position means the same thing to the editor, the debugger and every language server — and
protects it, because every line of it is load-bearing and none of it is the developer's to type over.

That is a deliberate divergence from VB6, and it follows the project's fidelity principle rather than
departing from it: what VB6 intended was that the developer not edit these lines, and that is reproduced;
that the developer could not see them was a limitation of its code window, and that is not.

> **Known divergence.** The requirements below state intended behaviour. One sentence is not met: "A document
> with no file yet SHALL hold the header its first save will write." A form the IDE has just created and not yet
> saved, such as a new project's `Form1`, shows no header in its code window until its first committed designer
> change, save or reload. Tracked in [#885](https://github.com/hexide-io/HexIDE/issues/885). The sentence is
> stated as intended rather than removed, because the code is incomplete here, not the intent.

## Requirements

### Requirement: Syntax keywords match regardless of case
The editor SHALL apply the VB6 syntax definition's keyword colours regardless of keyword casing.

#### Scenario: Keywords differ from the definition's casing
- **WHEN** the editor highlights `Begin`, `begin`, a standalone `End`, `end`, `private` or `SUB`
- **THEN** each word receives its named keyword colour from the syntax definition

### Requirement: The code window SHALL hold the whole file
The code window for a form, module, class, UserControl or PropertyPage SHALL hold the file's complete text.
It SHALL be the text of the file as it stands, and after a save it SHALL be the text that save wrote.

Holding anything less means a server that reads the file counts lines differently from the text the editor
sent, and every position crossing between them has to be translated — in both directions, for every message,
forever. Holding the file means there is nothing to translate.

A save is named as well as a change to the document, because a form is written by rendering its model, and
that render need not reproduce the file it was read from byte for byte. After such a save the file has moved,
not the model, and the window follows the file.

A document with no file yet SHALL hold the header its first save will write.

#### Scenario: Opening a class module
- **WHEN** a class module is opened
- **THEN** the code window's text is the text of its file, header included

#### Scenario: Opening a form and saving it unchanged
- **WHEN** a form is opened and saved without any change
- **THEN** the code window's text afterwards is the text of the file that was written
- **AND** that update is not something the developer can undo, and does not mark the document as edited

### Requirement: Syntax colours SHALL meet the editor contrast bar
Every named VB6 syntax foreground SHALL be a solid colour with a WCAG AA contrast ratio of at least
4.5:1 against Classic's editor background in the light palette and against both Dark and Abyss editor
backgrounds in the dark palette. Metadata keywords SHALL retain their magenta hue in the light palette.

#### Scenario: Checking the light syntax palette
- **WHEN** the light VB6 syntax palette is applied
- **THEN** every named foreground, including MetadataKeyword, clears 4.5:1 against Classic's editor background

#### Scenario: Checking the dark syntax palette
- **WHEN** the dark VB6 syntax palette is applied
- **THEN** every named foreground, including MetadataKeyword, clears 4.5:1 against both dark editor backgrounds

### Requirement: The header SHALL be folded, greyed out and read-only
The header SHALL be shown folded when a code window opens, and SHALL be folded again whenever its fold is
re-created — unless the developer has expanded it in that window, or it is a single line. Its text SHALL be shown in a colour that
marks it as not the developer's to edit, meeting the same contrast requirement as other editor text in every
theme. It SHALL NOT be editable.

The header is the class `VERSION` and `BEGIN … END` block, or the `VERSION` line, object references and
designer block of a form, UserControl or PropertyPage — followed, for every kind, by the run of `Attribute`
lines that opens the code.

The fold is what keeps the window recognisable to somebody who never saw the header in VB6: a form's code
still starts at the top of what they see. A fold that reopened whenever the document was reformatted or
reloaded would put a designer block of hundreds of lines back in front of them at unpredictable moments.

The fold SHALL NOT depend on any language server, and SHALL be present when none is attached.

A header of a single line SHALL NOT be folded. A fold shows its first line in place of the lines it hides, so on
one line it would hide nothing. A standard module's header, which is its `Attribute VB_Name` line alone, is that
case: it is still shown greyed out, and is still read-only.

A code window opened with no place asked for SHALL put its caret on the first line below the header, where typing is
allowed. VB6's code window had no header, so its View Code put the caret in the code, and a caret left at the top of
the file would refuse the first keystroke and show nothing (#1518). A place asked for, by navigation, a breakpoint or
the procedure list, SHALL still win.

#### Scenario: Opening a form
- **WHEN** a form's code window opens
- **THEN** its designer block and attribute lines are folded into one line, shown greyed out

#### Scenario: Opening a form's code with nowhere asked for
- **WHEN** View Code opens a form's code window
- **THEN** the caret is on the first line below the header, and the first character typed appears there

#### Scenario: Opening a standard module
- **WHEN** the code window of a standard module whose header is its `Attribute VB_Name` line alone opens
- **THEN** that line is shown greyed out and cannot be edited, and is not folded

#### Scenario: Reformatting after expanding the header
- **GIVEN** a developer who has expanded a class's header
- **WHEN** the document is formatted
- **THEN** the header stays expanded

#### Scenario: Reformatting a document whose header is folded
- **WHEN** the document is formatted, or reloaded after an external change
- **THEN** the header is still folded afterwards

### Requirement: Toggle Fold acts on the innermost section at the caret
The code window SHALL offer Toggle Fold on the Edit menu, its context menu and the default Ctrl+M gesture
on Windows and Linux. Toggle Fold SHALL be unbound on macOS, leaving Cmd+M to the system's Minimize.
It SHALL toggle the section with the greatest start offset, then the smallest end offset,
among sections containing the caret or starting on the caret's document row. With no such section it SHALL
do nothing. Before collapsing a section with the caret strictly inside, it SHALL move the caret to the
section's start. A member run collapsed this way SHALL remain collapsed across a reload, as with the margin.
The context-menu command SHALL act at the caret.

#### Scenario: An expanded member run shares its declaration row with a procedure fold
- **WHEN** Toggle Fold is invoked with the caret on that declaration row
- **THEN** the run collapses and the procedure's fold state stays unchanged

#### Scenario: The caret is in the procedure body
- **WHEN** Toggle Fold collapses the procedure containing the caret
- **THEN** the caret moves to the procedure fold's start before its body is hidden

### Requirement: An attribute line that describes a member SHALL be read-only and folded into that member's line
An `Attribute` line describing a procedure, property, or module-level variable or constant SHALL be greyed out,
read-only, and folded — by default — into the end of the line it describes: the procedure's declaration, or the
variable or constant declaration it follows. Such a run SHALL follow the line it describes: deleting that line SHALL delete the
run with it, and a member's own attribute lines SHALL follow that member's rename, whether the IDE or a
language server makes it.

These lines give a member its description, help topic, dispatch id or default-member status. A developer who
edits one by hand breaks what it binds without being told, and VB6's only way to change them was a dialog. A
fold that starts at the end of the line it describes hides them the way VB6 did while keeping them in the
file. Making the run follow its line is what stops an ordinary edit leaving an attribute describing something
that is no longer there — which read-only text would otherwise do, because a deletion is carved around it.

An attribute line left describing nothing SHALL be preserved, not removed: it is inert text, and the IDE does
not know what the developer meant by it. A run of attribute lines describes a member when it sits directly
under that member's declaration, with any lines continued into it, and VB6 reads every line of it as that
member's: a run of procedure attributes under a procedure's declaration, whatever member its lines name, since
VB6 gives such a line to the procedure above it; or a run of `VB_Var…` lines under a module-level variable or
constant's declaration, each naming a name that line declares or, where the line declares one name, all naming
one name nothing in the module declares, which is what retyping that name leaves. A run that does not — under a
blank line, a comment or a statement, mixing the two kinds, or a variable attribute under a procedure or naming
a variable declared elsewhere — SHALL NOT be read-only, greyed or folded, because editing it by hand is the
only way to repair it.

When a paste or Insert File replaces a described declaration's whole text and ends at its anchor, the IDE
SHALL keep the run under the first logical line of the replacement that declares a member it can describe:
a procedure for a procedure run, or variables or constants naming every qualifier of a variable run.
Continuation lines SHALL be read together by the member-declaration reader. Text through that declaration
SHALL replace the selection; any remaining text SHALL follow the run. With no remaining text the caret SHALL
end at the pasted declaration's end. If no compatible declaration exists, or the selection starts inside the
declaration, the existing split at the first line break SHALL apply.

This placement rule (#1039) is a HexIDE decision; the exact VB6 paste has not been measured. It settles the
whole-declaration paste question in the archived #273 design record without changing that historical record.

For a file ending in a folded member attribute run without a final line break, Select All SHALL select the
whole buffer while leaving the caret at the declaration's end; Shift+Left SHALL then end the selection one
character before that point. Select-to-document-end from above the run SHALL select only to the declaration's
end. This edge case is retained by decision #1019; the measured VB6-saved files all have a final line break.

#### Scenario: Pasting a comment before a replacement declaration
- **WHEN** the developer selects a described declaration's text through its end and pastes a comment followed
  by a compatible declaration
- **THEN** the comment precedes the declaration, its attribute run remains directly below the declaration,
  and one Undo restores the original text

#### Scenario: A procedure with a description
- **WHEN** a module containing a procedure with an `Attribute <Name>.VB_Description` line is opened
- **THEN** the attribute is folded into the end of the procedure's declaration line, and cannot be edited

#### Scenario: Deleting a described procedure
- **WHEN** the developer selects a procedure including its declaration line and deletes it
- **THEN** its attribute lines go with it, and none is left behind

#### Scenario: An attribute line that describes nothing
- **WHEN** a run of `Attribute` lines sits where VB6 gives it to no member: under a blank line, a comment or a
  statement, or under a declaration its lines do not belong to
- **THEN** it is shown and edited as ordinary text, and the file keeps it

### Requirement: Only the IDE SHALL change a read-only region
A read-only region is the file's header, or a run of attribute lines describing a member. A document the IDE
holds read-only as a whole — a form it cannot save faithfully — SHALL NOT thereby make its code lines part of
a read-only region.

No edit the developer makes — by typing, pasting, deleting, replacing, completing, inserting a file, or
running an add-in or automation command — SHALL change a read-only region. The IDE SHALL change such a region
only from its own model: a designer change, a save, a rename, a reload after an external change.

Protecting the text against typing alone is not protection. Most writes into a code window are programmatic —
formatting, rename, replace-all, completion, event stubs, add-ins, automation — and none of them passes
through the typing path. A region that one of them can overwrite is only read-only until somebody formats the
document. And the two kinds of read-only have to stay apart, or a form the IDE cannot reproduce would take no
breakpoint anywhere and match nothing in a search.

Where a server's formatting answer would change a read-only region, the part that would SHALL be discarded and
the rest applied. Where a server's rename would change the header, the rename SHALL be refused as a whole and
the developer told why.

A replacement of a document's whole content SHALL be refused where it would change, add or remove a member's
attribute run other than by moving the run with its member, renaming it with its member, or removing it together
with the member's declaration, and the caller SHALL be told which member. If the header also differs, the
refusal SHALL report the header first and then every member finding, in one reply. A run carried with its member is still
that member's: VB6 gives a procedure's run to the procedure above it whatever its lines name, and the run's lines
follow the member's rename as the requirement above has them do. A run added is not exempt for having been inert
text before, because a replacement cannot tell an orphan it repairs from a run its caller wrote.

An add-in's range edits SHALL also be compared as a complete resulting document and refused together if
that result changes, adds or loses a described member run under the same comparison as a whole replacement.
The refusal SHALL name the members. This includes promoting an orphan, extending a run at its end, mixing a
run into an orphan and inserting a whole new described member. The existing region check SHALL run first;
an edit explicitly reaching into a run remains refused. An empty range edit deleting a declaration's whole
text SHALL widen over its run, as a key deletion does. A set whose widened spans overlap another edit
SHALL be refused without changing the document. Find's Replace and Replace All SHALL likewise widen
empty replacements over the runs of declarations they empty; non-empty replacements SHALL keep those runs.
Formatting is unchanged.

A mark SHALL NOT be set on a line in a read-only region: such a line never executes, and a folded header is
one visible line, so a mark set there would land on a line the developer cannot see.

#### Scenario: An add-in extends a member's run
- **WHEN** an add-in inserts an Attribute line at the start of the line following Total's described run
- **THEN** the entire edit set is refused, naming Total, and the buffer is unchanged

#### Scenario: Formatting a class
- **WHEN** a class is formatted by a server whose answer replaces the whole document
- **THEN** the code is formatted and the header is unchanged

#### Scenario: Replacing text that also appears in the header
- **GIVEN** a form with a control named `Command1` and code that refers to it
- **WHEN** the developer replaces every `Command1` with `cmdOK`
- **THEN** the code is changed and the designer block is not

#### Scenario: Renaming a procedure that has a description
- **WHEN** a procedure with an `Attribute` line is renamed
- **THEN** the attribute line names the procedure's new name once the document is saved

#### Scenario: An add-in replaces a document's content
- **WHEN** an add-in replaces a document's content with text whose header differs from the document's
- **THEN** the replacement is refused and the add-in is told why

#### Scenario: An add-in replaces a document's content, changing a member's attribute lines
- **WHEN** an add-in replaces a document's content with text that changes, adds or drops a member's attribute
  lines, other than by moving or renaming them with that member or deleting them with its declaration
- **THEN** the replacement is refused and the add-in is told which member

#### Scenario: A breakpoint in a form the IDE cannot save faithfully
- **WHEN** the developer sets a breakpoint on a line of code in such a form
- **THEN** it is set, because the document being read-only as a whole is not a read-only region

### Requirement: A designer change SHALL reach the header without becoming a code edit
A committed change in a form's designer — a property, a control added or removed, a move, the menu editor, the
colour palette, the same change made through automation — SHALL be reflected in the header of the form's open
code window. It SHALL NOT be undoable from the code window, SHALL NOT remove or block the code window's undo
of an earlier code edit, SHALL NOT make an earlier code edit undo the wrong text, and SHALL NOT raise the
prompt that editing code during a run raises.

The designer and the code window keep separate undo histories, and the developer expects each to undo what was
done in it, and to keep working afterwards.

The header SHALL be refreshed once per committed change, not per intermediate state of a drag, and the
refresh SHALL take the model as the commit left it rather than a state the designer has not yet written back.

Where a refresh changes how many lines the header occupies, every breakpoint and bookmark below it SHALL move
with the code it was set on, whether or not the document is open in a code window.

#### Scenario: Moving a control with the code window open
- **WHEN** the developer moves a control in the designer
- **THEN** the code window's header shows the new position, and its code is untouched
- **AND** undo in the code window does not undo the move, and still undoes the developer's last code edit

#### Scenario: Adding a control below a breakpoint
- **GIVEN** a breakpoint on a line of a form's code
- **WHEN** a control is added, so the designer block grows
- **THEN** the breakpoint is still on the same statement

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

VB6's compiler is the one exception, and it is not ours to change: it reports a line counted against its own
code view, with every attribute line excluded. That number SHALL be converted where its output is read, and
nowhere else.

#### Scenario: A class with the standard header
- **WHEN** the caret is on the first line of code after a class's header
- **THEN** the status bar shows that line's number counted from the top of the file

#### Scenario: A compile error from VB6
- **WHEN** a build reports an error against one of the project's files
- **THEN** it is shown on the line of that file the developer sees, not on the line the compiler counted

#### Scenario: A compile error around a continued attribute
- **WHEN** VB6 reports a compile error in code containing continued lines
- **THEN** mapping SHALL exclude every physical line of a logical line starting with `Attribute`
- **AND** it SHALL count a physical line starting with `Attribute` when it continues code or a comment

### Requirement: Find SHALL NOT match inside a read-only region
Find, Find Next and Replace SHALL search only text outside read-only regions.
If matches exist only in those regions within the selected scope, Find SHALL explain that the text is in the
header or a procedure's Attribute lines, which only the IDE changes. Replace All SHALL report the number of
matches skipped in those regions beside the number of replacements made, including when none were made.

VB6's Find never searched the header, because the header was not in its code window. A search that now stopped
inside a folded designer block would land the developer on text they cannot change, in a region they cannot
see.

#### Scenario: Searching for a control's name
- **WHEN** the developer searches a form's code for the name of one of its controls
- **THEN** only matches in the code are found

#### Scenario: Search text exists only in protected regions
- **WHEN** Find Next finds no searchable match but the selected scope contains a match in a read-only region
- **THEN** the message explains why Find skips the header and procedure Attribute lines instead of saying not found

#### Scenario: Replace All leaves protected matches alone
- **WHEN** Replace All encounters matches in the header or procedure Attribute lines within the selected scope
- **THEN** its message reports both the replacements made and the matches skipped, using the selected search options

### Requirement: Event-handler searches SHALL start after the header
Both designer double-click navigation and the Object/Procedure boxes SHALL search for an event handler starting
at the end of the file header. Text in designer properties SHALL NOT intercept navigation or prevent a missing
handler from being generated.

#### Scenario: A caption names an event handler
- **GIVEN** a control whose caption contains `Sub Command1_Click`
- **WHEN** its handler is requested by double-click or by selecting Click in the Procedure box
- **THEN** the caret goes to the existing handler below the header, or a new handler is generated if absent

### Requirement: New event handlers SHALL keep the caret in their body
Selecting an event without a handler in the Procedure box SHALL generate its stub and place the caret on the
blank line inside it. A diagnostics or document-symbol refresh SHALL NOT navigate the caret out of that body.

#### Scenario: Typing after a new handler's diagnostics arrive
- **WHEN** the developer selects an event with no handler and the language server publishes diagnostics
- **THEN** typing lands on the blank body line between the declaration and `End Sub`

### Requirement: Delete SHALL work without a selection
The code window's Delete command SHALL delete the selection when one exists, and otherwise use the text area's
forward deletion. Both the Delete key and the menu command SHALL arm the existing reset prompt when a run is active.

#### Scenario: Deleting inside code with nothing selected
- **WHEN** the developer presses Delete with the caret inside editable code and no selection
- **THEN** the next character is removed
- **AND** if a run is active, the reset prompt is shown and answering No restores the edit

### Requirement: Find SHALL wrap after searching other open documents
Under All Open Documents, Find SHALL search the active document in the chosen direction without wrapping,
then the other open documents, and only then wrap within the active document.

#### Scenario: Find Up from the start of the active document
- **WHEN** the caret is at offset 0 and both the active document and another open document contain the search text
- **AND** the developer uses plain-text Find Up under All Open Documents
- **THEN** the last match in the other document is selected before any match in the active document

### Requirement: Code writers SHALL follow the document's line ending

Enter (including indentation and auto-close), Insert File, automation's document-mode `type_text`,
procedure and event-stub insertion, and whole-content replacement SHALL write new line breaks in the
current document's line ending. Whole replacements SHALL follow the same rule with or without a code
window open. A document containing CRLF SHALL supply CRLF; otherwise an existing LF or lone CR SHALL
supply its terminator. A document without a line break SHALL fall back to CRLF. Saving SHALL NOT normalize
existing line endings merely because the file is saved (#530).

#### Scenario: A client sends LF text for a CRLF file
- **WHEN** an add-in, automation or AI Chat replaces a CRLF document's content with LF text
- **THEN** the accepted replacement's line breaks are CRLF, and the document's own header is retained

#### Scenario: An LF file is edited
- **WHEN** Enter, Insert File or automation inserts a line break in an all-LF document
- **THEN** the new line breaks are LF

#### Scenario: No line ending exists yet
- **WHEN** a writer introduces line breaks into an empty or single-line buffer
- **THEN** it writes CRLF on every host

### Requirement: Insert toggles overtype with immediate status feedback
The code window SHALL toggle between insert and overtype mode when Insert is pressed.
The status bar SHALL reflect the new mode immediately, without requiring the caret to move.

#### Scenario: Toggle overtype and return to insert
- **WHEN** the developer presses Insert in a focused code window
- **THEN** overtype mode is enabled and the status bar displays the localized overtype label
- **WHEN** the developer presses Insert again
- **THEN** insert mode is restored and the status bar displays the localized insert label

### Requirement: Inactive conditional branches SHALL be greyed for the selected view target
The code window SHALL grey only the interpreter preprocessor's determined inactive lines in the theme's
`InactiveCodeTextBrush`. It SHALL recompute after text edits with a debounce, and SHALL grey nothing when
conditional compilation fails. Undetermined lines SHALL remain ungreyed. An edit that leaves every line where it
was SHALL keep the greying until the next evaluation, and an edit that adds or removes lines SHALL clear it.

#### Scenario: Editing an incomplete directive
- **WHEN** an edit leaves an unterminated `#If` block
- **THEN** the code window removes its inactive greying until preprocessing succeeds again

#### Scenario: Typing on a line
- **WHEN** the developer types within a line of a module whose inactive branches are greyed
- **THEN** the greying stays as it is until the next evaluation, rather than flashing off with each keystroke

### Requirement: The view-as selector SHALL appear only with conditional compilation, and SHALL belong to its window
A ComboBox after the Procedure box SHALL appear only while the module has an `#If` line. It SHALL offer
VB6, VBA 32-bit, VBA 64-bit, twinBASIC 32-bit and twinBASIC 64-bit, defaulting to VB6, the constants currently used by F5 for every personality.
The selection SHALL belong to the window and SHALL NOT be persisted or affect execution or LSP diagnostics.
Changing the selection SHALL NOT send language-server requests or notifications.
VBA's constants are not measured, since there is no VBA oracle.

#### Scenario: Previewing a VBA branch
- **WHEN** a module contains `#If VBA7 Then` with an `#Else` branch
- **THEN** the default VB6 view greys the VBA7 branch
- **AND WHEN** the developer chooses VBA 32-bit or VBA 64-bit
- **THEN** the view greys the Else branch instead, without changing what F5 runs


#### Scenario: Previewing a twinBASIC branch
- **WHEN** the developer chooses twinBASIC 32-bit or twinBASIC 64-bit
- **THEN** greying uses the measured twinBASIC built-ins for that bitness and function calls read zero
- **AND** the build number is unknown, so conditions depending on it remain ungreyed
- **AND** the build type is "Standard EXE", "ActiveX DLL" or "ActiveX Control" for the corresponding VB6 project type; otherwise it is unknown and conditions depending on it remain ungreyed
- **AND** execution and language-server diagnostics retain their own constants
