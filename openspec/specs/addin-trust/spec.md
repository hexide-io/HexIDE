# addin-trust Specification

## Purpose
Define what has to be true before third-party code runs inside the IDE: cryptographic identity, informed
consent, and a way to withdraw both after the fact.

The add-in system originally loaded every assembly it found next to the executable, with no signature
check, no consent, and no way to stop a bad one — arbitrary code execution on next launch, with a disable
list keyed by file path that a rename defeated. This capability exists because that was found and closed,
which is worth stating plainly: the elaborateness below is a response to a real hole, not speculative
design.

What it deliberately does **not** do is contain a malicious add-in. Add-ins run in-process with the user's
privileges; the host surface is an API convenience, not a security boundary, and the platform offers no
in-process sandbox. What signing, consent and revocation buy is provenance, an informed decision, and a
kill switch — the same posture every comparable IDE takes. Containment would require running add-ins out of
process, which is a different capability and is not built.

## Requirements
### Requirement: An add-in SHALL be a signed package verified before any of its code runs
An add-in SHALL be distributed as a package carrying a manifest, the manifest's signature, and the records
and signatures forming a chain to a key the IDE ships. Verification SHALL complete before any assembly from
the package is loaded, and SHALL succeed entirely offline. A package failing any check SHALL NOT load, and
the reason SHALL be recorded.

Verifying before loading is the whole point: metadata read by reflecting over an assembly has already
executed that assembly's loader, so the manifest — not the assembly — has to be the source of truth. Offline
verification means the check cannot be defeated by taking the network away, and means the IDE works on a
machine that never has one.

#### Scenario: A correctly signed package
- **WHEN** a package verifies against the shipped root
- **THEN** it proceeds to the consent decision

#### Scenario: A tampered file
- **WHEN** any byte of any file the manifest lists is altered after signing
- **THEN** verification fails and the add-in does not load

#### Scenario: Verifying with no network
- **WHEN** the machine has no network connection
- **THEN** verification still completes, because the whole chain travels in the package

### Requirement: The manifest SHALL be the complete inventory of the package
Verification SHALL reject a package whose directory contains any payload file the manifest does not list,
and the add-in's dependencies SHALL be resolved only from listed files.

Signing the files you know about leaves the obvious attack: add one more. Requiring the manifest to be
exhaustive means an unlisted file cannot exist in a loadable package, which in turn means no dependency
resolution mechanism — including ones the runtime provides and the IDE does not control — can reach
anything unverified.

#### Scenario: An extra file added to a valid package
- **WHEN** a file not listed in the manifest is added to a package whose listed files all verify
- **THEN** the package is rejected

### Requirement: A verified add-in SHALL require explicit consent before its first run
The IDE SHALL obtain explicit user consent before running a third-party add-in for the first time, SHALL
record the decision against the exact build consented to, and SHALL ask again when that build changes.
Add-ins the IDE itself ships SHALL be pre-consented.

A valid signature answers "who wrote this", not "do I want it". Because there is no in-IDE installation
step that could carry consent implicitly, the first launch is the only honest moment to ask. Keying the
record to the build rather than the identity means an update gets a fresh decision — an add-in the user
allowed cannot silently become a different add-in.

#### Scenario: First launch with a new third-party add-in
- **WHEN** a verified third-party add-in is present and has no recorded decision
- **THEN** the user is asked, and the add-in runs only if they allow it

#### Scenario: Relaunching after allowing
- **WHEN** the IDE restarts with a previously allowed add-in unchanged
- **THEN** it loads without asking again

#### Scenario: The add-in is updated
- **WHEN** an allowed add-in is replaced by a different build
- **THEN** the user is asked again

#### Scenario: Withdrawing consent
- **WHEN** a user revokes their consent for an add-in
- **THEN** it is asked for again on the next start

### Requirement: The consent decision SHALL be re-verified before the add-in is loaded
The IDE SHALL re-verify each package immediately before loading its entry assembly, on both the startup
and post-consent paths. It SHALL load only if the verdict and manifest hash match the original verification
and a manifest is still present. A changed package SHALL be listed as Error with a localized LoadError
saying it changed on disk after verification, and SHALL retain its id reservation against duplicates.

