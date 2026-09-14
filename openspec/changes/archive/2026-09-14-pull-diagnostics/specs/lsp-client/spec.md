## MODIFIED Requirements

### Requirement: Diagnostics SHALL be pushed through a single channel from more than one source
The client SHALL expose one diagnostics channel that carries both server-published diagnostics and
diagnostics injected by the IDE, and SHALL raise on it rather than requiring a consumer to poll.

How the client OBTAINS diagnostics from a server is a separate question from how it DELIVERS them onward,
and the two SHALL NOT be conflated. A server may publish unbidden or may require the client to ask; either
way what reaches the channel SHALL be indistinguishable, so that no consumer of the channel — the marker
pipeline, the editor, or anything later — can tell which model the server on the other end uses.

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

#### Scenario: A server that answers only when asked
- **WHEN** a server delivers its diagnostics by answering requests rather than by publishing
- **THEN** what reaches the channel is the same shape, under the same source identity, as if it had published

## ADDED Requirements

### Requirement: The client SHALL ask servers that answer only when asked
A server MAY deliver diagnostics by answering `textDocument/diagnostic` requests instead of publishing
them. The client SHALL support that model as well as the published one, because a server that only answers
when asked is otherwise indistinguishable from a broken one: it starts, completes the handshake, advertises
honestly, and then says nothing about the developer's code for as long as it runs.

The client SHALL declare its ability to ask, and SHALL ask only of a server that advertised the capability.
Both halves are required and neither alone is sufficient: gating on an advertisement the client never
invited produces a state in which both parties are correct and nothing happens, which is the deadlock
already recorded for save negotiation.

A server that does not advertise the capability SHALL NOT be reported as deficient. Its silence is a
complete and correct answer — it publishes instead — and treating the absence as a missing feature would
blame every conformant publishing server for the way it conforms.

The client SHALL ask when a document is opened, after it changes, and when it is saved. It SHALL NOT
introduce pacing of its own for this: the edit stream is already coalesced before it reaches the client, and
a second authority over the same question would answer it differently.

A server MAY additionally ask to be asked again, and the client SHALL honour that by re-requesting every
open document. It is the only means such a server has of reporting a change that is not a document — its
own configuration, a rule set, something it watches — and without it the editor would keep showing answers
derived from a configuration that no longer exists, with no event that would ever correct them.

#### Scenario: A server that only answers when asked
- **WHEN** such a server claims a document and that document is opened
- **THEN** the client asks it for that document's diagnostics
- **AND** what comes back reaches the editor as markers

#### Scenario: A server that publishes
- **WHEN** a server did not advertise that it answers when asked
- **THEN** the client does not ask it
- **AND** nothing reports that server as missing a capability

#### Scenario: The document changes
- **WHEN** an open document is edited
- **THEN** the client asks again, and the answer replaces what that server last said about the document

#### Scenario: The server says its answers are stale
- **WHEN** such a server asks for diagnostics to be refreshed
- **THEN** the client asks again about every open document, rather than refusing

#### Scenario: A server that both publishes and offers to answer
- **WHEN** a server advertises that it answers when asked and also publishes unbidden
- **THEN** the client takes only what it answers, so that one source of truth decides the document's marks

### Requirement: An answer that says "nothing has changed" SHALL preserve what it refers to
A server answering a diagnostics request MAY say that its previous answer still stands rather than
repeating it. Such an answer carries no diagnostics, and the client SHALL treat it as leaving that server's
diagnostics for the document exactly as they were — never as an empty set.

The distinction is the whole purpose of the model's result identifiers and it fails destructively if
missed: an answer meaning "what I told you before is still true" would erase precisely the diagnostics it
was sent to preserve, and it would do so only on the second request, so the markers would appear correctly
and then vanish.

To make such an answer possible the client SHALL retain the identifier the server gave with its last answer
for a document and send it back with the next request for that document. It SHALL send the identifier the
server named for itself where the server named one, so a server distinguishing its own answers from another
party's can do so.

Where a request has been overtaken — the document changed again before the answer arrived — the client
SHALL discard the answer rather than publish it, because it describes text that is no longer open.

#### Scenario: The server says its previous answer still stands
- **WHEN** the client asks again and the server answers that nothing has changed
- **THEN** the diagnostics already shown for that document remain shown

#### Scenario: An answer arrives after the document moved on
- **WHEN** a further change is made before an answer to an earlier request arrives
- **THEN** that answer is discarded rather than rendered

#### Scenario: The document is closed
- **WHEN** a document served this way is closed
- **THEN** that server's diagnostics for it are cleared, since it has no way to withdraw them itself
