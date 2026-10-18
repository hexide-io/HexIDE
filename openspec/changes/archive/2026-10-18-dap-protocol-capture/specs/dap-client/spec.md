## ADDED Requirements

### Requirement: Optional shared protocol capture

The DAP library SHALL accept optional per-session capture into the shared conversation log. It SHALL record
requests, successful and unsuccessful responses, events, reverse requests, connection lifecycle and owned
process diagnostics. Capture SHALL NOT wait for a consumer or propagate diagnostic failures into protocol
operation. Existing capture retention limits, arming and drop accounting SHALL apply.

#### Scenario: Request ids overlap across directions

- **WHEN** the client and adapter both send a request with the same sequence number
- **THEN** each response is paired with the request from the opposite direction using request_seq
- **AND** unsuccessful responses mark only the corresponding exchange as failed

#### Scenario: Capture cannot keep up

- **WHEN** the capture queue is full
- **THEN** protocol traffic continues without waiting for the capture consumer
- **AND** the log reports lost capture entries

### Requirement: Honest captured DAP evidence

Retained DAP bodies SHALL preserve the original JSON bytes independently of parsed fields. Outbound records
SHALL identify write attempts rather than claim delivery. Failed writes SHALL produce connection diagnostics.
Local cancellation and closure SHALL end unanswered captured requests, subject to reported capture loss.
Late responses SHALL NOT replace a locally completed request's outcome.

#### Scenario: An adapter writes invalid JSON

- **WHEN** a fully framed body is not valid JSON
- **THEN** capture retains the invalid body according to its retention policy
- **AND** connection closure records the protocol failure

#### Scenario: A caller cancels before the reply

- **WHEN** a sent request is locally cancelled and the adapter later replies
- **THEN** the capture retains the cancelled request outcome and records the late reply separately

### Requirement: DAP path pseudonymisation at export

Pseudonymised exports SHALL rewrite complete JSON path string fields using the existing local-path policy.
Local inspection SHALL retain the original body, and opting out of pseudonymisation SHALL retain the path
in exported bodies. Document URI paths SHALL use the URI policy without a second transformation.

#### Scenario: A source has a filesystem path

- **WHEN** a captured DAP message contains a plain source path
- **THEN** the pseudonymised export replaces its path segments
- **AND** the original raw message remains available for local inspection
