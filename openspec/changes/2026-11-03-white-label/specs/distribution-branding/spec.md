## Purpose

Allow an adopter distribution to configure identity, presentation and isolated state without changing
the managed implementation or carrying patches, while preserving HexIDE credits, MIT notices and trust.

## ADDED Requirements

### Requirement: Installation branding is declarative and fixed independently of profiles
The host SHALL resolve a validated installation descriptor beside its real executable, independently
of invocation directory, project and user configuration.
Profiles and saved preferences MUST NOT change distribution identity.

#### Scenario: Launching from a project directory
- **WHEN** the working directory or selected profile contains a different branding file
- **THEN** only the descriptor beside the executable establishes installation identity

### Requirement: The descriptor supplies inert distribution configuration
The descriptor SHALL establish a stable distribution id, display identity, contained inert assets,
additional credits, distinct support/home/repository links and an optional default-profile reference. It SHALL NOT supply executable hooks,
arbitrary state paths or trust-anchor overrides.

#### Scenario: Descriptor content
- **WHEN** an adopter supplies installation branding
- **THEN** the descriptor supplies identity, inert assets, credits, role-specific links and an optional default profile
- **AND** executable hooks, arbitrary state paths and trust-anchor overrides are refused

### Requirement: Missing branding selects stock identity
A missing descriptor SHALL select stock HexIDE.

#### Scenario: Stock installation
- **WHEN** no installation descriptor is present
- **THEN** stock identity and stock default roots are used

### Requirement: Identity validation precedes mutable-state access
The host SHALL validate and capture descriptor content and referenced assets before accessing settings,
profiles, logs, recovery, caches or session state. It MUST reject incompatible schemas, unsafe ids,
rooted or escaping references, links, duplicate fields and invalid assets with an attributed diagnostic.
Reads and decoding MUST be bounded. Invalid branding SHALL refuse normal startup without falling back
to stock state. Identity resolution SHALL require no network access.

#### Scenario: Invalid branding on a machine with stock recovery data
- **WHEN** installation branding fails validation
- **THEN** startup refuses with the descriptor/file identified
- **AND** no stock settings or recovery data are read, offered, changed or deleted

### Requirement: Distribution ids isolate per-user state
A stable distribution id SHALL select separate per-user configuration and local log, recovery, cache
and session locations. An adopter id MUST NOT alias the stock id or another accepted id through case,
reserved names or platform path mapping. Changing display name SHALL NOT move state. The host SHALL NOT
silently import stock settings.

#### Scenario: Two brands and stock on one machine
- **WHEN** distinct distributions run under their default roots for the same user
- **THEN** their settings, logs, recovery, cache and session files remain separate
- **AND** cleanup or uninstall of one does not remove another's user state

#### Scenario: A display-name update
- **WHEN** the display name changes and the distribution id does not
- **THEN** the distribution uses the same state roots

### Requirement: State redirects require an explicit CLI choice
An explicit CLI user-data redirect SHALL remain a deliberate override, with its isolation consequences
documented; a descriptor MUST NOT choose arbitrary directories.

#### Scenario: Explicit state redirect
- **WHEN** a user supplies a CLI user-data redirect
- **THEN** the host honors the deliberate override and documents its isolation consequences
- **AND** branding cannot select an arbitrary directory

### Requirement: Bundled consumers use the resolved distribution paths
The host SHALL supply resolved paths to its bundled child services, including the bundled language
server's log location, and SHALL show the actual paths in diagnostic hints. Pruning, cache eviction
and session cleanup SHALL stay inside this distribution's roots. Third-party servers SHALL retain
their own logging policy unless a separate supported contract redirects it.

#### Scenario: Bundled language server starts
- **WHEN** a branded host starts its bundled language server
- **THEN** both write logs within the brand's local log root with existing subsystem/session partitioning
- **AND** diagnostic hints identify those resolved locations

### Requirement: Host presentation consumes distribution identity without rewriting compatibility data
Host titles, dialog captions, startup/About/window assets, help labels and host-authored diagnostics
SHALL consume the captured display identity. The host SHALL retain upstream provenance and version
separately in About.

