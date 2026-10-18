## ADDED Requirements

### Requirement: Asynchronous launch configuration
The client SHALL provide a single-launch session that initializes an external adapter, waits for initialized,
configures source breakpoints and advertised default exception filters, and sends configurationDone only
when supported. It SHALL permit launch responses to follow configuration and release ownership on startup
failure or cancellation within a bounded startup deadline.

#### Scenario: Deferred launch response
- **WHEN** the adapter sends initialized but defers its launch response until configurationDone
- **THEN** the client configures breakpoints and completes the handshake without waiting for launch first
- **AND** breakpoint verification responses remain available to the caller

### Requirement: Stop-scoped asynchronous inspection
The session SHALL preserve remote thread/frame IDs, source information and variable references and SHALL
reject inspection results belonging to a different session or expired stop. Consumers SHALL be able to
check a stop identity again when applying a result after UI dispatch.

#### Scenario: Delayed locals response
- **WHEN** a variable response arrives after resume and a new stop, even with reused remote IDs
- **THEN** the old inspection fails instead of being offered as current locals

### Requirement: Race-safe execution state
The session SHALL invalidate stopped inspections before sending continue or a step command and SHALL NOT
overwrite a newer stopped event when that request completes. A definitive refusal SHALL establish a fresh
stop; an uncertain resume cancellation or timeout SHALL fault and release the session.

#### Scenario: Immediate second breakpoint
- **WHEN** a stopped event arrives before a successful continue response
- **THEN** the session remains at that new stop after the response

### Requirement: Observable session lifetime
The session SHALL expose bounded ordered updates and raw events, including output, and SHALL fault and close
on update overflow. Program termination SHALL remain distinct from adapter process completion. Disconnect
SHALL request debuggee termination and release ownership even on failure, with process diagnostics retained.

#### Scenario: Adapter remains alive after program termination
- **WHEN** the program terminates but the adapter still awaits disconnect
- **THEN** the session reports the program ended and can explicitly disconnect and await owned cleanup
