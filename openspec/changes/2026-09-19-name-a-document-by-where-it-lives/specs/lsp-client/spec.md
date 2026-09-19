## MODIFIED Requirements

### Requirement: The IDE SHALL depend only on the language-service contract
Editor and view-model code SHALL obtain every language feature — diagnostics, hover, completion, document
symbols, folding, signature help, definition, document highlight, rename, formatting — through one
language-client interface, and SHALL NOT reference the transport, the RPC library, or any server assembly.

The point of the seam is that a replacement backend is a configuration concern rather than a refactor. That
only holds if nothing upstream of the interface knows what is behind it, so the constraint is on the
consumers as much as on the implementation.

Folds the editor makes itself to present a file's structure — the header, and attribute lines that describe
a member — SHALL NOT be obtained through that interface, and are not a language feature in this sense. They
describe the layout of the file format, which is the same whichever server is attached and present when none
is, and a fold that disappeared with the server would expose the text it exists to keep out of the way.

#### Scenario: Adding a feature that needs language data
- **WHEN** a view model needs language information
- **THEN** it calls the language-client interface
- **AND** it does not observe which server, transport, or protocol produced the answer

#### Scenario: Replacing the backend
- **WHEN** the language backend is replaced with a different implementation
- **THEN** no editor or view-model code changes

#### Scenario: No server is attached
- **WHEN** a form or module is opened with no language server running
- **THEN** its header is still folded, and the fold does not depend on any server's answer

### Requirement: A document SHALL be routed by extension, and each server told the identifier it declared
Routing SHALL key on the document's extension. Where a document is offered to more than one server, each
server SHALL be told the language identifier **that server declared**, rather than a single identifier
shared between them.

Two servers may legitimately disagree about what an extension means. A document carries exactly one language
identifier when it is opened, so a single shared table forces a winner and makes the loser wrong about every
file it sees. Each server has its own connection, and the protocol does not require two connections be told
the same thing — so telling each what it asked for dissolves the disagreement instead of adjudicating it.

An extension no entry claims SHALL route nowhere, and the document SHALL open with language features absent.
That is a normal outcome and SHALL NOT be reported as an error.

Every document the IDE offers SHALL carry an extension, including a form or module that has no file yet, so
no URI scheme SHALL take precedence over the extension and a language identifier SHALL NOT act as a claim.
The identifier a server declares names what it is told a document is; which documents reach it is decided by
the extensions it claims.

#### Scenario: Two servers claim one extension under different identifiers
- **WHEN** a document of that extension is opened
- **THEN** both servers receive it, each told the identifier it declared

#### Scenario: An extension nothing claims
- **WHEN** a document is opened whose extension no entry claims
- **THEN** it opens with language features absent and no error is reported

#### Scenario: A module with no file yet
- **WHEN** a module that has never been written to disk is opened
- **THEN** it is routed by the extension its kind will be saved with, exactly as a saved module is

#### Scenario: An entry that names the language but claims none of its extensions
- **WHEN** an entry declares the identifier `vb6` and claims no VB6 extension
- **THEN** it is not offered the IDE's forms and modules

### Requirement: A server SHALL be given the project's working directory
Each server SHALL be started with the current project's working directory as its working directory, and told
that directory as its workspace root, unless its entry states otherwise.

Servers routinely resolve their own configuration relative to where they are run. A server started somewhere
arbitrary reads none of the user's settings for it and reports subtly different results with no indication
why — a wrong answer rather than a missing one.

Where a project's working directory changes during a session, running servers SHALL be restarted so that
none continues against a root that no longer describes the project. **Every document open in an editor at
that moment SHALL be offered again to each restarted server that claims it**, before any change to it is
sent. A restarted connection starts with nothing open; without that, the next change to a document the
developer already had open reaches a server that was never told it exists, and every document except the
one whose opening happened to trigger the restart goes silently dark. A project's first save moves its
directory, so this is the ordinary path rather than a corner.

