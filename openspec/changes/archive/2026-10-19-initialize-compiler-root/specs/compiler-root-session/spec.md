## Purpose
Initialize an explicitly supplied compiler root endpoint with bounded I/O, validated metadata and clear socket ownership. Tracks #1199.

## ADDED Requirements

### Requirement: Explicit initialization inputs
The adapter SHALL accept explicit package entries and a nonempty compiler language-data object, validate them before connection, and encode package lengths in UTF-8 bytes. It SHALL use the observed Community Edition defaults without reading settings or paid licence keys.

#### Scenario: Unicode package entries
- **WHEN** a package entry contains multibyte characters
- **THEN** the initialization prefix records its byte length including the separator
- **AND** the language JSON starts immediately after the complete package string

#### Scenario: Empty language data
- **WHEN** the caller supplies an empty or non-object language value
- **THEN** initialization fails before opening a connection

### Requirement: Correlated root handshake
The adapter SHALL send pre-initialize command 22 with id 0 followed by initialize command 0 with id 1 only after a valid pre-initialize reply. It SHALL require success status 0, a positive PID, five valid TCP ports and a nonempty strict UTF-8 instance identity. Unexpected reply ids and malformed metadata SHALL fail initialization.

#### Scenario: Pre-initialize rejection
- **WHEN** the compiler rejects pre-initialization or returns an invalid PID
- **THEN** the adapter closes the connection without sending initialize

#### Scenario: Valid initialized connection
- **WHEN** both replies are valid
- **THEN** the returned session exposes the PID, five channel ports and opaque identity
- **AND** it does not interpret the identity as a version

### Requirement: Bounded initialization notifications and deadline
The adapter SHALL preserve interleaved id -1 notifications as opaque messages in arrival order. It SHALL enforce per-message and aggregate received-byte limits, notification count and one deadline spanning connection and both initialization exchanges. It SHALL NOT execute reverse requests during initialization.

#### Scenario: Notification flood
- **WHEN** notifications exhaust the count or aggregate byte budget before a reply arrives
- **THEN** initialization fails and the connection closes without returning a partial session

#### Scenario: Notifications without completion
- **WHEN** notifications arrive but initialization does not complete before the handshake deadline
- **THEN** the adapter reports a timeout rather than extending the deadline on each message

### Requirement: Explicit root socket ownership
The adapter SHALL close its socket on initialization failure or cancellation without replaying commands. A successful session SHALL retain socket ownership until disposal. Caller cancellation SHALL preserve the caller cancellation token. Disposal SHALL NOT terminate an externally owned compiler process.

#### Scenario: Cancel initialization
- **WHEN** the caller cancels while waiting for an initialization reply
- **THEN** the pending initialization ends with caller cancellation and the owned socket closes

### Requirement: Limited compatibility claim
The adapter SHALL NOT claim independent process launch, project loading, ongoing notification handling or language/debug services from successful initialization alone. Compiler language-data schema compatibility SHALL remain distinct from nonempty-object validation.

#### Scenario: Live initialization through native-host startup
- **WHEN** a fresh compiler launched by the isolated stock IDE completes initialization
- **THEN** evidence identifies the compiler build and the native-host dependency
- **AND** standalone startup remains unverified
