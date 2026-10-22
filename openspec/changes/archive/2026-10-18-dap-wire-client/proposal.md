# Speak DAP over a pair of streams

## Why

The debugger UI currently controls only the in-process interpreter. External execution backends need a DAP
client (#696). LSP's framing is familiar, but its JSON-RPC envelope is a different protocol. Establish and
test the wire boundary before adding process management, launch configuration or remote UI state.

## What changes

- A framework-independent DAP client library owns a readable and a writable stream.
- Requests use DAP envelopes and correlate responses by request sequence; events retain wire order.
- Unknown JSON fields survive, and unsuccessful responses retain structured adapter errors.
- Cancellation, timeout, malformed input, EOF and disposal settle pending requests. Partial writes close the connection.
- Limits bound incoming frames, pending requests and queued events. Unsupported reverse requests receive a failure response.

## Capabilities

### New capabilities

- `dap-client`: DAP byte-stream communication and connection lifetime.

## Impact

Adds `IDE/HexIDE.Dap`, referenced by existing unit tests and the solution. No new package dependency. This
slice (#1160) exposes no IDE command or configuration; process launch, protocol capture, the active-session
abstraction and real-backend UI integration remain subsequent work under #696. The interpreter stays unchanged.
