## ADDED Requirements

### Requirement: Add-ins integrate as code contributions

Add-ins SHALL be code-tier package contributions using the shared installer. Package integration
SHALL preserve the curated host API, removable registrations, lifecycle disposal, independent
enablement, failure isolation and declared private dependency resolution. Installation SHALL NOT
grant additional host capabilities or bypass existing capability declarations.

#### Scenario: Package contains an add-in

- **WHEN** a suite activates a verified and consented add-in
- **THEN** the add-in receives the existing host surface and its registrations are removed through
  the existing lifecycle contract.
