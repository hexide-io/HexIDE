# MCP tools do not re-attach to a resumed session while the IDE keeps running

> **Not explained by entry 3, and as far as is known the client's (#645).** Nothing shut down here, so neither
> the stale session nor the refused GET stream that entry 3 describes was in play. When this was recorded,
> though, a server that did not answer when a session began could be attached by `/mcp` reconnect (measured
> 2026-09-21), which is the remedy to try before a restart.

**Symptom.** HexIDE was running throughout (`HexIDE.Desktop` PID alive, port 5123 `LISTENING`), the session
was restarted twice specifically to pick the tools up, and `mcp__hexide__*` was still absent from the tool
set both times. This is distinct from the known "tools drop when the IDE shuts down" case: nothing shut
down, and the server was answering on its port the whole time.

**Cost.** It blocks the mandatory visual verification in CLAUDE.md, and the documented escape hatch — raw
HTTP — is explicitly forbidden, so the correct response is to stop and ask, which costs a round trip and
sometimes more than one.

**Workaround that does not violate the rule.** For an *isolated* surface, a headless Skia render test does
the job: `AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }` is already configured in
`TestApp.cs`, so `window.CaptureRenderedFrame()?.Save(path)` produces a real PNG that can be read back and
looked at. Used to verify the reworded read-only banner at English and German lengths, and at a narrow dock
width, without the running IDE. This does not replace MCP for anything live or modal — it only reaches
surfaces that can be constructed standalone.

**Suggested fix, as first written.** Make tool discovery retry against `.mcp.json` when the endpoint becomes
reachable mid-session, or have the launcher expose a readiness signal the client re-polls. Both are the
client's to do. The advice it ended with, to start the IDE *after* the session, is superseded: `/mcp`
reconnect attaches either way. No HexIDE issue is open for it: as far as is known the fix is the
client's (#645).
