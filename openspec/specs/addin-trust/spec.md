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

### Requirement: An add-in's protocol prefixes SHALL be bound to its publisher's signed namespaces
When the verified publisher record issues namespaces, a contributed language server's message prefix SHALL be
`<namespace>/` and its command prefix `<namespace>.` (or under them) for one of them, or registration SHALL be
refused naming both. The namespaces SHALL be read only from the signed publisher record. A record without the
field SHALL keep the earlier rule, with one notice per add-in and no refusal. `hexide/` and `hexide.` SHALL stay
reserved whatever a record says.

#### Scenario: A prefix outside the issued namespaces
- **WHEN** an add-in whose publisher was issued `rdcore.` registers a server with the message prefix `other/`
- **THEN** registration is refused, naming `other/` and `rdcore.`

#### Scenario: A record from before the field
- **WHEN** an add-in whose publisher record has no namespaces registers a server with the prefix `rdcore/`
- **THEN** the server is added, and one notice says the prefix isn't checked against the publisher

#### Scenario: A record claiming HexIDE's namespace
- **WHEN** a signed publisher record lists `hexide.`
- **THEN** that entry grants nothing, and `hexide/` and `hexide.` prefixes are still refused

### Requirement: The IDE SHALL be able to revoke a build, a publisher, or an intermediate signer
The IDE SHALL consult a signed revocation list before the consent decision, and SHALL refuse to load
anything it revokes. Revocation SHALL be expressible against a specific build, a publisher, or an
intermediate signer, and against a signing key by its SHA-256 fingerprint. A revoked publisher key SHALL refuse every
package it signed, first-party included. A revoked intermediate key SHALL refuse the packages chained through it except
key-pinned first-party ones, as a revoked intermediate id does.

A signature says a package is authentic, not that it is safe — a publisher can turn out to be malicious, or
have their key stolen. Checking before consent matters because a revoked add-in must never be presented to
the user as a decision they can get wrong. The three granularities correspond to the three things that
actually go wrong: a bad release, a bad publisher, and a compromised signer. A key is finer than an id: revoking a
publisher's stolen key leaves the record re-issued for their new key standing.

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

#### Scenario: A stolen publisher key
- **WHEN** the list names a publisher's key by its fingerprint
- **THEN** packages signed with that key refuse to load, and packages signed with the publisher's re-issued key still load

#### Scenario: A leaked first-party key
- **WHEN** the list names the first-party key by its fingerprint
- **THEN** first-party add-ins signed with it refuse to load

#### Scenario: A stolen intermediate key that also signed first-party add-ins
- **WHEN** the list names an intermediate's key and first-party add-ins chain through it
- **THEN** the key-pinned first-party add-ins still load

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

#### Scenario: The list the IDE ships does not verify
- **WHEN** the build's own bundled list fails root verification
- **THEN** the IDE SHALL still start, with the cached and retrieved lists, and SHALL log the refusal at error level,
  because the build's own file failing is a defect in the build rather than a retrieval problem (#4537)

#### Scenario: No retrieval configured
- **WHEN** no revocation source is configured
- **THEN** no retrieval is attempted and startup is not delayed

### Requirement: Startup revocation retrieval SHALL depend on installed packages
The default source SHALL be `https://hexide.io/revocations.json`, with its signature at the URL plus
`.sig`. A user URL SHALL override it; null SHALL select the default and an empty URL SHALL disable retrieval. Startup SHALL fetch
whenever an enabled discovered package has a manifest, first-party packages included, before packages
load or await consent, because a build revocation applies to first-party packages too. Retrieval SHALL retain the three-second
timeout, root verification and strictly newer `issuedUtc` check.

#### Scenario: Only first-party components installed
- **WHEN** every enabled discovered package is verified against the pinned first-party key and a nonempty source is configured
- **THEN** startup attempts retrieval before those packages load, so a revoked first-party build is refused

#### Scenario: Third-party package awaiting consent
- **WHEN** an enabled third-party package is discovered and a nonempty source is configured
- **THEN** startup attempts retrieval before deciding whether that package may load

#### Scenario: No add-in packages
- **WHEN** no discovered package has a manifest
- **THEN** startup makes no revocation fetch

### Requirement: The revocation cache SHALL replace signed pairs atomically
The cache SHALL atomically replace the signed bytes and signature together, preserving the previous
cached list if a write is interrupted. Existing two-file caches SHALL remain readable.

#### Scenario: Interrupted cache update
- **WHEN** a newer valid list is fetched but its cache write is interrupted
- **THEN** the current session uses the fetched list and the next session retains the previous valid cache

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

The inspector's dialog title, row headings, root explanation and fingerprint comparison instructions
SHALL use the active language pack in both the consent dialog and Options expander. The open Options
page SHALL refresh these strings on a language switch. Branding SHALL preserve references to HexIDE's
root and TRUST.md; chain identifiers, versions and fingerprints SHALL remain data.

