## Purpose

Define one installer and manifest contract for data packs, host-run tools and code contributions,
preserving tier-specific trust and existing payload semantics.

## ADDED Requirements

### Requirement: One package inventories every contribution

The installer SHALL use one manifest as the complete inventory of all package payload files and
dependencies, excluding only the manifest and its trust envelope from self-inventory. It SHALL
verify inventory completeness and file digests before activation, and SHALL reject missing, altered
or undeclared payloads.

#### Scenario: Undeclared payload

- **WHEN** an archive contains a payload absent from its inventory
- **THEN** installation is refused before any contribution is activated.

### Requirement: Installation preserves containment and atomicity

The installer MUST reject rooted paths, traversal, links, duplicate entries and case-colliding paths
on every host. It SHALL enforce bounded extraction and SHALL activate only a completely verified
installation. Failed or interrupted installation SHALL leave the prior active version usable. No
install hook SHALL execute. On Unix, executable permissions SHALL be assigned by the installer only
after verification.

#### Scenario: Unsafe archive

- **WHEN** an entry escapes the installation root or exceeds extraction limits
- **THEN** installation fails without activating partial content or changing the previous active
  version.

### Requirement: Contribution tiers determine privileges

The host MUST derive data, host-run and code privileges from validated contribution kinds and
payloads. An aggregate suite SHALL NOT downgrade executable children by claiming to be data.
Data-only packages SHALL support signed and unsigned installation with signature state disclosed.

#### Scenario: Mixed suite

- **WHEN** a suite contains icons and a language-server launch
- **THEN** the launch requires host-run trust and consent even if the suite labels itself data.

### Requirement: Data contributions stay declarative

Data contributions SHALL cover language definitions, themes, keymaps, translations, icons, profiles,
settings defaults, placements, templates, snippets and property schemas without executing scripts or
arbitrary widgets. Language definitions SHALL support extensions, language ids, filename patterns,
root markers and declarative syntax colouring.

#### Scenario: Language definition installed

- **WHEN** a suite provides a declarative language definition
- **THEN** its file routing metadata is available without granting code privileges.

### Requirement: Executable contributions disclose their tier

Language servers, debug adapters, build/run tools and property-provider processes SHALL be host-run
contributions. Managed add-ins and custom editor, control or designer implementations SHALL be code
contributions. Their acquisition, trust and consent SHALL follow the corresponding executable tier.

#### Scenario: Provider-backed properties

- **WHEN** a schema needs a process to read or apply values
- **THEN** the provider process is treated as host-run rather than hidden in a data contribution.

### Requirement: Acquisition supports three routes

Packages SHALL support bundled artifacts, digest-verified downloads and detected installations with
explicit per-platform selection and unavailable-platform reporting. Downloaded archives MUST match
pinned SHA-256 before extraction or execution; extracted payloads SHALL also match the inventory.

#### Scenario: Digest mismatch

- **WHEN** downloaded bytes differ from the selected digest
- **THEN** no extraction or process launch occurs and the mismatch is reported.

### Requirement: Digest provenance records its source

Digest provenance SHALL distinguish publisher-attested checksums with their source from locally
computed digests with their author and tested scope. A computed digest SHALL NOT be presented as
publisher-attested.

#### Scenario: Locally computed checksum

- **WHEN** a manifest uses a digest computed by a tester
- **THEN** disclosure identifies who computed it and the tested scope without claiming publisher attestation.

### Requirement: Detection does not imply verification

Detection SHALL NOT execute a probe without consent or imply inventoried-byte guarantees it has not
established. A detected installation SHALL expose the executable identity and verification scope
before launch consent.

#### Scenario: Detection needs a version command

- **WHEN** a detector would have to execute a program to identify its version
- **THEN** the command does not execute without consent.

### Requirement: Outside acquisition remains publisher-owned

HexIDE SHALL download outside suites only from their publishers over HTTPS, SHALL NOT host or mirror
outside artifacts, and SHALL retain a per-user cached copy. Redirects MUST preserve HTTPS and remain
within the approved publisher asset-host policy. Cached bytes SHALL be verified before use.
Adopter-bundled outside artifacts SHALL retain outside-publisher trust and licence disclosure, never
inheriting first-party pinning from their transport. HexIDE releases MUST NOT bundle non-MIT code.

