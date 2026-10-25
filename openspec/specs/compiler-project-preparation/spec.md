# compiler-project-preparation Specification

## Purpose
Prepare an owned staged project for an external compiler without changing the original project or persistent source projection. Tracks #1202.

## Requirements

### Requirement: Staged preparation boundary
The preparation component SHALL take exclusive ownership of an initialized root and copy a bounded project container into a unique owned temporary folder. It SHALL require successful load and readiness replies before returning a prepared result, without changing the original container or source projection.

#### Scenario: Project becomes ready
- **WHEN** loading and readiness succeed
- **THEN** the result owns the staged copy and root and carries both opaque replies
- **AND** the original bytes remain unchanged

#### Scenario: Root already claimed
- **WHEN** another preparation attempts to claim the root
- **THEN** it fails without disposing the existing owner's connection

### Requirement: Explicit reverse-request handling
The component SHALL preserve settings, obtain raw package bytes only from an explicit provider, correlate all reverse-response acknowledgements, and retain unknown notifications without executing actions. Peer-supplied identifiers SHALL NOT implicitly resolve to filesystem paths or commands.

#### Scenario: Load reply precedes reverse acknowledgements
- **WHEN** the load reply arrives while settings or package acknowledgements remain outstanding
- **THEN** readiness is not requested until all outstanding acknowledgements succeed

#### Scenario: Package unavailable or reply unexpected
- **WHEN** a package cannot be resolved or a reply ID is unknown or duplicated
- **THEN** preparation fails and closes the owned root

### Requirement: Bounded terminal failure
Preparation SHALL enforce configured project, message, aggregate data, package, notification and elapsed-time limits. Rejection SHALL preserve its status code without replay, restart or settings changes. Cancellation SHALL close the owned root and attempt staging cleanup.

#### Scenario: Configuration mismatch
- **WHEN** the compiler rejects the project for DEP or LAA configuration
- **THEN** the mismatch is reported without changing configuration or restarting the compiler

#### Scenario: Provider completes after cancellation
- **WHEN** an asynchronous provider returns a stream after preparation was cancelled
- **THEN** that stream is disposed without resuming preparation

### Requirement: External process and staging lifetime
The prepared result SHALL own staging cleanup but SHALL NOT terminate the externally owned compiler process. Callers SHALL stop attached channels and release the compiler's stage ownership before disposing the result. Cleanup failure SHALL expose the retained directory and permit retry.

#### Scenario: Compiler retains a staging handle
- **WHEN** the operating system refuses removal because the compiler still owns a handle
- **THEN** cleanup reports the retained stage
- **AND** cleanup can succeed after the external owner releases that handle
