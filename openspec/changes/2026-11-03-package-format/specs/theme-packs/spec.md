## ADDED Requirements

### Requirement: Package themes preserve payload semantics

Theme packages SHALL use shared package discovery and installation while retaining the existing
declarative payload format, live switching, saved user precedence, deterministic Light/Dark
selection and designer theme isolation. Migration of loose packs SHALL preserve user content and
provenance without silently changing the selected theme.

#### Scenario: Installed theme selected

- **WHEN** a user selects a package-contributed theme
- **THEN** its payload applies through the existing theme semantics and the form designer retains
  its isolated theme.
