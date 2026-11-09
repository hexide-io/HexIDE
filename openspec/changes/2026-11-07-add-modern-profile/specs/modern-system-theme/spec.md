## Purpose
Follow OS light/dark in Modern through a chosen theme pair while applying concrete variants (#3301).

## ADDED Requirements

### Requirement: System following resolves to a concrete pack
Modern SHALL support following the OS light/dark preference through a chosen light and dark theme
pair. The IDE SHALL apply the matching pack at startup and when the OS preference changes, using a
concrete Light or Dark variant, never the default variant. The form design canvas SHALL remain light.

#### Scenario: OS changes appearance
- **WHEN** system following is active with a valid pair and the OS changes from light to dark
- **THEN** open IDE chrome uses the dark member and its concrete Dark variant
- **AND** the form canvas retains its pinned light appearance

### Requirement: Fixed user themes keep precedence
An explicit fixed-theme choice SHALL supersede the Modern following default. OS preference changes
SHALL NOT replace that choice. Classic's existing fixed-theme behaviour SHALL remain unchanged.

#### Scenario: Saved fixed choice
- **WHEN** Modern starts with an explicitly saved fixed theme and the OS preference changes
- **THEN** the chosen theme remains active

#### Scenario: Classic on a dark system
- **WHEN** Classic is selected with its fixed light theme on a dark OS
- **THEN** IDE chrome retains its concrete Light variant
