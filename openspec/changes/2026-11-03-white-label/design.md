# Distribution branding design

## Authority and boundaries

The three policy decisions are dated 2026-11-03, card `q-2042-white-label`; the
[proposal](proposal.md) and [surface inventory](../../../docs/design/white-label.md) record their source.
This is an implementation design for review, not a claim of shipped support. The tree remains MIT,
including any repack tooling; required copyright, permission and third-party notices travel with output.
No VB6 behaviour changes. Browser deployment, arbitrary chrome skins and an update-feed client remain
outside this slice. Program icons and SaveSetting compatibility are not IDE branding.

## Selected vehicle and rejected alternatives

Use an installation descriptor beside the real executable, provisionally `brand.json`. Resolve it
independently of working directory, project files and user settings. Its conceptual fields are schema
and minimum host version, stable distribution id, display name/copyright, additional localized credits,
contained runtime/native assets, distinct support/home/repository links and default-profile reference.
Exact spelling, id grammar, version comparison and platform metadata mapping need engineering acceptance;
this is not an accepted JSON schema. It contains no commands, install hooks, arbitrary absolute paths,
root keys or trust-policy defaults. Package compatibility/containment/inventory rules are reused rather
than introducing a second package installer. It is data tier; executable children keep their own tiers.

A branding section in an arbitrary selected profile was rejected as the identity authority: profile
lookup already needs the data root, profiles can be switched, and `none` bypasses customization.
The installation descriptor can reference a profile deployed in the same package, reusing theme,
keymap, translations, offerings and allowed settings defaults without a second payload format.
A separate file introduces early validation, but fixes the circular bootstrap and keeps identity stable.
Build properties alone were rejected because they require rebuilding and miss runtime consumers.
They can remain implementation inputs to shared packaging tooling. Identical native bytes with only
branded shortcuts were rejected because their embedded icon/version identity remains upstream.

## Bootstrap and failures

Capture invocation directory and parse flags without accessing user state. Resolve and validate the
installation descriptor and capture contained assets before constructing logging, recovery, profile,
settings, cache or service consumers. Missing descriptor means stock HexIDE; malformed, incompatible
or unsafe branding refuses normal startup with source attribution, never a silent stock fallback.
Help remains available with an explicit installation error and no mutable-state reads or writes.
The exact help/error exit ordering and a repair interface need acceptance alongside the CLI contract.

Use bounded regular-file reads with no links, rejecting traversal, rooted references, duplicate fields
and unsupported schemas on every platform. JSON size, asset byte/pixel bounds, decoder formats and
race-resistant snapshot/update mechanics remain engineering decisions; profile JSON limits do not
implicitly become image limits. No network request determines installation identity. Installation
permissions protect the descriptor; it is not a security boundary against someone replacing the host.

Compute separate per-user configuration and local log/recovery/cache/session roots from the stable id.
Validate portable id mapping, reserved stock aliases, case and platform collisions before any consumer
opens state. Display-name updates cannot move roots; no automatic import from stock. Preserve explicit
`--user-data-dir` relative to invocation directory as an intentional redirect, with documented isolation
consequences; the descriptor cannot select arbitrary directories. Decide its exact relationship to
local logs/cache/session paths during engineering acceptance, preserving recovery's existing redirect.
Pass resolved log paths to bundled children; third-party servers retain their own logging policy.
All diagnostics must describe the actual resolved paths. Scope cache eviction, recovery offers/claims,
pruning and session cleanup to this distribution, including simultaneous branded and stock launches.

Apply an explicit profile selector before the installation default; without either, use embedded `vb6`.
Resolve installed profile names under the already-selected configuration root and contained packaged
references through package/profile readers. Keep host defaults, profile defaults and explicit saved user
choices in their existing precedence. `--profile none` retains brand, protected credits and isolated
roots while bypassing customization and suppressing writes under the existing recovery contract.
Identity is never saved as a user preference. Distribution-id migration is not implicit or authorized.

## Presentation, credits and compatibility

Host titles, dialogs, startup/About/window assets, CLI/help labels, path hints and host-authored diagnostics
consume one immutable identity snapshot. Localization supplies host templates with an explicit brand
argument, retaining locale fallback, placeholder checks and mnemonic rules. Do not globally replace
HexIDE in source documents, serialized projects, protocol payloads or third-party messages. Runtime asset
support uses inert contained data, never executable AXAML or scripts. Link roles remain distinct and use
package verification rules where applicable; a support URL does not implement an update service.

Keep upstream credits in a host-owned block, followed by adopter additions, with upstream provenance,
version and actual signing fingerprints visible. Shipped translations may localize the base, but profile,
user and package translation overrides cannot blank, replace or suppress it. This specifically closes
the current `Str.Dialog.About.Credits.Thanks` override route: protecting only AdditionalCredits is
insufficient. No hide/replace/style suppression API is introduced. Rejected override entries need an
attributed diagnostic; other valid overrides still apply. The in-flight package translation contract
must retain user precedence only for overridable text when reconciled with this protection.
Exact block rendering and localization-key
split require acceptance and later running-IDE verification.

Keep assembly/resource URIs, schema ids, tool ids, LSP/DAP vocabulary, session headers and environment
names stable. Branding changes presentation, not client compatibility. Project-local `.user.hexproj`
and archive working markers need an explicit sharing/namespacing review; a data-root redirect cannot
isolate them. No indiscriminate renaming or speculative protocol identity is prescribed. The existing
surface table identifies those remaining consumer reviews rather than claiming a measured wire value.

## Repack tool and distribution trust

A supported common tool reads the same validated descriptor for executable filename/native icon/version
resources, installer AppId/name/install directory/shortcuts, and platform bundle identity/assets. Managed
code, API assemblies and resource identities stay unchanged. The adopter signs the completed executable,
installer and platform bundle as required; prior signatures cannot be represented as valid after rewriting.
Validate actual output metadata, executable launch/probes and installer coexistence, not just generated
JSON. Native format libraries and signing integration need licence review before use, with no new
dependency chosen here. Platform coverage, deterministic mappings, transactional output and update/uninstall
mechanics remain engineering acceptance items. Repacking must carry licence and notice texts.

The tool never creates an adopter root, changes embedded anchors, labels an adopter add-in first-party,
or bypasses inventory, compatibility, consent or revocation. Outside publishers use #1210's HexIDE-root
publisher certificates; private root material is never distributed. Profile suite dependencies require
the suite publisher's own signature at exact versions. Branding cannot enable #2945's separate unsigned
server/adapter exception. Whether the first adopter needs more is outstanding consultation, not permission
for an independent root; conflicts return to Jen.

## Validation and rollout

Implement one coherent phase at a time after engineering acceptance. First establish descriptor/bootstrap
and roots, then host consumers and protected credits, then packaging. Add pre-change failing behaviour
tests, including Linux path/child-process helpers and invalid-descriptor refusal before state access.
Exercise two brands alongside stock, profile switching/none, display-name updates, hostile overrides,
actual native resources/installer ids and required notices. Do not count Windows-only skips as Linux
coverage. Live review checks captions/icons, recovery notices and About credit order/fingerprints.
Only implementation updates feature coverage and archives the deltas into live specs.
