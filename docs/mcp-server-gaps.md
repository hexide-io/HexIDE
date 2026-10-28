# MCP dev-server gaps (found in use)

Limitations of HexIDE's embedded MCP automation server, each hit while actually driving the IDE rather
than reasoned about in advance. The MCP server is a **dev/automation tool, `#if DEBUG`-only** (compiled
out of Release), so these are developer-workflow gaps, not product bugs. Recorded here so they can be
fixed deliberately; the generic trio (`dump_visual_tree` / `interact` / `inspect_element`) otherwise
works well for non-modal surfaces.

**Everything below is open.** Closed entries move to
[`archive/mcp-server-gaps-closed.md`](archive/mcp-server-gaps-closed.md) — kept rather than deleted,
because what each one records is a *measurement*, and that is the part which stops a fixed gap being
rediscovered as a new one. Retiring them is also what lets this document's length mean something: it is
a count of what still bites.

**Record the exact call an entry failed with, arguments included.** A conclusion drawn from a call with
unstated arguments cannot be checked by the next reader, and it can be wrong without anyone noticing: one
entry here concluded that a document tab's content was "neither drivable nor readable" from a
`dump_visual_tree` run with its default `interactiveOnly: true`, which filters out plain text. With
`interactiveOnly: false` every field was there (#362). Write `dump_visual_tree(root: …, interactiveOnly: false,
maxDepth: 14)`, not "`dump_visual_tree` returns only the tab chrome". Misspelled arguments such as
`dump_visual_tree {"interactive_only": false}` now run with an explicit ignored-argument note and a suggestion
of `interactiveOnly` (#1315); an omitted argument still uses its declared default.

**Write-tool source audit (#2504, not a live probe).** The [file-safety audit](file-safety-audit.md)
records missing disk recovery and project-root containment on source/property/control saves, with their
entry points and remaining work. MCP conversation exports now preserve earlier exports; that fix does
not close the other write-tool gaps. Tracked by [#2504](https://github.com/hexide-io/HexIDE/issues/2504).

---

## 3. Three tool calls while the IDE is down drop the tools until `/mcp` (narrowed 2026-10-12)

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

---

## A tool's schema, once loaded in a session, stays stale after `/mcp`, in subagents too (measured 2026-10-12)

**Symptom.** A rebuild changed two descriptions and added a tool, and the user ran `/mcp`:

```
ToolSearch {"query":"select:mcp__hexide__get_debug_state,mcp__hexide__get_window_state,mcp__hexide__zz_probe_added"}
→ get_debug_state:   the OLD description   (this session had loaded it before the rebuild)
→ get_window_state:  the NEW description   (this session had never loaded it)
→ zz_probe_added:    present               (added in the rebuild)
```

A fresh subagent then asked `ToolSearch` for `get_debug_state` and got the old description too, so the cache
belongs to the Claude Code session rather than to one conversation. The running DLL held the new text, and
`zz_probe_added` answered, so the server was the new build. Claude Code 2.1.296.

**Found by** another session, which saw the same after a compaction: `add_file` and `set_file_content` read as
they are on `main` while the running build was a branch that extends both descriptions, and the tools' replies
were the branch's. That session had loaded both schemas before its compaction.

**Why it matters.** What a session believes about a tool is how it calls it. A description edited for a
first-time caller cannot be judged in the session that edited it, and a changed signature produces calls
built from the old one. That is how the 2026-09-20 entry (archived) failed: a tool gained a parameter, and the
first call after the rebuild failed against the stale schema.

**Workaround.** Check a changed tool in a new session, or headlessly. Do not rely on `/mcp` or a subagent to
refresh it. A tool the session has not loaded yet is safe to load after `/mcp`.

**Suggested fix.** The client's: drop cached schemas for a server when it reconnects, or when its `tools/list`
changes. Nothing on the server side can reach a schema the client already holds.
[#815](https://github.com/hexide-io/HexIDE/issues/815).
Its issue, #815, is closed, and no HexIDE issue is open for what remains, which is the client's (#645).

---

## 10. A crashed IDE is indistinguishable from a slow tool call (narrowed 2026-09-22)

> **Narrowed by #643.** An exception nothing caught is now written to the IDE log with its stack, and the log
> is flushed before the process ends, so the post-mortem no longer needs the Windows event log. Checked live
> with `HEXIDE_DEBUG_CRASH=thread` and `=ui` in a Debug build. **What remains** is the caller's side: once the
> process is dead it cannot answer, so the client still says `Unable to connect`, and the reader must know to
> look in the log. The four IDE-owned timer callbacks named in #652 now catch and log exceptions with
> their purpose and stack, remaining nonfatal. This covers dock-layout capture, the language revert
> countdown, the minimap redraw and the designer code-navigation follow-up; the DEBUG crash hook remains
> an unwrapped framework probe. VB6 Timer events keep their existing interpreter runtime-error handling
> (checked in the later #652 comment). WSL/Ubuntu headless probes on Avalonia 12 confirm that throwing Tick and Run
> callbacks execute without reaching `Dispatcher.UIThread.UnhandledException`; the three logging regressions
> (Tick, Run, RunOnce) each fail with an empty log when the guard is reduced to calling the body directly.

**Symptom.** Driving the designer to compose a screenshot, `add_control` returned success, then the next
call hung for the full 120 s timeout and every subsequent call failed with `Unable to connect. Is the
computer able to access the url?`. That message reads like a networking problem. The IDE had in fact
died — the tool call was fine, the process wasn't. The background-task notification said only
`transport dropped mid-call; response for tool "add_control" was lost`.

**Workaround.** When any tool starts failing to connect, check the process before changing approach:

```sh
curl -s -m 4 -o /dev/null -w '%{http_code}' "http://localhost:${HEXIDE_MCP_PORT:-5123}/health"
```

A 200 says it is alive. Anything else, with your own IDE's PID from `scripts/dev-ide.ps1` in hand, is checked with
`Get-Process -Id <pid>`. Not `Get-Process HexIDE.Desktop`: other sessions' IDEs share the name.

If it's gone, the IDE log (`%LOCALAPPDATA%\HexIDE\logs\ide\`) will end cleanly with no exception,
because an unhandled render-thread exception terminates the process before Serilog flushes. The actual
stack is in the Windows Application event log:

```sh
powershell -NoProfile -Command "Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'; StartTime=(Get-Date).AddMinutes(-25)} | Where-Object { $_.Message -match 'HexIDE' } | Select-Object -First 1 -ExpandProperty Message"
```

That is how the `VBOptionButton` render crash was identified — nothing else surfaced it.

**Fix consideration.** Have the MCP layer distinguish "server unreachable" from "server was reachable and
has now gone", and surface the last few lines of the IDE log with the failure. A `--crash-log` style
handler that flushes Serilog on `AppDomain.UnhandledException` would also make the IDE log self-sufficient.

## Not an MCP gap (recorded to avoid confusion)

- **`Debug.Print` didn't reach the Immediate window** — that was an *interpreter* bug (the `Debug` object was
  seeded only in the test fixture, not the live F5 run), fixed this session (`VBDebugConsole`), not an MCP
  limitation. It's listed here only because gap #1 made it hard to *see* the resulting error.
- **`hexide` missing from `/mcp` entirely, even after resumes and reconnects**: the project-local
  `.claude/settings.local.json` had `"disabledMcpjsonServers": ["hexide"]`, so the client skipped the
  `.mcp.json` entry altogether (2026-09-25). Check that list before diagnosing the server.

## MCP tools do not re-attach to a resumed session while the IDE keeps running

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

## shutdown_ide can kill the MCP server and leave the IDE running

**Symptom.** `shutdown_ide` replied `Unable to connect. Is the computer able to access the url?`. The
process was still alive afterwards with its window intact (`MainWindowTitle` unchanged), but `/health` now
answered `connection actively refused` — the web host had gone down while the application had not.

**Consequence.** The documented recovery for a failed shutdown is to call the tool again, and there is no
tool left to call. Worse, the state reads as "the IDE is gone" from the automation side and "the IDE is
fine" from the desktop, so the next build fails on a file lock for a process the agent believes it closed.

**Workaround used.** `Stop-Process -Name HexIDE.Desktop -Force`, then relaunch — then the documented
fallback for an MCP disconnect, but reached here *because of* the shutdown rather than before it. The fallback is
now `pwsh scripts/dev-ide.ps1 -Stop`, which finds the IDE by the `--server-port` on its command line, so it finds
one in this state, with no server listening, and stops it only if it was started for this session. Stopping by
name would end every session's IDE on a machine running several.

**Suggested fix.** Send the reply before tearing the host down (or stop the host last), so the caller gets
the confirmation the tool promises. Either way, do not leave a window up with no server behind it: if the
shutdown cannot complete, keep the server alive so the next call can say why. Not reproduced since 2026-09-22, so
what sets it off is unknown. [#644](https://github.com/hexide-io/HexIDE/issues/644).

**Ordering hardened for #644 (2026-10-25; live verification pending).** The main-window close now waits
for HTTP response completion, and server/context cleanup waits for desktop `Exit`, rather than the
cancellable `ShutdownRequested` event. Headless regression coverage checks response ordering and the
exit subscription. The original trigger remains unknown; keep this entry open until a running IDE check
confirms a complete reply followed by exit, and a cancelled close leaves `/health` and tools working.

---

## An empty reply that does not say why is a defect, not a null result

**The standing bar for this surface, recorded because it was stated as a correction.** The automation
surface is not internal scaffolding for whoever is building HexIDE. It ships to every developer who wants
it, driven by models nobody here chooses. **A suboptimal AI surface bites exactly as a bad UI/UX surface
bites a human user**, and "I found a way around it" is not the test — the person who found the way around
it had context a first-time caller does not.

**The worked example, and it was mine.** The first call to `list_lsp_messages` in a fresh session returned:

```
{"messages":[],"matched":0,"truncated":false,"framesDropped":0}
```

I knew why: servers start on the first document of a language they claim, and nothing was open. A caller
who did not know that cannot tell "nothing happened" from "nothing was configured" from "the tool is
broken" — **which is the exact ambiguity the protocol inspector exists to destroy**, reintroduced inside
the tool built to destroy it. That is the worst available place to put it.

It now answers:

```
"note": "No language server has connected yet, so there is nothing recorded. Servers start on the first
         document of a language they claim — open a file and ask again. Nothing needs arming for
         envelopes to be recorded."
