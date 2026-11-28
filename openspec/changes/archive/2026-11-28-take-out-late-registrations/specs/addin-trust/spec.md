## MODIFIED Requirements

### Requirement: A loaded add-in revoked mid-session SHALL be withdrawn
When a newer list revokes a loaded add-in, by the identity verified when it loaded, the IDE SHALL mark it Revoked, take
out what it contributed, whenever it registered it, run none of its commands, menu items or event handlers, and refuse
what it registers afterwards. Its code SHALL stay loaded until restart.

#### Scenario: A build revoked while it is loaded
- **WHEN** a newer list names a loaded add-in's build
- **THEN** its menus, commands and tool windows are taken out, and it hears no more events

#### Scenario: A publisher revocation and a first-party add-in
- **WHEN** a newer list revokes a publisher id while a first-party add-in is loaded
- **THEN** the first-party add-in stays loaded

#### Scenario: A tool window registered after the add-in started
- **WHEN** a revoked add-in had registered a tool window or Options page after its initialisation
- **THEN** that tool window or Options page is taken out too
