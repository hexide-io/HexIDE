# Profiles supersede personalities

## Why

Issue #504 records the decision to replace personalities with profiles and remove `--personality`.
A fixed enumeration cannot represent a bundle authored outside this repository. Safe mode, a directory
and an installed name are three selections of the same base experience.

**Status: proposal for supervisor review and Jen's approval; no implementation is authorised by this
change.** The supersession decision is settled; the detailed choices below are proposed for approval.
The feasibility evidence is `docs/profile-bundles.md`; current behaviour is checked against
`PersonalityProfiles`, `IProjectTemplate.Templates` and `ServerOptions` in this checkout.

## What Changes

- Define a versioned, data-only directory bundle, reusing existing keymap, theme and language JSON.
- Select one bundle with `--profile <value>`: `none`, a directory path, or an installed name. Omission
  selects the built-in `vb6`. Built-in names are reserved; installed names resolve under the active
  user-data directory. Selection is session-only.
- Remove `--personality`, with an actionable startup error rather than today's silent unknown-flag
  handling. No alias remains.
- Replace the personality capability with `profiles`. Keep the current shipped spec in place until
  implementation is complete and this change can be archived through the CLI.
- Convert the three built-ins without changing their current effects. Fixes to inconsistent project
  creation surfaces remain separate work under #418.

## Capabilities

### New Capabilities

- `profiles`: bundle contents, resolution, defaults, safe mode, validation and migration.

### Modified Capabilities

- `ide-personalities`: remove all requirements; profiles subsume them.

## Impact

This deliverable changes documentation only. Runtime, localization packs, CLI help, living coverage
counts and published capability specs continue to describe today's implementation. The future loader
will run before settings-dependent services and the first window; surfaces will consume a resolved
profile rather than branch on its name. No interpreter or VB6 semantics change.

## Delivery after approval

`design.md` defines the proposed format and alternatives. `tasks.md` keeps implementation unchecked,
split into reviewable work with sizes and validation. #415, #418 and #419 remain separate issues;
additional split-outs are proposed here, not filed or claimed by this change.
