## ADDED Requirements

### Requirement: Package contributions share publisher trust

Executable contributions MUST use existing offline signature verification. HexIDE's root SHALL sign
outside publishers' public-key certificates; publishers SHALL sign manifests binding inventory and
acquisition metadata with their own keys. Distribution roots SHALL NOT be accepted. Outside
certificates SHALL NOT establish first-party status.

#### Scenario: Outside publisher claims first-party status

- **WHEN** an outside publisher signs a package claiming a built-in identity
- **THEN** the claim does not confer first-party trust or bypass key pinning.

### Requirement: Package publishers remain revocable

The shared installer SHALL consult the existing signed revocation list before consent and refuse
known revoked publishers, builds and intermediate signers. Retrieval failure alone SHALL NOT
establish revocation.

#### Scenario: Known revoked suite

- **WHEN** the revocation list identifies the suite publisher as revoked
- **THEN** installation is refused before consent or execution.

### Requirement: Shared consent preserves exact-build guarantees

Shared install/update disclosure SHALL list everything that runs and retain existing add-in
exact-build consent and re-verification immediately before loading. Changed command lines SHALL
require renewed consent; unchanged command lines SHALL NOT exempt changed add-in builds. Publisher
link presentation SHALL follow the packages domain-proof contract.

#### Scenario: Payload changes after consent

- **WHEN** an inventoried executable changes between consent and launch
- **THEN** re-verification prevents execution of the changed bytes.

### Requirement: Unsigned server release opt-in is separate from developer mode

Release builds SHALL allow unsigned language servers and debug adapters only behind a separate
persisted setting that is off by default. Enabled sessions MUST be flagged loudly with persistent
session markers and a per-start notice, as tracked by #2945. This exception SHALL NOT waive
compatibility, authenticate embedded suite manifests, or extend the existing unsigned-add-in
exception beyond its Debug-only developer opt-in.

#### Scenario: Ordinary release session

- **WHEN** an unsigned server is configured while the release setting remains off
- **THEN** the server is refused rather than silently enabling the exception.
