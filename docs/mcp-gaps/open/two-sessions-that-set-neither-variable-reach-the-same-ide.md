# Two sessions that set neither variable reach the same IDE, and nothing in a reply says so (narrowed 2026-10-22)

**Symptom.** Measured 2026-10-22 with two Debug IDEs, one on port 5128 with a new project and one on 5130 with
`SpringTide.vbp`. A headless session started with `HEXIDE_MCP_PORT=5128` called `get_project_info` and got
`Project1`; one started with `HEXIDE_MCP_PORT=5130` got `SpringTide`. So a port picks the IDE, and the replies are
the same whichever IDE answers. Since #1212, sessions that each set `HEXIDE_MCP_PORT` stay apart. Two sessions that
set nothing both reach 5123, and a session given the port of a stale IDE left running drives that IDE. Either
way, the replies look exactly as they would from the session's own build.

**Workaround.** Start the IDE with `pwsh scripts/dev-ide.ps1`, which returns only once `/health` answers with the
PID of the process it started, and stop it with `shutdown_ide` or `scripts/dev-ide.ps1 -Stop` rather than leaving it
running.

**Narrowed by #1306.** An IDE started with `--session <name>`, as `scripts/dev-ide.ps1` starts one whenever
`HEXIDE_SESSION` is set, serves a tool call only when the client's `X-HexIDE-Session` header names that session, and an
IDE started without one refuses a client that names a session. Each refusal is an error result naming the IDE's session,
PID and build directory, so it never counts towards the client's three connection errors. Guarded by
`SessionGuardTests`. What is left is the case with no names at all: two sessions that both set neither variable both
reach 5123, and an IDE there serves both, since neither names itself.

**Suggested fix.** None that needs no setup: telling two unnamed clients apart needs something a client sends
unasked, and Claude Code sends nothing per session to an HTTP MCP server. Start every session with its variables;
CLAUDE.md's *Parallel sessions* section says how, and tells a session to check them before launching an IDE.
