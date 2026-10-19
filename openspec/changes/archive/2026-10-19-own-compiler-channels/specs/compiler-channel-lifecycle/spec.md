## Purpose
Provide internal ownership and bounded I/O for the four sockets of an already prepared compiler project. Tracks #1201.

## ADDED Requirements

### Requirement: Prepared-project boundary
The lifecycle component SHALL remain internal and SHALL accept ownership only under the caller contract that project loading and readiness acknowledgement have completed. It SHALL NOT expose a public bare-root initialization-and-attachment workflow or claim to implement project preparation.

#### Scenario: Live startup ordering
- **WHEN** the compiler has acknowledged project loading and all-projects-loaded readiness
- **THEN** the internal component can attach the advertised filesystem, language and debugger sockets
- **AND** live evidence distinguishes this sequence from unsafe early attachment

### Requirement: Atomic attachment ownership
The component SHALL retain root and all three acquired service sockets together. It SHALL derive service endpoints using the authenticated root path and advertised ports, enforce one attachment deadline, and close all acquired sockets on failure/cancellation without reconnecting.

#### Scenario: Third service connection fails
- **WHEN** two service sockets connect and the third fails
- **THEN** the component closes the root and both acquired service sockets

### Requirement: Bounded opaque message flow
The component SHALL preserve per-channel message order, channel identity, type and bytes without interpreting protocol semantics. It SHALL bound message size, queue count and queued bytes, releasing byte budget when messages are consumed. Overflow SHALL terminate the whole session without dropping individual messages silently.

#### Scenario: Queue byte budget exhausted
- **WHEN** an incoming message would exceed aggregate queued bytes
- **THEN** the session fails and closes all four sockets

#### Scenario: Consumer cancellation
- **WHEN** a caller cancels waiting for the next queued message
- **THEN** that consumer stops waiting while channel receive pumps remain active

### Requirement: Idle-aware assembly deadlines
Internal session readers SHALL permit idle waiting and start the message-assembly deadline after their first completed receive operation. Existing public transport receive deadlines SHALL retain their prior behavior. The component SHALL apply active message deadlines independently of attachment timeouts and SHALL document the first-frame-header limitation.

#### Scenario: Idle then incomplete message
- **WHEN** a socket stays quiet longer than the message timeout and then supplies a non-final fragment
- **THEN** idle waiting does not fail
- **AND** failure to complete the message within the assembly deadline terminates the session

### Requirement: Unified terminal lifecycle
Any channel failure, attachment failure or active send failure SHALL close all owned sockets without replay. Explicit disposal SHALL be idempotent, async disposal SHALL join readers and terminal cleanup, and pending consumers SHALL settle. Terminal errors SHALL exclude peer text and authenticated endpoint paths. The component SHALL NOT terminate an externally owned process.

#### Scenario: Peer closes one channel
- **WHEN** any one peer closes its socket
- **THEN** all sockets close and pending consumers observe a terminal failure

#### Scenario: Explicit async disposal
- **WHEN** the owner asynchronously disposes a session with pending readers
- **THEN** all readers settle, queued messages are discarded and termination identifies explicit disposal
