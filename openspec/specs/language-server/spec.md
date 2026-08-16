# language-server Specification

## Purpose
Define the language server HexIDE ships: what it must do to serve the IDE, what it deliberately does not
do, and the licensing constraint it exists under.

Two things make this worth stating as a contract rather than leaving as whatever the current implementation
happens to do. The server is meant to be replaceable, and a replacement can only conform to something
written down — several of the behaviours the editor depends on are choices the protocol permits rather than
requires, so an implementation could be perfectly protocol-conformant and still break the editor. And the
licence constraint is not a property of the code so much as a property of everything it embeds, which is
the kind of thing that erodes silently unless it is a requirement.

## Requirements
### Requirement: The source tree SHALL be uniformly permissively licensed
The language server and every artifact it embeds SHALL be permissively licensed, and the tree SHALL NOT
contain a copyleft licence file, a copyleft-derived grammar, or test inputs derived from a copyleft
project.

The licence claim is either unconditional or it is not worth making. A single embedded dependency is
enough to change the terms of everything that links it, and the obligation travels in forms that are easy
to overlook — generated parser output, and fixtures copied from another project's test suite look like
ordinary files.

#### Scenario: Auditing the tree
- **WHEN** the tree is searched for copyleft licence files, grammars, or derived test inputs
- **THEN** none are found

#### Scenario: Adding a parsing dependency
- **WHEN** a new grammar or parsing dependency is proposed
- **THEN** it is adopted only if its licence permits redistribution under the project's own terms

### Requirement: The language server SHALL run as a replaceable external process
The server SHALL run outside the IDE process and communicate over a duplex byte stream, and the IDE SHALL
NOT link it.

Parsing is unbounded work on hostile input, so a wedged parse must not be able to wedge the editor.
Keeping the boundary also means any implementation that speaks the protocol can take its place — including
one whose licence would forbid linking — which is what makes "replaceable backend" a fact rather than an
intention.

#### Scenario: The server becomes unresponsive
- **WHEN** the server hangs or crashes while handling a request
- **THEN** the IDE continues running and editing is unaffected

#### Scenario: Substituting a different backend
- **WHEN** a different language backend is placed behind the seam
- **THEN** it serves the IDE without any change to the IDE, provided it honours the contract below

### Requirement: The server SHALL publish an empty diagnostic set when a document closes
On being notified that a document closed, the server SHALL publish diagnostics for that document with an
empty set, rather than publishing nothing.

Consumers evict cached state for a document when they see its diagnostics go empty. A server that simply
stops publishing leaves them holding markers for a document that is no longer open — which surfaces as
stale squiggles rather than as an obvious protocol fault.

#### Scenario: Closing a document that had errors
- **WHEN** a document with reported errors is closed
- **THEN** the server publishes an empty diagnostic set for it
- **AND** consumers clear the state they were holding for that document

### Requirement: The server SHALL publish diagnostics on every open and change
The server SHALL publish diagnostics for every open and change notification it receives, and SHALL NOT
debounce, coalesce, or suppress a publish because the contents match the previous one.

The publish is used as a signal as well as a payload: a consumer refreshes derived state on its arrival,
not on its contents. Suppressing an identical publish is a reasonable-looking optimization that silently
stops that refresh from happening.

#### Scenario: A change that does not alter the diagnostics
- **WHEN** a document changes and the resulting diagnostics are identical to the previous set
- **THEN** the server publishes them again rather than suppressing the notification

### Requirement: The server SHALL process requests in order with respect to document changes
The server SHALL apply a document change before handling any request that arrives after it.

The IDE flushes pending edits and then immediately asks a question about the edited text — formatting on
save is the clearest case. Concurrent handling would let the question be answered against the text as it
was before the edit, producing a result that is wrong in a way that looks intermittent.

#### Scenario: Requesting formatting immediately after an edit
- **WHEN** a change notification is followed by a request for the same document
- **THEN** the request is answered against the changed text

### Requirement: The server SHALL answer "nothing found" with an empty result
Where a request has no answer, the server SHALL return an empty result rather than a protocol-level error.

The two are not interchangeable to the caller: an empty result is an ordinary outcome that the editor
absorbs silently, whereas an error is surfaced. Reporting "no definition at this position" as an error
turns a normal interaction into a visible failure.

#### Scenario: Requesting a definition where none exists
- **WHEN** a definition is requested at a position with nothing to resolve
- **THEN** an empty result is returned rather than an error

### Requirement: The server SHALL parse a document once per change
The server SHALL parse a changed document once and share the resulting tree across the features that need
it, rather than parsing separately per feature.

Diagnostics, document symbols and completion all want the same tree for the same text. Parsing once is
both faster and safer: separate parses of the same document can disagree if the text changes between them,
producing symbols that do not match the diagnostics beside them.

#### Scenario: A change requiring diagnostics and symbols
- **WHEN** a document changes and both diagnostics and symbols are produced
- **THEN** both are derived from a single parse of that text

### Requirement: The server SHALL confine itself to syntactic analysis
The server SHALL operate on the syntax tree only, and SHALL NOT build a bound semantic model, resolve names
across documents, or perform project-wide analysis.

This is a scope boundary rather than a limitation to be lifted later. Producing a semantic model is the job
of a real language engine behind the replaceable seam; a half-built one here would be a compiler frontend
nobody committed to maintaining, and it would make the seam harder to hand over rather than easier.

#### Scenario: A feature that needs cross-document resolution
- **WHEN** a proposed feature requires resolving a name defined in another document
- **THEN** it is out of scope for this server and belongs behind the replaceable seam

#### Scenario: Checking for undeclared variables
- **WHEN** the undeclared-variable check runs without a symbol table covering intrinsics, controls and project types
- **THEN** it is disabled by default, because it cannot distinguish an undeclared variable from a name it simply cannot see