#### Scenario: Inspecting trust in another language
- **WHEN** the user opens Trust details at consent or the Trust expander in Options
- **THEN** the inspector's text uses the active pack
- **AND** changing language with Options open updates the inspector's headings
- **AND** a distribution with another product name still names HexIDE's root and TRUST.md

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

### Requirement: A refusal SHALL name a root the chain verifies under that this build doesn't carry
A package MAY carry its root's public key as `root.pub`. When the intermediate's signature fails under the embedded
root, the verifier SHALL check the chain under that key, and SHALL name the key's SHA-256 fingerprint only when the
whole chain verifies under it. Such a package SHALL be refused like any other unverified package.

#### Scenario: A package signed under a newer root
- **WHEN** a package's whole chain verifies under the root it carries, and that root isn't this build's
- **THEN** the package is refused, and Options → Add-Ins names that root's fingerprint and asks the user to compare it
  with a newer release's TRUST.md

#### Scenario: A real root's key copied into a forged package
- **WHEN** a package carries a genuine root's public key, but its chain wasn't signed under it
- **THEN** the package is refused as an invalid signature, and no fingerprint is named

#### Scenario: A broken package under its own root
- **WHEN** a package's chain leads to the root it carries, but a file's hash doesn't match
- **THEN** the refusal names the hash mismatch, not the root

### Requirement: A root key a package carries SHALL never be trusted
The verifier SHALL NOT trust, remember or decide anything by a root key a package carries. Nothing SHALL offer to trust
it, and no message SHALL say such an add-in is genuine, because a newer HexIDE's root and a forger's look alike.

#### Scenario: A package signed under a root this build doesn't carry
- **WHEN** such a package is installed
- **THEN** it is never shown as verified, and no control offers to trust its root

### Requirement: The packer SHALL write the chain's root into each package
`pack` SHALL write the trust folder's `hexide-root.pub` into each package as `root.pub`. It SHALL refuse, before
writing anything, a trust folder without `hexide-root.pub` or one whose root didn't sign its intermediate.

#### Scenario: A trust folder whose root is from another chain
- **WHEN** the trust folder's `hexide-root.pub` didn't sign its `intermediate.json`
- **THEN** `pack` refuses and writes nothing

### Requirement: A publisher request SHALL be signed by the key it asks to certify
The packer's `request-publisher` command SHALL write `request.json` in one canonical ASCII form and sign it with the
publisher's key, and a rotation also with the key it replaces. Every signature SHALL cover a fixed domain prefix
followed by the request's bytes, so that it can't pass for any other signature HexIDE checks.

#### Scenario: Making a request
- **WHEN** a publisher runs `request-publisher` with a new key
- **THEN** the key is written on its own, never into the request folder, and the request verifies

#### Scenario: A signature over the bare bytes
- **WHEN** a request's signature covers its bytes without the prefix
- **THEN** `verify-request` refuses it

### Requirement: verify-request SHALL treat a request as hostile input
`verify-request` SHALL refuse a request that is oversized, a link, not in canonical form, has an unknown or invalid
field, or whose signatures don't verify. With a ledger it SHALL refuse an id, key or overlapping namespace another
publisher holds. It SHALL exit 0 only when every check passed, and 3 for a rotation it couldn't check against a ledger.

#### Scenario: A request for a held id
- **WHEN** the ledger already holds the request's id, key or an overlapping namespace
- **THEN** the request is refused

#### Scenario: A rotation checked without a ledger
- **WHEN** a rotation is checked with no ledger
- **THEN** it exits 3 and says its previous key was not checked

### Requirement: Revocation SHALL be re-checked hourly while add-ins are installed
While an add-in package is installed and a revocation source is configured, the IDE SHALL fetch the list again about
every hour, in the background, after startup's fetch. The interval SHALL NOT be configurable. The list SHALL be requested
conditionally with the validator of the last list that verified, kept in memory only.

#### Scenario: A session left open for days
- **WHEN** a newer list is published while the IDE runs
- **THEN** the IDE fetches it within about an hour, or at once after waking from sleep

#### Scenario: Checking turned off
- **WHEN** the revocation source is set empty while the IDE runs
- **THEN** the next hourly check fetches nothing

### Requirement: A loaded add-in revoked mid-session SHALL be withdrawn
When a newer list revokes a loaded add-in, by the identity verified when it loaded, the IDE SHALL mark it Revoked, take
out what it contributed, whenever it registered it, run none of its commands, menu items or event handlers, and refuse
what it registers afterwards. Its code SHALL stay loaded until restart.

#### Scenario: A build revoked while it is loaded
- **WHEN** a newer list names a loaded add-in's build
- **THEN** its menus, commands and tool windows are taken out, and it hears no more events

#### Scenario: A publisher revocation and a first-party add-in
- **WHEN** a newer list revokes a publisher id while a first-party add-in is loaded
- **THEN** the first-party add-in stays loaded

#### Scenario: A tool window registered after the add-in started
- **WHEN** a revoked add-in had registered a tool window or Options page after its initialisation
- **THEN** that tool window or Options page is taken out too

