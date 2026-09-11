## ADDED Requirements

### Requirement: The server SHALL advertise exactly the capabilities it implements
The bundled server's `initialize` result SHALL declare every feature it has a live handler for, and SHALL
NOT declare any feature it does not. Both directions are the same defect: understating leaves a
capability-respecting client with nothing to call, and overstating invites requests that nothing answers.

This is the companion half of the client's capability gate, and it SHALL land before that gate does. A gate
applied to a server that advertises nothing blacks out every feature, so the ordering is a correctness
requirement rather than a preference.

Full document synchronization SHALL be advertised, and a ranged content change SHALL be refused rather than
mis-applied. Refusal SHALL evict the affected document, because leaving a stale buffer in place is what
turns the refusal into a destructive write: whole-document formatting computed from a fragment returns an
edit spanning the real file.

#### Scenario: A capability-respecting client connects
- **WHEN** a client reads the server's `initialize` result
- **THEN** every feature the server implements is named in it, in a shape the protocol permits
- **AND** no feature the server does not implement is named at all

#### Scenario: A feature is implemented without being advertised
- **WHEN** a handler is registered for a request the capabilities do not declare
- **THEN** the capability assertions fail the build
- **AND** the omission is corrected rather than the assertion relaxed

#### Scenario: A client sends a ranged content change anyway
- **WHEN** a `textDocument/didChange` notification carries a change with a range
- **THEN** the change is refused and the document is evicted from the server's store
- **AND** the refusal is reported rather than left indistinguishable from a document with nothing wrong

#### Scenario: The shipped binary is stale
- **WHEN** the packaged executable is driven over its real transport
- **THEN** its advertised capabilities are asserted against the same set
- **AND** a binary predating the advertisement fails rather than answering with an empty capability object
