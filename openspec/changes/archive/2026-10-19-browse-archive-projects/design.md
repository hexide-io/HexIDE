## Context
The HexIDE.TwinBasic library supplies immutable snapshots and a synchronous lifecycle. The IDE must keep these separate from VB6 ProjectDefinition and language-server identities.

## Decisions
- A singleton ArchiveProjectService serializes reads using a semaphore and schedules file I/O off the UI thread. Its callers own the UI context; publication and document changes occur after awaiting the worker.
- Archive tabs own protected preview buffers. They do not create VB6 models, sidecars, filesystem member paths or LSP sessions. A whole-document read-only search contract allows Find and blocks Replace, unlike the existing excluded header regions.
- Reload refreshes retained identities and closes removed members only after successful validation. Tree expansion and selection follow entry identity.
- Archive focus suppresses implicit startup-project actions. Explicitly addressed VB6 operations remain possible in a mixed workspace, and an already-running debugger can still be controlled.
- Hidden and superhidden flags are filtered by default; an inspection toggle reveals them. Unsupported formats retain metadata and raw bytes in the snapshot.
- The original archive remains authoritative. A persistent folder suitable for source control, reconciliation and write-back are later slices, not temporary extraction in this UI.

## Risks
Large trees still require UI projection work after bounded parsing. Symlink aliases are not deduplicated. No automatic file watching is introduced. Optional constructor dependencies preserve existing consumers; composition tests verify production wiring.
