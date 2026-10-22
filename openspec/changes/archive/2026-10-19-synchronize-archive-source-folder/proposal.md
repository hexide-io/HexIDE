## Why
Archive users need persistent, source-control-friendly files without repeatedly exporting code by hand. The offline browser now works, so the next foundation is a repeatable projection that preserves bytes and refuses to overwrite external edits.

## What Changes
- Add a library operation that synchronizes a saved project into a stable physical hierarchy beside it.
- Preserve every stored file and ordered entry metadata; keep local transaction state separate from tracked content.
- Detect divergence, validate portable paths, and recover interrupted updates before another synchronization.
- Keep the saved archive authoritative until import and lossless write-back are implemented.

## Capabilities
### New Capabilities
- `archive-source-folder`: Explicit, byte-preserving synchronization into a persistent source-control folder.

## Impact
Container library and its tests only. This is the engine portion of #1190 under #1178; the Project Explorer command and translations follow in a separate PR. No compiler, DAP, archive writing, automatic synchronization or editable archive tabs.
