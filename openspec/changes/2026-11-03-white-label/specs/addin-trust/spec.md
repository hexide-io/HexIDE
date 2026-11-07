## ADDED Requirements

### Requirement: Distribution branding cannot replace package trust authority
A distribution descriptor, profile or repack operation MUST NOT replace the embedded HexIDE root or
first-party anchors, distribute the root private key, or grant first-party status by adopter identity.
Outside publishers SHALL use publisher keys certified under HexIDE's root, with no adopter root.
Branding SHALL preserve compatibility checks, complete inventory verification, consent and revocation,
and MUST NOT enable unsigned execution through branding or preference defaults.

#### Scenario: An adopter supplies its own root key
- **WHEN** branding attempts to install an independent trust root or claim first-party status
- **THEN** the branding route refuses that override and the shipped anchors remain authoritative
- **AND** adopter packages still require the established verification and consent path
