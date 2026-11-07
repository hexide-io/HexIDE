## ADDED Requirements

### Requirement: Package translations preserve locale semantics

Language packages SHALL use shared package discovery and installation while retaining existing
declarative payloads, locale and region fallback, placeholder validation, script mnemonic rules,
user overrides and shipped-pack coverage requirements. Migration of loose overrides SHALL preserve
user content and SHALL NOT silently expand the selectable locale inventory.

#### Scenario: Package translation omits a string

- **WHEN** a selected locale has no package translation for a key
- **THEN** the existing locale fallback supplies the string and valid user overrides retain
  precedence.