#### Scenario: A package altered while the prompt is open
- **WHEN** the package on disk differs from the one shown at the consent prompt
- **THEN** it does not load, even if the user pressed Allow
- **AND** Options → Add-Ins shows the localized changed-package error

#### Scenario: A package altered between discovery and startup loading
- **WHEN** another package's initialization or an external writer changes a package after discovery
- **AND** its re-verification returns a different verdict, a different manifest hash, or no manifest
- **THEN** no assembly from the changed package loads and it is not initialized
- **AND** it is listed as Error with the localized changed-package message
- **AND** a duplicate of its id remains refused

#### Scenario: An unchanged package
- **WHEN** re-verification matches the original verdict and manifest hash and still has a manifest
- **THEN** the package loads if it was already allowed, is first-party, or the user allows it at the prompt

### Requirement: First-party status SHALL be established by key, not by claimed identity
An add-in SHALL be treated as first-party only when its verified signing key matches a key the IDE ships
for that purpose.

First-party status skips the consent prompt, so it is worth attacking. If it were decided by an identifier
string in the manifest, anyone able to obtain a valid signature — or anyone who compromised an intermediate
signer — could claim it. Requiring the actual key means claiming first-party status requires holding the
first-party private key.

#### Scenario: An add-in claiming a first-party identifier
- **WHEN** a third-party package claims the first-party identifier but is signed by a different key
- **THEN** it is not treated as first-party and consent is still required

### Requirement: The IDE SHALL be able to revoke a build, a publisher, or an intermediate signer
The IDE SHALL consult a signed revocation list before the consent decision, and SHALL refuse to load
anything it revokes. Revocation SHALL be expressible against a specific build, a publisher, or an
intermediate signer.

A signature says a package is authentic, not that it is safe — a publisher can turn out to be malicious, or
have their key stolen. Checking before consent matters because a revoked add-in must never be presented to
the user as a decision they can get wrong. The three granularities correspond to the three things that
actually go wrong: a bad release, a bad publisher, and a compromised signer.

#### Scenario: A revoked build
- **WHEN** an add-in's build is revoked
- **THEN** it does not load and the user is not prompted about it

#### Scenario: A compromised intermediate signer
- **WHEN** an intermediate signer is revoked
- **THEN** every add-in whose identity chains through it refuses to load

#### Scenario: Revoking a bad first-party release
- **WHEN** a specific first-party build is revoked
- **THEN** that build does not load, even though it is first-party

#### Scenario: A compromised signer that also signed first-party add-ins
- **WHEN** a signer is revoked at the identity level and first-party add-ins chain through it
- **THEN** the key-pinned first-party add-ins still load, so the IDE's own features survive the revocation

### Requirement: Revocation SHALL fail open on retrieval and closed on a known revocation
Retrieving an updated revocation list SHALL be optional and time-bounded, and failure to retrieve one SHALL
fall back to the last known good list and then to the list the IDE ships. The IDE SHALL refuse to load only
on a revocation present in a validly signed list.

The alternative — refusing to load add-ins when the list cannot be fetched — turns any network problem, or
any outage of whoever hosts the list, into a broken IDE. A revocation list is a reactive control; treating
its absence as a revocation would do far more damage than the case it guards against.

#### Scenario: The list cannot be retrieved
- **WHEN** retrieval times out or the machine is offline
- **THEN** the previously cached list applies and the IDE neither hangs nor fails

#### Scenario: A tampered list
- **WHEN** a retrieved list does not verify, or is older than the one already held
- **THEN** it is ignored

#### Scenario: No retrieval configured
- **WHEN** no revocation source is configured
- **THEN** no retrieval is attempted and startup is not delayed

### Requirement: Unsigned add-ins SHALL load only under an explicit developer opt-in
An unsigned or invalid package SHALL load only when developer mode is active and the developer has
separately opted into loading unsigned add-ins, and such an add-in SHALL be visibly marked as unsigned.

