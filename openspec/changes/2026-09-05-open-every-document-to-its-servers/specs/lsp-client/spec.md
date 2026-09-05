## ADDED Requirements

### Requirement: Every document the editor opens SHALL be offered to the language layer
Every text document the IDE opens in an editor SHALL be opened to the language client, kept synchronized
while it is edited, and closed when its editor closes — including documents the IDE does not itself compile
or understand.

Routing already decides correctly which servers, if any, claim a document. That decision is only ever
reached for documents an editor hands over, so an editor that does not hand its documents over makes the
whole configuration inert for exactly the file types it exists to serve. This requirement is upstream of
routing: it says every editor participates, not which server wins.

A document no server claims SHALL still be offered. Whether anything claims it is the language layer's
answer to give, and an editor that decides in advance not to ask would have to hold an opinion about file
types — which is the coupling the configurable server list removed.

#### Scenario: A file the project carries but does not compile
- **WHEN** the developer opens a carried file whose extension a configured server claims
- **THEN** that server receives the document, and its diagnostics are rendered in that editor

#### Scenario: A carried file nothing claims
- **WHEN** the developer opens a carried file whose extension no server claims
- **THEN** it opens normally with language features absent, no server is started, and no error is reported

#### Scenario: A carried file is edited and closed
- **WHEN** the developer edits a carried file and later closes it
- **THEN** the changes are synchronized as for any other document, and the servers are told it closed

### Requirement: A document on disk SHALL be identified by a URI carrying its extension
A document that exists as a file SHALL be identified to servers by a `file:` URI. The IDE's own documents,
which have no file behind them, SHALL keep a scheme that names their language.

Routing keys on the extension, so an identifier that discards it cannot be routed. The two schemes are not
a preference: a document with no file cannot have a `file:` URI, and a document with a file must not be
given an opaque one, or it becomes unroutable and unopenable by any server that wants to read it from disk.

#### Scenario: A carried file is opened
- **WHEN** a carried file is offered to the language layer
- **THEN** it is identified by a `file:` URI whose extension is the one servers match against

#### Scenario: A document with no file behind it
- **WHEN** a form or module the IDE holds in memory is offered to the language layer
- **THEN** it keeps the scheme that names its language, and is routed by that
