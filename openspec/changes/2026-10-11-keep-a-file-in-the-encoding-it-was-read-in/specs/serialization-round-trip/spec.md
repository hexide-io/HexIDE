## ADDED Requirements

### Requirement: A source file SHALL be written back in the encoding it was read in

The IDE SHALL record, for each VB6 source file it reads (`.vbp`, `.vbg`, `.frm`, `.bas`, `.cls`, `.ctl`,
`.pag`), the encoding it was read in, and SHALL write the file back in that encoding, including a UTF-8
byte-order mark when the file had one. It SHALL NOT change a file's encoding to accommodate an edit, and
SHALL NOT re-encode text that was not edited.

#### Scenario: Accented bytes that also form valid UTF-8 survive an unrelated edit

- **GIVEN** a module without a byte-order mark whose only non-ASCII bytes are `C3 A9`, read in Windows-1252
- **WHEN** an ASCII line is edited and the module is saved
- **THEN** the bytes `C3 A9` are written back unchanged

#### Scenario: A byte-order mark survives a save

- **GIVEN** a module that begins with a UTF-8 byte-order mark
- **WHEN** it is edited and saved
- **THEN** the saved file still begins with the byte-order mark

### Requirement: A file the host's code page cannot read SHALL still load and save back unchanged

When a source file's bytes cannot be decoded in the host's ANSI code page, or would not encode back to the
same bytes, the IDE SHALL still load the file, reading each byte as the Latin-1 character with that value. It
SHALL warn, naming the file, and SHALL write the file back byte-for-byte when it is saved unedited. This SHALL
apply when a project is opened, when an existing file is added, and when a file changed outside the IDE is
reloaded.

#### Scenario: A Western file on a double-byte host

- **GIVEN** a host whose ANSI code page is 932, and a module containing the byte `E9` followed by a line break
- **WHEN** its project is opened
- **THEN** the module is part of the project, a warning names it, and saving it unedited writes the same bytes

#### Scenario: A byte-order mark over text that is not UTF-8

- **GIVEN** a module that begins with a UTF-8 byte-order mark followed by Windows-1252 text
- **WHEN** its project is opened and saved without edits
- **THEN** the module's header is recognised, and the saved file is byte-identical to the original

#### Scenario: A file recorded as UTF-8 is replaced by an ANSI revision

- **GIVEN** a module read as UTF-8, which an external tool replaces with bytes that are not valid UTF-8
- **WHEN** the IDE reloads it
- **THEN** the reload succeeds and shows the new content, and the next save does not write the old content

### Requirement: A project member that fails to load SHALL keep its line in the project file

When a form or module named in a project file cannot be loaded, for any reason, the IDE SHALL keep that
member's line and SHALL write it back on save, as it does for a member whose file is missing. Saving the
project SHALL NOT remove a member the IDE failed to load.

#### Scenario: A member that throws during load

- **GIVEN** a project whose module fails to load
- **WHEN** the project is opened and saved
- **THEN** the saved project file still names that module

### Requirement: Files earlier versions of the IDE wrote as UTF-8 SHALL be read as UTF-8

Earlier versions of the IDE wrote a source file as UTF-8 without a byte-order mark exactly when its text held
a character above U+00FF. A file without a byte-order mark whose bytes are valid UTF-8 and decode to at least
one character above U+00FF SHALL therefore be read as UTF-8, and SHALL be written back as UTF-8 without a
byte-order mark. A file without such a character SHALL be read in the host's ANSI code page even when its
bytes are valid UTF-8.

#### Scenario: A module an earlier version saved with a curly quote

- **GIVEN** a module without a byte-order mark holding `“café”` encoded as UTF-8
- **WHEN** its project is opened
- **THEN** the module reads `“café”`, and saving it unedited writes the same bytes

#### Scenario: Accented text alone is not taken as UTF-8

- **GIVEN** a module without a byte-order mark whose only non-ASCII bytes are `C3 A9`
- **WHEN** its project is opened on a Windows-1252 host
- **THEN** the module reads `Ã©`, not `é`

### Requirement: An edit the file's encoding cannot hold SHALL be refused the same way on every save path

When a document holds a character its encoding cannot represent, the IDE SHALL refuse to save it rather than
convert the file or substitute the character, and SHALL name the document and its code page. It SHALL check
every file an operation would write before writing any of them. This SHALL hold for Save, Save As, Save
Project, Make, adding a new form or module, and saves requested through the automation server. An automation
caller SHALL receive the refusal in its reply, naming the document, the code page and the first character
that cannot be held, and SHALL NOT be left waiting on a dialog.

#### Scenario: Make with a form the code page cannot hold

- **GIVEN** a form whose caption holds a character outside the project's code page
- **WHEN** the project is made
- **THEN** no file is written, and the refusal names the form and the code page

#### Scenario: An automation save

- **GIVEN** a module read in Windows-1252
- **WHEN** an automation caller sets its content to text containing `→` and saves
- **THEN** the reply says the save was refused, naming the module, code page 1252 and `→`, and no dialog is shown
