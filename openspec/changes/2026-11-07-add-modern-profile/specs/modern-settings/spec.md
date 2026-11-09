## Purpose
Provide a searchable Modern settings presentation sharing Options' source of truth (#3301).

## ADDED Requirements

### Requirement: Settings presentations share values
Modern SHALL expose a searchable settings page over the same setting identities, validation and
persistent store as Classic Options. Search SHALL use localized setting labels and categories.
Classic's existing Options entry point SHALL remain available with its existing presentation.

#### Scenario: Values cross profile presentations
- **WHEN** a user commits a setting in Modern and opens Classic Options against the same user-data directory
- **THEN** Options reads the committed value through the shared settings service
- **AND** changing it there is reflected when Modern next reads the setting

### Requirement: Changed settings are individually resettable
The Modern settings page SHALL mark changed values and offer a localized reset action for each
setting. Resetting one setting SHALL NOT reset unrelated settings. Both presentations SHALL preserve
profile-default and explicit-user-choice precedence.

#### Scenario: Resetting one changed value
- **WHEN** the user resets one of two changed settings
- **THEN** only the selected setting returns to its applicable default under the approved reset policy
- **AND** the other setting retains its value
