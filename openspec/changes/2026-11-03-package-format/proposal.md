# One format and installer for packages (#1210)

## Why

A suite needs to install related tooling and data together, while a data pack needs the same installer
without executable privileges. #1208 calls for one format; #457 supplies language-server contributions
and #459 supplies capability disclosure and exact-build consent. This change translates the
[reviewed design note](../../../docs/design/package-format.md) into intended contracts. It ships no
implementation and does not supersede the existing payload formats or add-in host APIs.

## Policy decisions

All four decisions are Jen's Team Desk decisions of **2026-11-03**, as recorded in the note:

1. `q-2848-package-policy`, its first question: outside publishers receive a publisher key signed by
   HexIDE's root. They never receive the root private key, and there is no distribution root.
2. `q-2848-suite-deps`: profiles may require exact suite versions and carry each manifest inside the
   bundle. Only the suite publisher's own signature authenticates it. This reverses the profiles
   exclusions on remote URLs and server configuration solely for suite dependencies.
3. `q-2848-cache`: download outside suites only from their publisher, never host or mirror outside
   artifacts, and retain a per-user cached copy.
4. `q-2848-devmode`, **03:18**, #2945: release builds may run unsigned language servers and debug
   adapters behind a persisted setting, off by default, with loud session markers and a per-start
   notice. This is independent of Debug-only developer mode and does not extend to unsigned add-ins.

## What changes

- Add `packages` for inventory, contribution tiers, acquisition, layering, compatibility, property
  providers and publisher-domain proofs.
- Add package integration to `addin-system`, shared trust to `addin-trust`, and migration contracts
  to `theme-packs`, `keymap-packs` and `language-packs`.
- Modify the live `profiles` bundle contract and add exact-version suite dependencies. The earlier
  profile change remains in flight for other surfaces; it is not the baseline for this delta.
- Retain engineering alternatives in design.md for supervisor acceptance before implementation.

## Impact and scope

Only design records change. No product code, tests, UI strings, packs, dependencies, feature-status
claims or changelog fragment are added. Implementation tasks remain open. The live specs and archived
history remain unchanged until the implementation is complete and OpenSpec archives this change.