**Where that directory does not exist, the server SHALL still be started**, inheriting the IDE's own working
directory, and SHALL still be told the project's directory as its workspace root. The two are separate
questions — *where the process runs* and *which tree it analyses* — and they only coincide by accident. A
project that has not been saved yet has a directory that is real as an answer to the second and not yet real
as an answer to the first: it is where the project's files will be written the moment the user adds a
module, and it is the parent of every document the server will be told is on disk. A form or module with no
file yet is outside it, because it is not anywhere yet. Starting a process there instead fails outright,
which costs the whole connection and every language feature with it.

The IDE SHALL NOT create the directory in order to launch there. Nothing reaps these directories, so one
created per server start would accumulate with no owner, and a directory that exists would then no longer
mean the project has content in it.

A working directory named by the server's **own entry** SHALL NOT be checked for existence and SHALL be used
as given. Somebody who named one meant it, and a name that does not resolve is a configuration error they
can correct and must be told about; inheriting silently there would start the server against the wrong tree,
which is a wrong answer rather than a failure.

Where a server cannot be started, the reported reason SHALL name the working directory it was to be started
in. The operating system's own message for an unusable working directory does not name it, so a report
without it points at the executable for a fault that has nothing to do with it.

#### Scenario: A server that reads its own configuration from the workspace
- **WHEN** a server is started for a project
- **THEN** its working directory and workspace root are the project's working directory

#### Scenario: A project that has not been saved yet
- **WHEN** a server is started for a project whose directory does not exist
- **THEN** the server starts, inheriting the IDE's working directory
- **AND** it is still told the project's directory as its workspace root
- **AND** the directory is not created

#### Scenario: An entry naming a working directory that is not there
- **WHEN** a server's own entry names a working directory that does not exist
- **THEN** the server does not start, and the reported reason names that directory

#### Scenario: The root moves while documents are open
- **GIVEN** two documents open in editors
- **WHEN** the project's directory changes and the running servers are restarted
- **THEN** each restarted server is told both documents are open before it is sent a change to either

### Requirement: A document on disk SHALL be identified by a URI carrying its extension
A document that has a file SHALL be identified to servers by that file's `file:` URI — a form, module or
class of the project exactly as much as a file the project carries. A form, module or class that has no file
yet SHALL be identified as `untitled:<project>/<name>.<ext>`: the project's name, the document's own name,
and the extension its kind will be saved with.

Routing keys on the extension, so an identifier that discards it cannot be routed. The two schemes are not
a preference: a document with no file cannot have a `file:` URI, and a document with a file must not be
given an opaque one, or it becomes unroutable and unopenable by any server that wants to read it from disk.

A file in a folder the IDE chose, such as the scratch folder of a project not yet saved, SHALL be named by
its `file:` URI like any other: it is where the bytes are, and it is inside the root servers are told about.
Naming it `untitled:` would show a server that indexes that root the same module twice, once from disk and
once from the editor. A saved document whose file has since disappeared SHALL keep its `file:` URI:
`untitled:` means a document that has no file yet, not one whose file is missing.

The `untitled:` name SHALL use the document's name, never a file name — there is no file — and SHALL be
written without a leading slash and percent-encoded, one spelling only. Two spellings of one name are two
documents to a server, and a server may reject a name that is not strictly valid without replying at all.
Two `untitled:` names SHALL be compared without regard to case, as VB6 compares the names they are made of.

A document's wire name is not its identity inside the IDE. It SHALL be fixed when the document is opened to
the language layer and SHALL change only as the next requirement describes, never by reading the document's
current path: a build repoints every path into a temporary folder and back, and that is not a move.

#### Scenario: A carried file is opened
- **WHEN** a carried file is offered to the language layer
- **THEN** it is identified by a `file:` URI whose extension is the one servers match against

#### Scenario: A document with no file behind it
- **WHEN** a form or module the IDE holds only in memory is offered to the language layer
- **THEN** it is identified as `untitled:` followed by its project's name, its own name and the extension of
  its kind
- **AND** it is routed by that extension

#### Scenario: A saved module is opened
- **WHEN** a module of a saved project is offered to the language layer
- **THEN** it is identified by the `file:` URI of its file, and a server may read that file

