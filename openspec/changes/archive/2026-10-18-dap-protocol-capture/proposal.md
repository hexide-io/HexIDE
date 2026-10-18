# DAP protocol capture

## Why

The DAP client and process owner can communicate with adapters, but their traffic is invisible to the
shared protocol inspector and export pipeline. This slice connects those boundaries, tracked by #1166
under #696, before implementing a user-facing debug session.

## What Changes

- Add optional connection-scoped capture using the existing bounded conversation log.
- Record exact JSON bodies, DAP envelope metadata, process diagnostics and connection closure.
- End unanswered request records on cancellation or teardown, ordered with captured replies.
- Pseudonymise plain JSON path fields on export while preserving raw local inspection.

## Impact

The DAP library gains a reference to framework-neutral HexIDE.Core, with no third-party packages. Existing
capture consumers can read DAP records without a second inspector. A later session host must supply the
shared log; this slice does not create a launch UI, connection-registry entry or debug-session policy.