#### Scenario: Branded host presentation
- **WHEN** an adopter distribution displays host chrome
- **THEN** its captions and assets use captured identity while About retains upstream provenance and version

### Requirement: Brand assets and links retain safe roles
Brand assets MUST be inert contained data, not scripts or executable markup.
Brand links SHALL keep their distinct roles and applicable package link-verification rules.

#### Scenario: Brand assets and support links
- **WHEN** a descriptor supplies an asset and a support link
- **THEN** the asset is inert contained data and the link retains its role and applicable package verification

### Requirement: Branding preserves compatibility identities and user content
Managed assembly/resource identities, protocol/schema/tool ids, session headers, environment names
and serialized project vocabulary MUST remain compatible; branding MUST NOT globally replace text
in projects, program messages or third-party output.

#### Scenario: Branded host opens an existing project
- **WHEN** an adopter distribution opens a project containing the text HexIDE
- **THEN** host captions show the adopter display name and project text remains unchanged
- **AND** existing protocol clients and assembly/resource references retain their compatibility identities

### Requirement: Upstream credits are protected and additions are append-only
About SHALL retain a visible host-owned block of HexIDE credits and append adopter credits after it.
Branding APIs MUST NOT remove, replace, blank or suppress the base block, including through styling,
profile, package or user translation overrides. Shipped translations MAY localize it without allowing
those override tiers to replace it.

#### Scenario: An adopter supplies additional credits and an override of base credits
- **WHEN** the descriptor adds credits and an override attempts to empty the base-credit key
- **THEN** HexIDE's base credits remain visible followed by the adopter additions
- **AND** the override does not suppress provenance or the actual key fingerprints

#### Scenario: A brand translates its own tagline or credits
- **WHEN** a tagline or credit supplies a required `text` fallback and a `translations` map
- **THEN** About resolves each through the selected region, neutral pack, `en`, then its own fallback (#3417)
- **AND** translated credits still follow HexIDE's protected credits

### Requirement: Distribution notices remain independent of displayed credits
Required copyright/permission and third-party notices SHALL remain in distributions independently of
the credits display.

#### Scenario: Repacked distribution notices
- **WHEN** an adopter supplies its own About credits
- **THEN** required copyright, permission and third-party notices remain in the distribution

### Requirement: About fingerprints identify actual embedded keys
Actual embedded-key fingerprints SHALL remain visible and MUST NOT be relabeled as adopter trust anchors.

#### Scenario: Branded About trust display
- **WHEN** an adopter distribution displays About
- **THEN** the actual embedded-key fingerprints are visible with no adopter-trust-anchor relabeling

### Requirement: Native repacking uses the same descriptor without changing managed implementation
HexIDE SHALL supply a supported tool that consumes the installation descriptor to rewrite executable
native icon/version metadata and installer identity, including distinct installation ids, names,
shortcuts and platform bundle identity/assets.

#### Scenario: Native distribution identity
- **WHEN** the supported tool repacks stock output with an adopter descriptor
- **THEN** actual executable and installer metadata reflect that descriptor and allow coexistence with stock

### Requirement: Repacking preserves managed implementation and provenance
The repack tool SHALL preserve managed implementation, assembly/API and resource identities and required
licence/notice texts.

#### Scenario: Managed payload after repacking
- **WHEN** the tool repacks an adopter distribution
- **THEN** managed implementation, assembly/API and resource identities and required notices are preserved

### Requirement: Repacked output requires adopter signing without changing package trust
The adopter SHALL sign the repacked result; the tool MUST NOT represent invalidated upstream signatures
as valid or alter trust anchors or first-party package status.

#### Scenario: An adopter prepares a distribution
- **WHEN** the supported tool repacks stock output with an adopter descriptor
- **THEN** actual executable and installer metadata reflect that descriptor and allow coexistence with stock
- **AND** managed implementation is unchanged and the result requires adopter signing
- **AND** required notices and embedded package trust identities are preserved
