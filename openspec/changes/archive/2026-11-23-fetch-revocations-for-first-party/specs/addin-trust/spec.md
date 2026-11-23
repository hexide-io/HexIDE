## MODIFIED Requirements

### Requirement: Startup revocation retrieval SHALL depend on installed packages
The default source SHALL be `https://hexide.io/revocations.json`, with its signature at the URL plus
`.sig`. A user URL SHALL override it; null SHALL select the default and an empty URL SHALL disable retrieval. Startup SHALL fetch
whenever an enabled discovered package has a manifest, first-party packages included, before packages
load or await consent, because a build revocation applies to first-party packages too. Retrieval SHALL retain the three-second
timeout, root verification and strictly newer `issuedUtc` check.

#### Scenario: Only first-party components installed
- **WHEN** every enabled discovered package is verified against the pinned first-party key and a nonempty source is configured
- **THEN** startup attempts retrieval before those packages load, so a revoked first-party build is refused

#### Scenario: Third-party package awaiting consent
- **WHEN** an enabled third-party package is discovered and a nonempty source is configured
- **THEN** startup attempts retrieval before deciding whether that package may load

#### Scenario: No add-in packages
- **WHEN** no discovered package has a manifest
- **THEN** startup makes no revocation fetch
