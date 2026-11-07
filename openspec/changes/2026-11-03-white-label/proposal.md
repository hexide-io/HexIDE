# White-label distributions without a source fork (#2042)

## Why

An adopter needs to ship the IDE under its own name, with its own look, defaults and isolated state,
without modifying the managed implementation or carrying patches. The
[design note](../../../docs/design/white-label.md) maps the gaps in current consumers. This proposal
records intended behaviour only; no descriptor, repack tool or new branding API is shipped here.

## Policy decisions

Jen accepted all three recommendations on **2026-11-03**, Team Desk card `q-2042-white-label`,
as recorded in the latest supervisor comment on [#2042](https://github.com/hexide-io/HexIDE/issues/2042):

1. Supply a supported repack tool using the same brand file for native executable icon/version
   metadata and installer identity. The adopter signs the result; managed implementation is unchanged.
2. Fix distribution identity in the installation descriptor. Profiles change preferences, never identity.
3. Keep #1210's trust model: outside publishers receive publisher keys signed by HexIDE's root,
   never the root private key. There is no adopter root. Any conflicting adopter need returns to Jen.

Jen's **2026-10-28** direction on #2746 also applies: credits are append-only, with no API to remove
or replace HexIDE's credits. This includes protection from translation overrides. The tree stays MIT.

## What changes

- Add `distribution-branding`: descriptor, fixed identity, isolated roots, bootstrap, host presentation,
  append-only credits and supported native repacking.
- Modify `profiles` resolution/startup to allow an installation default while preserving explicit
  selection, and add the identity boundary.
- Add localization contracts for brand-aware host templates and protected upstream credits; modify
  `about-dialog` and `translation-editor` to close the existing override/editing routes.
- Add the no-root-override boundary to `addin-trust`.
- Add distribution scoping to `ide-state-persistence` and `crash-recovery`, and early identity/help
  handling to `command-line`. Logging and child-process path isolation belong to the new capability.

## Impact and scope

Only this change and the design note change. Live specs, archived records, product code, tests,
strings, packs, dependencies and feature-status documents stay unchanged. No changelog fragment.
Implementation tasks remain open. The package proposal
[2026-11-03-package-format](../2026-11-03-package-format/) owns package schema, inventory, acquisition
and consent; this proposal neither duplicates those payloads nor grants executable trust through branding.
