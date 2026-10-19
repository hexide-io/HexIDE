## Why
The root session does not supervise the compiler's other three sockets. Independent attachment/disposal can invalidate the compiler session, and the existing receive deadline incorrectly treats prolonged idle waiting as a stalled message for continuous readers.

## What Changes
- Add an internal lifecycle component for a prepared project root plus filesystem, language and debugger sockets.
- Own attachment rollback, terminal failures, bounded incoming queues, opaque sends and asynchronous reader teardown.
- Separate idle waiting from bounded message assembly while preserving existing public transport receive semantics.
- Record the live-discovered requirement to load a project and acknowledge readiness before service attachment.

## Capabilities
### New Capabilities
- `compiler-channel-lifecycle`: Internal ownership and bounded message I/O across all four compiler channels.

## Impact
Library and tests only. Closes #1201 under #1178. The component stays internal until production project preparation exists. No project loader, native launcher, request correlation, language-service UI or shared DAP changes are shipped.
