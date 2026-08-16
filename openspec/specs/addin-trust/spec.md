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
Where verification and the consent prompt are separated in time, the IDE SHALL re-verify the package before
loading it and SHALL load only if the result still matches what the user was shown.

The prompt can stay open indefinitely, and the files it describes are on disk the whole time. Without
re-verification, the window between "the user reads the publisher name" and "the code runs" is a window in
which the package can be swapped for a different one that the user never saw.

#### Scenario: A package altered while the prompt is open
- **WHEN** the package changes between the prompt appearing and the user allowing it
- **THEN** re-verification fails and the add-in does not load

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
- **WHEN** developer mode and the unsigned opt-in are both active
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
