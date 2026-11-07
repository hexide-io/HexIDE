## MODIFIED Requirements

### Requirement: Resolution has deterministic sources
The loader SHALL classify `none`, paths and names in that order. Omission SHALL select the installation
descriptor's validated default-profile reference when supplied, and otherwise indexed embedded `vb6`.
The names `vb6`, `vbaode` and `vba` SHALL be reserved embedded names. Other names SHALL resolve only under
the supplied user-data directory's `profiles/<id>` directory, whose manifest id MUST match. Explicit
paths SHALL resolve against the supplied invocation directory and retain absolute-path provenance.

#### Scenario: An installed bundle collides with a built-in
- **WHEN** the installed profiles directory contains `vb6`
- **THEN** name selection returns embedded `vb6` and explicit path selection returns the disk bundle

#### Scenario: Installation default with explicit selection
- **WHEN** a distribution supplies a default profile and the user explicitly selects another profile
- **THEN** the explicit selector wins without changing installation identity
- **AND** omitting the selector uses the installation default under the resolved distribution root

### Requirement: Startup selects one profile before constructing consumers
The desktop CLI SHALL accept one `--profile <value>` or `/profile <value>` selector in Debug and Release.
It SHALL resolve against the invocation directory and the fully parsed user-data directory, independent
of flag order. Omission SHALL select the installation default when present, otherwise embedded `vb6`;
`none` SHALL select a null snapshot with baseline offerings under the recovery contract.
Installation identity and paths MUST be fixed before profile resolution. A selected
bundle MUST validate before application, and rejection MUST exit with code 2 and an attributed localized
remedy rather than silently selecting a different bundle. Selection SHALL NOT be persisted.

#### Scenario: User-data flag follows the selector
- **WHEN** an installed profile name precedes the user-data flag
- **THEN** lookup still uses that user-data directory's profiles folder

### Requirement: Profile language overrides retain fallback and user precedence
The language adapter SHALL read language-override strings from the validated snapshot without reopening
bundle files. It SHALL merge profile neutral and region strings above shipped strings using the existing
locale chain, including Chinese script neutrals, and SHALL retain user neutral and region overrides as
the final tiers for overridable host text. The protected upstream-credit block SHALL use shipped
translations and fallback only; profile and user entries targeting that block MUST be ignored with
source attribution while other valid entries continue to apply. It SHALL reuse user-translation orphan-key, blank-value and placeholder validation,
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

## ADDED Requirements

### Requirement: Profiles cannot set distribution identity
A profile SHALL change only its supported preference surfaces and SHALL NOT set the distribution id,
product identity, native packaging identity, state roots or trust anchors. Selecting a profile,
including `none`, MUST retain installation identity, protected upstream credits and distribution-scoped
paths. Recovery selection SHALL retain its customization bypass, notice and write suppression.

#### Scenario: A branded recovery launch
- **WHEN** an adopter installation starts with `--profile none`
- **THEN** it retains the adopter identity and isolated roots with protected upstream credits
- **AND** it bypasses customization and suppresses configuration writes under the recovery contract