#### Scenario: A module in a project not yet saved
- **WHEN** a module has been added to a project that has never been saved, and so written to the project's
  scratch folder
- **THEN** it is identified by the `file:` URI of that file

#### Scenario: Building the project
- **WHEN** the project is built and every path is temporarily pointed elsewhere
- **THEN** no server is told any document closed, opened or moved

### Requirement: A document SHALL be offered to every server that claims its language
The IDE SHALL route a document by its language identity, derived from the document's extension, and SHALL
offer it to **every** registered server claiming that language rather than selecting one. Where more than one
server answers, the IDE SHALL combine their results: diagnostics from all sources SHALL be shown together,
and list-shaped results SHALL be concatenated.

Routing to a single server is the simpler design and the wrong one. The arrangement it forecloses — a
language server and a separate linter or checker on the same file — is ordinary rather than exotic, and it is
the arrangement the wider ecosystem is built around. The asymmetry decides it: a combining router can be
configured down to one server, while a router that picks one cannot be widened without changing every caller.

Language identity SHALL come from the document's extension rather than from its role in the project. A
project member's kind is a project-file concept, and the documents most likely to need a second server —
files carried alongside the project rather than compiled by it — have no such kind.

**An extension another language also uses SHALL NOT be enough, on its own, to receive a project's forms,
modules and classes.** A project member is VB6 whatever its extension shares, so where its extension is
ambiguous it SHALL be offered only to servers whose claim establishes VB6: one that claims an extension no
other language uses for source, or that declares the identifier `vb6`. A file the project carries is not
known to be VB6, and SHALL be routed by its extension alone. The alternative costs a started process and the
developer's source sent to a server with nothing to say about it, for every class module in the project.

#### Scenario: Two servers claim the same language
- **WHEN** a document is opened whose language is claimed by more than one registered server
- **THEN** every claiming server receives the document, and their diagnostics are shown together

#### Scenario: No server claims the language
- **WHEN** a document is opened whose language no registered server claims
- **THEN** the document opens normally with language features absent, and nothing is reported as an error

#### Scenario: A class module and a server claiming only the shared extension
- **GIVEN** a server entry whose only VB6-looking claim is `.cls`
- **WHEN** a class module of the project is opened
- **THEN** that server does not receive it, and the VB6 server does

#### Scenario: A carried file with the shared extension
- **GIVEN** the same server entry
- **WHEN** a `.cls` file the project carries is opened
- **THEN** that server receives it

## ADDED Requirements

### Requirement: A document whose name changes SHALL be closed and reopened
Where a document's wire name changes while it is open, each server that has it open SHALL be told it closed
under the old name and then that it opened under the new one, with its current text, in that order on each
connection. The name changes when a document is written to a file for the first time, when it is saved to a
different file, and — for a document with no file — when its name or its project's name changes. Where the
change was caused by a save, a server that asked to hear about saves SHALL be told under the new name.

This is the shape the protocol itself prescribes for a rename, and for the reason it gives: more than the
name can change, including which servers claim the document. Sending both without ordering them lets a
server see two open documents for one file, or close the one it has just been given.

Nothing a developer set on the document SHALL move: its breakpoints, bookmarks and diagnostics belong to the
document, not to the name it is known by on the wire.

#### Scenario: Saving a form for the first time
- **GIVEN** a form with no file, open in the editor as `untitled:Project1/Form1.frm`
- **WHEN** the developer saves it to `Form1.frm`
- **THEN** each server that had it open is told it closed as `untitled:Project1/Form1.frm`
- **AND** only then that it opened under the `file:` URI of `Form1.frm`
- **AND** its breakpoints and bookmarks are unchanged

#### Scenario: Renaming a form that has a file
- **WHEN** a saved form is renamed
- **THEN** its wire name does not change, because its file has not

#### Scenario: Renaming a module that has no file
- **WHEN** a module with no file is renamed
- **THEN** each server that had it open is told it closed under the old name and opened under the new one
