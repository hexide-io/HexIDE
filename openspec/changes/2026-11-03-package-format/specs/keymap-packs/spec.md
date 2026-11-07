## ADDED Requirements

### Requirement: Package keymaps preserve binding semantics

Keymap packages SHALL use shared package discovery and installation while retaining the existing
declarative payload format, command validation, conflict handling, immediate application and
pack-free Default keymap. Migration of loose packs SHALL preserve user bindings and explicit
choices.

#### Scenario: Package keymap applied

- **WHEN** a package keymap is selected
- **THEN** existing command and gesture validation applies and switching to Default restores
  baseline bindings.
