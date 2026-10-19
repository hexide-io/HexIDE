## Why
The container reader and document model are available, but users cannot yet open these projects in the IDE. This slice connects offline browsing to normal project opening, without introducing an editor or compiler write path.

## What Changes
- Open archive projects from the project picker, Existing, Recent and path-based startup opening.
- Show the stored hierarchy, read-only text previews and entry metadata.
- Support explicit reload and close, and opt-in hidden entry inspection.
- Prevent startup-project command fallback while an archive is selected.

## Capabilities
### New Capabilities
- `archive-project-browsing`: Offline project exploration and searchable read-only previews.

## Impact
IDE project opening, Project Explorer, document docking, Find/Replace and command availability. Tracks #1187 under #1178 and #1122. No extraction or compiler connection.
