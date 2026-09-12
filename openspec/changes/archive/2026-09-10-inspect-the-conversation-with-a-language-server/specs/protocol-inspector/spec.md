## ADDED Requirements

### Requirement: The IDE SHALL record every language-server conversation continuously
The IDE SHALL record, for every running language-server connection and without being asked, the direction,
method, correlation id, timestamp, size, outcome and elapsed time of every message. This record SHALL carry
no content from the user's documents.

A conversation cannot be recorded retrospectively. The thing a developer wants to look at is almost always
something they noticed after it happened, and a tool that must be turned on first answers only the questions
its user already knew to ask. Recording the envelope and not the content is what makes always-on defensible:
it is a few dozen bytes per message and it discloses nothing.

#### Scenario: Something odd just happened
- **WHEN** a developer opens the inspector having armed nothing
- **THEN** the conversation that has already taken place is there to read

#### Scenario: What the record does not contain
- **WHEN** nothing has been armed
- **THEN** no document text, no file path and no user-authored string has been retained

#### Scenario: A frame this client cannot read
- **WHEN** something arrives that cannot be decoded as a JSON-RPC message
- **THEN** the record holds the bytes and says they could not be decoded, rather than holding nothing

### Requirement: Message content SHALL be retained only for a connection that has been armed
Retaining message payloads SHALL require arming that connection explicitly. Arming SHALL be per connection,
SHALL be visible wherever connections are listed, and SHALL NOT survive a restart of the IDE. It SHALL
survive a restart of the *server*, and SHALL be re-applied to the replacement process.

The payload is where the user's source lives, so arming is the entire disclosure boundary and has to be a
decision somebody made. It does not survive an IDE restart because a capture does not either, and an arming
flag that outlives the thing it was arming quietly converts off-by-default into off-until-once. It does
survive a server crash, because a crash is what the developer armed capture to watch, and taking the tool
away at that moment is taking it away when it was about to earn its keep.

#### Scenario: Arming one server
- **WHEN** one connection is armed
- **THEN** its messages are retained in full and no other connection's are

#### Scenario: The server crashes and is replaced
- **WHEN** an armed server dies and is restarted within the session
- **THEN** the replacement is armed too, without the developer arming it again

#### Scenario: The IDE is restarted
- **WHEN** the IDE is closed and reopened
- **THEN** nothing is armed

### Requirement: The handshake SHALL be recorded in full regardless of arming
The IDE SHALL retain the full content of each connection's initialization exchange whether or not that
connection is armed, and SHALL NOT discard it to make room for later messages.

Servers here start lazily, when a document of their language is first opened, so arming after the fact
cannot reach a handshake that has already happened. That is where the expensive failures live: several of
the defects this project has paid for turn on what was in the initialization reply, which an envelope does
not carry. The cost is fixed and small, one exchange per server per process, which is what makes an
exception affordable here and nowhere else.

#### Scenario: Arming after a server has started
- **WHEN** a developer arms a connection whose server started earlier
- **THEN** that connection's initialization exchange is available in full

#### Scenario: A long session
- **WHEN** the record has filled and older messages are being discarded
- **THEN** the initialization exchange is still there

### Requirement: The record SHALL carry what did not cross the wire
The record SHALL include requests this client declined to send because the server did not advertise them,
naming the capability responsible; requests this client tried to send and could not; capabilities the server
advertised that this client does not consume; and, for a connection whose transport the IDE cannot fully
observe, a statement of what cannot be observed. These entries SHALL be distinguishable from messages that
were actually sent.

An absence has causes, and reporting the wrong one is worse than reporting nothing. A feature that is off
because a server never claimed it looks exactly like a feature that is broken, and a wire trace alone cannot
tell them apart because the request was never made. The inverse is the entry a server author most wants: a
list of what they have offered that this client is not yet taking. And "this transport produced no lifecycle
events" reads identically to "lifecycle for this transport cannot be observed", the first of which is wrong.

#### Scenario: A feature that never fired
- **WHEN** a request was not sent because the server advertised no such capability
- **THEN** the record says so and names the capability, marked as never sent

#### Scenario: A request that failed inside this client
- **WHEN** a message cannot be serialized and so never reaches the transport
- **THEN** the record says so, naming the method, marked as never sent

#### Scenario: An offer nobody took up
- **WHEN** a server advertises a capability this client does not use
- **THEN** the record says so

#### Scenario: A server the IDE did not start
- **WHEN** a connection uses a transport whose process the IDE does not own
- **THEN** the record states which observations are unavailable for it

### Requirement: The record SHALL carry the process, not only its messages
For a connection whose process the IDE started, the record SHALL include its start, its termination, its
exit code, and everything it wrote to standard error, attributed to that connection and interleaved with its
messages in one timeline.

A server that fails badly does not explain itself in the protocol. Standard error is its only channel for a
crash, because a server speaking over standard output may write nothing else there, and measurement bears
this out: when servers in this project's own fixture genuinely failed, the human-readable cause appeared
only on standard error and in a protocol error reply, and nothing at all appeared in the protocol's
user-facing message channels. The exit code is the other half and is easy to miss: LSP gives that number a
meaning — a server exits 0 when a shutdown preceded exit and 1 otherwise — which is how a defect making every
clean exit look like a crash was found by reading one. The record is the only place this codebase surfaces
it.