```

Four states a bare zero collapses, each now named: no server has started; the connection id does not exist
(and here are the ones that do); connections exist with an empty record, so it was cleared; the filter
excluded everything. Six tests pin them, including one asserting the note is **absent** on an ordinary
reply — a field that is always populated stops being read.

**The checklist this generalises to**, for any tool added here:

- An empty or surprising reply explains itself, and says what to do next where there is an obvious next step.
- Check the generated schema's `required` array, not the C# signature. They disagree, and only one is what
  a caller sees.
- Enumerate the vocabulary a reply uses. A `kind` of `Unconsumed` or `Lifecycle` means nothing to somebody
  who was never told the set, so the description now lists all seven and says which three exist nowhere else.
- Explain anything that looks like a defect and is not. Sequence numbers have gaps, because a reply
  completes its request's envelope rather than adding one; beside a field called `framesDropped`, an
  unexplained gap reads as data loss.
- A reply that mutates reports the new state, and reports it even when the answer is empty.

**The mechanical half of this is now tested; whether the prose helps is not.** A description that misleads
a caller produces a wrong call and a green build. `ToolDescriptionParameterTests` and
`FirstContactTranscriptTests` (#535, closing #396) catch a parameter or reply field named in a spelling the
wire does not use, and a worked transcript that has drifted from the tools it shows.
`ToolDescriptionEnumTests` (#529) catches a vocabulary missing a member. #549 made all of them fail when
they meet source they cannot parse. None of them can say whether a description *helps*, and the author is
the one person who cannot judge that — the empty-reply defect above was caught by *being* the caller, not
by re-reading prose that had just been written. That half is the first-contact exercise with a fresh model,
[#534](https://github.com/hexide-io/HexIDE/issues/534), and it is still open.

---

## A newly created pathless form initially reads without its header (#885)

**Symptom, since #273 task 3.18.** Read in code, not run. `get_file_content`
returns a form's whole file, except a form created in this session and not yet saved or changed in the designer,
which has no header yet, so its text is its code alone; the tool's description says so. Its first committed designer
change or save gives it one (`HeaderRefresher.ApplyHeader`), its marks move down by the header's height, and from then
its lines count from the top of that header. So a line number read before then is out by the header's height after
it. **Workaround:** read the content again after any designer change or save before using a line number from it.
Where a created form's first header should come from, so that it holds one from the start, is
[#885](https://github.com/hexide-io/HexIDE/issues/885).

## Three tools accept a negative control size and report success

**Symptom.** On a `--newproject` form in the designer:

```
add_control {"formName":"Form1","type":"commandbutton","x":-50,"y":5000,"width":-10,"height":0}
→ {"success":true,"controlName":"Command2"}
set_control_property {"formName":"Form1","controlName":"Command1","property":"Width","value":"-10"}
→ {"success":true}
move_control {"formName":"Form1","controlName":"Command1","left":null,"top":null,"width":-20,"height":null}
→ {"success":true}
```

`add_control` and `set_control_property` save the form, so the negative size reaches the `.frm` on disk,
and the project then cannot start. The start is refused with the reason, which was
#590 (closed, see the archive). A drag in the designer cannot produce a negative size;
nothing in the property model refuses one.

**Workaround.** Do not pass a negative `width` or `height`; read `get_form_controls` back after a size change.

**Suggested fix.** One rule in the shared property validation, so the Properties window and all three tools
refuse the same values with the same message. What that rule is (refuse, clamp, and whether `0` is allowed)
needs measuring against `vb6.exe` first:
[#589](https://github.com/hexide-io/HexIDE/issues/589). A negative `Left` or `Top` is ordinary VB6 and not
part of it.

## A newly added module reads as having unsaved changes although its file matches

**Symptom.** On a saved project, `add_file {"name":"Helpers","type":"Module"}` succeeds and writes
`Helpers.bas`. Then `get_file_content {"name":"Helpers"}` (`project` left at null) answers
`{"content":"","hasUnsavedChanges":true}`, although saving would write the same bytes. The IDE's own
`invoke_menu_item {"path":"Project/Add Module"}` does the same for `Module3`. An untouched module loaded from
disk reads `false`.

**Resolved (#500, #597).** #489 measured that a new document has no file until it is saved, and #500
removed the immediate write. The reply now correctly reports `hasUnsavedChanges: true` for a new
form, module, class, UserControl or PropertyPage. `get_file_content`'s description explicitly says that
a document with no file yet always has unsaved changes. Save Project asks where to write it; after
saving, an untouched document reads `false`. No render baseline should be recorded at creation.

`WriteAtSaveTests.A_new_member_has_unsaved_changes_until_its_first_save` covers the transition for all
five document kinds, in both saved and unsaved projects. The calls above record the original symptom,
before #500; they no longer describe a file written by `add_file`.


## The code window's gutters are not in the control tree, so a gutter click cannot be driven

**Symptom (2026-10-01, during #273 task 3.12's live check).**
`dump_visual_tree {"root":"…/Custom[Root]/None[TextEditor]/Pane[PART_ScrollViewer]/None","interactiveOnly":false,"maxDepth":4}`
returns the `TextArea` with two children: an empty `ItemsControl` and the hidden `PART_Watermark`. The
breakpoint gutter, the bookmark gutter and the line-number margin are all absent, because AvaloniaEdit's
`AbstractMargin` has no automation peer, so `interact` has nothing to click. The task's own named case, a
click on a folded header, could only be tested headlessly (`MarkGutterOnReadOnlyLineTests`, which raises
the press on the gutter itself).

**Workaround.** Place the caret with `interact set_property CaretOffset=N` on the code view, then
`invoke_menu_item Debug/Toggle Breakpoint` or `Edit/Bookmarks/Toggle Bookmark`. That reaches the same
toggle a gutter click calls, but not the gutter's own mapping from a point to a visual line, which is
what decides what a click on a folded row marks.

**Suggested fix.** Undecided: a peer on the gutters that `interact` can drive with a line, a `click` action
on `interact` at a point relative to its target (which would cover other drawn surfaces too), or a
dedicated tool if the authoring policy justifies one. Filed as
[#707](https://github.com/hexide-io/HexIDE/issues/707).

**The fold margin is the same (2026-10-02, during #273 task 3.14's live check).**
`dump_visual_tree {"interactiveOnly":false,"maxDepth":40}` over the whole window has no node whose class is
`FoldingMargin` or `FoldingMarginMarker`, so a fold cannot be expanded or folded by its marker. The
`TextEditor` node does report `expandCollapse`, which reads as the answer and is not: `inspect_element` on it
shows no expand state, and the provider is advertised because the editor owns a context menu, which is what
`expand` opens. **Workaround:** placing the caret inside a folded section with `interact set_property
CaretOffset=N` on the code view expands it, because the editor unfolds any fold the caret moves into. Nothing
folds one again in that build. Since #1014, Edit → Toggle Fold can toggle the innermost section at the caret
or starting on its row, including a member run beside its procedure fold. This supplies a command workaround;
it does not address clicking the margin itself. A `click` action on `interact` would cover this margin as well
as the gutters.

**A member's Attribute lines fold too since #273 task 4.3**, into a box at the end of the line they describe, and
the workaround reaches them with one difference, read in code and not run. An offset strictly inside the run opens
it, as for any fold. An offset at the run's very end does not: the window moves a caret that lands after a folded
run's box to the end of the declaration, in front of the box (`CodeEditorView.KeepCaretOffFoldedRunsEnd`), and the
run stays folded.

## `set_file_content` puts its content into a file the IDE will not save, and its refusal says only that the file was not written

**Symptom.** Read in code, not run, at ec9bfc4. On a form HexIDE holds read-only as a whole because it cannot save it
faithfully, such as the `frmAbout` with no `.frx` that #273's live check used on 2026-10-13 (archived under the #649
entry):

```
get_file_content {"name":"frmAbout"}
set_file_content {"name":"frmAbout","content":"<that text, with one line of code changed>"}
→ {"success":false,"error":"HexIDE cannot reproduce this file faithfully, so it was not written and the copy on
   disk is unchanged.", "project":…, "document":"frmAbout"}
get_file_content {"name":"frmAbout"}
→ the text that was sent
```

The content is applied before the save is refused. `DocumentContent.Write` asks `GuardedContent` about the header
and members' Attribute lines, never about the file as a whole, so the open code window's buffer takes the content, or
with no window open the model's code does; then `ProjectService.SaveForm` refuses the form (`RefusesUnfaithful`). The
reply says nothing of the IDE's copy, where the same tool's encoding refusal says "The new code is in the IDE, but
the file was not written." A caller reading `success:false` takes it that nothing changed. The window's banner says
typing in it is refused, and `type_text` and `press_key` are refused there with that reason, yet its text has
changed, and no save will write it.

**Workaround.** Before sending content, open the file's code window and `inspect_element` its code view, which lists
the view model's `IsReadOnly` and `ReadOnlyReason` (read in code; #273's check read `BufferBody` there). When
`IsReadOnly` is true the file is held read-only as a whole: send nothing. After this refusal, read it as "the IDE
holds the content and no save will write it", and put the IDE's copy back by sending the text `get_file_content`
returned before the write, which is applied the same way.

**Suggested fix.** #701's: refuse in the guarded write path, before anything changes, with the reason the banner and
`type_text` give. The issue lists this tool among the writers that still edit such a file.
[#701](https://github.com/hexide-io/HexIDE/issues/701).


## `invoke_format_command`'s no-op reply states a selection rule the spec contradicts

**Symptom.** With one control selected (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf,
2026-10-13):

```
interact {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/Pane/None[FormContainer]/List[ControlsContainer]/ListItem[Command1]","action":"select"}
invoke_format_command {"command":"AlignLefts"}
→ "Nothing changed, and no undo step was added: the selection is Command1. The spacing commands need two or
   more selected controls, the others at least one, and a selection that already satisfies the command is
   left as it is."
```

The spec makes the nine alignment and sizing commands unavailable below two selected controls, and with one they
can never change anything, so "at least one" sends a caller back with the same single selection. The text is
hard-coded and the command table has no per-command minimum. Cross-platform by code reading.

**Workaround.** Before any Align* or MakeSame* command, select two or more controls with `interact` `select` and
then `add_to_selection`. Read "at least one" as true only of SizeToGrid, CenterHorizontally and CenterVertically.

**Suggested fix.** A per-command minimum in the `FormatCommands` table, a `success:false` refusal below it that
names the rule and points to `add_to_selection`, and the no-op note generated from the same table, so the prose
cannot drift from the code again. [#930](https://github.com/hexide-io/HexIDE/issues/930).


## `set_window_state` replies with the window's state from before the change on Linux

**Symptom.** Each call followed by `get_window_state {}` (Linux, openSUSE Leap 16 under XWayland, Debug build of
main at b15c7cf, 2026-10-13):

```
set_window_state {"state":"Normal"}
→ {"window":{"state":"Maximized","x":0,"y":32,"width":1920,"height":1011}}
get_window_state {}
→ {"state":"Normal","x":240,"y":171,"width":1440,"height":733}

set_window_state {"state":"Normal","x":100,"y":100,"width":1280,"height":720}
→ position updated, width and height still 1440x733
get_window_state {}
→ 1280x720

set_window_state {"state":"Maximized"}
→ {"window":{"state":"Normal", ...}}
get_window_state {}
→ Maximized, 0,32 1920x1011
```

The reply says `success: true` and carries the old value in the field described as the resulting state. #602's
re-read waits only for HexIDE's own layout, and on Avalonia 12.0.4's X11 backend, which every Linux session uses,
XWayland included, state and geometry change only when the window manager answers. Linux-specific by code
reading: the other backends, Headless included, take the requested value at once, so tests cannot reproduce it.
Read in code and not run, from Maximized: a restore with a size writes the maximised origin back as the position,
and a refused off-screen position cannot undo a restore already sent while the reply says "Nothing was changed."
The archived #602 entry's "read after layout, so a new size is not reported as the old one" holds only where the
platform applies a change at once.

**Workaround.** Ignore the reply's `window` and re-read with `get_window_state {}`.

**Suggested fix.** Wait, bounded at about a second, until the window reports the target state and, when geometry
was passed, a move or resize has arrived; on timeout, reply with what the window reports and a note to re-read.
Check the frame against the screens before any state change, so a refusal never has to undo a restore.
[#946](https://github.com/hexide-io/HexIDE/issues/946).

## A document save reached through a menu, key or click can reply before its picker opens

**Symptom.** Narrowed by headless measurements for [#1335](https://github.com/hexide-io/HexIDE/issues/1335),
2026-10-23 (`UiSaveAnswerTimingTests`; no MCP calls were made). After arming a cancel with
`ScriptedFileDialogs.AnswerNextWith(null)`, these tests read `ScriptedFileDialogs.Pending` immediately after the
same action code the tools use returns:

- `invoke_menu_item(path: "File/Save Project As...")`: resolving the real MainView menu and executing its routed
  command takes the answer before returning: **0 pending**.
- `interact(target: "Window/#Standard.SaveProject", action: "click", window: "ide")`: the real toolbar button's
  press and release take the answer before returning: **0 pending**. The count is read after `FinishAsync`, as in
  the tool, not after the press alone.
- `press_key`: the exact headless call is
  `UiAutomationDriver.PressKey(editor, gesture.Key.ToString(), gesture.KeyModifiers.ToString())`, with the real
  CodeEditorView's TextEditor and `gesture = ApplicationCommands.SaveCommand.Gestures.Single()`: **0 pending**
  without Format on Save; **1 pending** while a formatting response is held, then **0** when the response completes.
  The test reads the platform binding (Ctrl on Windows/Linux, Cmd on macOS), rather than hard-coding Ctrl.

Project-save menu and button replies now reuse #810's pending-answer sentence when no picker took an answer,
with the reason "this save opened no picker" (a project command can save several documents). The tests also
exercise an empty project set, where **1 remains pending**, and assert the reply, not just the count.

The remaining gap is the focused document's save. `CodeEditorViewModel.SaveForm` / `SaveModule` discard the
`SaveWithFormattingAsync` task through `ListenErrors`; `RoutedCommand.Execute`, the command binding, and key /
pointer delivery expose no save task. Awaiting the action or draining the dispatcher cannot await an arbitrary
language-server response. A pending count at that point does not justify saying the save opened no picker.
The same document commands can be reached by a menu or `interact`; they are deliberately excluded from the
project-command feedback.

**Workaround.** `answer_next_file_dialog` warns about document-save timing. `clear_file_dialog_answers` reports
and discards answers still pending, but if formatting is still running, clearing an answer can remove it before
the eventual picker takes it. Prefer the tools that await their save and report the answer's state where applicable.

**Suggested fix.** Expose the focused editor's save task through routed-command execution before adding
pending-answer feedback to document saves. Waiting for dispatcher idle or adding a fixed delay would not establish
completion. The remaining scope is [#1335](https://github.com/hexide-io/HexIDE/issues/1335).

## Two sessions that set neither variable reach the same IDE, and nothing in a reply says so (narrowed 2026-10-22)

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

## `press_key` with Alt and a letter never reaches a control's access key

**Symptom (2026-10-23, #1390, Windows).** On a running form whose `cmdWide` is captioned `One &Wide`,
`press_key {"target":"Window/Button[cmdTom]","key":"W","modifiers":"Alt","window":"Form1"}` answered
`{"success":true,"mechanism":"keyboard","detail":"pressed Alt+W"}`, and `press_key {"target":"Window","key":"W",
"modifiers":"Alt","window":"Form1"}` answered `pressed Alt+W on VBCommandButton, at Window/Button[cmdWide]`. Neither ran
`cmdWide_Click`. On the IDE, `press_key {"target":"Window","key":"F","modifiers":"Alt","window":"ide"}` highlighted
File without opening it. `press_key` raises routed `KeyDown` and `KeyUp` on the target, which Avalonia's access-key
handling doesn't act on.

**Workaround.** None through the surface. A headless test sending the chord as raw input
(`window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Alt)`) with a button focused clicked `cmdWide`, so the product
works.

**Suggested fix.** Deliver the keys through the window's input path, as real keystrokes, the way `interact click`
does for the pointer, or offer that as a mode and say in the reply which path was used. Until then, say in the
description that an access key can't be reached. [#1523](https://github.com/hexide-io/HexIDE/issues/1523).

## A border given a name but no type is folded away, name and all, so `match` cannot find it

**Symptom (2026-10-22, #157, headless).** The designer's box for a control HexIDE does not model was a
`ClassicBorderDecorator` with `AutomationProperties.Name` set to the control's name. `dump_visual_tree`'s `match`
path, `UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false), "imlIcons", 10)`,
the code `{"match":"imlIcons"}` runs, found nothing. A border's control type is `None`, it has no provider and takes no
focus, so the control view hides it as plumbing, and the name it was given goes with it. This is the rule
[#1436](https://github.com/hexide-io/HexIDE/issues/1436) is about, reached from the other side: a name set on purpose is
no more plumbing than an AutomationId.

**Workaround.** Give the element a type: `AutomationProperties.ControlTypeOverride="Pane"` keeps it, as `VBProps` does
for a running form's Frame, PictureBox, Label and Image.

**Suggested fix.** Count an explicitly set `AutomationProperties.Name` as the AutomationId already counts, so a view's
author is not left to discover the override. The path-stability part of #1436 is fixed; this explicit-name
discovery gap remains open.

## No tool says which control has the focus, and `press_key` moves it there

**Symptom (2026-10-23, #1583, Windows).** On a running form with Text1 focused, whose program logs every GotFocus,
`press_key {"target":"Window","key":"Tab"}` answered `{"success":true,"mechanism":"window-input","detail":"pressed Tab
on VBTextBox, at Window/Edit[Text3]"}`. Six of them logged `Text3_GotFocus`, `Text1_GotFocus` six times. `PressKey`
calls `target.Focus()` before delivering the key, and a container's target is its first focusable control. So each
press moved the focus to Text3, raising the program's own events, and Tab then went round to Text1. The reply names
Text3, but not that the focus was moved there.

**Workaround.** Address the control that has the focus. Nothing reports it: `inspect_element` gives
`isKeyboardFocusable`, not focus, and `dump_visual_tree` has no focused node. So a Tab sequence can be driven only by
knowing where each press will land, which assumes the order being checked. A wrong guess moves the focus to the guess,
and the program's log shows it as a GotFocus that no Tab caused.

**Suggested fix.** Report `isFocused` from `inspect_element`, and the focused node in `dump_visual_tree`. Press on the
focused control when the target is a container that holds the focus. Say in the reply when delivering the key moved the
focus, and from where. [#1627](https://github.com/hexide-io/HexIDE/issues/1627).

## `set_value` on a read-only ComboBox throws, though the combo lists the `value` provider

**Symptom (2026-10-24, #1632, Windows).** In a UserControl's code window, `inspect_element` on the Object box,
`ComboBox[ObjectCombo]`, listed providers `selection`, `value` and `expandCollapse`.
`interact {"target":"…/ComboBox[ObjectCombo]","action":"set_value","value":"UserControl1"}` answered
`{"success":false,"mechanism":"peer","error":"action 'setvalue' threw: Specified method is not supported."}`.
- **Why it throws.** The combo isn't editable, so its value pattern is read-only, and setting it throws
  `NotSupportedException`.
- **Why the reply doesn't help.** It passes the exception's text through and doesn't say what does work. A caller who
  was shown `value` among the providers has no reason to expect the refusal.

**Workaround.** `expand` the combo, then `select` the item. In the PropertyPage's code window,
`…/ComboBox[ObjectCombo]/Pane/ComboBoxItem[PropertyPage1]` answered `selected 'PropertyPage1' and closed the dropdown,
as a click does`. Or use `set_property` on the code window's view model: `SelectedObject=UserControl1` answered `set
CodeEditorViewModel.SelectedObject = UserControl1`.

**Suggested fix.**
- On a ComboBox that isn't editable, have `set_value` select the item whose text matches, as `select` does, and report
  the selection.
- Where no item matches, refuse, and name the items.
- Either way, don't list `value` as settable on a read-only control without saying so: give `inspect_element` an
  `isReadOnly` for the value pattern.

[#1672](https://github.com/hexide-io/HexIDE/issues/1672).

## Properties category rows refused expand/collapse (#1967 — fixed)

**Symptom.** With Categorized Properties showing, `interact {"target":"…/List[CategorizedProperties]/Pane[PART_ScrollViewer]/ListItem[Appearance]","action":"collapse","window":"ide"}` refused with `element does not support 'collapse'`.

**Workaround.** `interact {"target":"…/ListItem[Appearance]/Button","action":"toggle","window":"ide"}` operated the unnamed toggle beneath the row.

**Fix.** Category rows now expose an expand/collapse provider tied to `IsExpanded`; their toggles use the localized category header as their accessible name. Inspection reports the provider's `expandCollapseState`.

## `match: "TextEditor"` does not find the Immediate window, though the description says it does

**Symptom (2026-10-28, Windows, #2496's live check).** With the Immediate window shown and seven lines of a program's
`Debug.Print` output in it, `dump_visual_tree {"window":"ide","match":"TextEditor"}` answered "Nothing matches
'TextEditor'", and so did the same call scoped with `root` to the Immediate pane. `match: "Immediate"` with
`interactiveOnly: false` found only the hidden toolbar button. The tool's description promises that `TextEditor` finds
"the Immediate window's" editor. The window is an `ImmediateToolView` over an unnamed `TextArea`, and no node carries
its text, so a caller cannot tell an empty window from one it cannot reach.

**Workaround.** `inspect_element` on `.../TitleBar[Immediate]/Pane[Immediate]/Custom`, the `ImmediateToolView`: its
`dataContextMembers` give `Document` with the whole text as its value.

**Suggested fix.** Give the editor a node `match: "TextEditor"` finds and `inspect_element` reads, or correct the
description to name the route that works; better, a read of the Immediate window's text, the commonest check after a run.
[#2513](https://github.com/hexide-io/HexIDE/issues/2513).
