## ADDED Requirements

### Requirement: An add-in SHALL see a document's whole file, and SHALL NOT change its header
The content an add-in reads for a form, module or class SHALL be the whole file as the code window holds it,
header included, and every position it reads or writes SHALL be counted from the top of the file. Replacing a
document's content SHALL accept either the whole file with its header unchanged or the code alone, keeping the
header. A modification that would change the header, or any other read-only region, SHALL be refused, and the
add-in SHALL be told why.

An add-in SHALL be able to name a document by its project as well as its name, and SHALL be able to reach a
document of any loaded project rather than the startup project alone. Where an add-in names a document that
more than one loaded project has, and does not say which, the call SHALL be refused rather than answered from
whichever project was loaded first.

An add-in and the developer must agree on what a line number means; the code window numbers from the top of
the file, so the add-in surface does too. Refusing rather than silently keeping the old header matters for the
same reason every write is awaitable: a caller that writes and reads back has to be able to tell that its
write did not land.

#### Scenario: Reading a form
- **WHEN** an add-in reads a form that is open
- **THEN** it receives the form's designer block and code together, as the file holds them

#### Scenario: Rewriting a module and keeping its header
- **WHEN** an add-in replaces a module's content with text whose header is unchanged
- **THEN** the replacement is applied

#### Scenario: Applying a block of code alone
- **WHEN** an add-in replaces a document's content with code that has no header
- **THEN** the code is applied and the document keeps its header

#### Scenario: Two projects with a document of the same name
- **WHEN** an add-in names such a document without naming a project
- **THEN** the call is refused and the add-in is told the name is ambiguous
