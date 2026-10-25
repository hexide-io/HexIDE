## Purpose

Define a data-only base-experience bundle selected by name, directory or none. Profiles subsume the
retired personality concept without changing project compatibility or interpreter semantics.

## ADDED Requirements

### Requirement: A profile is a versioned directory bundle
A profile SHALL be a directory with `profile.json` declaring a schema version and id. It MAY reference
existing keymap, theme and language-override JSON formats and settings defaults, and MAY declare ordered
project type ids and menu/toolbar visibility by host-owned ids. Omitted surfaces SHALL retain host
baseline behaviour. An empty project-type list SHALL offer no project types. A profile MUST NOT supply
code, markup, new localization keys or executable callbacks.

#### Scenario: A third party ships a partial bundle
- **WHEN** a selected valid bundle supplies only a keymap
- **THEN** the IDE applies that keymap through the existing format and retains other baseline surfaces
  subject to explicit user settings

#### Scenario: An empty project-type list
- **WHEN** a selected bundle declares an empty project-type list
- **THEN** its available project types are empty rather than inherited

### Requirement: Selection uses one session flag
The IDE SHALL accept `--profile <value>` and `/profile <value>` in distributed and Debug builds.
`none` SHALL select safe mode; an explicit path SHALL select that directory; a name SHALL select a
built-in or installed bundle. Relative paths SHALL resolve against the invocation directory. Omitting the flag
SHALL select embedded `vb6`. Selection SHALL NOT be persisted. Missing values and repeated selectors
SHALL refuse startup with an actionable localized diagnostic.

#### Scenario: Unambiguous relative directory
- **WHEN** a launch selects `--profile ./example`
- **THEN** the IDE loads the bundle from that directory relative to the invocation directory

#### Scenario: Default launch
- **WHEN** no profile flag is supplied
- **THEN** the IDE selects embedded `vb6`

### Requirement: Named resolution has deterministic precedence
The IDE SHALL reserve `vb6`, `vbaode` and `vba` for its indexed embedded bundles. Other names SHALL
resolve only under the active user-data directory's `profiles/<id>` directory, with lower-case ordinal
ids matching directory names. Explicit paths SHALL select exactly that directory regardless of id.
`--user-data-dir` SHALL affect installed-bundle discovery regardless of argument order.

#### Scenario: An installed directory uses a built-in name
- **WHEN** an installed directory is named `vb6` and the launch selects `--profile vb6`
- **THEN** the embedded bundle wins

#### Scenario: Isolated installed profiles
- **WHEN** a launch specifies both a user-data directory and an installed profile name
- **THEN** discovery uses that directory even if its flag follows the profile flag

### Requirement: Bundle validation precedes application
The IDE MUST validate a selected bundle before applying any of it. Unknown schema versions, invalid
manifest members, unsupported surface/template ids, missing or malformed payloads and file references
escaping the bundle root SHALL refuse startup with an attributed localized diagnostic. Explicit
selection failure SHALL NOT silently fall back. Existing payload-level validation and warning rules
SHALL remain in force. Bundle files SHALL remain read-only.

#### Scenario: A reference escapes through a symlink
- **WHEN** a referenced payload resolves outside the bundle directory
- **THEN** startup is refused before any profile surfaces are applied

#### Scenario: An installed profile is broken
- **WHEN** an explicitly named installed profile has an invalid manifest
- **THEN** the diagnostic identifies its source and the IDE does not start with a different profile

### Requirement: Profile defaults preserve explicit user choices
Configuration SHALL layer host defaults, profile defaults/payloads, explicit saved user values, then
session flags. Defaults SHALL use the design's settings allowlist. Profile payloads SHALL override
profile settings selections. User translations SHALL override profile translations at each locale
fallback layer. Saved visibility SHALL NOT reverse profile exclusions.

#### Scenario: Saved choice differs from the profile
- **WHEN** a user explicitly saved a theme selection and the selected profile supplies a theme
- **THEN** the saved selection wins without modifying the bundle

### Requirement: None is the recovery selection
`--profile none` SHALL load no bundle, discovered user packs, user translations, saved settings/layout
or add-ins. It SHALL use the host baseline, Default keymap, Classic theme and neutral English.
Configuration changes SHALL stay in memory and MUST NOT rewrite the saved settings/layout. Safe mode
SHALL retain explicit project-opening arguments and SHALL NOT claim to sandbox language servers.

#### Scenario: Recovering from a broken customization
- **WHEN** the user launches with `--profile none`
- **THEN** customization inputs are bypassed and the saved configuration remains intact

### Requirement: Built-ins preserve personality migration behaviour
The built-ins SHALL preserve the existing References placement and project offerings. They SHALL
contribute no toolbar, theme, keymap, language or settings overrides. Profiles SHALL affect offerings
rather than project compatibility or runtime functionality.

#### Scenario: Full and intermediate built-ins
- **WHEN** the launch selects `vb6` or `vbaode`
- **THEN** `vb6` shows Project References, hides Tools References and offers all eight existing template entries
- **AND** `vbaode` hides Project References, shows Tools References and offers Standard EXE only

#### Scenario: Migrating the reduced built-in
- **WHEN** the launch selects `--profile vba`
- **THEN** Tools References is shown, Project References is hidden and the New Project offerings are empty
- **AND** the existing toolbar offerings are unchanged at the migration boundary

#### Scenario: Opening an existing project under a reduced bundle
- **WHEN** a project created under `vb6` is opened under `vba`
- **THEN** it still loads and runs through the same underlying implementation

### Requirement: Removed personality flags explain the replacement
`--personality` and `/personality` SHALL NOT be accepted options or aliases. A launch using either SHALL
refuse startup before opening a window, exit with code 2 and show a localized diagnostic naming
`--profile vb6`, `--profile vbaode` and `--profile vba` as replacements. Help-only launches SHALL still
succeed. The replacement option's rendered help row SHALL fit the 94-column budget.

#### Scenario: A stale launch script
- **WHEN** a launch contains `--personality vba`, even alongside a valid profile selector
- **THEN** it reports the removed flag and replacement and does not silently start the default profile
