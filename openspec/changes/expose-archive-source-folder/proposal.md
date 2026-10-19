## Why
The synchronization engine is available, but opening an archive still only browses it. Users need an explicit command to create and refresh the persistent folder for source control.

## What Changes
- Add Synchronize source folder to the archive selection context menu in Project Explorer.
- Use the selected project's saved bytes and report the physical output path.
- Explain folder-edit conflicts and retain read-only previews until explicit reload.
- Localize the action and result messages in every shipped language.

## Capabilities
### Modified Capabilities
- `archive-source-folder`: Expose explicit synchronization through the IDE.

## Impact
Archive lifecycle service, Project Explorer, shipped language packs and headless integration tests. Completes the UI portion of #1190 under #1178. No automatic export on open, archive writer or compiler connection.
