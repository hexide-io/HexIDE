# compiler-websocket-transport Specification

## Purpose
Provide bounded, owned WebSocket I/O for an external compiler adapter, independently of compiler process startup and protocol semantics. Tracks #1196.

## Requirements

### Requirement: Explicit loopback connection
The transport SHALL connect only to an explicit plain WebSocket endpoint with a loopback IP literal and no URI credentials, query or fragment. It SHALL disable proxies and redirects, bound the upgrade deadline, and exclude authenticated paths from connection exceptions.

#### Scenario: Redirected upgrade
- **WHEN** the endpoint returns an HTTP redirect
- **THEN** the connection fails without contacting the redirect destination

#### Scenario: Authenticated endpoint failure
- **WHEN** upgrading a path containing a passkey fails
- **THEN** the reported exception excludes that path and any inner exception that could expose it

### Requirement: Bounded complete messages
The transport SHALL preserve each message's bytes and text/binary type while bounding reassembled bytes, receive operations and elapsed active-operation time. It SHALL reject invalid limits before connecting. It SHALL discard partial messages on peer closure or limit failure.

#### Scenario: Fragmented exact-limit message
- **WHEN** multiple fragments total exactly the byte limit
- **THEN** one complete message is returned with unchanged bytes and type
- **AND** the next message is received separately

#### Scenario: Empty fragment sequence
- **WHEN** receives continue without completing a message and exhaust the operation budget
- **THEN** the transport fails and aborts the connection even if the byte limit was not exceeded

### Requirement: Serialized and terminal lifecycle
The transport SHALL serialize sends and receives independently, permit one of each concurrently, and bound outbound message size. Cancellation while queued SHALL NOT abort active I/O. Interrupted active I/O SHALL abort the connection without reconnecting or replaying requests. Disposal SHALL settle active and queued calls without a semaphore disposal race.

#### Scenario: Cancel queued receive
- **WHEN** a second receive is cancelled while waiting behind an active receive
- **THEN** only the queued call is cancelled and the active call can still receive its message

#### Scenario: Dispose during receive
- **WHEN** the owner disposes the transport while one receive is active and another is queued
- **THEN** the active call ends with a connection failure and the queued call observes disposal
- **AND** repeated disposal is harmless

### Requirement: Separate transport and session claims
The transport SHALL NOT launch a compiler, initialize a session, interpret control payloads or implement language/debug services. A successful socket upgrade SHALL NOT be presented as full compiler-session compatibility.

#### Scenario: Live socket smoke test
- **WHEN** a live compiler endpoint accepts a transport connection without session initialization
- **THEN** evidence records connection success and leaves initialization compatibility unverified

### Requirement: Offline exchange recordings

The adapter SHALL support opt-in recording at the complete-message transport boundary, without an IDE
shell dependency. A shared recorder SHALL write a format-version-1 JSON-lines header naming the beta,
edition and scenario, then sequenced channel, direction, frame type and payload records. Text SHALL be
stored as text and binary as base64. Explicit run values SHALL be replaced with named placeholders at
record time and restored with the replay run's values. Recording SHALL reject user-home paths and
licence-shaped values before writing a frame.

#### Scenario: Synthetic preparation round trip
- **WHEN** the scripted root handshake and load/settings/package/readiness exchange is recorded
- **THEN** its checked-in synthetic fixture replays through loopback transports without a compiler
- **AND** the adapter's requests match and it accepts the fixture's replies

#### Scenario: A request changes
- **WHEN** a replayed adapter frame differs from its fixture after substitution
- **THEN** replay fails at that sequence and reports expected and actual texts
- **AND** text frames compare as structural JSON and binary frames compare byte for byte

#### Scenario: A run value was not supplied
- **WHEN** a replay frame still contains a named run placeholder after substitution
- **THEN** replay refuses it before sending or waiting for that frame
