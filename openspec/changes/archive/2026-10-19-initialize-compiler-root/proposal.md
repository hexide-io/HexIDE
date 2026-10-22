## Why
A successful WebSocket upgrade does not establish a usable compiler root connection. The adapter needs the observed initialization exchange, validated metadata and explicit ownership before project loading or channel services can be added.

## What Changes
- Initialize an explicitly supplied fresh root endpoint with Community Edition settings.
- Accept package entries and compiler language data explicitly, encoding their byte lengths without reading user settings or licence keys.
- Correlate the two initialization replies, validate status/PID/channel metadata, and bound interleaved notifications and the total handshake.
- Own the successful root socket until disposal; close it on every unsuccessful initialization.

## Capabilities
### New Capabilities
- `compiler-root-session`: Bounded root initialization and validated channel metadata.

## Impact
Library and tests only. Closes #1199 under #1178. Independent compiler launch, project loading, ongoing root request dispatch, channel attachment and UI services remain subsequent slices. No shared DAP changes.
