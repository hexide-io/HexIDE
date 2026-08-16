# theme-packs Specification

## Purpose
Define how the IDE's colours are themed: declared as data rather than compiled in, swappable while the IDE
runs, and extensible without rebuilding.

Dark mode is the obvious motivation and the least interesting part of it. The reason to build a pack system
rather than a second colour file is that a theme is data, and data does not need a compiler — which means a
user can have a theme the project never shipped, and adding one is a contribution anybody can make.

This is one of three independent pack systems that follow the same shape and compose freely: this one swaps
colours, `keymap-packs` swaps keyboard gestures, and `language-packs` swaps text.

## Requirements
### Requirement: A theme SHALL be declared as data, not compiled in
A theme SHALL be a data file declaring colour values by key, and adding one SHALL require no code change and
no rebuild.

A theme expressed as compiled markup can only come from whoever builds the product, which rules out the
people most likely to want one. Keeping the compiled layer to the *definition* of the keys and their default
values, and expressing every theme as overrides on top, means the extensible part has no build step at all.

#### Scenario: Adding a theme
- **WHEN** a new theme is contributed
- **THEN** it is a data file listing colour overrides, with no code change

#### Scenario: A theme that overrides some colours
- **WHEN** a theme declares only some of the colour keys
- **THEN** the remainder keep their default values

### Requirement: Changing theme SHALL take effect immediately
Selecting a theme SHALL restyle the IDE without a restart, including windows already open.

Choosing a theme is an aesthetic judgement made by looking at it, so a restart between choosing and seeing
turns a two-second decision into a chore and effectively stops people trying the alternatives. Applying live
also means a theme in development can be iterated on without relaunching.

#### Scenario: Switching theme
- **WHEN** the user selects a different theme
- **THEN** the IDE restyles immediately, including windows already open

### Requirement: A theme SHALL declare whether it is light or dark
A theme SHALL state which variant it is, and the IDE SHALL adopt that variant rather than following the
operating system.

The controls the IDE is built from pick their own defaults from the variant, so it has to be set
deliberately and it has to agree with the theme's own colours. Following the operating system instead
produces the specific failure this rule prevents: an explicitly light theme rendering with dark control
defaults on a machine set to dark, giving unreadable chrome that no single colour key can fix.

#### Scenario: Applying a light theme on a dark system
- **WHEN** a theme declaring itself light is applied on a system set to dark
- **THEN** the IDE renders light throughout, ignoring the system setting

### Requirement: The form design surface SHALL NOT follow the IDE theme
The design canvas SHALL remain light regardless of the active theme.

The canvas shows the form as it will look when it runs, and a VB6 form's colours are its own — they do not
change because the developer prefers a dark IDE. Theming the canvas would mean the developer designs against
one appearance and ships another, which makes the designer misleading in exactly the way a designer must
not be.

#### Scenario: Designing a form under a dark theme
- **WHEN** a dark theme is active and a form is open on the design surface
- **THEN** the canvas and the controls on it render with the form's own colours

### Requirement: Chrome colours SHALL come from the theme, never from literals
Interface colour SHALL be taken from a named theme key, and SHALL NOT be written as a literal value at the
point of use.

A literal colour is one a theme cannot reach. It survives every theme switch unchanged, so it looks correct
in whichever theme it was written against and wrong in all the others — and it fails quietly, appearing as
one panel that did not change rather than as an error.

#### Scenario: Adding interface chrome
- **WHEN** new chrome is added
- **THEN** its colours reference theme keys

#### Scenario: A colour that has no key
- **WHEN** chrome needs a colour the theme does not define
- **THEN** a key is added to the definition layer and every theme inherits a default for it