Somebody has to be able to run an add-in they are still writing. The risk is that whatever enables this
becomes the thing an attacker talks a user into turning on, so it is deliberately not a single checkbox in
ordinary settings: it needs developer mode *and* a specific opt-in, and what loads is labelled so it can
never be mistaken for a verified add-in.

#### Scenario: An unsigned build with the opt-in off
- **WHEN** an unsigned package is present and the opt-in is not active
- **THEN** it does not load

#### Scenario: An unsigned build with the opt-in on
- **WHEN** the package is compatible, and developer mode and the unsigned opt-in are both active
- **THEN** the package loads and is marked as an unsigned developer build wherever it is shown

### Requirement: The trust surface SHALL show only what the IDE verified
Where the IDE reports an add-in's identity at a moment the user is making a trust decision, it SHALL
display only values it verified itself, and SHALL NOT display publisher-supplied presentation content.
Publisher-supplied content MAY appear on informational surfaces away from that decision.

A trust prompt decorated with attacker-supplied artwork and prose is a phishing surface: the more
convincing the publisher can make it look, the worse it is. Restricting the decision moment to
IDE-verified structural facts makes it structurally incapable of carrying a persuasive message. There is
no harm in a logo on a details page, which is a different moment with different stakes.

#### Scenario: Presenting the identity chain
- **WHEN** the user inspects an add-in's identity
- **THEN** they see each link with its identifier and key fingerprint, and whether it is key-pinned first-party

#### Scenario: A publisher-supplied logo
- **WHEN** a package supplies a logo
- **THEN** it appears only on the informational details surface, never at the consent decision

#### Scenario: Verifying an identity independently
- **WHEN** a user wants to confirm the IDE trusts what it should
- **THEN** the root and first-party key fingerprints are published outside the application so they can be compared

#### Scenario: A package that fails verification
- **WHEN** an add-in fails to verify
- **THEN** the surface reports how far the chain got and why it stopped, so the failure can be diagnosed

### Requirement: Manifest compatibility precedes trust

After guarding the entry path, verification SHALL refuse a manifest whose `schema` exceeds the current
schema (2), whose `minHexideVersion` exceeds the running assembly version, or whose non-null minimum
version cannot be parsed as a numeric `major.minor[.build[.revision]]` version. Missing components compare
as zero. Older schemas and an absent minimum SHALL retain their existing behavior. The verdict SHALL be
`Incompatible`, independent of signature trust, developer mode, unsigned loading and recorded consent.
Options → Add-Ins SHALL display the required and supported schema and host versions; the log SHALL give
the refusal reason. Unknown JSON members SHALL be retained as extension data for inspection, without
changing the behavior of otherwise compatible packages.

#### Scenario: Developer mode cannot override compatibility

- **WHEN** a package requires a newer schema or host version, or supplies an invalid minimum version
- **THEN** it is not loaded or prompted for consent, even with developer mode and unsigned loading enabled
- **AND** its compatibility requirements are visible in Options → Add-Ins and the refusal is logged

### Requirement: Package tooling stamps compatibility metadata

The packer SHALL write the current `schema` on every manifest and SHALL accept an optional
`--min-hexide-version` value, recording it as `minHexideVersion` in the signed manifest. An invalid numeric
minimum SHALL be rejected before changing the output directory.

#### Scenario: Package declares a minimum host version

- **WHEN** a publisher packs with `--min-hexide-version 1.4`
- **THEN** the signed manifest records schema 2 and minimum HexIDE version `1.4`

### Requirement: Signing generators preserve existing output

The add-in packer's `genkeys`, `genkeys-release`, `sign-publisher` and `sign-revocations` commands SHALL create their output files
exclusively. If any destination already exists, the command SHALL fail without replacing that file
or writing generated content to any of its other destinations. If a reservation or write fails,
the command SHALL remove exactly the output files created by that call.

#### Scenario: An existing private key or revocation output

