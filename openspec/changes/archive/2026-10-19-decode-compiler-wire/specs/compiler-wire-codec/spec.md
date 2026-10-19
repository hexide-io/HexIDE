## Purpose
Provide bounded message codecs for an external compiler adapter while preserving protocol-specific content and keeping framing independent of connection lifecycle. Tracks #1194.

## ADDED Requirements

### Requirement: Opaque control message framing
The codec SHALL encode requests with a one-byte operation and signed little-endian int32 nonnegative request id followed by opaque bytes. It SHALL decode replies with signed little-endian int32 request id and code fields, preserving the remaining bytes. It SHALL identify id -1 as a notification and reject other negative ids.

#### Scenario: Unknown control code and binary payload
- **WHEN** a structurally valid reply uses an unknown code and payload containing non-text bytes
- **THEN** the codec preserves the code and bytes without interpreting them
- **AND** later mutation of the input buffer cannot change the returned payload

#### Scenario: Notification
- **WHEN** a control message carries request id -1
- **THEN** the decoded message explicitly identifies itself as a notification

### Requirement: Ordered JSON message decoding
The codec SHALL decode either a single UTF-8 JSON object or a binary batch beginning with little-endian marker 0xffeaeaea followed by repeated signed int32 byte lengths and JSON objects. It SHALL preserve batch order, unknown fields and JSON values without imposing standard language/debug protocol semantics. It SHALL encode ordinary outbound JSON objects as UTF-8.

#### Scenario: Unicode batch
- **WHEN** a batch contains multiple objects with multibyte Unicode strings and nonstandard fields
- **THEN** byte lengths delimit each object correctly and all fields remain available in their original object order

#### Scenario: Invalid later object
- **WHEN** the second object in a batch is malformed
- **THEN** decoding fails without returning the first object as a partial result

### Requirement: Explicit bounds and malformed-input errors
The codec SHALL bound complete message bytes, JSON object count and JSON nesting depth. It SHALL reject invalid limits before processing and SHALL reject malformed UTF-8, invalid lengths, incomplete headers, empty batches, non-object JSON and unexplained trailing content with a controlled invalid-data error.

#### Scenario: Length exceeds remaining bytes
- **WHEN** a batch length is negative, zero or larger than its remaining payload
- **THEN** decoding fails without allocating based on that length

#### Scenario: Configured limits
- **WHEN** a message exceeds the configured byte, object-count or depth limit
- **THEN** the operation fails instead of silently truncating content

### Requirement: Codec isolation
The codec SHALL operate without starting a process, opening a socket, mutating a project, choosing an endpoint or interpreting operation payloads. Complete-message decoding SHALL NOT be presented as live compiler compatibility verification.

#### Scenario: Future unsupported wire format
- **WHEN** input matches neither supported framing format
- **THEN** decoding fails instead of guessing a new protocol
