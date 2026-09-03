## ADDED Requirements

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

#### Scenario: Two servers claim the same language
- **WHEN** a document is opened whose language is claimed by more than one registered server
- **THEN** every claiming server receives the document, and their diagnostics are shown together

#### Scenario: No server claims the language
- **WHEN** a document is opened whose language no registered server claims
- **THEN** the document opens normally with language features absent, and nothing is reported as an error

### Requirement: Features that cannot combine SHALL select one server by stable identity
Where combining answers is meaningless — formatting a document, or renaming a symbol — the IDE SHALL select
exactly one server. Every registered server SHALL carry an identity that is stable across restarts, and
selection SHALL be expressed in terms of that identity.

Two edits to the same document cannot both be applied, so these features need a winner rather than a merge.
The identity requirement is what makes choosing one *expressible*: a server known only by its position in a
list cannot be named in a setting, referred to in a message, or remembered between sessions.

Where several servers remain eligible, selection SHALL be deterministic: an explicit priority where one is
declared, and registration order otherwise. Registration order alone is deterministic but accidental, because
it varies with discovery — which changes when a server is installed or removed.

#### Scenario: Two servers offer to format the same document
- **WHEN** a format is requested and more than one registered server offers formatting
- **THEN** exactly one server's edits are applied, chosen by declared priority and then registration order

### Requirement: Requests SHALL be gated on the capabilities the server advertised
For each request, the IDE SHALL send it only to servers that advertised support for it during
initialization, and SHALL treat an unadvertised feature exactly as it treats an absent server — returning
the same empty or absent result rather than raising an error.

A server states what it implements; calling it for anything else produces routine failures indistinguishable
from a broken server, and gives a reader no way to tell "not supported here" from "not working". Reusing the
existing degradation path means gating changes nothing for any caller.

The IDE SHALL NOT interpret an empty capability set as support for everything. A server that advertises
nothing is stating that it serves nothing, and a client that overrides that statement cannot detect the same
omission in any other server.

A capability SHALL be recognised in each of the forms the protocol permits it to take, including both a plain
flag and an options object. A capability arriving in a legal form the IDE fails to parse SHALL NOT prevent
initialization from completing.

The last paragraph is not defensive coding. A parse failure during initialization leaves the connection
uninitialized, which disables **every** language feature including diagnostics — so a single capability
arriving in its other legal shape silently costs the whole backend.

#### Scenario: A feature the server did not advertise
- **WHEN** a language feature is requested that the answering server did not advertise
- **THEN** the request is not sent, and the caller receives the same result it receives when no server is running

#### Scenario: A capability advertised as an options object
- **WHEN** a server advertises a capability as an options object rather than a plain flag
- **THEN** the capability is recognised as supported and initialization completes normally

### Requirement: Connections SHALL be observable
The IDE SHALL expose its language-service connections as inspectable state: for each, a stable identity, what
kind of connection it is, its current state, the languages it serves, and the capabilities it advertised as
received. A server that is configured but not yet started SHALL be observable as such, rather than absent.

This exists so the question people actually bring to a language service — *why is this file getting no help?*
— has an answer. Without it, a server that is quiet because nothing triggered it is indistinguishable from
one that is missing, misconfigured, or crashed, and those need different responses.

Advertised capabilities SHALL be retained as received rather than reduced to a summary. A summary invented
now will be wrong for a server not yet met, and the raw answer is the only honest response to "why is this
feature unavailable here".

The connection description SHALL NOT be specific to one protocol. A debug connection has the same properties
worth showing, and the shape a user interface consumes should not need rebuilding to gain a second kind.

#### Scenario: A configured server that has not started
- **WHEN** a server is configured for a language and no document of that language has been opened
- **THEN** the connection is observable, and is distinguishable from a server that failed to start

### Requirement: Servers SHALL start on first use and own their transport
A server SHALL be started when a document of a language it claims is first opened, and not before. Each
registered server SHALL carry its own transport configuration.

Starting every configured server on project open spends time and memory on languages a project does not
contain, and lets an absent or broken server for an unused language degrade startup for everyone. Transport
is per-server because servers do not agree on one: a single global choice cannot describe two servers that
communicate differently, and the transport a server needs is a property of that server.

#### Scenario: A project containing no documents of a configured language
- **WHEN** a project is opened that contains no document of some configured language
- **THEN** that language's server is not started
