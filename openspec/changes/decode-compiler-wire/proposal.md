## Why
The next integration milestone is a compiler session. Its observed channels do not share a stock language/debug adapter wire contract: control channels use binary headers, while language/debugger messages carry JSON, sometimes batched inside a binary envelope. A bounded codec establishes the first testable adapter boundary before session lifecycle is introduced.

## What Changes
- Encode control requests and decode control replies/notifications without interpreting operation codes or payloads.
- Decode single JSON objects and the observed length-prefixed batch format; encode ordinary outbound JSON objects.
- Enforce message-size, batch-count, JSON-depth and UTF-8 checks with deterministic malformed-input errors.
- Record pinned upstream evidence and local version observations without claiming live compatibility.

## Capabilities
### New Capabilities
- `compiler-wire-codec`: Bounded parsing and encoding of the observed external compiler message formats.

## Impact
The isolated container/integration library and tests. Tracks #1194 under #1178. No compiler process, socket connection, document mapping, language services, build/run/debug command or change to existing project behavior. Startup and handshake remain subsequent slices.
