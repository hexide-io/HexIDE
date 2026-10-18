# dap-client Specification

## Purpose
Communicate with an external debug adapter over streams and own explicitly launched adapter processes without
depending on interpreter execution machinery or the language-server protocol. This capability provides the
library boundary; backend selection in the IDE, session negotiation and debugger UI routing remain future work.

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

### Requirement: Explicit adapter process ownership

The client library SHALL support launching one explicitly configured adapter executable directly, preserving
individual argument boundaries, an optional working directory and environment overrides. It SHALL own the
redirected stdio connection and SHALL NOT restart the process or negotiate a debug session automatically.
Invalid configuration and launch failures SHALL be reported to the caller.

#### Scenario: Arguments contain spaces and shell characters

- **WHEN** a caller supplies individual arguments containing spaces, quotes or shell metacharacters
- **THEN** the adapter receives each argument literally without shell expansion

#### Scenario: A command cannot start

- **WHEN** the configured executable does not exist
- **THEN** startup reports the failure without returning a live adapter owner

### Requirement: Bounded adapter diagnostics

The process owner SHALL continuously drain standard error independently of protocol output. It SHALL retain
only a configurable tail and report the number of discarded characters. Its final result SHALL expose the
exit code, connection failure and cleanup failure separately.

#### Scenario: An adapter floods stderr without newlines

- **WHEN** standard error exceeds the retained diagnostic budget without a newline
- **THEN** the pipe continues to drain, the retained tail stays bounded and discarded characters are counted
- **AND** the adapter can continue writing protocol messages to standard output

#### Scenario: The adapter exits unsuccessfully

- **WHEN** the process exits with a nonzero code and diagnostic text
- **THEN** completion retains both the exit code and the diagnostic tail

### Requirement: Adapter process cleanup

Process exit, connection failure and local disposal SHALL trigger cleanup. Local disposal SHALL settle
pending requests and close the connection. After a bounded grace period, the owner SHALL attempt to terminate
the still-running adapter's process tree and wait for its exit. Each asynchronous cleanup wait SHALL be
bounded, and failures SHALL be reported. Repeated or concurrent disposal SHALL join the same cleanup.

#### Scenario: The adapter ignores closed input

- **WHEN** local disposal closes the connection but the adapter remains running
- **THEN** cleanup forcibly terminates its process tree after the grace period
- **AND** completion reports forced termination and the observed exit status

#### Scenario: A process exits immediately after replying

- **WHEN** the adapter writes a complete response and exits
- **THEN** the client allows buffered protocol input to drain before closing its read stream
- **AND** the waiting request can receive that response

#### Scenario: Invalid protocol output leaves a process alive

- **WHEN** malformed output closes the DAP connection but the adapter remains running
- **THEN** process cleanup runs without requiring another disposal request
