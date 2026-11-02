# profiles Specification

## Purpose

Provide session profiles selected from validated bundles, including settings-default, theme, keymap,
language, References visibility and New Project offerings (#504). Profiles replace the personality APIs.
Bundle containment and byte capture use the shared [profile catalog](../profile-catalog/spec.md).
The surface renderers remain subsequent deliveries in
`openspec/changes/2026-10-25-profiles-supersede-personalities/`.

## Requirements

### Requirement: Bundle loading produces an immutable snapshot
The profile loader SHALL validate a version-1 `profile.json` and read all referenced JSON into an
immutable snapshot before returning it. It SHALL preserve omitted surfaces and distinguish omitted
project types from an explicitly empty array. It SHALL accept only the approved manifest members,
project type ids, currently published visibility ids and settings-default allowlist. Existing pack
wire formats SHALL be reused; pack semantic warning rules remain with their adapters.

#### Scenario: A later payload fails
- **WHEN** a bundle has valid visibility entries but a missing or malformed referenced payload
- **THEN** loading fails with source and file attribution, without applying any surface or changing an
  earlier snapshot
- **AND** the error can be formatted through the localization service with a bundle-checking remedy

### Requirement: Resolution has deterministic sources
The loader SHALL classify `none`, paths and names in that order. Omission SHALL select indexed embedded
`vb6`; `vb6`, `vbaode` and `vba` SHALL be reserved embedded names. Other names SHALL resolve only under
the supplied user-data directory's `profiles/<id>` directory, whose manifest id MUST match. Explicit
paths SHALL resolve against the supplied invocation directory and retain absolute-path provenance.

#### Scenario: An installed bundle collides with a built-in
- **WHEN** the installed profiles directory contains `vb6`
- **THEN** name selection returns embedded `vb6` and explicit path selection returns the disk bundle

### Requirement: Referenced files stay inside the bundle
The loader MUST reject rooted references, drive-relative references, traversal and links inside bundles
on every host, including Windows path spellings on Unix. It MUST read bundles without writing them.

#### Scenario: A payload traverses an intermediate symlink
- **WHEN** a referenced file or its ancestor inside the bundle is a symbolic link
- **THEN** loading is refused with the source and referenced filename identified

### Requirement: Settings defaults preserve explicit user choices
The settings reader SHALL accept defaults from a validated profile snapshot, layering host defaults,
explicitly present profile defaults and explicitly present saved user properties in that order. It
SHALL apply existing settings value validation and SHALL NOT persist unedited profile defaults.
Startup SHALL supply the selected snapshot to the settings reader.

#### Scenario: A saved value equals the host default
- **WHEN** a profile supplies a different value and the saved settings file explicitly holds the host default
- **THEN** the saved value wins
- **AND** omitted saved properties retain their profile defaults

#### Scenario: An unrelated setting is saved
- **WHEN** a user changes one setting after loading profile defaults
- **THEN** only edited settings are written, preserving saved properties without copying unedited profile values

### Requirement: Profile themes preserve saved choices and apply atomically
The theme adapter SHALL consume the snapshot's existing theme-pack format without reopening its file.
A theme payload SHALL supersede a profile settings theme default; an explicitly saved user theme
choice SHALL supersede the payload, including a choice equal to the host default. Known invalid
colours MUST reject the bundle before any surface is applied. Unknown colour keys SHALL retain the
existing warn-and-skip behaviour, with bundle and file attribution. The adapter SHALL prepare its
resource dictionary before replacing the active override and SHALL select a deterministic Light or
Dark variant. Startup SHALL supply the selected snapshot to this adapter.

#### Scenario: A profile has a theme payload and a saved Classic choice
- **WHEN** the saved settings explicitly select Classic
- **THEN** Classic is applied instead of the profile theme
- **AND** an omitted saved theme choice would allow the profile payload to override the profile default

#### Scenario: A known colour is invalid
- **WHEN** a theme payload contains a valid colour followed by an invalid known colour
- **THEN** the loader refuses the bundle with source and file attribution
- **AND** the active theme and resource dictionaries remain unchanged

### Requirement: Profile keymaps preserve explicit choices and validate before application
The keymap adapter SHALL consume the snapshot's existing keymap-pack format without reopening its
file. Its payload SHALL supersede a profile settings keymap default; explicit saved or edited user
choices SHALL supersede the payload, including Default, and saving SHALL preserve that provenance.
Omitted command bindings SHALL retain baseline gestures; empty bindings SHALL clear them. Invalid
known gestures MUST reject the bundle before application. Unknown commands SHALL retain warn-and-skip
behaviour with bundle and file attribution. Startup SHALL supply the selected snapshot to this adapter.

#### Scenario: A payload is selected without an explicit user choice
- **WHEN** the profile supplies a keymap payload and a different settings default
- **THEN** the snapshot payload is applied without persisting it as a user choice
- **AND** a subsequent explicit Default choice restores baseline gestures, including after saving

#### Scenario: A known gesture is invalid
- **WHEN** a payload contains a valid binding followed by an invalid known gesture
- **THEN** loading fails with bundle and file attribution
- **AND** the current command gestures remain unchanged

### Requirement: Profile language overrides retain fallback and user precedence
The language adapter SHALL read language-override strings from the validated snapshot without reopening
bundle files. It SHALL merge profile neutral and region strings above shipped strings using the existing
locale chain, including Chinese script neutrals, and SHALL retain user neutral and region overrides as
the final tiers. It SHALL reuse user-translation orphan-key, blank-value and placeholder validation,
skipping invalid entries. It MUST NOT extend the selectable locale inventory or persist profile strings
as user translations. Startup SHALL supply the selected snapshot to this adapter.

#### Scenario: A profile supplies neutral and regional translations
- **WHEN** a region is selected with profile overrides for both it and its neutral language
- **THEN** regional profile strings override neutral profile strings and missing entries retain shipped fallback
- **AND** English profile strings affect only the English locale chain

#### Scenario: A user clears a translation override
- **WHEN** a user override that supersedes a profile string is cleared
- **THEN** the active language is reapplied and reveals the profile string without writing it to user storage

#### Scenario: A profile translation breaks placeholders
- **WHEN** a profile entry omits a canonical placeholder or contains malformed format braces
- **THEN** that entry is skipped while other valid entries still apply

### Requirement: Startup selects one profile before constructing consumers
The desktop CLI SHALL accept one `--profile <value>` or `/profile <value>` selector in Debug and Release.
It SHALL resolve against the invocation directory and the fully parsed user-data directory, independent
of flag order. Omission SHALL select embedded `vb6`; `none` SHALL select a null snapshot with baseline
offerings. Recovery-mode customization and write suppression remain a subsequent delivery. A selected
bundle MUST validate before application, and rejection MUST exit with code 2 and an attributed localized
remedy rather than silently selecting a different bundle. Selection SHALL NOT be persisted.

#### Scenario: User-data flag follows the selector
- **WHEN** an installed profile name precedes the user-data flag
- **THEN** lookup still uses that user-data directory's profiles folder

### Requirement: Removed flags explain the replacement
The CLI MUST remove `--personality` and `/personality` from accepted options and help. Either spelling
SHALL refuse startup with exit code 2 and a localized diagnostic naming `--profile vb6`, `--profile vbaode`
and `--profile vba`, even alongside a valid profile selector. Missing or repeated profile selectors SHALL
also refuse startup with an actionable localized message. Help-only requests SHALL succeed before
validation. The rendered replacement help row MUST fit the 94-column budget.

#### Scenario: A stale launch script
- **WHEN** the command line includes the removed personality flag and a valid profile selector
- **THEN** the IDE explains the replacement without opening a window

### Requirement: Built-ins preserve migration offerings
The profile service SHALL use ordered stable template ids and References visibility from the selected
snapshot, retaining host defaults for omitted entries and preserving an explicitly empty project list.
Built-in `vb6` SHALL show Project References and offer all eight baseline templates in order; `vbaode`
SHALL show Tools References and offer only Standard EXE; `vba` SHALL show Tools References and offer none.
The opposite References placement SHALL be hidden. Built-ins SHALL supply no theme, keymap, translation,
settings or toolbar overrides. Profiles SHALL NOT change runtime functionality.

#### Scenario: Reduced built-in
- **WHEN** the selected bundle is `vba`
- **THEN** New Project has no built-in templates and Tools References remains visible
- **AND** opening and running an existing project uses the same underlying implementation

### Requirement: None is the recovery selection
`--profile none` SHALL load no bundle, discovered user packs, user translations, saved settings/layout
or add-ins. It SHALL use the host baseline, Default keymap, Classic theme and neutral English.
Configuration changes SHALL stay in memory and MUST NOT rewrite or delete saved settings/layout or
translation overrides. Safe mode SHALL retain explicit project-opening arguments and new-project
flags and SHALL NOT claim to sandbox language servers.

#### Scenario: Recovering from a broken customization
- **WHEN** the user launches with `--profile none`
- **THEN** customization inputs are bypassed and the saved configuration remains intact
- **AND** layout reset and automatic saves change only this session's state

#### Scenario: Language confirmation during recovery
- **WHEN** a recovery session changes the IDE language
- **THEN** the confirmation gate and automation reply use a localized notice that the change lasts only this session
- **AND** the reply does not promise saving for later starts

#### Scenario: Opening a project during recovery
- **WHEN** a recovery launch names an explicit project path
- **THEN** the startup hook still opens that project

### Requirement: Project surfaces share profile offerings
New Project and the toolbar Add Project flyout SHALL read the same ordered built-in template offerings
from the profile service. The flyout SHALL disable unsupported templates. With no offerings, each
surface SHALL show a localized explanation directing the user to open an existing project, and the
dialog SHALL keep its New action disabled. Existing and Recent project opening SHALL remain available.
Add-in templates SHALL retain their existing separate contribution to the New Project dialog.

#### Scenario: Reduced profile on both surfaces
- **WHEN** the selected profile is `vba`
- **THEN** neither surface offers a built-in template and both explain how to continue

#### Scenario: Full and intermediate profiles
- **WHEN** the selected profile is `vb6` (including omission) or `vbaode`
- **THEN** both surfaces offer respectively eight templates or Standard EXE only, in profile order
- **AND** the flyout cannot invoke unsupported template entries
