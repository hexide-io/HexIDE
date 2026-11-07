# Profiles supersede personalities

## Why

Issue #504 records the decision to replace personalities with profiles and remove `--personality`.
A fixed enumeration cannot represent a bundle authored outside this repository. Safe mode, a directory
and an installed name are three selections of the same base experience.

**Status: approved by Jen as written on 2026-10-25 in #504, following #1953.** The latest supervisor
comment (2026-10-29) authorises one unchecked task per run, in order; subsequent tasks remain
separate deliveries.
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

The original proposal changed documentation only. Phase 2 completed and guarded the routed-command
keymap table and verified the already-corrected personality spec. The internal manifest/resolver now
validates immutable snapshots and indexed built-ins, with localized load-error attribution. It is not
yet connected to startup. The settings reader accepts snapshot defaults beneath explicitly present
saved user values; theme, keymap and language adapters remain pending. CLI help and living coverage counts remain unchanged. The subsequent migration
will run before settings-dependent services and the first window; surfaces will consume a resolved
profile rather than branch on its name. No interpreter or VB6 semantics change.

## Delivery after approval

`design.md` defines the approved format and alternatives. `tasks.md` records progress,
split into reviewable work with sizes and validation. #415, #418 and #419 remain separate issues;
additional split-outs are proposed here, not filed or claimed by this change.
