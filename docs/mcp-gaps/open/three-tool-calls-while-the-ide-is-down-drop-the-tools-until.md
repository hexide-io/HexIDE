# 3. Three tool calls while the IDE is down drop the tools until `/mcp` (narrowed 2026-10-12)

Additional client-side observation, 2026-10-18: in a later agent session, no `mcp__hexide__*` tools
were present in the tool catalog even after launching the IDE and confirming its PID through `/health`.
The call `tools.mcp__hexide__answer_next_file_dialog({path:
"<repo>/artifacts/dap-frames/artifacts/live/launch.json"})` failed locally with
`TypeError: tools.mcp__hexide__answer_next_file_dialog is not a function`; it never reached the server.
This is missing tool discovery, not a rejected file dialog. Reconnect the MCP integration while the IDE
is running; automatic tool rediscovery after server recovery remains a client-side improvement.
Restarting the client while the IDE was running restored discovery: `dump_visual_tree` and the
file-dialog/menu calls then succeeded, allowing live verification to resume. Raw transport calls
are not a substitute for restoring the schemas.

> **What remains is the client's, and it is narrower.** With the server stateless (#763), a shutdown no longer
> drops anything by itself. Tool calls made while the IDE is down still can:
>
> ```
> shutdown_ide {}                  → requested; process exited 13:39:49 UTC
> get_project_info {}              → ECONNREFUSED   (log: "Terminal connection error 1/3")
> get_project_info {}              → ECONNREFUSED   (log: "Terminal connection error 2/3")
> get_project_info {}              → "MCP server \"hexide\" is not connected"
>                                    (log: "3/3", "Closing transport (max consecutive terminal errors)")
> ```
>
> The client then retried at 1, 2, 4 and 8 seconds and logged "Max reconnection attempts (5) reached, giving
> up". The IDE answered again about 35 seconds after the process exited, and every `mcp__hexide__*` tool stayed gone until `/mcp`.
> The three calls were issued together in one turn. A single refused call is harmless, and the count resets at
> the next success (measured on the earlier cycles the same day); two in a row were not measured. Claude Code
> 2.1.296.
>
> **Workaround:** while the IDE is down, poll `/health`, not a tool. No server-side fix exists: nothing
> answers for a server that is not running. Retiring this needs a client that re-attaches a stateless server
> once it answers again.
>
> **Half of it was the server, and that half is fixed by #763.** Until 2026-10-12 the server ran the SDK's
> default transport, which is stateful. That gave a relaunch two ways to break an attached client:
>
> - **The session.** A relaunched IDE answers the old `Mcp-Session-Id` with 404 "Session not found". Claude
>   Code recovers from that by re-initialising. The SDK's own clients do not.
> - **The GET stream. This is what dropped the tools.** Claude Code holds a GET open to a stateful server, and
>   a shutdown refuses its reconnects. From Claude Code **2.1.282**, three refused reconnects make it close the
>   transport ("Closing transport (max consecutive terminal errors)") and retry at 1, 2, 4 and 8 seconds. If
>   the IDE is still down about 15 seconds after the transport closed, it logs "Max reconnection attempts (5) reached, giving up"
>   and stays down until `/mcp`. A Debug rebuild always takes longer than that. The stream lives about five
>   minutes after each connect, so only a shutdown inside that window meets it.
>
> **The evidence** is Claude Code's own MCP log for `hexide`, under
> `%LOCALAPPDATA%\claude-cli-nodejs\Cache\<project>\mcp-logs-hexide\`, one file per session.
> - **2.1.270 to 2.1.278:** refused reconnects were logged as "Terminal connection error 1/3" hundreds of
>   times (265 in one session), and the counter never reached 3/3. So shutdowns did happen inside the window,
>   and none was fatal.
> - **From 2.1.282:** every "3/3" is followed by the closing-transport line. The first give-up was observed on
>   2.1.282, on 2026-09-25.
> - **2.1.283, 2.1.286 and 2.1.296** all show the same.
>
> So the measurements below that relaunches were survived (2026-09-08, 09-11 and 09-22) were true, and were a
> property of 2.1.278 and earlier. The rule CLAUDE.md drew from the 2026-10-05 failure, that a reconnect's
> attachment does not survive a shutdown, named the wrong variable. The variable is the client version and the
> age of the GET stream.
>
> **The fix:** the server runs stateless (`McpTransport`), so there is no session to go stale and no stream to
> refuse. `McpRestartTests` proves the server side under the SDK's real client.
>
> **Confirmed live on 2026-10-12** (main a816e0d, Claude Code 2.1.296). Three `shutdown_ide` and relaunch
> cycles: two within a minute of a connect, one of them holding the IDE down for 40 seconds, and a third, 6
> minutes 41 seconds after it, that rebuilt with a throwaway extra tool. The tools survived every one. The
> added tool did not appear without `/mcp`: ToolSearch found nothing, while the running DLL contained it.
> A single `get_project_info` call made while the IDE was down answered `ECONNREFUSED`,
> logged as "Terminal connection error 1/3", and the count reset at the next successful call. Three such calls
> in a row are the narrower gap at the top of this entry.
>
> **The cost:** a tool added in a rebuild used to arrive through the 404's re-initialise; a changed one never
> did (the 2026-09-20 entry, now archived). An added one now needs a `/mcp` reconnect, measured above. A changed
> one is seen only by a session that has not loaded it, which `/mcp` does not change (the next entry).
>
> **A 400 at every switch-over, in both directions.** A session attached to a build from before #765 holds a
> session id, and a build from after it answers that id with 400 ("Mcp-Session-Id header is not supported in
> stateless mode"). The other way round, a session that never received an id meets a stateful build that
> answers 400 to any request but `initialize` without one. So it recurs whenever an older and a newer build
> are swapped on port 5123, and several worktrees here predate #765.
>
> **Not met on the one switch-over measured, stateful to stateless on 2026-10-12, and the client's log says
> why.** The old build's session had expired while idle (the SDK's default idle timeout is two hours, and it
> had been idle for eleven), so the `shutdown_ide` call got the 404 and Claude Code re-initialised against the build it was about to shut down. That new session's GET stream then met the
> shutdown: three refused reconnects, "Closing transport (max consecutive terminal errors)", and retries at 1,
> 2, 4 and 8 seconds. The fifth attempt, 15 seconds after the transport closed and 18 after the shutdown,
> reached the stateless build, which had been compiled
> beforehand in another worktree. So the session crossed over by a fresh connect, with no id to refuse. It
> survived only because the last retry landed: a switch-over that waits on a build loses the tools until
> `/mcp`. How the client handles the 400 itself is still unobserved, and the idle timeout narrows when it can
> occur at all: only within about two hours of a stateful build's last request. After a quiet spell, a
> switch-over takes the 404 path instead.
>
> Unchanged: the client keeps a cached schema for a tool name it already knows, and does not attach at all
> when a session began with no server answering (#645).

**What was observed, and what it now means.** Each of these is kept as measured.

- **The original symptom.** Shutting the IDE down disconnected the `hexide` server and removed every one of its
  tools from the session: `ToolSearch("mcp__hexide__…")` returned "no matching deferred tools". Relaunching
  the IDE (health 200) did not bring them back; a session resume did. Shutting down is required for any
  rebuild that holds file locks, so the documented dev loop (`shutdown_ide`, build, relaunch, verify through
  MCP) broke at its last step. This predates the earliest Claude Code log kept here (2026-09-14), so the
  version story above neither confirms nor explains it.
- **2026-09-08.** A new tool (`get_last_runtime_error`) was added, the IDE stopped, rebuilt and relaunched, and
  the tool reached the running session without a restart, as a `deferred_tools_delta`. An audit then found
  that an earlier piece of evidence had been a full restart misread as a mid-session add.
- **2026-09-11, twice.** With the IDE attached since session start, `shutdown_ide`, rebuild and relaunch left
  the existing tools callable, and a tool added in the same build (`get_lsp_capture_state`) arrived and worked
  on its first call. The same day, four tools added while no IDE was running at session start did not appear
  on launch, because there was no attachment to re-list from.
- **2026-09-22.** About a dozen relaunches in one session that began attached; existing tools stayed callable
  every time, and a changed description was not picked up until a `/mcp` reconnect.
- **2026-10-05.** On 2.1.286, a session attached by `/mcp` reconnect dropped every tool at `shutdown_ide`, and
  a relaunch did not bring them back.

The 09-08, 09-11 and 09-22 runs were survived because a refused reconnect was not yet terminal, and new tools
arrived because the 404 made Claude Code re-initialise and re-list. The 10-05 run was the GET-stream give-up.
Whether the session attached at its start or by `/mcp` played no part in either.

**Workaround now.** After a rebuild that adds or changes a tool, have the user run `/mcp` and reconnect
`hexide`. Headless integration tests (`HexIDE.Integration.Tests`) remain the better check for anything that
does not strictly need the live IDE. While the IDE is down, poll `/health` rather
than calling a tool: three refused calls in a row drop the tools until `/mcp`.

**Retiring this entry** needs the client change named at the top. Everything #765 asked to be checked live has
been, except what the client does with a 400, which no switch-over has produced yet.
No HexIDE issue is open for it: what remains is the client's, the footing #645 kept this entry on.

**SDK upgrade (#764).** The server now uses 2.2.0, supporting `server/discover` and revision 2026-07-28;
legacy clients remain stateless too. The historical Claude Code measurements above are not measurements of
this SDK. Recheck a fresh connection for the absence of the legacy negotiation fallback, then call an existing
tool before and after a rebuild/relaunch, including a 40-second downtime, and check that the tools remain
callable without `/mcp`. Repeat tool-list discovery after adding a tool. The comment on #764 suggests
`notifications/tools/list_changed` on a call's response stream; no notification or subscription handler has
been added, and its effect on Claude Code remains unmeasured. Keep this entry open until the client behaviour
is measured again; the upgrade alone does not retire it.