#### Scenario: Bundled outside suite

- **WHEN** an adopter supplies an outside artifact without a network transfer
- **THEN** the installer still checks publisher trust and inventory and discloses the artifact
  licence.

### Requirement: Acquisition and first launch require an accepted offer

Before downloading or launching, the host SHALL disclose artifact licence, origin, executable
identity, command arguments, environment and declared access. It SHALL require an explicit accepted
offer before fetching artifact bytes; that decision SHALL cover download and first launch without a
second prompt for the same accepted build. Changed command lines SHALL require renewed consent.
Platform and runtime prerequisites SHALL be reported without executing unconsented probes.

#### Scenario: Offer declined

- **WHEN** the user declines a missing-suite offer
- **THEN** no artifact body is downloaded and no contributed process starts.

### Requirement: Contribution layering retains user ownership

Resolution SHALL apply built-ins, installed contributions and user configuration in that order. An
override SHALL retain its target contribution id. Uninstall SHALL preserve the override and mark it
orphaned, rather than silently deleting it or binding it to another contribution.

#### Scenario: Uninstall with override

- **WHEN** a package is removed while a user override targets one of its contributions
- **THEN** the override remains identifiable as orphaned.

### Requirement: Compatibility precedes activation

The host MUST enforce manifest schema and minimum host version before consent or activation,
including unsigned-server opt-in and developer mode. Unsupported newer schemas, malformed minimum
versions and unmet minimum versions SHALL produce an explicit incompatible state without execution.

#### Scenario: Newer schema

- **WHEN** an older host reads an unsupported newer manifest
- **THEN** it reports incompatibility rather than attempting activation.

### Requirement: Property contributions persist validated values

Project properties SHALL support declarative editor schemas, provider-backed read/apply operations
over the backend seam and declarative mappings for supported text formats. A successful apply SHALL
persist requested values to the project file while preserving unrelated content. Data schemas SHALL
NOT grant arbitrary executable widgets.

#### Scenario: Property edit saved

- **WHEN** a supported property edit succeeds
- **THEN** reading the actual project file returns the requested value with unrelated content preserved.

### Requirement: Property writes reject stale or unauthorized edits

The host MUST validate editable types, authorized targets, read-only and unsaved state and stale
revisions before applying property changes. Rejected or stale changes SHALL NOT report success or
overwrite newer content.

#### Scenario: Stale property edit

- **WHEN** a provider applies values against an obsolete project revision
- **THEN** the edit is refused and current project bytes remain unchanged.

### Requirement: Publisher links require key-bound domain proof

The host SHALL bind domain proofs to an authenticated signing key across packages, check HTTPS
well-known proof first and offer DNS TXT proof. It SHALL date results and recheck on update. Proof
failure SHALL NOT block installation.

#### Scenario: Another package uses the same key

- **WHEN** a new package has a link on a domain proven for its authenticated signing key
- **THEN** the key-bound proof applies without trusting a manifest display name.

### Requirement: Link presentation distinguishes proof from endorsement

Verified links SHALL be clickable and show the registrable domain without implying endorsement.
Unverified links SHALL be marked plain text and not clickable. IDNs SHALL use ASCII xn-- form or
explicit mixed-script warnings. Digest-pinned download URLs SHALL be exempt from this presentation
rule.

#### Scenario: Unproven support link

- **WHEN** a manifest claims a support URL whose domain is not proven for its signing key
- **THEN** installation may continue but the URL is marked unverified plain text.

### Requirement: Unsigned packages cannot borrow domain proofs

Links from packages without an authenticated signing key SHALL remain unverified. Publisher names
and download digests SHALL NOT authenticate domain ownership.

#### Scenario: Unsigned icon pack claims a publisher

- **WHEN** an unsigned pack uses the name of a publisher with proven domains
- **THEN** its support links remain unverified.
