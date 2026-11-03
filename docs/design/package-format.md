# Package format — policy decided (#1210)

## Scope and authority

All four policy questions are decided, and this design note is ready to become an OpenSpec change.
This is the document-only deliverable requested by the supervisor’s 2026-10-29 comment and
2026-11-02 and 2026-11-03 review revisions on
[#1210](https://github.com/hexide-io/HexIDE/issues/1210). It makes no new decisions and authorizes no
implementation. It records Jen’s four 2026-11-03 policy decisions; open engineering questions below retain their
alternatives and trade-offs. The issue's stated constraints are distinguished from candidate encodings. No manifest in this document is installable today.

The design can now become `openspec/changes/package-format/`, with proposal, design, tasks,
manifest schema, examples and capability deltas. Keeping this draft under `docs/design/` avoids publishing
unapproved choices as normative contracts. No live specification or archived change is modified.

## Questions for Jen

These four policy questions come from the
[second review of PR #2848](https://github.com/hexide-io/HexIDE/pull/2848#pullrequestreview-5745582142)
(2026-11-03). All four are decided by Jen (Team Desk, 2026-11-03); remaining
engineering questions stay in §§1–9.

1. **Outside publishers’ keys (§3) — decided (Jen, Team Desk, 2026-11-03):** “RD Core doesn't
   get to see the root key, they get a publisher key signed by my root.” An outside publisher such as
   RDCore gets a publisher key signed by HexIDE’s root; there is no distribution root. The root key
   stays with HexIDE. §3 describes the signing route and the remaining revocation engineering question.
2. **Reversing the approved profiles design (§8) — decided (Jen, Team Desk, 2026-11-03), Desk card `q-2848-suite-deps`:**
   a profile may name the suites it needs at exact versions, with each suite’s manifest inside the
   profile. Only the suite publisher’s own signature can authenticate that manifest; a profile author
   cannot introduce a server. This reverses “no remote URLs” and “server configuration cannot be supplied”
   for this purpose only. §8 names the profiles spec the reversal will change.
3. **Mirroring and caching (§3) — decided (Jen, Team Desk, 2026-11-03), Desk card `q-2848-cache`:**
   HexIDE downloads an outside suite only from its publisher, never hosts or mirrors outside artifacts,
   and keeps a per-user cached copy. Cache location and eviction remain engineering questions in §4.
4. **Unsigned language servers and debug adapters in release builds (§3) — decided (Jen, Team Desk, 2026-11-03, 03:18), Desk card `q-2848-devmode`:**
   a release build may run them behind a persisted setting that is off by default. The session must
   be flagged loudly so ordinary developers do not enable it accidentally.
   [#2945 (security)](https://github.com/hexide-io/HexIDE/issues/2945) covers the setting, session
   markers and per-start notice. Developer mode is false outside DEBUG builds and is not this route.

## Motivation and source boundaries

[#1208](https://github.com/hexide-io/HexIDE/issues/1208) calls for one format and installer for all pack
kinds. A suite should bring its related tooling and data together, while an icon pack should use the
same installation path without acquiring code privileges.
[#457](https://github.com/hexide-io/HexIDE/issues/457) supplies the language-server contribution need;
[#459](https://github.com/hexide-io/HexIDE/issues/459) supplies capability disclosure and consent.
The 2026-11-02 review on [PR #2848](https://github.com/hexide-io/HexIDE/pull/2848#pullrequestreview-5743878220)
supplies the consumer needs from #2904 and #2820 and the #1208 language-definition inventory below.
The [second review](https://github.com/hexide-io/HexIDE/pull/2848#pullrequestreview-5745582142)
supplies the further #2904 constraint and policy questions recorded here.
Provider facts attributed to those reviews are reported evidence, not measurements made by this draft.

Repository evidence available for review:

- [Add-in trust](../../openspec/specs/addin-trust/spec.md) already requires a complete inventory,
  offline signature verification, exact-build consent, re-verification before loading, first-party key
  pinning, revocation and compatibility checks before trust. Schema 2 is current in that contract.
- [Add-in system](../../openspec/specs/addin-system/spec.md) owns host APIs, contribution removal,
  lifecycle and dependency isolation. Packaging does not replace those responsibilities.
- [Capability declaration proposal](../../openspec/changes/2026-10-26-addin-capability-declarations/proposal.md)
  records Jen's decisions: five capabilities, no host capabilities for undeclared legacy add-ins,
  declarations for first-party and developer packages, template creation requiring `project.write`,
  and one consent per build. Schema 2 is now recorded in the live trust contract; this draft adds no
  implementation.
- [Theme](../../openspec/specs/theme-packs/spec.md),
  [keymap](../../openspec/specs/keymap-packs/spec.md) and
  [language](../../openspec/specs/language-packs/spec.md) contracts remain authoritative for payloads.
- [Profile bundle proposal](../../openspec/changes/2026-10-25-profiles-supersede-personalities/design.md)
  is work in flight. It proposes directory bundles, restricted data, reserved built-in names and safe
  mode. Package integration must reconcile these explicitly rather than silently superseding them.
- [Foreign-server acquisition](../foreign-language-servers.md) and its fixture distinguish
  publisher-attested digests from digests computed locally. The latter pin tested bytes without
  establishing publisher provenance.

## Constraints already stated in #1210 and #2904

One manifest inventories the whole package. Contributions have distinct data, host-run and code tiers.
Data-only packages can be unsigned or signed, with that state disclosed. Anything HexIDE runs requires
a publisher signature using existing trust and revocation machinery, subject to the existing Debug-only
unsigned-add-in exception and Jen’s release-build exception for language servers and debug adapters:
a persisted setting, off by default, with loud session markers and a per-start notice (#2945). Install/update disclosure lists
what runs; a changed command line requires renewed consent. This does not weaken add-ins' existing
requirement to ask again for every changed build.

Acquisition supports bundled, digest-verified download and detection, with per-platform selection.
Nothing downloaded is extracted or run before the archive digest matches. Resolution layers built-ins,
installed contributions,
then user configuration. An override keeps the contributed id; uninstall preserves it as orphaned.
Schema and minimum host versions are enforced from the outset. External suites live in their providers'
repositories under their licences; the bundled VB6 suite, format, schema and validator originate in this
MIT tree. No provider-specific compiler implementation is required to draft this format.

Jen’s direction on [#2904](https://github.com/hexide-io/HexIDE/issues/2904), as relayed in the
2026-11-03 review, permits Mat’s site to distribute a pre-bundled build under its own licence;
HexIDE’s own releases never bundle non-MIT code. #2904 also proposes a white-label installer that
installs the suite itself. Publisher-only downloads, no hosting or mirroring, and a per-user cached copy follow Jen’s
2026-11-03 decision above, as do outside publisher keys.

Publisher link verification binds domain control to the signing key across packages, not to a manifest
claim. HTTPS well-known proof is checked first; DNS TXT proof is also offered. Checks are dated and
repeated on update. Failure changes link presentation only. Verified links are clickable and show the
registrable domain; unverified links are marked plain text. IDNs use ASCII `xn--` form or mixed-script
warnings. Verification establishes domain control, never endorsement. Download URLs are exempt from
this presentation rule because their bytes have pinned SHA-256 digests.

## Questions for review

### 1. Container, inventory and installation

**Which archive and layout should be used?** ZIP with a root `package.json`, detached trust envelope
and payload directories permits familiar tooling and random access. A tar-based archive preserves Unix
metadata more naturally but adds platform handling. Reusing the add-in directory layout minimizes trust
migration but is not itself a transport archive. None is selected here.

**How are inventory paths and signature metadata represented?** Reuse existing add-in raw-manifest-byte
signing and file digests, or introduce a generic envelope while retaining the verifier. Reuse avoids a
second cryptographic protocol; an envelope separates package metadata from the legacy add-in entry.
Either needs an explicit rule excluding only the manifest/signature envelope from self-inventory, and
must cover every other payload file, including dependencies. Canonical JSON would ease reproducible
signing but changes today's byte-based contract; it is an alternative, not an assumed improvement.

**What are the extraction and transaction rules?** Reject traversal, rooted paths, link escapes,
case-colliding names and duplicate entries, or allow a narrower portable subset of archive features?
A portable subset is simpler; accepting platform features is more flexible. Size/file-count/expanded-byte
limits, staging, atomic activation, rollback, repair and interrupted uninstall need
explicit contracts. Immutable version directories ease rollback but consume more storage than replacing
one installation. No download or install hook is implied.

The contained-read precedent is [ProfileFileReader](../../IDE/HexIDE/IDE/Profiles/ProfileFileReader.cs)
(#2621): opens do not follow links, only regular files are accepted, and `MaximumBytes` caps each read
at 1 MiB. [ProfilePaths](../../IDE/HexIDE/IDE/Profiles/ProfilePaths.cs) rejects Windows device names on
every platform. Extraction needs equivalent containment for each entry, including concurrent replacement,
with separately chosen per-entry, count and total expansion limits; the profile read cap is not an archive
limit. Archive bytes must match their digest before any extraction. On Unix the installer sets the
executable bit after verification rather than trusting archive metadata. Which inventoried entries may
receive it, and how is that permission signature-bound?

### 2. Contribution inventory and tiers

The following is a candidate checklist, not an approved exhaustive vocabulary. Which kinds from the
epic are missing, and which need separate manifest kinds rather than facets of a suite?

| Candidate contribution | Candidate tier | Consent consequence / unresolved boundary |
|---|---|---|
| Language definitions: extensions, language ids, file-name patterns, root markers (#1206), syntax colouring | Data | Routes files to the suite’s server; declarative syntax only, with conflicts and parser limits to settle. |
| Theme, keymap, translations, icons | Data | Signature state disclosed; payload parsers must exclude scripts and executable markup. |
| Profile, settings defaults, menu/toolbar placements | Data | Existing host ids plus exact-version suite dependencies with embedded publisher-signed manifests (§8); other profile restrictions still apply. |
| Templates, snippets, project/property schemas | Data | Declarative content only; materializing templates also needs the recorded project-write policy. |
| Language-server launch, debug-adapter launch, build/run tool | Host-run | Signed executable identity, argv, environment and acquisition shown before launch. |
| Provider-backed property read/apply | Host-run | Reuses a consented provider process; must disclose project reads/writes. |
| Managed add-in, custom editor/control/designer implementation | Code | Existing add-in trust, consent and lifecycle, with no in-process sandbox claim. |
| Suite | Aggregate | Contains typed contributions; it cannot declare itself data to downgrade executable children. |

**Should tiers be derived solely from contribution kinds, or also validated against payload features?**
Derivation gives predictable disclosure; payload checks catch scripts hidden inside nominal data formats.
A publisher-selected tier alone cannot safely establish what will run. Unknown kinds could reject the
package entirely or leave known data usable with an explicit unsupported-contribution state; partial
activation risks presenting an incomplete suite. How are dependencies and mixed-tier activation handled?

### 3. Trust, consent and updates

**How does the shared installer reuse add-in trust without losing its stronger rules?** A common verified
package record can feed the existing loader; adapters around existing signed add-in directories reduce
migration work but prolong two manifest formats. Existing first-party pinning, known-revocation refusal,
offline chain verification, retrieval fail-open policy and developer opt-in must be accounted for.
Jen’s final 2026-11-03 decision permits unsigned language servers and debug adapters in release builds
behind a persisted setting that is off by default. This is a separate route: `DeveloperModeService.IsEnabled`
is false outside DEBUG, and the existing add-in exception also requires a persisted load-unsigned setting.
[#2945 (security)](https://github.com/hexide-io/HexIDE/issues/2945) owns the release setting, loud session
markers and per-start notice. Without that opt-in, publisher signatures remain required.
**How are the session markers and per-start notice worded and placed?** Title-bar and status-bar
markers are candidate persistent reminders; their visibility and lifecycle, and the notice’s wording
and placement, remain engineering questions for #2945. The exception does not waive compatibility checks.

**Outside publisher signing route — decided by Jen, 2026-11-03.** HexIDE’s root signs the
outside publisher’s public-key certificate, not a distribution root. The publisher retains its own
private key and uses it to sign the package manifest, binding the full payload inventory and acquisition
metadata to that publisher. Verification checks that signature and its chain to HexIDE’s baked root;
RDCore never receives the root private key. There is no distribution root, and an outside publisher’s
certificate does not grant first-party identity or the built-in suite’s key pinning.

**How is a publisher key revoked in the shared installer?** The existing
[add-in trust contract](../../openspec/specs/addin-trust/spec.md) consults a signed revocation list before
consent and refuses known revoked publishers, builds or intermediate signers; retrieval failure alone
does not revoke a key. Should package publishers reuse its publisher identity records, or should the
shared format add explicit certificate/key fingerprints to those records? Reuse avoids a second
revocation model; fingerprints distinguish rotated or compromised keys but need identity and migration
rules. The record encoding, revocation-list signing authority and key-rotation mapping remain open
engineering questions; Jen’s issuance decision does not settle them.

**What licences and origins are disclosed before acquisition?** The review requests an SPDX licence per
artifact, including adopter-bundled artifacts, shown before download or installation. It reports GPLv3
for RDCore’s server, runtime, parser, diagnostics and CLI (`LICENSE-GPLv3.md`); which precise SPDX expression applies to each release remains
for the publisher to supply. Should artifact licences be mandatory strings or structured expressions with
notice paths? Strings are compact; structured records can expose bundled components but need validation.
Jen decided on 2026-11-03 that HexIDE downloads outside suites only from their publishers, never
hosts or mirrors outside artifacts, and keeps a per-user cached copy. This records the acquisition
policy, without asserting a separate legal conclusion about redistribution. Cache mechanics remain in §4.

**What is the consent identity for acquired and detected executables?** Manifest hash plus artifact
identity makes replacement visible; manifest hash alone works only if it binds every executable byte and
launch input. Detection can find mutable binaries outside package inventory. Hash and recheck them, or
bind a validated installation/version identity with a documented weaker guarantee? Neither can silently
be treated as a bundled, inventoried binary.

**When is consent collected and invalidated?** One package-build decision can list all commands, arguments,
environment, source access and writes, while per-contribution activation exposes more controls but must
not contradict #459's one-consent-per-build decision for add-ins. #2820 (requested by Jen on 10-30)
and #2904 require no download without a click: the offer’s consent covers download and first launch
together, without another dialog at first activation for that same decision. How is this recorded across
restart or a deferred launch? A persisted build/artifact grant can cover later activation; a persisted
pending-activation record can carry the accepted offer through first launch before becoming the grant.
The latter makes the transition explicit but needs crash recovery; neither may add a second prompt for
the same accepted decision. Declining must leave acquisition unstarted. Changes to argv
must ask again; executable, environment, capability and provider
changes need a reviewed invalidation matrix. Verify the same bytes after the dialog and before execution.

### 4. Acquisition and platforms

**How is an outside suite pre-bundled by an adopter trusted?** Bundled transport does not confer
first-party identity or inherit the built-in VB6 suite’s key pinning. The reviewer recommends verifying
the outside publisher’s signature and full inventory exactly as for a download, skipping only transport.
The publisher key chains to HexIDE’s root as decided in §3; a distribution cannot substitute its own
root. When are verification and licence disclosure performed if a white-label installer installs the
suite itself? Bundling may not silently grant first-party trust. The candidate
licence field below covers bundled as well as downloaded artifacts.

**Which platform key grammar is public?** .NET runtime identifiers reuse tooling vocabulary; explicit
OS/architecture/libc objects make matching clearer but add a separate model. Specify Windows, macOS,
Linux glibc/musl, unsupported-platform reporting and ambiguous matches before implementation. No numeric
or architecture fallback should be inferred from examples.

**How are runtime prerequisites declared, detected and presented?** A platform match does not establish
that a server can start. The review records RDCore’s server as framework-dependent on net10.0;
HexIDE’s [publish workflow](../../.github/workflows/publish.yml) also ships self-contained builds, whose
users may have no separate .NET runtime. The fixture’s vscode-json-language-server needs Node. Should
artifacts declare a runtime kind (`none`, `.NET` with a minimum version, or Node with a version range), or
reference a reusable prerequisite record? Inline declarations are simple; shared records avoid duplication
but complicate resolution. How can installed runtimes and compatible versions be detected without running
anything: known installation metadata, user-supplied locations, or platform registries? Metadata can be
stale; explicit paths need validation. The reviewer recommends checking before consent, naming what to
install when missing, and never offering a download that cannot start. Should the missing-runtime state
block the offer or present prerequisite instructions and a retry? Automatic runtime installation would
require a separate acquisition and consent design; none is authorized here.

**Where is the cache?** A content-addressed directory beneath the active user-data root supports isolation
and deduplication; an OS cache directory follows platform cleanup conventions but complicates profile
isolation. The fixture's repository-local `artifacts/foreign-lsp/` is evidence for tests, not a chosen
installed-product cache. Decide ownership, permissions, locking, eviction, retention and offline operation.

**Can an installation be writable?** A verified read-only content-addressed tree makes tampering
rechecks possible, but the review reports RDCore writes Config, Extensions, Logs and Symbols inside
its install folder. It cannot run there read-only, and checking the entire tree would fail after first
start. Options are (a) verify once at install and accept later writes, sacrificing continuing integrity;
(b) retain a verified read-only tree plus separate writable state where the server supports relocation,
which preserves integrity but is not universally supported; or (c) recheck only manifest-listed files,
which tolerates new state but leaves unlisted executable extensions to address. Which files may change,
and how do repair, consent identity and updates treat those changes? No option is selected.

**Which download and redirect policy is allowed?** Downloads are HTTPS only. The review measured a
GitHub release download for rumdl v0.2.79 returning `302` to `release-assets.githubusercontent.com`;
an exact-host rule would refuse GitHub release assets. Should a signed manifest enumerate allowed asset
hosts, or should the installer recognize a bounded publisher-to-asset-host mapping? Enumeration is explicit
but changes with hosting; a maintained mapping is convenient but adds policy maintenance. Decide redirect
limits, HTTPS enforcement on every hop and whether consent names the manifest host, the final host, or
both. One host is simpler; both disclose the actual source without hiding the publisher-facing URL.
Any pre-consent discovery must not fetch the artifact body, preserving the no-download-without-click rule.
The digest still has to match before extraction, regardless of redirect destination.

**What is hashed?** Hashing the downloaded archive pins transport bytes; an extracted-file inventory
also binds what runs. Both cover more than either alone. Redirect policy, archive extraction, partial
transfers and revalidation of cached artifacts need tests. Record digest provenance as publisher-attested
with its checksum source, or locally computed with who computed it and what was tested. Never relabel a
computed hash as publisher-attested. Define how acquisition metadata itself is signature-bound.

**How is detection expressed?** Restricted declarative PATH/known-location probes avoid executing a
probe command; a version command gives stronger version information but itself needs consent before
running. Environment overrides and explicit user paths can preserve today's precedence, or be modeled
as separate user configuration. What happens when an acquired artifact is unavailable offline?

### 5. Layering and identity

**How are ids scoped and collisions resolved?** Publisher/package/contribution ids make ownership clear;
global contribution ids simplify existing configuration joins. `hexide.vb6` is already
`LanguageServerDefaults.BundledVb6Id`, so using it as a package id too requires typed/scoped references
that distinguish a package from its contained server; renaming one avoids ambiguity but needs migration.
Decide duplicate installed ids, same-priority
collisions and whether first-party ids are reserved. User overrides retain the target id in either scheme.

**Which ids become directory names?** The sketch’s `minLength: 1` is not path validation. Should package
ids reuse profiles’ lower-case letters/digits/hyphens grammar and cross-platform device-name refusal
(#2621), or retain dotted logical ids and encode/hash them for storage? Reuse reduces validation rules
but rejects the dotted examples; encoding preserves logical ids but needs collision-free mapping.
Specify whether install/version directories derive from package ids, and whether cache directories derive
only from digests. Contribution ids should not implicitly become path components. The examples retain
dotted logical ids pending this decision, not approval to join them directly into paths.

**How do overrides merge?** Whole-entry replacement is predictable; field-level merge allows a command
argument change without copying the entry but needs explicit deletion and array semantics. Uninstall
keeps an override marked orphaned. Does it remain executable as an explicit user command, or become
inactive until rebound? Automatic execution could defeat uninstall; disabling it changes saved user
behaviour. Decide reinstall rebinding and update conflicts explicitly.

### 6. Project property providers

**Which editor schema is sufficient?** A restricted JSON Schema subset can map strings, booleans,
numbers, enums and paths to existing editors; a dedicated grid schema is smaller but duplicates
validation vocabulary. Categories, localized labels, ranges, read-only state, defaults, nullable/missing
values and unknown enum values require review. A property schema does not grant arbitrary widgets.

**What are the custom request names and wire shapes?** Candidates are
`hexide/projectProperties` and `hexide/applyProjectProperties`, versus one namespaced request with an
operation field. Two methods separate read and write capabilities; one reduces the public method count.
These are proposed custom LSP extensions, not standard methods or observed provider behaviour.
Illustrative shapes for the two-method option:

```json
{"method":"hexide/projectProperties","params":{"projectUri":"file:///workspace/demo.proj","schemaId":"example.properties"}}
{"result":{"revision":"opaque-1","values":{"outputName":"Demo","optimize":false}}}
{"method":"hexide/applyProjectProperties","params":{"projectUri":"file:///workspace/demo.proj","schemaId":"example.properties","revision":"opaque-1","changes":{"optimize":true}}}
{"result":{"edit":{"documentChanges":[]},"revision":"opaque-2"}}
```

**Who persists the changes?** Returning a versioned `WorkspaceEdit` lets the host validate targets,
read-only state and unsaved buffers, then apply/save through project services. Provider-owned writes
support opaque formats but need a separate transaction/reload contract and stale-buffer protection.
Decide cancellation, stale revisions, errors, atomicity, URI authorization, schema capability negotiation
and how successful edits reach the actual project file. The empty edit above does not prove a write.

**What is the simple text mapping?** A map from property id to JSON pointer or INI section/key avoids
starting a server. Format-specific adapters preserve comments, encoding, duplicate keys and line endings
more reliably than general regex substitution, at the cost of more adapters. Decide supported formats,
missing-value creation/removal and round-trip guarantees; opaque archives cannot be treated as text.

### 7. Versioning and compatibility

**Should the generic manifest reuse `schema` and `minHexideVersion`, or adopt new names?** Reuse aligns
with #310's enforced add-in contract; new names separate package schema evolution but require a migration
adapter. Numeric host versions match today's add-in comparison; semantic versions admit prereleases but
change ordering. A newer unsupported schema or unmet minimum must be reported as incompatible before
activation, including developer mode. Decide unknown fields/kinds, older schema migration, package version
ordering, downgrade policy and simultaneous versions. Coordinate the proposed add-in schema 2 before
reserving a generic number. No placeholder version here is a supported release promise.

### 8. Ownership and migration

**How do existing loose packs migrate?** Import them into generated packages, discover them as legacy
user overrides, or support a transition period with both readers? Import unifies validation but can erase
provenance; continued discovery avoids rewriting user files but needs deterministic collision rules.
Reuse payload parsers and retain live theme switching, designer theme isolation, keymap command validation,
locale fallback, placeholder checks and shipped translation parity.

**How does the profile proposal fit?** A package could contain a `profile.json` contribution without
changing its payload rules; alternatively the generic manifest could replace that file. Containment
preserves the proposed selector and safe mode, whereas replacement reduces nesting but reopens its
reviewed interfaces. Decide directory-path selection, reserved names and safe mode before drafting deltas.

**Profile suite dependencies — decided by Jen, 2026-11-03.** A profile may name the suites it
needs at exact versions, carrying each suite’s manifest inside its bundle, read through
`ProfileFileReader` containment. No index is needed at first start; snapshots still need update and
compatibility validation. A profile is data tier and cannot vouch for a host-run suite: the embedded
manifest is accepted only with the suite publisher’s own signature and verified inventory. An unsigned
copy, or one signed only by the profile’s author rather than the suite publisher, is refused; the
profile’s signature cannot authorize a launch. The unsigned-server exception in §3 does not let a profile
author vouch for an embedded suite manifest.

This decision reverses the approved profile design’s exclusions “no remote URLs” and “server
configuration cannot be supplied” for exact-version suite dependencies only. The future delta must
change the [profiles spec](../../openspec/specs/profiles/spec.md) and reconcile the
[profile change’s design](../../openspec/changes/2026-10-25-profiles-supersede-personalities/design.md),
preserving containment and safe-mode recovery. Listing a suite does not itself authorize its download
or launch (see §3). **How are already-installed versions, refusal, offline starts and partial
availability represented without silently substituting another version?** Reusing an exact installed
version avoids acquisition; an unavailable version needs an explicit missing-suite state rather than
an automatic fallback. The first-start offer and its recovery details still need engineering design.

**When do external suites move out?** The format/schema/validator and bundled suite are authored here
under MIT. External providers own their repositories, releases and licence notices; installing an external
tool does not import its implementation into this tree. Establish which shared host seams must ship first,
and a migration sequence that leaves the bundled VB6 experience available throughout.

### 9. Proven-domain links

**What exactly is bound to a key?** Candidate proof content is a versioned document listing key
fingerprints; one fingerprint per proof is simpler but makes rotation and concurrent keys harder.
Choose the fingerprint algorithm/encoding, TXT label and record grammar, and well-known JSON shape.
The issue's `_hexide-publisher` and `/.well-known/hexide-publisher.json` are starting points, not a complete
protocol. HTTPS is tried first. DNS TXT needs either a licence-recorded resolver dependency or an explicit
DNS-over-HTTPS privacy policy; which should be offered? No new dependency is introduced by this draft.

**What is the domain boundary?** Exact-host verification avoids granting sibling subdomains; registrable-
domain verification reduces repeated proofs but broadens authority. Showing a registrable domain requires
an authoritative public-suffix source, update policy and licence review. Decide subdomains, shared hosting,
ports, redirects, TLS errors, timeouts, key rotation/revocation, stale cached results and offline display.
HTTPS redirects must not silently prove a different domain. Timestamp checks and repeat on update in all
options. Proof-cache scope must bind both key and domain, never package display name.

**How are non-HTTPS links handled?** `mailto:` has a domain but no ordinary web destination; verify its
address domain under the same key or leave it unverified. Decide other schemes and safe rendering.
Unsigned data packages cannot borrow another publisher's proof: without an authenticated key, their links
remain unverified. Download digests bind bytes, not domain ownership or publisher endorsement; their
exemption must not make arbitrary download URLs clickable as verified support links.

## Illustrative manifest schema and three worked examples

This deliberately permissive JSON Schema sketch exposes the proposed shared fields without choosing
kind-specific payloads, archive layout, signature envelope or version grammar. `payload` remains open
except for illustrative acquisition licence fields until the above questions are settled. It is not the
acceptance validator, and passing it establishes neither trust nor path safety. The `id` grammar remains open in §5; the artifact `license` sketch below
is an SPDX expression disclosed before download or bundled installation (§3), with validation still to
decide. Should SHA-256 accept mixed-case hexadecimal and normalize on read (matching the fixture’s case-insensitive comparison),
or require lower-case manifests? Normalization accommodates publisher checksum spelling; lower-case-only
simplifies canonical display but requires publishers to convert it. The sketch accepts both cases without
selecting a normalization policy. Runtime and redirect metadata likewise remain questions in §4.
Field names and version 1 below are illustrative choices only.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "HexIDE package review sketch",
  "type": "object",
  "required": ["schema", "id", "version", "minHexideVersion", "files", "contributions"],
  "properties": {
    "schema": {"type": "integer", "minimum": 1},
    "id": {"type": "string", "minLength": 1},
    "version": {"type": "string", "minLength": 1},
    "minHexideVersion": {"type": "string", "minLength": 1},
    "publisher": {"type": "string"},
    "links": {"type": "object", "additionalProperties": {"type": "string"}},
    "files": {"type": "array", "items": {
      "type": "object", "required": ["path", "sha256"],
      "properties": {"path": {"type": "string"}, "sha256": {"type": "string", "pattern": "^[a-fA-F0-9]{64}$"}}
    }},
    "contributions": {"type": "array", "items": {
      "type": "object", "required": ["id", "kind", "payload"],
      "properties": {"id": {"type": "string"}, "kind": {"type": "string"}, "payload": {"type": "object", "properties": {
        "acquisition": {"type": "object", "properties": {
          "license": {"type": "string", "minLength": 1},
          "platforms": {"type": "object", "additionalProperties": {
            "type": "object", "properties": {"license": {"type": "string", "minLength": 1}}
          }}
        }}
      }}}
    }}
  }
}
```

Examples use empty inventories and fictional paths to keep them visibly non-installable. Real packages
must enumerate and hash every payload, bind acquisition metadata and carry any required signature and
chain. The repeated `0.0` is no claim about an actual compatible host version.

The acquisition-level `license` illustrates bundled-artifact disclosure; per-platform `license`
illustrates downloaded-artifact disclosure. Whether these share one artifact record or use route-specific
fields remains open; neither field is a package-wide substitute for component notices.

Built-in VB6 suite: a signed, first-party-key-pinned aggregate, with existing pre-consent rules. Does it
reference separately inventoried built-in resources or package copies of those resources?

```json
{
  "schema": 1, "id": "hexide.vb6", "version": "0.0", "minHexideVersion": "0.0",
  "publisher": "hexide", "files": [],
  "contributions": [
    {"id":"vb6.profile","kind":"profile","payload":{"path":"profiles/vb6/profile.json"}},
    {"id":"vb6.server","kind":"languageServer","payload":{"acquisition":{"route":"bundled","path":"bin/vb6-server","license":"MIT"},"arguments":[]}},
    {"id":"vb6.theme","kind":"theme","payload":{"path":"themes/classic.json"}}
  ]
}
```

Data-only icon pack: no process or assembly contribution. Signature state is unsigned unless a verified
signature is supplied separately; no signing identity is established by a publisher name.

```json
{
  "schema": 1, "id": "example.icons", "version": "0.0", "minHexideVersion": "0.0",
  "files": [], "links":{"homepage":"https://example.com/icons"},
  "contributions":[{"id":"example.icons.default","kind":"icons","payload":{"path":"icons/index.json"}}]
}
```

External suite: the shared artifact models RDCore’s real shape reported in the review: one build serves
LSP and DAP with `--dap`, and its server is framework-dependent on net10.0. All ids, URLs, versions and
hashes below remain fictional; they are not an RDCore installation manifest. Candidate `runtime` fields
illustrate the prerequisite question, and `license` illustrates per-artifact pre-download disclosure.
A signed manifest binds the download choice and both process commands, plus the data-only
schema. Platform spellings are candidates. Zero digests are placeholders, not verified downloads; computed
provenance here is fictional and deliberately makes no publisher-attested claim.

```json
{
  "schema": 1, "id": "example.suite", "version": "0.0", "minHexideVersion": "0.0",
  "publisher": "example", "files": [],
  "links":{"documentation":"https://example.com/docs"},
  "contributions":[
    {"id":"example.server","kind":"languageServer","payload":{
      "acquisition":{"route":"download","platforms":{
        "win-x64":{"url":"https://example.com/server-win.zip","license":"GPL-3.0-only","runtime":{"kind":"dotnet","minimumVersion":"10.0"},"sha256":"0000000000000000000000000000000000000000000000000000000000000000","provenance":{"kind":"computed","by":"example-review","tested":"illustration only"}},
        "linux-x64":{"url":"https://example.com/server-linux.zip","license":"GPL-3.0-only","runtime":{"kind":"dotnet","minimumVersion":"10.0"},"sha256":"0000000000000000000000000000000000000000000000000000000000000000","provenance":{"kind":"computed","by":"example-review","tested":"illustration only"}}
      }},"entry":"bin/provider","arguments":[]}},
    {"id":"example.debug","kind":"debugAdapter","payload":{"artifactFrom":"example.server","entry":"bin/provider","arguments":["--dap"]}},
    {"id":"example.properties","kind":"projectProperties","payload":{"path":"schemas/project.json","provider":"example.server"}}
  ]
}
```

## Planned OpenSpec handoff and validation

With policy decisions recorded, create `package-format` with a proposal explaining the above motivation,
a design recording actual choices and rejected alternatives, and tasks that distinguish design acceptance
from implementation. Add these deltas without describing them as shipped support:

| Capability | Intended delta boundary |
|---|---|
| `packages` (new) | Container, inventory, tiers, acquisition, layering, versioning, property providers and domain proofs. |
| `addin-system` | Add-ins as package contributions; preserve host API and lifecycle contracts. |
| `addin-trust` | Shared signing/revocation, link presentation and install/update consent; preserve exact-build guarantees. |
| `theme-packs`, `keymap-packs`, `language-packs` | Package discovery/migration while preserving existing payload and application semantics. |
| `profiles` (currently proposed) | Add exact-version suite dependencies and first-start install offers; reverse “no remote URLs” and “server configuration cannot be supplied” for this purpose only, with containment, selectors, precedence and recovery preserved. |

No implementation slice begins in this run. Future tests should prove rejection before execution,
complete inventories, atomic installs, tier-derived consent, changed-command re-consent, all acquisition
routes, digest provenance and cache tampering, orphan retention, compatibility refusal, property-file
round trips and domain-proof presentation. Property tests must assert actual persisted bytes and stale
revision rejection. Network proofs need deterministic fake HTTPS/DNS responses, including redirects,
expiry and offline cases; skipped Windows tests cannot establish Linux helper behaviour. Each changed
behaviour test needs a pre-change failure. A later live check must inspect install/update disclosure,
unverified-link non-clickability, ASCII domain display and incompatible/orphaned states.

This draft changes no running behaviour, UI strings, feature status or language coverage. It introduces
no VB6 measurement and changes no oracle page. `MISSING_FEATURES.md` and `MISSING_LANGUAGE.md`, including
the latter's counts, remain unchanged. Remaining engineering questions and the normative OpenSpec/schema handoff remain with the supervisor.
