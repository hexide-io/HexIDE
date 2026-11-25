## ADDED Requirements

### Requirement: Revocation SHALL be re-checked hourly while add-ins are installed
While an add-in package is installed and a revocation source is configured, the IDE SHALL fetch the list again about
every hour, in the background, after startup's fetch. The interval SHALL NOT be configurable. The list SHALL be requested
conditionally with the validator of the last list that verified, kept in memory only.

#### Scenario: A session left open for days
- **WHEN** a newer list is published while the IDE runs
- **THEN** the IDE fetches it within about an hour, or at once after waking from sleep

#### Scenario: Checking turned off
- **WHEN** the revocation source is set empty while the IDE runs
- **THEN** the next hourly check fetches nothing

### Requirement: A loaded add-in revoked mid-session SHALL be withdrawn
When a newer list revokes a loaded add-in, by the identity verified when it loaded, the IDE SHALL mark it Revoked, take
out what it contributed, run none of its commands, menu items or event handlers, and refuse what it registers afterwards.
Its code SHALL stay loaded until restart.

#### Scenario: A build revoked while it is loaded
- **WHEN** a newer list names a loaded add-in's build
- **THEN** its menus, commands and tool windows are taken out, and it hears no more events

#### Scenario: A publisher revocation and a first-party add-in
- **WHEN** a newer list revokes a publisher id while a first-party add-in is loaded
- **THEN** the first-party add-in stays loaded
