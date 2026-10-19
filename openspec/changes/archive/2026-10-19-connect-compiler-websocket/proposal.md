## Why
The complete-message codec cannot enforce limits while a WebSocket message is still arriving. Compiler sessions need a bounded, disposable transport before process startup and initialization can be composed safely.

## What Changes
- Add an owned loopback WebSocket connection with bounded reassembly and I/O deadlines.
- Serialize sends and receives independently; abort interrupted I/O without reconnecting or replaying commands.
- Keep authenticated endpoint paths and peer close text out of transport errors.
- Exercise actual WebSocket framing and HTTP upgrades using local sockets.

## Capabilities
### New Capabilities
- `compiler-websocket-transport`: Explicit loopback connections and bounded message I/O.

## Impact
Library and tests only. Closes #1196 under #1178. No UI, process launch, compiler initialization, request correlation, language service or DAP changes. A live socket connection is verified separately from compiler-session compatibility.
