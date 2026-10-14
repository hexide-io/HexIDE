## ADDED Requirements

### Requirement: The server SHALL accept a whole VB6 file, and SHALL leave its header alone
The server SHALL accept a document whose text is a complete VB6 file: a class's `VERSION` and `BEGIN … END`
block, a form's `VERSION` line, object references and designer block, the run of `Attribute` lines that opens
the code, and `Attribute` lines describing a member further down. It SHALL report no diagnostic inside any of
those, and no formatting, rename or highlight answer it gives SHALL change or point inside them, with one
exception: renaming a member SHALL change that member's own name where it qualifies one of its attribute lines
(`Attribute Total.VB_Description`), and nothing else on that line. A rename or highlight asked for from a
position inside any of those lines SHALL have no answer, and so SHALL a rename of a name the designer block
declares: the form's own, a control's or a menu's, wherever it is asked for. So SHALL a rename to a new name that
is not a VB6 name. A header the server cannot parse SHALL NOT stop it analysing the code after the header.

The client now sends the file rather than the code, because that is the only way a position means the same
thing to the editor, to a server reading the file from disk, and to anything reading it afterwards. A server
that reported the designer block as a syntax error would bury every real diagnostic in a form under hundreds
of false ones.

The formatter is the sharper case. An answer that reformatted the header would rewrite the file's layout on
every save: VB6 indents a designer block by three spaces, and the header carries meaning at column zero. An
answer of one edit spanning the document would point inside the header even if its text left the header
alone, so the bundled server answers with an edit for each run of lines it changes. The client discards edits
that fall inside a protected region, and this requirement stops the server producing them in the first place,
so that a replacement backend is held to the same rule rather than relying on the client to repair it.

Rename and highlight are lexical in the bundled server, matching whole words across the document. With the
designer block in view, a developer renaming a local called `Text`, `Top` or `Caption` would otherwise be
offered edits inside the layout. The exception for a member's own qualifier is the code editor's rule that a
member's attribute lines follow its rename, whether the IDE or a language server makes it: left behind,
`Attribute Total.VB_Description` describes a member that no longer exists. A member's own lines are the run VB6
gives that member, which for a procedure is the run under its declaration whatever its qualifiers name, so a run
naming `Total` under another procedure is that procedure's, and renaming `Total` leaves it alone.

A new name that is not a VB6 name, a letter followed by letters, digits and underscores, would write text that
does not compile wherever the name stands, and `Grand Total` would split one name into two. Nor is one of the four
words VB6 was measured to refuse as a name though each has a name's shape, `Rem`, `Empty`, `New` and `Nothing`:
renamed `Rem`, a function's `Rem = mSum` would be a comment. The code editor refuses such a name before it asks; a
client that does not is answered with nothing.

Keeping off the header makes a declared name the one rename that has to be declined outright. Its
declaration is the `Begin` line in the designer block, so a rename that kept off the header would rename every
reference in the code and not the control, and the code would name a control that no longer exists. A lexical
rename cannot tell such a reference from a local that happens to share the control's name, so both are
declined: refusing that rare rename is the price of never breaking the common one. A control is renamed in the
Properties window, as in VB6.

The last clause is not implied by the others. A parser recovering from a designer line it cannot read can
consume the code after it, so a server that merely withheld the header's errors would report nothing for the
whole file, the code's real errors included, and give no reason why.

#### Scenario: Opening a form
- **WHEN** a form's whole file is opened, designer block and all
- **THEN** no diagnostic is reported against any line of the header

#### Scenario: Opening a form whose designer block cannot be parsed
- **GIVEN** a form with a designer line the server cannot parse, and a syntax error in its code
- **WHEN** the form's whole file is opened
- **THEN** the syntax error in the code is reported, and nothing in the header is

#### Scenario: Formatting a class
- **WHEN** formatting is requested for a class whose file begins with the standard class header
- **THEN** the answer leaves every line of that header exactly as it was

#### Scenario: Renaming an identifier that also appears in the layout
- **GIVEN** a form whose designer block sets a property, such as `Caption`, and a local variable of that name
- **WHEN** the local is renamed
- **THEN** no edit in the answer falls inside the designer block

#### Scenario: Renaming a control from its code
- **GIVEN** a form with a control, and code that refers to it by name
- **WHEN** a rename is asked for on that name in the code
- **THEN** there is no answer

#### Scenario: Renaming a member that has a description
- **GIVEN** a property followed by an `Attribute <Name>.VB_Description` line
- **WHEN** the property is renamed from its declaration
- **THEN** the answer changes the name that qualifies the attribute line, and nothing else on that line

#### Scenario: Renaming to a name VB6 does not accept
- **WHEN** a rename is asked for with a new name that does not start with a letter, holds a character other than
  a letter, a digit or an underscore, or is `Rem`, `Empty`, `New` or `Nothing` in any case
- **THEN** there is no answer
