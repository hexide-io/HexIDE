## ADDED Requirements

### Requirement: An add-in SHALL see a document's whole file, and SHALL NOT change its header
The content an add-in reads for a form, module or class SHALL be the whole file as the code window holds it,
header included, and every position it reads or writes SHALL be counted from the top of the file. A
modification that would change the header, or any other read-only region, SHALL be refused, and the add-in
SHALL be told why.

An add-in and the developer must agree on what a line number means; the code window numbers from the top of
the file, so the add-in surface does too. Refusing rather than silently keeping the old header matters for the
same reason every write is awaitable: a caller that writes and reads back has to be able to tell that its write
did not land.

#### Scenario: Reading a form
- **WHEN** an add-in reads a form that is open
- **THEN** it receives the form's designer block and code together, as the file holds them

#### Scenario: Rewriting a module and keeping its header
- **WHEN** an add-in replaces a module's content with text whose header is unchanged
- **THEN** the replacement is applied