### Requirement: issue-publisher SHALL sign only the approved request
The packer's `issue-publisher` command SHALL sign a publisher record only from a request whose SHA-256 equals the approved
one and whose `verify-request` check, against the ledger, the root and, for a rotation, the revocation list, passes with
nothing refused or left unchecked. It SHALL ask for the publisher id to be typed back at a terminal, and refuse input that
isn't one. It SHALL write nothing when it refuses.

#### Scenario: A request edited after approval
- **WHEN** the request's SHA-256 is not the approved one
- **THEN** nothing is signed or written

#### Scenario: A rotation from another account
- **WHEN** a rotation's github handle or fingerprintUrl differs from the request its id was issued from
- **THEN** it is left to a person, and nothing is signed

### Requirement: The ledger SHALL hold every id and key ever issued
Each issued record SHALL be added to the ledger with the request it was issued from. A rotation SHALL move the old record
to `retired/<n>/`, numbered without gaps. No id or key in the ledger, live or retired, SHALL be issued again, and with a
root every record and stored request SHALL be verified before the ledger is trusted. `issue-publisher` SHALL write the
ledger entry before the publisher's `--out` folder, and SHALL refuse an `--out` inside the ledger, so that every record it
signs is one the ledger holds.

#### Scenario: A retired key requested under a new id
- **WHEN** a request names a key the ledger holds in a retired record
- **THEN** it is refused

#### Scenario: A tampered ledger
- **WHEN** a ledger record or its stored request doesn't verify under the root
- **THEN** the check refuses rather than trusting the ledger

#### Scenario: The publisher's folder can't be written
- **WHEN** `issue-publisher` signs a record and then can't write `--out`
- **THEN** the ledger already holds the record, and the error says where to copy it from

#### Scenario: The publisher's folder inside the ledger
- **WHEN** `--out` is the ledger or a folder inside it
- **THEN** it is refused, and nothing is signed or written

### Requirement: A mid-session revocation SHALL be announced
When a newer list revokes add-ins whose code is loaded, the IDE SHALL show a notice naming them, with a button that closes
the IDE through its normal close and its prompts to save. One notice SHALL show at a time, and add-ins revoked while it is
open SHALL be named in the next. An add-in whose code never loaded SHALL be revoked without a notice.

#### Scenario: A loaded add-in is revoked
- **WHEN** the hourly check revokes a loaded add-in
- **THEN** a notice names it, and its close button asks to save before HexIDE closes

#### Scenario: A second revocation while the notice is open
- **WHEN** another add-in is revoked while the notice is open
- **THEN** a second notice names it once the first is answered

### Requirement: Options SHALL show the revocation check's state
While an add-in package is installed, Options → Add-Ins SHALL say when the revocation list was last checked and which list
is in use, that the last check failed and when one last worked, or that checking is off. It SHALL NOT say a list was
checked when no check has worked. A revoked add-in's page SHALL offer no Activate or Deactivate button.

#### Scenario: Checking that has stopped working
- **WHEN** the last check failed
- **THEN** Options says so, with the time of the last check that worked

#### Scenario: A page open during a revocation
- **WHEN** an add-in is revoked while its Options page is open
- **THEN** the page shows it as Revoked, with no Activate or Deactivate button

### Requirement: A revocation list SHALL stay readable by older builds
The IDE SHALL skip a revocation list's unknown fields and SHALL NOT refuse a list for its `schema` number, so a list that
adds a field still updates every older install. A build from before key revocation applies the rest of such a list and
ignores its key fingerprints, so a malicious publisher's id SHALL be revoked as well as its key.

#### Scenario: A list with a field this build doesn't know
- **WHEN** a validly signed list carries a field this build doesn't know
- **THEN** the list becomes active and its known revocations apply

### Requirement: A revoked key SHALL never be issued
`verify-request` and `issue-publisher` SHALL refuse a request whose key the revocation list names. A rotation away from
a key the list names SHALL be left to a person (exit 3), because that key's signature on the request proves nothing once
it is stolen. `issue-publisher` SHALL issue nothing under an intermediate whose key the list names.

#### Scenario: A request for a revoked key
- **WHEN** the list names the request's key
- **THEN** it is refused, and nothing is signed

#### Scenario: A rotation away from a stolen key
- **WHEN** the list names the key a rotation replaces
- **THEN** it exits 3, and a person checks the account and the published fingerprint

### Requirement: sign-revocations SHALL refuse a list HexIDE would ignore
`sign-revocations` SHALL refuse, before reading the root key, a list that doesn't parse, has a null set, has an issue
time HexIDE can't read, or names a key fingerprint that isn't 64 hex digits. `verify-revocations` SHALL fail such a list
even when its signature verifies.

#### Scenario: A mistyped fingerprint
- **WHEN** a list's key fingerprint is a digit short
- **THEN** `sign-revocations` refuses it, and nothing is signed
