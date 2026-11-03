## ADDED Requirements

### Requirement: Window and layout persistence is scoped to distribution identity
Per-user window and layout state SHALL use the configuration root resolved from installation identity,
subject to an explicit CLI user-data redirect. It SHALL NOT be selected by display name or profile,
nor silently imported from stock HexIDE. Existing recovery-mode write suppression SHALL still apply.

#### Scenario: Switching between stock and an adopter installation
- **WHEN** the same user rearranges an adopter's layout and launches stock under default roots
- **THEN** stock retains its own layout and the adopter layout remains in the adopter configuration root
