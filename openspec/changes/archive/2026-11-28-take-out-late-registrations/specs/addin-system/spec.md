## MODIFIED Requirements

### Requirement: Add-ins SHALL be independently enabled and disabled
The IDE SHALL let a user disable an add-in without removing it, SHALL persist that choice, and SHALL skip
disabled add-ins at load. Enablement SHALL be independent of whether the user has consented to the add-in. Disabling
an add-in, or revoking its consent, SHALL take out at once every menu item, command, tool window, Options page, project
template and language server it registered, whenever it registered them, and SHALL take out at once any it registers
afterwards. Its code SHALL stay loaded until restart.

These are different questions — "do I trust this?" and "do I want it running right now?" — and collapsing
them makes both worse. A user disabling an add-in to isolate a problem should not have to re-make a trust
decision to turn it back on.

#### Scenario: Disabling an add-in
- **WHEN** a user disables an add-in
- **THEN** it does not load on the next start, and the choice survives restart

#### Scenario: Re-enabling a previously consented add-in
- **WHEN** a user re-enables an add-in they had already allowed
- **THEN** it loads without asking for consent again

#### Scenario: Disabling an add-in during a session
- **WHEN** a user disables a loaded add-in that opened a tool window after it started
- **THEN** the tool window, and its menus, commands and Options page, are taken out at once
