# Package format design

## Authority and scope

This is a design handoff, not approval to implement unresolved engineering choices. Policy authority is
recorded in proposal.md and the [source note](../../../docs/design/package-format.md). Normative deltas
express intended observable behaviour; the schema and examples below remain non-installable sketches.
The format, schema, validator and bundled suite are authored here under MIT. External suites remain in
their publishers' repositories under their licences. HexIDE releases never bundle non-MIT code; an
adopter may distribute a pre-bundled outside suite under its own licence (#2904).

## Choices and rejected policy alternatives

- **Publisher keys:** the root signs the publisher's public-key certificate; the publisher signs the
  manifest with its private key, binding inventory and acquisition metadata. Distribution roots and
  sharing HexIDE's root private key are rejected by Jen's issuance decision. Outside certificates
  never confer first-party status. Revocation record encoding and key rotation remain engineering work.
- **Profile dependencies:** carry exact-version, suite-publisher-signed manifests through contained
  profile reads. Index-only resolution is rejected for this handoff: first start needs no index.
  Profile-author signatures cannot authorize a suite launch. This narrowly reverses the exclusions in
  the live profiles spec's bundle-loading contract and the earlier profile design; all other profile
  data restrictions, selectors, precedence and recovery survive.
- **Acquisition ownership:** publisher-only downloads with a per-user cache replace the alternative
  of HexIDE hosting or mirroring outside artifacts. This is policy, not a legal conclusion about
  redistribution. Cache location, eviction and writable-state handling remain engineering questions.
- **Unsigned servers:** a separate persisted release setting, default off, implements the decision
  tracked by #2945. Debug-only developer mode cannot provide it. Debug-only exceptions and mandatory
  signing without this release opt-in were rejected for language servers and debug adapters. Unsigned
  add-ins retain their existing Debug-only rules. Compatibility and embedded-manifest authentication
  are not waived. Session markers and a per-start notice are required; wording and placement are open.

## Engineering acceptance boundary

The alternatives below were not decided by the policy answers. They remain explicit acceptance tasks,
not silent selections of archive format, public field names, wire methods or numeric limits. The spec
contracts constrain their outcomes without promoting example encodings to a public protocol. In
particular, the proposed property request pair and empty WorkspaceEdit example do not establish a
persistence implementation. Review must select and validate the schema, examples and wire shapes before
any implementation slice starts. No archived design is edited by this handoff.

## Accepted choices (supervisor, 2026-11-15)

This section records the engineering choices the acceptance tasks asked for. Where a choice below conflicts
with an alternative described in "Questions for review", this section wins. Questions not answered here stay
open, and their tasks stay unticked: §6 (project property providers), §9 (domain proofs) and the
profiles reconciliation in §8. No implementation slice may depend on an open question.

What these choices unblock:
- **Now:** #2945 (unsigned servers, the release opt-in) and a local-file install for #1208.
- **Downloads (#2820, #2904):** also wait for a default revocation-list URL (A3). #2904 also waits for §8's
  profiles reconciliation.

The trust choices in A1, A3, A4 and A5 had a single-opinion Fable review on 2026-11-15 (A4 in a second
pass). Its twelve
findings are folded in. Two of them were claims about the code on main, and both were checked before
being relied on.

### A1. Container, inventory and installation (§1)

- **Archive:** ZIP, file extension `.hexpkg`. Only *stored* and *deflate* entries are accepted. Encrypted,
  multi-disk and spanned archives are refused. ZIP64 is accepted, so size limits are enforced from the
  measured expansion and not from the headers' claims.
- **Layout:** the archive root holds the signing envelope and the payload. The envelope is exactly six root
  files: `package.json` (the manifest), `package.sig`, `publisher.json`, `publisher.sig`,
  `intermediate.json` and `intermediate.sig`. These are compared case-insensitively.
- **Signing:** this reuses today's add-in protocol unchanged. Signatures are over the raw bytes on disk,
  with the root → intermediate → publisher → manifest chain checked by the same code, generalised over the
  manifest's file name. Canonical JSON is rejected, so there's still one cryptographic protocol.
- **Inventory:** the manifest's `files` must list every payload file. The six envelope files are the only
  exclusion, and only at the root. The verifier requires set equality: an unlisted file, a listed file
  that is missing, or a duplicate entry refuses the package. A directory holding no listed file is
  refused too.
- **Add-ins inside a package:** an `addin` contribution names a payload directory holding today's add-in
  layout, minus its own envelope. The package signature covers it. A nested `addin.sig` chain inside a
  package is refused rather than evaluated twice. Existing signed add-in directories keep loading through
  today's loader during the transition (A7).
- **Portable extraction subset.** Only regular files and directories are accepted. Every entry is refused
  if:
  - it is a symbolic link, hard link or device;
  - it has an absolute or rooted path, a drive letter, a `..` segment or a backslash in its name;
  - it has an empty segment, or a segment ending in a dot or a space;
  - any segment is a Windows device name (the same check `ProfilePaths` makes, #2621);
  - any character is invalid in a Windows file name;
  - its name, NFC-normalised and compared case-insensitively, duplicates another entry.

  Archive mode bits are ignored.
- **Limits:**
  - 20,000 entries;
  - 256 MiB expanded per entry;
  - 1 GiB expanded in total;
  - a compression ratio of 100:1 per entry;
  - 240 characters per relative path, and 32 levels of depth.

  The limits are enforced while expanding, not trusted from the central directory. A later schema may
  raise them, never this one silently.
- **Entry names:** in schema 1, every entry name is ASCII only, compared ordinal-ignore-case. Entries are
  enumerated from the central directory alone, and digests are computed from the extracted files, so
  local-header differences can't change what's verified.
- **Order of checks:**
  - A downloaded archive's SHA-256 must match the signed manifest before anything is extracted.
  - A local `.hexpkg` has no prior digest. Its six envelope entries are read into memory first, and the
    signature, chain and revocation are verified there. The inventory's names are also checked against the
    central directory before any byte is written to disk.
  - In both cases, nothing is activated, launched or loaded until the signature, chain, revocation and
    inventory all pass. Leftover `.staging` directories are deleted at start.
- **Transactions:**
  - Install into `<packages>/.staging/<random>`, verify, then rename atomically to
    `<packages>/<package-dir>/<version>/`. Version directories are immutable.
  - The active version is a pointer file written with `AtomicFileWriter`.
  - The previous version is kept until the next successful start with the new one, and then removed.
    Rolling back is offered, never automatic. It re-runs verification, revocation and the consent
    lookup, so a crash can't silently reactivate an older, possibly vulnerable build.
  - Uninstall writes the pointer's removal first, then deletes, and an interrupted uninstall completes at
    the next start.
  - Repair re-verifies the inventory. A mismatch marks the installation broken and offers reinstalling
    from the cache.
  - No install hook or script runs.
- **Executable bit:** on Unix it is set after verification, and only on inventory entries that carry
  `"executable": true`. That flag is in the signed manifest, so the permission is signature-bound.
- **Data-tier packages can't carry executables.** In a package whose tier is data, which may be unsigned,
  these refuse the package:
  - an inventory entry that isn't a file referenced by one of its contributions and accepted by that
    contribution's parser;
  - an entry marked `"executable": true`.

  Detection (A4) never searches the packages directory or the cache. So a "theme" can't drop a binary at
  a predictable path for a probe or an override to find.

### A2. Contribution vocabulary, tiers and activation (§2)

- **Kinds:** schema 1 has a closed set of contribution kinds, each with a fixed tier.

  | Tier | Kinds |
  |---|---|
  | Data | `languageDefinition`, `theme`, `keymap`, `translation`, `icons`, `profile`, `template`, `snippets`, `projectPropertySchema` |
  | Host-run | `languageServer`, `debugAdapter`, `tool` |
  | Code | `addin` |

  Menu and toolbar placements, settings defaults and property providers wait for later schemas.
- **A package's tier** is the highest tier among its contributions, derived only from their kinds. A
  publisher-declared tier field isn't accepted. A "suite" is a package with several contributions, not a
  kind, so it can't declare itself data.
- **Payload validation:** every data payload is read by its kind's existing strict parser, which already
  refuses scripts and executable markup. A payload that fails its parser refuses the package. It never
  changes the tier.
- **Unknown kinds:** an unknown kind makes the whole package *incompatible*, naming the kind. There is no
  partial activation of a package build. After installation, the user may disable individual
  contributions, and that is a user setting, not a partially accepted package.
- **Dependencies:** schema 1 has none, except a profile's exact-version suite dependencies (Jen,
  q-2848-suite-deps).

### A3. Trust, revocation, consent and invalidation (§3)

- **Revocation:** reuse today's signed revocation list and identity records, and extend them with
  optional publisher key fingerprints, so one compromised key can be revoked without revoking the
  publisher. A fingerprint is the SHA-256 of the key's SubjectPublicKeyInfo DER, in lower-case hex. The
  list's signing authority is unchanged.
- **Fetching the revocation list:**
  - Before a newly acquired host-run or code build gets its first grant, the host must attempt a fresh
    fetch of the revocation list. If the fetch fails, the offer says so plainly ("revocation status could
    not be checked") and doesn't pass silently.
  - Builds already granted keep today's fail-open rule on a failed fetch.
  - On main, `SettingsDefaults.RevocationListUrl` is `null` (checked 2026-11-15), so a default install
    only ever sees the list bundled with its release. **No download slice (#2820, #2904) ships until a
    default list URL does.** That is filed as its own security issue, with the cache's torn-write fix.
- **Key rotation:** a publisher may hold more than one valid publisher record, each signed by the
  intermediate. Packages signed by an old key stay valid until that key's fingerprint is revoked.
  Schema 1 adds no expiry.
- **Consent identity:** for bundled and downloaded artifacts, the identity is the package id, the version
  and the manifest hash. The manifest binds every inventory digest, every archive digest, and every argv,
  environment, working-directory and acquisition field, so it binds every executable byte and every
  launch input.
  - The working directory defaults to the immutable install directory, and is shown in the dialog.
  - Schema 1 allows no variable expansion except a closed set the host defines: `${installDir}` and
    `${userDataDir}` anywhere, and `${workspaceFolder}` only in argv after position 0. It is refused in
    the command, the working directory and environment values, so a hostile workspace can't become code
    through `NODE_OPTIONS` or the working directory. The dialog shows `${workspaceFolder}` unexpanded, with
    a note that it varies per workspace.
  - An artifact that runs on a runtime (`dotnet`, `node`) has its runtime resolved to an absolute path at
    consent. That runtime is treated as a detected executable: its path and SHA-256 are in the consent
    identity and rechecked before each launch, so a runtime planted on PATH after consent can't stand
    in for it. A detected executable, outside any
  inventory, adds its absolute path and its SHA-256 to the identity. Both are rechecked immediately before
  each launch, and any difference asks again. A detected binary is never presented as inventoried.
- **When consent is asked:** one consent per package build covers all of its host-run and code
  contributions. That matches #459's one consent per build. It lists:
  - commands, argv and environment;
  - the files read and written;
  - the download hosts (A4);
  - each artifact's licence.
- **The offer flow** (#2820, #2904):
  - The offer is rendered from manifest bytes that have already passed the signature and revocation
    checks.
  - Accepting it writes a pending-activation record (package id, version, manifest hash, time)
    atomically, before any download starts.
  - The grant is created only if the installed manifest's hash equals the recorded one.
  - Only the host's own accept action writes a pending-activation record. Anything an installer or
    another process places beside a cached archive is an unaccepted offer, and always goes through the
    dialog (A4).
  - When the install verifies, that record becomes the grant, with no second prompt.
  - A pending record found at start whose install never completed asks again rather than resuming a
    download on an old click.
  - Declining starts nothing.
- **Invalidation:** consent is asked again when any of these happens:
  - the manifest hash changes, for any byte of the manifest;
  - a detected executable's path or hash changes;
  - the publisher or its key is revoked, which refuses outright.

  An override's hash is part of the launch identity, so an override written outside the settings UI asks
  once (A5).
- **Re-verification before every launch:**
  - Before every launch, not only the first, the host checks the whole tree of the launched artifact: set
    equality and every file's digest, plus its manifest. That's what the add-in loader does on each load
    (#3888). So a DLL dropped beside the executable, or into a folder the tool reads such as
    `Extensions`, refuses the launch, just as a swapped executable does.
  - For a downloaded artifact, the per-file digests recorded at extraction are local metadata. The hash
    of that metadata is part of the grant, so rewriting it voids the grant.
  - Immutable version directories are a convention, so the check doesn't rely on them, and A4's
    writable-installation rule may not weaken it.
- **Unsigned servers** (#2945):
  - The opt-in is a separate persisted release setting, off by default.
  - It's read only from the user's own settings file. It's excluded from the profile settings allowlist,
    and no package or workspace can set it.
  - It records the specific unsigned server and adapter ids it was turned on for. A later unsigned one
    needs its own opt-in.
  - Unsigned host-run packages still get consent for each build, keyed on the manifest hash.
  - While it is on and an unsigned server or adapter is active, the title bar carries the marker. That
    placement is Jen's answer to q-2945-unsigned-marker, and it sits beside developer mode's (#2452).
  - Each start shows a non-modal notice naming every unsigned server, and closing the notice is not
    consent.
  - The wording is localized like all chrome.
  - The opt-in never covers add-ins, compatibility checks, or a profile vouching for a suite.

### A4. Acquisition and platforms (§4, except writable installations)

- **Platform keys:**
  - The keys are a closed set of .NET runtime identifiers: `win-x64`, `win-arm64`, `osx-x64`,
    `osx-arm64`, `linux-x64`, `linux-arm64`, `linux-arm`, `linux-musl-x64` and `linux-musl-arm64`.
  - Matching is exact. There is no fallback across architectures (not osx-arm64 to x64, not win-arm64 to
    x64) and none from musl to glibc.
  - A duplicate key refuses the manifest.
  - A contribution with no key for this platform is listed as *not available on this platform*, and is
    never offered.
- **Runtime prerequisites:**
  - Each artifact declares `runtime` inline: `{"kind":"none"}`, `{"kind":"dotnet","minimumVersion":"M.m"}`
    for the Microsoft.NETCore.App shared framework, or `{"kind":"node","minimumVersion":"M"}`.
  - .NET is detected without running anything, by reading the `shared/Microsoft.NETCore.App/<version>`
    folders under `DOTNET_ROOT`, the platform's default install locations and the folder of `dotnet` on
    PATH.
  - Node can't be version-checked without running it. So detection finds `node` on PATH, and the offer
    names the minimum version as something the user must confirm, never as verified.
  - A missing prerequisite blocks the offer. It is shown with what to install and a Retry, so HexIDE
    never offers a download that can't start. Installing a runtime automatically is not authorised.
- **Downloads and redirects:**
  - HTTPS on every hop, with at most 5 redirects.
  - Each artifact in the signed manifest lists the hosts its download may come from, in `hosts`. The
    initial URL's host is implicitly allowed, and every redirect target must match an entry exactly.
  - Consent shows the publisher-facing URL's host and the listed asset hosts, both from the signed
    manifest, since nothing is fetched before the click.
  - The archive's SHA-256 must match before extraction, whatever the redirect path.
  - Each artifact's byte size is in the signed manifest, and a download is aborted as soon as it exceeds
    it.
  - A partial transfer is discarded, never resumed into the cache.
- **What is hashed:**
  - The signed manifest carries each artifact's archive digest.
  - After extraction, the installer records the extracted files' digests in the installation's
    metadata. Repair and tamper checks compare against those, and the metadata is rebuilt only from a
    verified archive.
  - Provenance (`publisher-attested` with its checksum URL, or `computed` with who computed it) is shown
    as information. Trust comes from the publisher's signature over the manifest, not from the label.
- **Cache:**
  - The cache is per user, under the active user-data root (so `--user-data-dir` and sessions isolate it),
    at `packages/cache/sha256/<digest>`. Directories are user-only on Unix (0700), and one installer runs
    at a time, behind a lock file.
  - A cached archive is re-hashed on every read, since a directory named for a digest proves nothing.
  - Eviction keeps the archives of the active version and the retained previous version always.
    Anything else unused for 30 days is evicted at start.
  - Offline: installing or repairing from the cache works offline, and a missing artifact shows as
    *unavailable offline*. Nothing is ever fetched from anywhere but the manifest's URL and hosts.
- **Adopter-bundled suites:**
  - An adopter's installer may place an outside suite's archive in the cache as an **unaccepted offer**.
    This is never A3's pending-activation record, which only the host's accept action writes, so it
    always goes through the dialog.
  - HexIDE verifies the publisher's signature, chain and digest exactly as for a download, skipping only
    transport. It grants no first-party identity, inherits no built-in pinning, and accepts no other root.
  - The licence and consent are shown at first activation, in the same dialog as a download offer.
- **Detection** of an installed tool is declarative only: PATH names and known locations. A probe never
  runs a version command before consent. Explicit user paths and environment overrides keep today's
  precedence over packages.
- **Writable installations: option (b).**
  - The install tree stays verified and read-only. Writable state lives outside it, wherever the tool
    keeps it, and the host never redirects or permits writes into the tree.
  - A tool that writes inside its install tree fails A3's per-launch re-verification and is shown as
    *modified since install*, with a repair. There is no allowlist of changeable files.
  - The review's premise was an older RDCore. At RDCore d6496d6fb (RDCore.SDK 0.1.4-alpha), logs go to
    the per-user log folder (`RDCORE_LOGS_DIRECTORY`), user settings and workspaces to the per-user data
    folder (`RDCORE_USER_DATA`), and `Symbols` and `Extensions` are shipped and only read. That was read
    in RDCore's code and checked on a real build, which wrote nothing in its tree across several server
    and adapter runs (rdcore, 2026-11-15).
  - Adopter-bundled artifacts follow the same rule.

### A5. Ids, paths, collisions and overrides (§5)

- **Package ids:** dotted logical ids, in lower case. The id is one to four segments separated by dots,
  at most 100 characters in all. Each segment starts with a letter, holds only `a–z`, `0–9` and `-`, is
  at most 40 characters, and is not a Windows device name. The `hexide.` prefix is reserved for the
  first-party key.
- **Directory names:**
  - An install directory is the validated package id followed by the version, and the version grammar
    (A7) admits only digits, letters, dots and hyphens.
  - Cache directories derive only from digests.
  - Contribution ids never become path components.
- **Scoping:**
  - A contribution's full id is `<packageId>/<contributionId>`.
  - References are typed, as `package:` or `<kind>:`, so the built-in package `hexide.vb6` and
    `LanguageServerDefaults.BundledVb6Id` no longer collide, and neither is renamed.
- **Publisher ownership:**
  - A package id's first segment is the publisher's namespace, carried in the publisher record that
    HexIDE's intermediate signs, as `hexide.` already is for the first-party key.
  - A signed package whose id doesn't start with its publisher's namespace is refused. That stops one
    chained publisher squatting another's ids locally, including through a profile's embedded dependency.
  - Issuance enforces uniqueness: signing a publisher record refuses a namespace already issued to a
    different publisher id. A publisher's rotated records share its namespace.
  - Unsigned packages, data or (under the #2945 opt-in) host-run, use only the reserved `local.`
    namespace, so they can never hold or borrow a publisher's id. An override never rebinds across the
    signed/unsigned line.
  - Only one version of an id is active at a time.
- **Contribution collisions** that select an executable (two packages claiming one language id or file
  pattern):
  - A first-party contribution wins over an outside one by default.
  - Between two outside packages, neither is chosen automatically. Both are listed as *conflicting*
    until the user picks, and the choice is persisted.
  - A user's own configuration (the settings file, not a loose pack) outranks all of them, as today.
- **Overrides:**
  - Overrides are read only from the user's own settings file, never from a workspace, a profile, a
    package or `settingsDefaults`.
  - An override replaces the whole entry, keyed by the full contribution id.
  - When the package is uninstalled, the override is kept, marked *orphaned*, and is inactive: it never
    launches.
  - Reinstalling rebinds it if the contribution id still exists, under the same publisher, and the UI
    says so.
  - An update that changes the overridden entry marks the override as overriding a changed entry; the
    user's choice still applies.

### A7. Versions, compatibility and loose packs (§7, §8's migration)

- **Field names:** reuse `schema` and `minHexideVersion` (#310). A package's `schema` numbers the package
  format, separately from `addin.json`'s, because the file names differ. `minHexideVersion` compares as
  `addin.json`'s does today.
- **Package versions:** SemVer 2.0 precedence, with prereleases below their release. Prerelease
  identifiers and build metadata are lower case only, so two version directories can't collide by case.
  Build metadata is ignored for ordering. Two builds of equal precedence with different manifest hashes
  can't both be installed.
- **Strictness:**
  - Within a schema, unknown fields refuse the manifest (incompatible), so validation stays honest.
    Extension data goes under one top-level `x` object, which the host never reads.
  - A newer schema, or an unmet minimum, is *incompatible* before activation, developer mode included.
- **Downgrade:** only by explicit user action, never automatic.
- **Loose packs:** existing loose theme, keymap and language packs keep being discovered by today's
  readers for a transition period, as user content. They are not imported into generated packages,
  which keeps their provenance and rewrites no user file. A loose pack with the same id as a package
  contribution wins and shows as an override, but only for kinds that can't select an executable
  (themes, keymaps, translations, icons, snippets and templates). A loose `languageDefinition` never
  overrides a package's routing; it is listed as conflicting under A5's rule. Payload parsers are reused unchanged, with every rule §8
  lists.

### Digest spelling (illustrative schema)

SHA-256 digests may be written in either case. The host normalises them to lower case on read and always
shows them in lower case.

### Validation

`openspec validate 2026-11-03-package-format --type change --strict` and `DesignRecordTests` were run on
this change (see tasks.md).

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

The contained-read precedent is [ProfileFileReader](../../../IDE/HexIDE/IDE/Profiles/ProfileFileReader.cs)
(#2621): opens do not follow links, only regular files are accepted, and `MaximumBytes` caps each read
at 1 MiB. [ProfilePaths](../../../IDE/HexIDE/IDE/Profiles/ProfilePaths.cs) rejects Windows device names on
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
[add-in trust contract](../../specs/addin-trust/spec.md) consults a signed revocation list before
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
HexIDE’s [publish workflow](../../../.github/workflows/publish.yml) also ships self-contained builds, whose
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
change the [profiles spec](../../specs/profiles/spec.md) and reconcile the
[profile change’s design](../../changes/2026-10-25-profiles-supersede-personalities/design.md),
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