#### Scenario: A server crashes
- **WHEN** a language server terminates unexpectedly
- **THEN** its exit code and its final standard-error output appear in the timeline at the point it died

#### Scenario: Several servers attached
- **WHEN** more than one server is running and one writes to standard error
- **THEN** the output is attributed to the server that produced it

### Requirement: A capture SHALL NOT outlive the session
The IDE SHALL NOT write captured content to disk of its own accord, and a capture SHALL be discarded when
the IDE closes. Exporting SHALL be the only way a capture is kept.

Retaining a developer's source on disk continuously is a decision taken on their behalf that they did not
make. An export is a decision they did make.

#### Scenario: Closing the IDE
- **WHEN** the IDE is closed with a capture in memory
- **THEN** nothing of it remains on disk

### Requirement: An export SHALL be shown before it leaves, and SHALL pseudonymise rather than erase
Exporting or copying SHALL apply a redaction that replaces identifying values with stable substitutes,
consistently within one capture, rather than removing them. The developer SHALL be shown what the export
contains before it leaves, and SHALL be told the scale of the disclosure in terms of their own material.
Redaction SHALL apply to what leaves, not to what is displayed.

Erasure makes a trace safe and useless, and a redactor that produces useless traces gets switched off. A
client sending one spelling of a path and a server echoing another is a real, already-paid-for defect class
that vanishes the moment both become the same placeholder; stable substitutes keep it visible. Display is a
different question, because the person reading their own conversation is the person who owns the files, and
redacting the view would break the one affordance that proves what actually crossed the wire.

A preview is the only way somebody catches a secret sitting inside a string literal, which no
content-agnostic redactor can recognise. And the size of a disclosure has to be stated in terms a person can
weigh, because a message count tells them nothing about how much of their source they are about to send.

#### Scenario: Exporting a conversation
- **WHEN** a developer exports a capture
- **THEN** they are shown the redacted content, and how much of their material it contains, before it is written

#### Scenario: A normalisation defect surviving redaction
- **WHEN** a client and a server referred to one document by two spellings
- **THEN** the two spellings remain distinguishable in the exported capture

#### Scenario: Reading your own conversation
- **WHEN** a developer views a captured message in the IDE
- **THEN** it is shown as it was, unredacted

### Requirement: The inspector SHALL present one timeline across all servers
The inspector SHALL present every connection's activity as a single ordered timeline, with the server
available as a filter rather than as a separate view, and SHALL default to the connection relevant to what
the developer is working on.

This IDE sends a document to every server that claims it and merges the answers, so "which of you answered,
and was the other even asked" is an ordinary question here rather than an exotic one. A view that shows one
server at a time cannot express it, and would make a second server's silence indistinguishable from its
absence.

#### Scenario: Two servers claiming one document
- **WHEN** a document is served by more than one language server
- **THEN** both conversations appear in one timeline, ordered together

#### Scenario: Narrowing to one
- **WHEN** the developer filters to a single server
- **THEN** only that server's activity is listed, and the filter is visible

### Requirement: A full record SHALL discard the oldest and say that it did
When a limit is reached the IDE SHALL discard the oldest material rather than stop recording, SHALL report
how much was discarded, and SHALL account for it per connection.

Limits SHALL be configurable, globally and per server, and a configured value outside the range the capture
can honour SHALL be clamped rather than refused — with the correction reported through a channel the
developer sees. A limit arrives from a file written by hand, so a zero or three extra digits are ordinary
typing accidents: refusing the whole configuration over one would cost a working capture, and accepting a
budget the machine does not have would cost the machine. A clamp nobody is told about is the worst of the
three, because the capture then behaves in a way its own configuration does not explain.

Stopping leaves the developer holding the least interesting part of a long session. Discarding silently is
worse than either, because a truncated record reads exactly like a complete one, which is the failure mode
this project has already met in a guard that skipped instead of failing.

Per connection matters because the volume is wildly uneven: measured on an identical editing script, one
server produced around a hundred times another's traffic. Under a single shared budget the noisy server
evicts the quiet one's history and a single count reports a loss nobody can attribute.

#### Scenario: A long editing session
- **WHEN** a connection's record reaches its limit
- **THEN** the oldest entries are discarded, recording continues, and the number discarded is visible

#### Scenario: One noisy server beside a quiet one
- **WHEN** one server produces far more traffic than another
- **THEN** the quiet server's record is not evicted to make room for it

#### Scenario: A limit written outside the range the capture can honour
- **WHEN** a configured limit is below the floor or above the ceiling
- **THEN** it is clamped, the capture runs, and the correction is reported where the developer will see it

#### Scenario: A shared ceiling written against one server
- **WHEN** an entry for a single server sets a limit that governs every connection together
- **THEN** it is not applied, and the developer is told it was not