- **WHEN** a signing generator encounters an existing output file
- **THEN** it reports failure and preserves that file's bytes
- **AND** no generated key, trust record or signature is written to the other destinations
- **AND** any output files reserved by this call are removed, including when the last destination collides

### Requirement: Release signing chains have explicit identities and separate key outputs

The packer SHALL provide `genkeys-release` with explicit intermediate and publisher identities and
separate root, intermediate, first-party and public output directories. It SHALL validate ids with
`PublisherId.IsValid` and names as trimmed, non-empty strings of at most 128 characters without controls,
Unicode format, line-separator or paragraph-separator characters.
This issuance validation SHALL NOT change package verification. Development generation SHALL retain
its defaults.

#### Scenario: Invalid signing identity

- **WHEN** an id is not 3–64 lowercase ASCII letters or digits joined by single dots or hyphens,
  or a display name is invalid
- **THEN** generation is refused before writing any files

#### Scenario: A release chain is generated

- **WHEN** all identities and four non-overlapping output directories are supplied
- **THEN** each private key is written to its own directory, public keys and signed records go to the
  public directory, and all three public-key fingerprints are printed without private key material

#### Scenario: An output file already exists

- **WHEN** any release-chain destination file already exists
- **THEN** it is preserved and no partial new chain is left behind

### Requirement: Offline publisher issuance

The packer's `sign-publisher` command SHALL issue a publisher record offline using an explicit root
public key, the intermediate private key and public record, and the publisher's public key, id and
display name. It SHALL validate inputs before writing, preserve existing files, write only
`publisher.json` and `publisher.sig`, verify the written chain and report success without exposing
private key material.

#### Scenario: Issuing a third-party publisher

- **WHEN** a maintainer supplies valid inputs to `sign-publisher` offline
- **THEN** the issued record signs the publisher's own public key and references the verified intermediate
- **AND** a package signed by that publisher verifies against the supplied root

#### Scenario: Invalid issuance inputs

- **GIVEN** publisher and intermediate ids must be 3–64 lowercase letters/digits joined by single dots or hyphens
- **AND** display names must be trimmed, non-empty, at most 128 characters and free of control, Unicode format,
  line-separator or paragraph-separator characters
- **AND** keys must be ECDSA P-256, the intermediate signature must verify against the supplied root,
  and the private key must match the intermediate public record
- **WHEN** any of these checks fails
- **THEN** issuance fails before writing publisher files

### Requirement: Package output replacement preserves user files

`pack` SHALL accept new or empty output folders and unchanged signed packages matching the supplied
key, payload hashes, trust files and exact inventory. It SHALL refuse output/internal links and
escaping payload paths, but allow linked ancestors. Ownership/link refusals SHALL name the path,
explain why, confirm no change, and suggest another folder or moving this one aside. Replacements
SHALL be staged; build or installation failure SHALL preserve the earlier package.

#### Scenario: Empty output or linked ancestor
- **WHEN** the output directory is empty or an ancestor of the output is a link
- **THEN** packing proceeds subject to the same output inventory checks

#### Scenario: Unrelated or edited output
- **WHEN** the selected output contains unrelated or modified files, an extra directory, or a link
- **THEN** packing refuses and preserves the existing output

#### Scenario: Replacing a previous signed package
- **WHEN** the existing output is an unchanged package signed with the supplied signing key and chain
- **THEN** packing replaces it without prompting after the complete replacement is staged

#### Scenario: Failure before installation
- **WHEN** copying or signing the staged package fails
- **THEN** the earlier package remains in place unchanged

### Requirement: Add-in identity in trust surfaces remains the signed manifest name
The consent prompt and Options → Add-Ins SHALL identify a signed add-in by the verified manifest's title.
Localized chrome titles SHALL NOT replace that identity. `AddinTitle` and the packer's `--title` SHALL
retain their separate roles as chrome text and signed package metadata respectively.

#### Scenario: A package supplies a translated chrome title
- **WHEN** an add-in registers a translated menu, tool window or command label and the language changes
- **THEN** the consent prompt and Options → Add-Ins retain its signed manifest title
