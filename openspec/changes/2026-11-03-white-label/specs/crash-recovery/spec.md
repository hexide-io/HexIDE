## ADDED Requirements

### Requirement: Recovery is scoped to distribution identity
Recovery storage, enumeration, live-run claims, offers and cleanup SHALL remain within the local root
resolved for the installation's stable distribution id, or the existing explicit user-data redirect.
A branded launch, including `--profile none`, MUST NOT enumerate or remove another distribution's
recovery data under default roots. Display-name or profile changes SHALL NOT move recovery state.

#### Scenario: Stock has pending recovery when a brand starts
- **WHEN** an adopter installation starts normally or in profile recovery mode with default roots
- **THEN** it neither offers nor cleans up stock HexIDE's pending recovery
- **AND** its own pending work remains reachable under its installation identity
