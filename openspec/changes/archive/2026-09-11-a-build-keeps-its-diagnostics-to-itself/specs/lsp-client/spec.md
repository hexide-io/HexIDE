## MODIFIED Requirements

### Requirement: Diagnostics SHALL be pushed through a single channel from more than one source
Diagnostics SHALL reach the editor by push rather than by polling, and the client SHALL expose one
diagnostics channel that carries both server-published diagnostics and diagnostics injected by the IDE.

Every diagnostic SHALL carry the identity of the source that published it, and a source's publication for a
document SHALL replace only what that source last published for it. What the channel raises SHALL remain a
single whole-document set — everything every source currently claims about that document — so that no
consumer has to reason about who published what. An empty set from a source SHALL clear that source's
diagnostics for the document, and SHALL NOT clear another source's.

A source SHALL additionally be able to withdraw everything it has published, across every document. That
withdrawal SHALL be scoped by what the source actually published rather than by a list of documents the
caller supplies, because the two disagree exactly when it matters: a document renamed since that source
last spoke is absent from such a list and still carries its marks.

The IDE has a second source of truth: compiling with the real VB6 toolchain produces errors the syntactic
server cannot know about, and more than one language server may claim one document. Feeding all of them
through one channel means the marker pipeline, the editor, and any future consumer handle them identically
and cannot disagree about which errors are current. Ownership is what keeps that from also meaning the last
publisher wins — without it, a build expiring its own previous errors deletes what a server published for
the same document, and two servers on one document erase each other.

#### Scenario: The server reports a syntax error
- **WHEN** the server publishes diagnostics for a document
- **THEN** they are raised on the diagnostics channel and rendered in the editor

#### Scenario: The compiler reports an error the server cannot see
- **WHEN** the IDE compiles with the real VB6 toolchain and that compiler reports errors
- **THEN** those errors are injected into the same diagnostics channel and rendered the same way
- **AND** they appear alongside anything a server has published for the same document

#### Scenario: Errors are resolved
- **WHEN** a source of diagnostics reports an empty set for a document
- **THEN** that source's diagnostics for the document are cleared
- **AND** what other sources have published for it remains

#### Scenario: A build that the compiler had nothing to say about
- **WHEN** a build succeeds and expires the errors of the build before it
- **THEN** the diagnostics a language server published for those documents are still present

#### Scenario: Two servers claiming one document
- **WHEN** each publishes diagnostics for that document
- **THEN** both sets are current, and neither replaces the other
