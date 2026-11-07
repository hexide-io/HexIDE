# Code text printing

## Why

File → Print, File → Print Setup and Project Explorer → Print are stubs (#45). Printing code lets
users review a selection, module or project on paper without exporting it through another editor.
Jen accepted the platform paths and defaults from `docs/printing.md` on 2026-10-25. This change records
those contracts before implementation, as the supervisor requested; it adds no executable behaviour.

## What Changes

- Introduce the `printing` capability for code scopes, immutable snapshots, pagination, session Setup,
  platform dialogs, submission, cancellation and failure handling.
- Use one snapshot and paginator with Windows printer-DC output, macOS PDF print operations and Linux
  PDF submission through the host's printing client tools, preceded everywhere by a shared scope dialog.
- Record accepted defaults: printer-default paper, 12.7 mm margins, editor font, black on white and
  line numbers off. Setup is session-only.
- Keep form images, Form As Text, runtime printing and rectangular selections outside this delivery.

## Capabilities

### New Capabilities

- `printing`: code text printing and page setup on Windows, macOS and Linux.

### Modified Capabilities

None. Existing commands and live capability specs remain unchanged until implementation lands.

## Impact

This is a documentation-only change. `design.md` carries the accepted design from `docs/printing.md`,
including the approved renderer version and licence provenance; no package is added or pinned here.
Future implementation touches command routing, editor/project snapshots, localized dialogs, layout
and platform adapters. No interpreter semantics, source files or project formats change.

Implementation and platform verification remain unchecked in numbered phases in `tasks.md`. Keep this
change unarchived until those phases are complete. MISSING_FEATURES status and MISSING_LANGUAGE counts
remain unchanged: a contract does not implement printing.
