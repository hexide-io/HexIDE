# White-label distributions — decisions and implementation design (#2042)

## Scope and authority

This note records the white-label policy accepted by Jen on 2026-11-03 for
[#2042](https://github.com/hexide-io/HexIDE/issues/2042), card `q-2042-white-label`.
An adopter ships the unchanged managed implementation plus a JSON descriptor, without a fork or
carried patches. A supported repack tool supplies native identity; the adopter signs its output.
The policy is decided; the [OpenSpec change](../../openspec/changes/2026-11-03-white-label/)
records intended behaviour and the engineering acceptance still needed before implementation.
A first slice of descriptor loading has shipped ([how to use it](../white-label.md)); repacking exists for Windows and Linux.

The issue's 2026-10-26 inventory was re-checked against `upstream/main` at `419088e1` on 2026-11-03.
The source locations below are that revision's locations. The landed profile consumers and recovery
storage change several inventory conclusions. The [package design](package-format.md), including
Jen's 2026-11-03 decisions, supersedes the inventory's proposed adopter root and profile suite exclusions.
No adopter consultation or live protocol measurement was performed for this note; RDCore is the
first likely adopter, not an acceptance claim on its behalf.

## Decisions

Jen accepted all three recommendations on **2026-11-03**, card `q-2042-white-label`, as recorded
in the latest supervisor comment on #2042.

1. **Native identity — 2026-11-03, `q-2042-white-label`.** HexIDE supplies a supported repack
   tool driven by the same brand file. It rewrites native executable icon/version metadata and
   installer identity; the adopter signs the result. The managed implementation stays unchanged.
   Identical executable bytes with only a launcher/shortcut were rejected as insufficient for full
   native identity. Build properties alone would require adopter rebuilds and miss runtime consumers.
2. **Installation identity — 2026-11-03, `q-2042-white-label`.** The installation descriptor fixes
   distribution identity. It selects a default profile; profiles change preferences, never identity.
   Profile-controlled identity was rejected because switching profiles or selecting recovery mode
   must not move storage or change the product being run.
3. **Trust — 2026-11-03, `q-2042-white-label`.** Keep #1210's publisher-key route under HexIDE's
   root, with no adopter root. RDCore is being asked whether it needs more; a conflict returns to Jen.
   An independent adopter root is not an available branding option and first-party pinning remains.

## Fixed constraints

- **Credits are append-only.** Jen's 2026-10-28 direction on
  [#2746](https://github.com/hexide-io/HexIDE/issues/2746) permits adding credits, never removing or
  replacing HexIDE's. There is no hide, replace, empty-base or style-based suppression API. Main already
  builds base credits before `AdditionalCredits` in `AboutDialogViewModel.cs:33–38` under
  `IDE/HexIDE/Forms/ViewModels/`; the manifest would feed only the additional list.
- **The tree stays 100% MIT.** Preserve the MIT copyright/permission text and required third-party
  notices. Replacing a mark does not erase authorship. Dependencies still require recorded permissive
  licences in `scripts/package-licences.tsv`. External providers stay out of process and out of this
  tree; the package design distinguishes adopter distributions from HexIDE's own MIT-only releases.
- **#1210 owns trust.** Outside publishers retain their own private key and receive a publisher
  certificate signed by HexIDE's root. They never receive HexIDE's root private key or a distribution
  root. A brand is data, not authorization to run code, grant first-party status or replace anchors.
  Existing consent, compatibility, complete-inventory verification and revocation remain applicable.

The issue body requested an adopter publisher root; the later package decision explicitly says there
is no distribution root. This design follows that decision; any conflicting adopter need returns to Jen.
Branding itself needs no special permission; signing host-run/code packages follows #1210.

## Surfaces and the remaining gaps on main

Paths are repository-relative. “Runtime proposal” below means future manifest support, not support
already present. Native packaging steps are gaps to support with common tooling, not a requirement
that adopters carry source edits.

| Surface | Main today, with source evidence | Proposed runtime configuration / build or repack boundary |
|---|---|---|
| Product titles and dialogs | Compiled suffix at `IDE/HexIDE/MainViewViewModel.cs:175`; About name, copyright and bitmap at `:1638–1641`; startup name at `IDE/HexIDE/Forms/Views/NewProjectView.axaml:18`; save caption at `IDE/HexIDE/Forms/ViewModels/SaveChangesViewModel.cs:12`; fallback captions at `IDE/HexIDE/IDE/WindowManager.cs:123,180,203`. Still gaps. | Runtime display name, adopter copyright and runtime logo, consumed consistently by all host captions. Keep upstream provenance and version separately visible in About. |
| Localized product references | `IDE/HexIDE/Localization/Packs/en.json:200` names HexIDE in About; profiles now supply language overrides and user translations remain final. Those overrides do not reach compiled strings. | Runtime brand name in localized host templates, rather than an adopter translating every pack merely to substitute a name. Preserve mnemonics/placeholders and locale fallback; review all inventory keys, including warnings. No global string replacement across documents or protocols. |
| About credits and fingerprints | `IDE/HexIDE/Forms/ViewModels/AboutDialogViewModel.cs:33–38` already appends credits; `:20–22` displays actual embedded-key fingerprints. No manifest feeds additional credits today. | Runtime additional localized credits. Keep the upstream credits visibly separate and immutable to branding, including through profile/user translation overrides of `Str.Dialog.About.Credits.Thanks` (`IDE/HexIDE/Localization/Packs/en.json:11`). The existing translation route needs protection here; an append-only list alone is insufficient. Never relabel fingerprints as adopter anchors. |
| CLI identity and mark | `IDE/HexIDE.Desktop/ServerOptions.cs:298,301,316` and `IDE/HexIDE.Desktop/HelpMark.cs:22,37` embed name/usage/art. Still gaps. | Runtime help name, invocation label and optional contained terminal art, with plain fallback and existing width limits. Bootstrap early enough for help and startup diagnostics. |
| Runtime look | Profile theme adapter at `IDE/HexIDE/Themes/ThemeService.cs:18`; keymap adapter at `IDE/HexIDE/Keymaps/KeymapService.cs:60`. Main supports palette/gesture payloads. Geometry remains compiled at `IDE/HexIDE/Themes/IconGeometry.axaml:18` and `IDE/HexIDE/Utils/IconFactory.cs:16`; window icon is fixed at `IDE/HexIDE/MainWindow.axaml:9`. | Reuse profile palettes/keymaps; runtime branding assets for window/About/startup and later package icon contributions. No executable AXAML/scripts. Arbitrary chrome/era skins (#1137) and a splash are not supplied by existing palette support. Rendering contracts and safe asset decoding remain future engineering work. |
| Native name and icon | `IDE/HexIDE.Desktop/HexIDE.Desktop.csproj:19,91,97` embeds icon/help assets; `IDE/Directory.Build.props:23–25` sets product/company/copyright. `IDE/HexIDE.Desktop/app.manifest:6` has Windows identity. | The supported tool consumes the same descriptor for staging names and native PE icon/version resources, then the adopter signs the result. Keep managed assembly names and resource URIs stable. JSON at runtime cannot change these native resources. |
| Installer and OS registration | `installer/HexIDE.iss:9–20,31` fixes AppId, name, folder, executable and shortcut. `.github/workflows/publish.yml:167,176,247–260,334` fixes apphost rename, macOS bundle identity/icon and installer input. | Shared packaging tool consumes brand data for unique installer AppId, install directory, shortcuts, archive names, macOS bundle id/name/icon and executable. Re-sign macOS bundles after repacking. Build properties alone do not update every consumer. |
| User configuration | `IDE/HexIDE.Core/IDE/UserDataPath.cs:38` fixes the default child `HexIDE`; `IDE/HexIDE.Desktop/Program.cs:82–87` redirects before profile resolution. Settings, layout, recents and LSP config follow that root (`IDE/HexIDE/IDE/SettingsService.cs:270`, `WindowStateService.cs:20`, `RecentProjectsService.cs:38`, `IDE/HexIDE.Lsp/LanguageServerConfigLoader.cs:63`). | Runtime stable distribution storage id, distinct from display name. Compute platform roots before any consumer; preserve explicit absolute `--user-data-dir` for intentional isolation. Profiles cannot choose it. Never silently import HexIDE's settings. |
| Logs and diagnostic hints | IDE `IDE/HexIDE/Infrastructure/LoggingSetup.cs:162` and bundled LSP `LspServer/HexIDE.VbLspServer/Program.cs:62` independently construct `LocalApplicationData/HexIDE/logs`. Root redirection still does not isolate logs; hints are fixed at `IDE/HexIDE.Desktop/Server/ToolFailures.cs:278–280`. | Runtime per-brand local log root; pass the resolved location to the bundled child process and fix path hints. Preserve subsystem/session partitioning and pruning within that root. Third-party servers own their logging; do not claim their paths are redirected. |
| Crash recovery | Recovery now exists: `IDE/HexIDE.Core/IDE/RecoveryPath.cs:28–33,46–57` uses local Windows/XDG state storage, or `Recovery` beneath an explicit user-data redirect. `IDE/HexIDE/Recovery/RecoveryService.cs:74` uses that path. | Runtime per-brand local recovery root, distinct from roaming settings. Enumeration, offers, cleanup and live-run claims stay inside it. The old inventory's “no recovery found” is obsolete. Default brand selection is still missing. |
| Sessions and project state | `scripts/dev-ide.ps1:105,156` assumes HexIDE session/process paths. `IDE/HexIDE/Sidecar/UserSidecarService.cs:966` uses project-local `.user.hexproj`; `IDE/HexIDE.TwinBasic/ArchiveSourceFolder.cs:47,405` uses a marker and `.hexide`. | Runtime/launcher isolation for brand sessions and temporary work. Project-local compatibility files need an explicit sharing/namespacing contract; root redirection cannot isolate them. Do not rename serialized vocabulary or ownership markers indiscriminately. This review reads host source only, not compiler binaries. |
| Defaults and offerings | `IDE/HexIDE/IDE/Profiles/ProfileManifest.cs:14–15` accepts theme, keymap, language overrides, project types, visibility and settings defaults, with no brand field. `IDE/HexIDE/IDE/ProfileService.cs:23–28` applies References placement and offerings; `IDE/HexIDE/IDE/SettingsService.cs:371` layers profile defaults. | Runtime default profile reference, reusing current payloads and explicit-user-choice precedence. Dock layout is saved state, not a profile payload. Arbitrary menu/toolbar construction remains compiled; profile toolbar visibility follow-through is still unchecked in the profiles change's tasks. |
| Servers and provider suites | Bundled display identity is compiled at `IDE/HexIDE.Lsp/LanguageServerDefaults.cs:46`. Current profile manifest has no suite dependencies. Package design §8 now permits exact-version dependencies with embedded suite-publisher-signed manifests; that is policy awaiting implementation. | Reference package suites, never duplicate commands or trust policy in branding JSON. Profile signatures do not authorize servers. An unavailable exact version needs a visible missing-suite state, never silent version substitution. |
| Trust and bundled add-ins | `IDE/HexIDE/Addins/TrustAnchors.cs:16–17` embeds root/first-party keys. `IDE/HexIDE.Desktop/HexIDE.Desktop.csproj:108,122–131` gates first-party packing on signing material. | Runtime publisher identity/certificate references for packages only; no root/first-party override. Repacking must not re-sign an adopter add-in as HexIDE first-party. Keyless cloning does not create a first-party bundle. |
| Automation and compatibility | `IDE/HexIDE.Desktop/Server/IdeServer.cs:43` leaves MCP identity SDK-defaulted; `SessionGuard.cs:42` fixes `X-HexIDE-Session`. The MCP server is Debug-only. | Runtime server display identity and host-authored descriptions where relevant. Exact emitted identity remains unmeasured. Retain existing headers, tool ids, environment names, DAP/LSP/schema ids, API assemblies and `avares://` identities as compatibility vocabulary; add aliases only with a migration contract. Branding is no reason to break clients. |
| Links, notices and support | `IDE/HexIDE/MainViewViewModel.cs:1802` compiles the repository URL. `LICENSE`, `IDE/LICENSE`, `LspServer/LICENSE` and `THIRD-PARTY-NOTICES.md` carry provenance. Publish staging still needs an explicit notice-file audit. | Runtime support/home/repository destinations as separate roles with package link-verification rules where applicable. An update feed is not implemented by these links. Packaging copies required notices; neither a brand name nor credits replace licence texts. |

The inventory's per-pack and environment-token totals are historical, not new counts asserted here.
The remaining literal diagnostics, temporary prefixes, exports and browser assets in that inventory
also need consumer review during implementation. Browser remains aspirational. Program `SaveSetting`
storage is language/runtime compatibility, not an IDE-brand preference; this design changes no VB6 value.

## Vehicle comparison

| Vehicle | Fit to Jen's goal | Cost and relationship to #1210 |
|---|---|---|
| Branding section in a profile bundle | Reuses JSON, containment and pack adapters; one bundle can carry the adopter's look and defaults. | Profile names/paths currently resolve using the already-selected user-data root, and users can select `none`. Letting profile loading choose that root creates circular bootstrap and unsafe identity switching. Viable as packaged content referenced by an installation descriptor, not as an arbitrary profile's authority over the installation. |
| Separate brand JSON beside the executable | Discoverable without consulting user state; works with upstream binaries and can select storage identity before profiles/logging/help. | Adds a small bootstrap descriptor and validation boundary. Keep its look/defaults as references to profiles and package contributions, not duplicate payload formats or another installer. Installation-directory permissions determine who can change this data; JSON is not a trust boundary against someone able to replace the binary. |
| Build properties | Useful for native executable metadata, icon inputs and packaging; compatible with a shared build/repack recipe. | Alone requires a rebuild and misses runtime consumers; the accepted route keeps managed implementation unchanged. Not a substitute for JSON. Exposing root-key build properties as branding would contradict #1210. |

**Selected vehicle:** one installation descriptor, provisionally `brand.json`, beside the real
executable, with a stable distribution id, display identity, additional credits, contained asset
references, distinct link roles and a default-profile reference. Ship its profile and assets in the
same deployment/package. A generic packaging command reads that same descriptor for native identity;
the accepted repack policy supplies native identity without changing managed implementation.
The descriptor name and members are illustrative, not a schema accepted by main. Do not put a root key, command line, arbitrary absolute
path or executable install hook in it.

Package containment, inventory, schema/minimum-host validation and signing should be reused from
#1210 rather than inventing a second package format. Branding is data tier; an aggregate with servers
or add-ins retains those children's host-run/code tiers. Unsigned data does not confer executable
trust. Package policy permits publisher-signed exact-version suite manifests inside profiles, downloads
only from the publisher, no HexIDE hosting/mirroring, and a per-user cache. Scope that cache under the
brand's local storage so uninstall/cleanup does not affect another distribution. Download/launch consent
is still required. The release unsigned-server/adapter exception belongs to #2945: off by default with
loud session markers and per-start notice, never enabled by an adopter's branding/defaults.

## Bootstrap, precedence and failure proposal

The installation descriptor is read from the executable directory, not the invocation directory or a
project folder. Validate and capture it and its assets before opening mutable user state. Reuse the
profile reader's contained, bounded regular-file reads; pick separate byte/pixel limits for images and
terminal art rather than assuming the JSON cap is appropriate. Reject traversal, rooted references,
links, duplicate fields and unsupported schemas with an attributed diagnostic. No network fetch is
needed to determine identity. A missing descriptor selects stock HexIDE; an invalid descriptor refuses
startup, since silently using stock identity could open another product's settings or recovery files.

Proposed order:

1. Capture invocation directory and parse flags, then resolve installation identity without reading user
   settings. Preserve useful help on a broken installation, but identify the brand error explicitly.
2. Compute independent configuration, local log, local recovery and cache roots from the stable id;
   a changed display name never moves state. Validate ids portably, including Windows reserved names
   and case collisions. Refuse an adopter id that aliases stock HexIDE's root. An explicit user-data
   redirect remains a deliberate escape hatch; document that pointing two products at one directory
   opts out of isolation rather than letting the brand choose someone else's directory.
3. Resolve the default profile against that root, or an explicit `--profile` selector. Host defaults,
   profile defaults/payloads and explicit user preferences retain today's ordering. Distribution identity
   is not a saved preference. A brand update does not overwrite user settings or silently migrate roots.
4. Resolve suite dependencies through the package machinery and its verification/consent, then start
   consumers with the immutable brand/profile snapshots and resolved paths. Pass paths explicitly to
   bundled children; do not infer brand from process filename or rely on renaming environment variables.

`--profile none` should preserve installation identity and isolated paths while bypassing optional
look/defaults, user packs/settings/layout and add-ins as today. Keep the recovery-session notice and
write suppression. It must not turn RDCore into stock HexIDE or expose stock recovery offers. Broken
branding recovery needs a diagnostic/repair path rather than falling back onto HexIDE's root. Exact
help/error ordering, repair interface, atomic descriptor updates and migration across distribution ids
are engineering questions for the later design, not additional policy questions for Jen.

## Implementation handoff

The [OpenSpec change](../../openspec/changes/2026-11-03-white-label/) records these decisions.
Accept the concrete schema and engineering details before implementation. Reconcile `profiles`,
`profile-catalog`, package-format work, localization, state persistence, recovery, logging and add-in
trust contracts without modifying archived records. New requirements belong under `## Requirements`,
retain SHALL/MUST and have scenarios. The deltas describe intended behaviour; live specs remain
unchanged until implementation and archival.

Future tests should first fail against the old behavior: branded titles/help/assets in all startup
paths; immutable upstream credits despite translation overrides; unchanged compatibility identifiers;
brand isolation of settings/logs/recovery/cache including child LSP and recovery mode; refusal before
any state access for invalid identity; profile/user precedence; and signatures/consent unaffected by
branding. Native packaging checks should inspect actual metadata and installer ids, not just generated
JSON. Live review should check About credit order/fingerprints, window/taskbar/startup icons, captions,
localized menu names, recovery notices and two brands running alongside stock HexIDE.

No feature or language-coverage status changes in this design record. No VB6 measurement is added or
assumed; `MISSING_LANGUAGE.md` and its counts stay unchanged. Implementation, concrete schema and
packaging mechanics, and first-adopter confirmation remain open.
Native repacking, fixed installation identity and the absence of an adopter root are settled policy.
