## MODIFIED Requirements

### Requirement: Bundle loading produces an immutable snapshot
The profile loader SHALL validate a version-1 `profile.json` and read all referenced JSON into an
immutable snapshot before returning it. It SHALL preserve omitted surfaces and distinguish omitted
project types from an explicitly empty array. It SHALL accept only the approved manifest members,
project type ids, currently published visibility ids and settings-default allowlist. Existing pack
wire formats SHALL be reused; pack semantic warning rules remain with their adapters.

Profiles SHALL remain restricted data: remote URLs and server configuration SHALL NOT be supplied
except through exact-version suite dependencies carrying embedded manifests authenticated by the
suite publisher. This exception SHALL NOT permit arbitrary profile-supplied commands or remote
payload references; all embedded manifest reads SHALL obey bundle containment. A profile author
signature SHALL NOT authenticate a suite publisher or authorize a process launch.

#### Scenario: A later payload fails
- **WHEN** a bundle has valid visibility entries but a missing or malformed referenced payload
- **THEN** loading fails with source and file attribution, without applying any surface or changing an
  earlier snapshot
- **AND** the error can be formatted through the localization service with a bundle-checking remedy

## ADDED Requirements

### Requirement: Profiles carry exact-version suite dependencies

A profile SHALL name required suites at exact versions and carry each suite manifest inside its
bundle, without requiring an index on first start. The host MUST authenticate each manifest with the
suite publisher's own signature and verify its inventory. Unsigned copies or copies signed only by
the profile author SHALL be refused, even with unsigned-server opt-in.

#### Scenario: Profile author signs a suite manifest

- **WHEN** only the profile author has signed an embedded outside suite manifest
- **THEN** the manifest is refused and cannot authorize a launch.

### Requirement: Missing suites require a consented first-start offer

The first start SHALL offer installation of missing exact suite versions through shared consent
without downloading on selection alone or silently substituting another version. Profile selectors,
containment, user precedence and recovery SHALL remain intact. Recovery selection SHALL bypass suite
acquisition and activation from profiles.

#### Scenario: Missing exact suite

- **WHEN** a profile requires a suite version that is not installed
- **THEN** the host offers that exact version; refusal or offline unavailability does not download or substitute another version.
