## ADDED Requirements

### Requirement: Session-only external breakpoint editing
The IDE SHALL initialize external source breakpoints from launch JSON and SHALL route gutter/F9 and Clear All
to the active external session once launch configuration is complete. Changes SHALL NOT modify the launch file
or saved interpreter marks. Per-source replacements SHALL include the complete desired set, including an empty
set for removal, and SHALL be allowed while running or stopped. Late replies SHALL NOT replace newer intent
or affect another session.

#### Scenario: Rapid edits to one source
- **WHEN** the user changes a source's breakpoints while its replacement request is pending
- **THEN** the newest complete set is sent after that request and only its reply supplies final verification

#### Scenario: Verification and relocation
- **WHEN** an adapter verifies, relocates or rejects a requested breakpoint
- **THEN** the gutter distinguishes that state at the requested line and exposes the requested line, adapter line,
  verification and adapter message on hover

#### Scenario: Clear refused
- **WHEN** the adapter refuses a removal or its outcome is uncertain
- **THEN** the affected markers remain visible as unverified and the failure is reported
- **AND** the IDE does not claim the backend has cleared them

#### Scenario: Adapter changes verification
- **WHEN** a breakpoint event names the ID of an owned non-pending breakpoint
- **THEN** its verification and resolved line update without changing the requested line

#### Scenario: Another launch
- **WHEN** an external session ends and another is launched
- **THEN** session edits are discarded and the launch file seeds the new set
