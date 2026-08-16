# tools-options-dialog Specification

## Purpose
Define the Options dialog: the single place IDE settings and add-in management live, and the seam that lets
an add-in put its own settings there.

VB6's Options was a fixed tabbed window sized for the handful of settings it had. HexIDE has more than that
already and will have more again, and tabs stop scaling at around a dozen — they wrap, or shrink, or hide.
This is the one surface where fidelity was deliberately traded for something that grows.

## Requirements
### Requirement: Settings SHALL be organised as a navigable category tree beside a content pane
The dialog SHALL present setting categories in a hierarchical tree, with the selected category's settings
shown beside it.

A tree scales where tabs do not: adding a page means adding a node, and related pages nest instead of
competing for one row of headers. It is also the arrangement developers already know from the tools they use
alongside this one, so the layout needs no explanation.

#### Scenario: Navigating to a category
- **WHEN** the developer selects a category in the tree
- **THEN** that category's settings are shown in the content pane

#### Scenario: Adding a category
- **WHEN** a new settings category is introduced
- **THEN** it appears as a node in the tree without displacing the existing ones

### Requirement: Changes SHALL be committed or discarded as a whole
The dialog SHALL apply its changes when confirmed and abandon them when cancelled, with no separate step
that applies changes while it remains open.

Confirm-or-discard is what makes the dialog safe to explore: the developer can change several things across
several pages knowing that one cancel undoes all of it. An apply-without-closing step breaks that guarantee
in the one case it matters — the developer who has already applied something and then cancels, expecting to
be back where they started.

#### Scenario: Confirming
- **WHEN** the developer changes settings across categories and confirms
- **THEN** all the changes take effect together

#### Scenario: Cancelling
- **WHEN** the developer changes settings and cancels
- **THEN** none of them take effect, including any that were previewed live

### Requirement: Settings that can be seen SHALL be previewed while choosing
Where a setting changes the IDE's appearance, the dialog SHALL apply it as the developer selects it, and
SHALL restore the previous value if the dialog is cancelled.

Choosing a theme or a keymap from a name is guesswork; choosing it by seeing it is not. The requirement that
makes the preview safe rather than a trap is the second half — a previewed change is still a change the
developer has not confirmed, so cancelling has to put it back.

#### Scenario: Previewing a theme
- **WHEN** the developer selects a different theme in the dialog
- **THEN** the IDE takes on that theme immediately

#### Scenario: Cancelling after previewing
- **WHEN** the developer previews a theme and then cancels
- **THEN** the IDE returns to the theme it had before the dialog opened

### Requirement: The dialog SHALL be where add-ins are managed
The dialog SHALL list the installed add-ins with their status, and SHALL allow enabling and disabling each
one.

Add-in management was previously an add-in of its own, which is a pleasing demonstration and a poor place to
put it — a developer whose add-ins are misbehaving should not have to rely on one of them loading to reach
the controls. Settings is where people look for it, and it is reachable regardless of what has gone wrong.

#### Scenario: Reviewing installed add-ins
- **WHEN** the developer opens the add-ins category
- **THEN** each installed add-in is listed with its status

#### Scenario: Disabling an add-in
- **WHEN** the developer disables an add-in
- **THEN** the choice is recorded and takes effect on the next start

### Requirement: An add-in SHALL be able to contribute its own settings page
The dialog SHALL accept settings pages contributed by add-ins, presenting them alongside the IDE's own.

An add-in with anything to configure otherwise has to build its own window and its own way of being opened,
which means every add-in invents a different one and none of them is where the developer looks. Letting them
contribute a page is what makes a third-party add-in feel like part of the IDE rather than a guest in it.

#### Scenario: An add-in with settings
- **WHEN** an installed add-in contributes a settings page
- **THEN** it appears in the tree alongside the IDE's own categories, and behaves like them
