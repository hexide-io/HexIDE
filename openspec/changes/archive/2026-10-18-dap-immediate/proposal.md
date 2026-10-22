## Why
The external debugger can inspect a selected frame but cannot yet evaluate Immediate commands in it.
This closes that gap for explicitly launched sessions. Tracks #1174, part of #696.

## What Changes
- Send stop-scoped DAP evaluate requests with the selected frame and REPL context.
- Route Immediate input and replies asynchronously, preserve adapter syntax, and refresh locals.
- Refuse overlapping input and replace expired replies with a clear diagnostic.

## Scope
One slice: Immediate evaluation. Watches, hover, and specialized MCP commands remain separate.
