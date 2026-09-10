# Inspect the conversation with a language server

## Why

A language feature that fails to appear has four possible causes, and this IDE can distinguish none of
them: the client never asked, the server was never asked because it advertised nothing, the server answered
nothing, or a good answer arrived and was rendered wrong. Only the last is a user-interface defect. Today
all four present identically, as nothing happening.

Nothing here can watch the wire. The out-of-process debug proxy is off in every default launch profile and
cannot resynchronise a stream it fails to parse, and **zero** of the protocol-shaped issues in this
repository's history were found by reading a HexIDE transcript — one was filed against a confident guess and
had to be retracted.

The audience that needs this most is not a HexIDE user. It is somebody writing a language server against
HexIDE.

## What Changes

- **The client learns the protocol's own tracing.** `trace` at initialize, `$/setTrace`, `$/logTrace`, and
  the message severity LSP 3.18 added. The bundled server learns to emit `$/logTrace`, so the client half
  has something conformant to prove itself against.
- **A capture model, with no user interface.** Every connection's envelopes recorded always in a bounded
  ring; payloads only when that connection is armed. Bytes captured, decoded presented. Process lifecycle,
  standard error, requests the client declined to send, and capabilities the server advertised that this
  client does not consume all share the record.
- **Automation tools over that model**, plus a launch flag, so the capture is drivable and readable before
  any window exists.
- **A Protocol Inspector document tab** showing one interleaved timeline with the server as a filter, and
  the first interactive controls the Language & Debug Servers window has ever had.
- **The debug proxy is retired**, its purpose absorbed.

## Impact

- **New capability** — `openspec/specs/protocol-inspector/`.
- `openspec/specs/lsp-client/spec.md` — the trace facilities.
- `openspec/specs/hexide-mcp-server/spec.md` — the tools over the capture.
- Retires `IDE/HexIDE.LspProxy` and the `VB6_LSP_DEBUG_PROXY` environment variable.

Tier: **Evolution**. VB6 had no equivalent and needed none, having no external language service to talk to.
