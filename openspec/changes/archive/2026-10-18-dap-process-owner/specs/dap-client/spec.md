## ADDED Requirements

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
