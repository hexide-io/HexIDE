## Purpose
Define what a form, module or class *is* to the IDE while it is loaded, as opposed to what it is called.

A document has several names — its own VB6 name, the file it is saved in, and the name language servers know
it by — and every one of them can change while the developer works: a rename, a first save, a Save As. The
state a developer builds up on a document (breakpoints, bookmarks, where the debugger is stopped, which
diagnostics belong to it) has to survive all of those. It can only do that if it is attached to the document
itself rather than to any of its names, so this capability separates the two.

## ADDED Requirements

### Requirement: Every document SHALL keep one identity for as long as it is loaded
Each form, module and class of a loaded project SHALL have an identity inside the IDE that does not change
while it stays loaded — not when it is renamed, not when it is saved for the first time, not when it is saved
to a new file, and not when its project is renamed. The identity SHALL include the project the document
belongs to, and SHALL NOT be derived from any of the document's names.

Names are what change. A document's VB6 name changes on rename, its file on first save and Save As, its name
on the wire with either; a key made from any of them moves every mark set on the document each time. Two
projects in a group may each hold a module of the same name — VB6 requires a name to be unique only within a
project — so an identity that does not include the project cannot tell them apart.

A UserControl or PropertyPage SHALL have one identity, not one for its designer part and one for its code.

#### Scenario: Renaming a document with breakpoints on it
- **GIVEN** a module with a breakpoint and a bookmark set on it
- **WHEN** the module is renamed
- **THEN** both are still shown on the same lines, still honoured by the next run, and still saved with the
  project

#### Scenario: Two projects with a module of the same name
- **GIVEN** a group of two projects, each with a module named `Module1`
- **WHEN** a breakpoint is set in one of them
- **THEN** it is shown, pushed to a run and saved for that module only

#### Scenario: A first save
- **GIVEN** a form with no file, carrying a bookmark
- **WHEN** it is saved for the first time
- **THEN** the bookmark is on the same line afterwards, and is written into the project's sidecar

### Requirement: Per-document state SHALL follow the document's identity
Breakpoints, bookmarks, the debugger's current statement, Run To Cursor, Set Next Statement, diagnostics shown
for a document, and every place the automation and add-in surfaces name a document SHALL resolve through the
document's identity. A document's name SHALL NOT be recovered by parsing the name it is known by on the wire.

Reading a module's name back out of its wire name works only while the wire name happens to end in it. A file
name need not match the module's name, and two files of the same name may sit in different folders of one
project; recovering the name from the text of a URI then names the wrong module, or none, and does so
silently — the current-statement bar simply never appears.

#### Scenario: Pausing in a module whose file is named differently
- **GIVEN** a module named `Utilities` saved as `util.bas`
- **WHEN** a run pauses in it
- **THEN** the current-statement bar is shown in that module's editor

#### Scenario: Naming a document through automation
- **WHEN** an automation client or add-in names a document by project and name, in any case
- **THEN** the document is found, and anything set through that call is shown in the document's editor

### Requirement: A document's name SHALL be unique within its project, and a new project's among those loaded
The IDE SHALL NOT give a new form, module or class a name already used by another form, module or class of the
same project, compared without regard to case, and SHALL refuse a rename that would. A new project SHALL NOT
be given the name of a project already loaded.

These are VB6's own rules, measured against the compiler rather than assumed: a project whose form and
standard module share a name does not build, and a group whose two projects share a name is refused at load
with nothing in it built. Both comparisons ignore case. Enforcing them as documents are created and renamed
turns a failure at build time into a refusal at the moment it is caused. It also keeps the name usable as an
identifier: the name of a document with no file is part of the only name servers can be given for it, and two
documents sharing one are a single document as far as a server can tell.

#### Scenario: Adding a form after deleting one
- **GIVEN** a project with `Form1` and `Form2`, from which `Form1` has been removed
- **WHEN** a form is added
- **THEN** it is not named `Form2`

#### Scenario: Starting a project after closing one
- **GIVEN** `Project1` and `Project2` loaded, and `Project1` then closed
- **WHEN** a new project is started
- **THEN** it is not named `Project2`
