## ADDED Requirements

### Requirement: A mid-session revocation SHALL be announced
When a newer list revokes add-ins whose code is loaded, the IDE SHALL show a notice naming them, with a button that closes
the IDE through its normal close and its prompts to save. One notice SHALL show at a time, and add-ins revoked while it is
open SHALL be named in the next. An add-in whose code never loaded SHALL be revoked without a notice.

#### Scenario: A loaded add-in is revoked
- **WHEN** the hourly check revokes a loaded add-in
- **THEN** a notice names it, and its close button asks to save before HexIDE closes

#### Scenario: A second revocation while the notice is open
- **WHEN** another add-in is revoked while the notice is open
- **THEN** a second notice names it once the first is answered

### Requirement: Options SHALL show the revocation check's state
While an add-in package is installed, Options → Add-Ins SHALL say when the revocation list was last checked and which list
is in use, that the last check failed and when one last worked, or that checking is off. It SHALL NOT say a list was
checked when no check has worked. A revoked add-in's page SHALL offer no Activate or Deactivate button.

#### Scenario: Checking that has stopped working
- **WHEN** the last check failed
- **THEN** Options says so, with the time of the last check that worked

#### Scenario: A page open during a revocation
- **WHEN** an add-in is revoked while its Options page is open
- **THEN** the page shows it as Revoked, with no Activate or Deactivate button
