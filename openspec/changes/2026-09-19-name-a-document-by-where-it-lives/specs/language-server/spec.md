## ADDED Requirements

### Requirement: The server SHALL accept a whole VB6 file, and SHALL leave its header alone
The server SHALL accept a document whose text is a complete VB6 file: a class's `VERSION` and `BEGIN … END`
block, a form's `VERSION` line, object references and designer block, the run of `Attribute` lines that opens
the code, and `Attribute` lines describing a member further down. It SHALL report no diagnostic inside any of
those, and no formatting, rename or highlight answer it gives SHALL change or point inside them.

The client now sends the file rather than the code, because that is the only way a position means the same
thing to the editor, to a server reading the file from disk, and to anything reading it afterwards. A server
that reported the designer block as a syntax error would bury every real diagnostic in a form under hundreds
of false ones.

The formatter is the sharper case. It answers with one edit spanning the document, so an answer that
reformatted the header would rewrite the file's layout on every save: VB6 indents a designer block by three
spaces, and the header carries meaning at column zero. The client discards edits that fall inside a protected
region, and this requirement stops the server producing them in the first place, so that a replacement backend
is held to the same rule rather than relying on the client to repair it.

Rename and highlight are lexical in the bundled server, matching whole words across the document. With the
designer block in view, a developer renaming a local called `Text`, `Top` or `Caption` would otherwise be
offered edits inside the layout.

#### Scenario: Opening a form
- **WHEN** a form's whole file is opened, designer block and all
- **THEN** no diagnostic is reported against any line of the header

#### Scenario: Formatting a class
- **WHEN** formatting is requested for a class whose file begins with the standard class header
- **THEN** the answer leaves every line of that header exactly as it was

#### Scenario: Renaming an identifier that also appears in the layout
- **GIVEN** a form with a control whose name is also used by a local variable
- **WHEN** that local is renamed
- **THEN** no edit in the answer falls inside the designer block
