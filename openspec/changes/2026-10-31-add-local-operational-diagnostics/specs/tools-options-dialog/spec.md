## ADDED Requirements

### Requirement: Diagnostics settings control local recording
Options SHALL have a localized, accessible Diagnostics category with Off and Local. It SHALL explain
bounded session retention, manual export and separate log/protocol-capture controls. It SHALL explain
that collector delivery needs an explicitly installed add-in, without acquiring or configuring one.
The core category SHALL contain no endpoint, authentication or network-export control.

#### Scenario: A standard installation opens Diagnostics settings
- **WHEN** the Diagnostics category is opened without any exporter add-in installed
- **THEN** Off and Local are available and no collector configuration fields are present
- **AND** the explanation distinguishes local recording from optional add-in delivery

#### Scenario: An exporter is installed
- **WHEN** an exporter add-in is installed and loaded
- **THEN** the core Diagnostics category still controls only local recording
- **AND** collector configuration is reached through the add-in's own command

### Requirement: Recording settings participate in the Options transaction
The chosen recording mode SHALL take effect only when the whole Options dialog is confirmed. Cancel
SHALL leave the active mode unchanged. Restoring this category's defaults SHALL stage Local and SHALL
NOT install an exporter or create an export grant. Applying Off SHALL revoke operational grants
and discard retained observations; a later Local choice SHALL NOT reinstate those grants.

#### Scenario: Off is selected and cancelled
- **WHEN** the developer selects Off and cancels Options
- **THEN** prior recording and existing export subscriptions remain unchanged

#### Scenario: Off is confirmed
- **WHEN** the developer confirms Off
- **THEN** local operational recording stops, retained observations are cleared and operational grants are revoked
- **AND** existing local application logging and protocol-capture settings are unchanged

#### Scenario: Defaults are restored after export was stopped
- **WHEN** Local is restored and Options is confirmed
- **THEN** local recording is enabled without creating or restoring an exporter grant

### Requirement: Stopping export is independent of Options
The core diagnostics view SHALL expose active operational subscriptions and a direct Stop Export action.
Stopping an exporter SHALL revoke its grant immediately without editing the staged Options transaction.
Confirming unrelated Options changes SHALL NOT restore a grant that was revoked while the dialog was open.

#### Scenario: Export is stopped while Options is open
- **WHEN** the developer stops an exporter from the diagnostics view with Options already open
- **THEN** the grant is revoked immediately
- **AND** later confirming Options does not restart delivery
