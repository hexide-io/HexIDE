## ADDED Requirements

### Requirement: CLI presentation resolves installation identity without user state
Help labels, invocation names and startup diagnostics SHALL use validated installation identity before
reading mutable user state.

#### Scenario: Branded startup diagnostic
- **WHEN** a branded installation reports a startup diagnostic
- **THEN** it uses validated installation identity before reading mutable user state

### Requirement: CLI paths retain their invocation-directory semantics
Descriptor discovery SHALL be independent of invocation directory; relative project and explicit
user-data arguments SHALL remain relative to invocation directory.

#### Scenario: Launch from a project directory
- **WHEN** a branded executable is invoked from another directory with relative project and user-data arguments
- **THEN** descriptor discovery uses the installation and those arguments resolve against the invocation directory

### Requirement: Invalid branding preserves help and refuses normal startup
Help-only requests
SHALL remain available on an invalid installation, reporting the branding error without starting the IDE
or opening stock state. Normal startup MUST refuse invalid branding rather than silently using stock identity.

#### Scenario: Help on a broken branded installation
- **WHEN** the installation descriptor is invalid and help is requested
- **THEN** usage remains available with an attributed branding error
- **AND** no settings, log, recovery or profile state is opened or modified
