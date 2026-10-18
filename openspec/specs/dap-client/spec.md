# dap-client Specification

## Purpose
Communicate with an external debug adapter over streams without depending on interpreter execution machinery
or the language-server protocol. This capability is a library foundation; IDE launch and debugger UI routing
remain outside this first slice.

## Requirements

### Requirement: DAP stream framing and envelopes

The client SHALL send and receive UTF-8 DAP messages with Content-Length byte-stream framing. It SHALL
reject malformed envelopes and invalid, duplicate, missing or oversized Content-Length headers, and SHALL
bound the header size. It SHALL retain unknown response and event fields.

#### Scenario: A message arrives in fragments

- **WHEN** a valid header or multibyte JSON body arrives in multiple reads
- **THEN** the client assembles exactly one complete message before interpreting it
- **AND** bytes belonging to a following message remain available to that message

#### Scenario: A new capability appears

- **WHEN** an adapter's initialize response includes an unfamiliar capability object
- **THEN** the response preserves that object for the caller

### Requirement: Independent requests and ordered events

The client SHALL assign increasing sequences to outgoing messages, serialize complete writes, and match
responses to requests using request_seq. Events SHALL be available in received order. An unsuccessful
response SHALL fail its request with the complete response available, without closing an otherwise healthy
connection. Unsupported adapter-initiated requests SHALL receive an unsuccessful DAP response.

#### Scenario: Responses arrive out of order

- **WHEN** an adapter answers a later request before an earlier request
- **THEN** each response completes only the request its request_seq identifies

#### Scenario: An adapter refuses launch

- **WHEN** a launch response reports success false and includes a structured error
- **THEN** the launch caller receives that error and the connection can carry a subsequent request

### Requirement: Bounded connection lifetime and resource use

Each request SHALL have a finite configurable deadline and accept caller cancellation. The client SHALL
bound pending requests and queued events. It SHALL NOT retry timed-out or cancelled requests automatically.
EOF, malformed input, event overflow and failed writes SHALL close the connection and fail pending work.
Disposal SHALL settle pending requests and release owned streams; repeated disposal SHALL be safe.

#### Scenario: A cancelled request receives a late reply

- **WHEN** a caller cancels its wait after a complete request was written
- **THEN** the late reply cannot complete another request
- **AND** subsequent requests can use the connection

#### Scenario: Cancellation interrupts a frame write

- **WHEN** cancellation interrupts writing a request frame
- **THEN** the connection is closed rather than reused with a potentially incomplete frame

#### Scenario: The adapter disappears

- **WHEN** the adapter closes its output with requests pending
- **THEN** every pending request completes with a connection failure
- **AND** the connection's completion reports the closure cause
