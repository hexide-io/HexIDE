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
maxDepth: 14)`, not "`dump_visual_tree` returns only the tab chrome".

---

## 3. Three tool calls while the IDE is down drop the tools until `/mcp` (narrowed 2026-10-12)

Additional client-side observation, 2026-10-18: in a later agent session, no `mcp__hexide__*` tools
were present in the tool catalog even after launching the IDE and confirming its PID through `/health`.
The call `tools.mcp__hexide__answer_next_file_dialog({path:
"C:/Repos/GitHub/HexIDE/HexIDE/artifacts/dap-frames/artifacts/live/launch.json"})` failed locally with
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

## 7. A declarative `ToolTip.Tip` cannot be made to open (narrowed 2026-09-08)

**Was:** *No pointer-hover action — can't trigger a data tip (or any hover) via MCP.* A `hover(target, x?, y?,
dwellMs?)` action now exists and raises real `PointerEntered`/`PointerMoved`, so the editor half of this is
closed. What remains is narrower and has a different cause, so the entry is narrowed rather than archived.

**Symptom.** `hover` reaches anything that opens a tip from its **own** pointer handler, but not a tip Avalonia
manages. Measured against the running IDE:

| Target | Result |
|---|---|
| Code editor over an identifier | `tip: count As Integer` — the LSP quick-info tip, text asserted |
| `Button[Standard.AddForm]`, which carries `ToolTip.Tip` | `no tip opened within 1000ms` |

`ToolTipService` opens a declarative tip in response to `IsPointerOver` changing. That property is set by the
input manager from a real device position; a synthetically raised `PointerEntered` does not set it, and it is
not publicly settable. So the toolbar button's tip cannot be made to appear at all.

**Workaround, and it is deliberately not a fix.** When nothing opens, `hover` reports what the control
*declares* — `declared tip: Add Form` — read straight off the attached property. That answers the question a
caller usually has (*has this button the right tooltip?*) without pretending a popup appeared: an observed tip
is reported as `tip:` and a declared one as `declared tip:`, and the two are never merged. Asserting the tip's
**existence on screen**, or its placement, is still out of reach for this case.

**Two further measurements worth keeping.**

- **A synthetic tip opens at the REAL pointer, wherever it last was over this window.**
  `CodeEditorView.axaml.cs:219` sets `ToolTip.SetPlacement(TextEditor, PlacementMode.Pointer)`, so Avalonia
  anchors the popup to the pointer position **it** tracks for that `TopLevel` — and a synthetically raised
  `PointerMoved` does not update that. The position is also *per window* and *sticky*: it holds the last place
  a real pointer crossed this window, so it stays stale while the pointer is over some other application.

  **Measured, not inferred — twice, with the prediction written down first each time.**

  1. *Does it follow the real pointer?* The pointer was parked over the Toolbox strip, far from the
     identifier, and `hover` fired at the caret — reported as `(176.1, 61.8)` inside the editor. The tip
     opened against the left edge beside the Toolbox: at the pointer, not at the caret, not at the origin.
  2. *Is the resting default the screen origin or the window's client origin?* A freshly launched IDE that no
     pointer had ever crossed, pinned to screen `(300, 250)` at 900×600, with the real pointer held on the
     taskbar where even a maximised window cannot reach it. The tip opened at screen `(0, 15)` — well outside
     the window. Client-relative would have put it at roughly `(300, 280)`. **The default is the screen
     origin.**

  3. *Does it survive the pointer leaving?* The pointer was walked from the taskbar up into the window, over
     the editor, and back out through the bottom edge at y≈850, then left outside. The tip still opened
     inside the window rather than reverting to `(0, 15)`. **The position persists after the pointer
     exits** — it is a last-known value, not a live one.

  **One part of that third run resists explanation, and is left that way.** The tip's **x** matched the exit
  point exactly; its **y** did not — it appeared near the *top* of the window, just below the toolbar, having
  exited at the *bottom*. Two candidate rules were considered and neither survives arithmetic: excluding the
  menu/toolbar chrome from the client area shifts the anchor **down**, not up, and clamping to the placement
  target's bounds would pin it to the editor's **bottom** edge. So: measured, reproducible in its x, and no
  vertical rule worth defending. It does not change what a caller should do, because the conclusion below
  never depended on the position being predictable — only on its not being controllable.

  3. *Does it survive the pointer leaving?* The pointer was walked from the taskbar up into the window, over
     the editor, and out again through the **top** edge, then left outside. The tip opened at that exit
     point — just below the toolbar — rather than reverting to `(0, 15)`. **The position persists after the
     pointer exits:** it is a last-known value, not a live one, which is why a window the pointer left
     minutes ago still places its tip where the pointer used to be.

  That run also settles a fair question — whether the menu bar and toolbars count as "client area" for this.
  They do, and no special case is needed: they are ordinary controls in the same `TopLevel`, so passing over
  them updates the tracked position like anywhere else. The tip landing *just below* the toolbar is the same
  constant nudge measured in run 2 — the anchor was on the toolbar, and the tip drew beneath it.

  Every earlier sighting fits the same rule, and each had looked like a different phenomenon:

  | Where the tip appeared | Where the real pointer had last crossed the window |
  |---|---|
  | At the caret — correct, by coincidence | the editor, where the maintainer had been clicking |
  | Just below the screen origin | nowhere: freshly relaunched, never crossed |
  | Over the status bar | near the window's bottom edge |
  | Bottom-left, while the pointer sat top-right | over the terminal, so HexIDE's tracked position was stale |

  **The small drop below `(0, 0)` is the tooltip's own offset, not the title bar.** It looked like title-bar
  height on a maximised window, which is a good guess and was worth testing — but the same ~15 px appeared
  with the window at `(300, 250)`, whose title bar sits at y≈250. It is the ordinary nudge that keeps a tip
  clear of the cursor, and it is constant.

  **Two earlier readings were wrong, and are kept because the next person will make them too.** First "an
  arbitrary position" — it is not arbitrary, it is anchored to something, just not to anything the *call*
  controls. Then "always the screen origin", asserted from two samples that happened to agree; a third
  sighting refuted it, and the origin turned out to be merely the default for a window no pointer had entered.
  A rule drawn from two agreeing measurements is a guess wearing a measurement's clothes.

  **Consequence for a caller, unchanged by the explanation:** `hover` makes a tip's **text** assertable and its
  **position** meaningless, because the position is set by a pointer no automated caller has. Assert the
  reported string; never snapshot the tip.

  **Rejected:** having `hover` impose a deterministic placement (a `PlacementRect` at the hover point) so the
  tip lands where it was asked for. It would make a snapshot *look* right while showing the tip somewhere no
  real user would ever see it — trading an honest limitation for a misleading picture.

- **A synthetic tip is transient.** Without `IsPointerOver` the tip closes again shortly after opening, which
  is why `hover` polls for it rather than looking once when the dwell expires. Sampling once reported "no tip"
  for a tip plainly visible on screen.

**The debugger's Auto Data Tips — the case this gap was filed for (P6c) — are now MEASURED and work.**
Paused at a breakpoint with `total = 42` in scope, `hover` on the identifier returns `tip: total = 42`,
confirmed on screen at the same moment. It took closing gap 4 to get there: while a program is paused the
frontmost window is the program's form, so until `window: "ide"` existed there was no path to aim at the
editor.

*Two issues were opened against this line and both are resolved, one of them wrongly filed:*
hexide-io/HexIDE#334 was real and is fixed (MCP writes saved the previous content and reported success);
**#335 was not a defect** — breakpoints in `Form_Load` work, and the report was an artefact of #334
corrupting the project under test. A negative observation made while another defect is active is not
evidence, which is the part worth keeping.

**A stale-tip bug fell out of this measurement, and is fixed.** Hovering `Debug.Print` right after hovering
`total` reported `declared tip: total = 42` — a wrong answer, not a missing one. The editor closed its tip
with `SetIsOpen(false)` and left `ToolTip.Tip` attached, so the control kept advertising the previous
identifier's value. This tip is *dynamic*, rebuilt per hover, unlike the static tips that property is meant
for. Closing now clears it (`CodeEditorView.CloseTip`), which also removes the live-user hazard: a real
pointer could be shown the previous word's value before the replacement evaluation returned.

**What remains open here is only the declarative case** — a toolbar button's `ToolTip.Tip` still cannot be
made to appear, for the `IsPointerOver` reason above.

**Fix consideration.** Nothing cheap. `IsPointerOver` has no public setter, so short of Avalonia exposing one
— or a real platform-level pointer injection, which is a much larger tool — the declarative case stays out of
reach. Calling `ToolTip.SetIsOpen(control, true)` directly would make the popup appear, and was rejected: it
would report a tip for a control that a real hover might never show one for, which is a false green of exactly
the kind this document exists to prevent.

---

## 10. A crashed IDE is indistinguishable from a slow tool call (narrowed 2026-09-22)

> **Narrowed by #643.** An exception nothing caught is now written to the IDE log with its stack, and the log
> is flushed before the process ends, so the post-mortem no longer needs the Windows event log. Checked live
> with `HEXIDE_DEBUG_CRASH=thread` and `=ui` in a Debug build. **What remains** is the caller's side: once the
> process is dead it cannot answer, so the client still says `Unable to connect`, and the reader must know to
> look in the log. An exception inside a `DispatcherTimer` callback is not caught at all; see #652.

**Symptom.** Driving the designer to compose a screenshot, `add_control` returned success, then the next
call hung for the full 120 s timeout and every subsequent call failed with `Unable to connect. Is the
computer able to access the url?`. That message reads like a networking problem. The IDE had in fact
died — the tool call was fine, the process wasn't. The background-task notification said only
`transport dropped mid-call; response for tool "add_control" was lost`.

**Workaround.** When any tool starts failing to connect, check the process before changing approach:

```sh
powershell -NoProfile -Command "Get-Process HexIDE.Desktop -ErrorAction SilentlyContinue"
curl -s -m 4 -o /dev/null -w '%{http_code}' http://localhost:5123/health
```

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
reconnect attaches either way.

## A collapsed ComboBox reports only its selected item, which reads as "the item is missing"

**Symptom.** `inspect_element` on a closed dropdown returns a `selectionItems` array containing just the
current selection:

```
"selectionItems": ["Form1"],
"value": "Form1"
```

The control had two items. `expand` then `dump_visual_tree` shows both:

```
ComboBoxItem[Sub Main]
ComboBoxItem[Form1]
```

**How it bit.** Verifying that `Sub Main` had been added to the Project Properties → Startup Object list
(#210). The inspection said the list held only `Form1`, which is exactly what a change that had failed to
take effect would look like — and the build had just been rebuilt and relaunched, so "the binary is stale"
was the obvious next hypothesis. The feature was working the whole time.

`interact(action: "select")` is honest about this — it fails with *"has no selectable items realized — if
it's a dropdown, 'expand' it first"* — and `dump_visual_tree`'s own description warns that virtualized
dropdown items are not addressable until realized. `inspect_element` carries no such warning and reports a
plausible-looking array instead of an empty one, which is the part that misleads.

**Workaround.** Never read `selectionItems` from a collapsed dropdown as the item list. `expand` first,
then `dump_visual_tree(root=<combo>)` and read the `ComboBoxItem` children. Expand again immediately before
`select` if the popup has closed. Since #907, `select` closes the dropdown as a click does, and its reply says so.

**Suggested fix.** Either omit `selectionItems` when the items are unrealized, or mark it — an
`itemsRealized: false` alongside it would be enough. A partial list that looks complete is worse than no
list, because it supports a confident wrong conclusion; an empty array with a flag supports none.

## take_snapshot's default capture picks a modeless dialog (narrowed 2026-09-22)

**Was:** *take_snapshot of the IDE fails while a menu is open, and the default capture picks a modeless
dialog.* The first half is closed. Measured for #362: `interact(target: ".../MenuItem[Edit]", action:
"expand", window: "ide")` then `take_snapshot(window: "ide")` returned an image with the open Edit menu
composed in. The second half, the Find dialog being preferred over the main window, was not re-tested and
stays open. The rest of this entry is the original record.


**Symptom.** Two halves of one gap, both hit while verifying that Edit ▸ Find greys out on a document with
nothing to search (hexide-io/HexIDE#363). With the Edit menu expanded, `take_snapshot(window: "ide")`
returns `An error occurred invoking 'take_snapshot'` — no path, no detail. Plain `take_snapshot()` succeeds
but captures the modeless **Find** dialog instead, because a dialog is preferred over the main window and a
menu popup is apparently neither.

**Consequence.** A greyed-out menu item is the whole of the feedback for a disabled command, and it is the
one thing that cannot be photographed. Both routes fail in a way that looks like a broken tool rather than
an unsupported surface.

**Workaround used.** `inspect_element` on the menu item reports `isEnabled: false`, and
`dump_visual_tree(root: ...Edit/Pane, interactiveOnly: false)` gives the whole menu's enabled states in one
call. That is a stronger assertion than a pixel diff, so nothing was lost here — but it means the *rendering*
of the disabled state (does it actually look greyed?) is unverified.

**Suggested fix.** Treat an open menu popup as a capturable window, and let `window` take a popup or
overlay selector. Failing that, return a real error message saying the surface cannot be captured while a
popup is open, rather than a bare "an error occurred".

**Since #603.** A tool that throws now answers with the tool name, the exception type and its message instead
of the bare line, and says it is a defect to report. Re-measured 2026-09-22: with the Edit menu expanded,
`take_snapshot {"window":"ide"}` no longer throws at all and returns a path, which agrees with the narrowing
note above. The half still open, the Find dialog being preferred, was not re-tested.

## shutdown_ide can kill the MCP server and leave the IDE running

**Symptom.** `shutdown_ide` replied `Unable to connect. Is the computer able to access the url?`. The
process was still alive afterwards with its window intact (`MainWindowTitle` unchanged), but `/health` now
answered `connection actively refused` — the web host had gone down while the application had not.

**Consequence.** The documented recovery for a failed shutdown is to call the tool again, and there is no
tool left to call. Worse, the state reads as "the IDE is gone" from the automation side and "the IDE is
fine" from the desktop, so the next build fails on a file lock for a process the agent believes it closed.

**Workaround used.** `Stop-Process -Name HexIDE.Desktop -Force`, then relaunch — already the documented
fallback for an MCP disconnect, but reached here *because of* the shutdown rather than before it.

**Suggested fix.** Send the reply before tearing the host down (or stop the host last), so the caller gets
the confirmation the tool promises. Either way, do not leave a window up with no server behind it: if the
shutdown cannot complete, keep the server alive so the next call can say why.

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

## The bookmark tools count lines from 0; every other line-taking tool, and the gutter, count from 1

**Symptom.** `set_bookmarks {"name":"Module1","lines":[1]}` (`project` left at null) answers `Carried/Module1
now has bookmarks on 1.`, and the bookmark is drawn on gutter line **2**. `get_breakpoints`,
`set_breakpoints`, `run_to_cursor`, `set_next_statement`, `get_debug_state` and `get_call_stack` all number
lines from 1, and so does the gutter. Both bookmark descriptions do say "0-based". A caller who has just used
any other line tool, or read a line number off a snapshot, is still one line out.

**Workaround.** Subtract one before calling `set_bookmarks`, and add one to what `get_bookmarks` returns.

**Since #273 both numberings count from the top of the file, header included** (task 3.12), as every line tool
now counts, and a line of the header or of a member's Attribute lines is refused, with where the code starts given
in the tool's own numbering. So being one out near the top is sometimes refused rather than placed. Measured in
task 3.16's live check (2026-10-01), on a class added through the IDE whose header is lines 1 to 13:
`set_breakpoints` with `[13]` was refused with "its code starts at 14", and `set_bookmarks` with `[13]` was
accepted, which counting from 0 is the gutter's line 14. One number, refused by one tool and accepted by the
other. Below the header the difference is as silent as it was.

**Suggested fix.** Undecided, and filed as needs-decision:
[#571](https://github.com/hexide-io/HexIDE/issues/571). Either convert at the tool boundary, which changes
the contract, or keep 0-based and have the descriptions say the gutter shows N+1.

## Every automation route to a new document saves it, so the pathless state cannot be reached

**Symptom.** `add_file` says, in its own description, "Adds a new form or module to the startup project, or to
the loaded project `project` names …, **saves the new file to disk**, and returns its path." (Reworded by #596 and
by #273, which added `project`; this entry first quoted "saves it to disk", and the point is the same.)
`--newproject` — the flag the documented dev loop uses on every launch — goes further and saves the whole project
into `%TEMP%` before the first tool call
(`DesktopStartup.cs`, the `--newproject` hook's `SaveProjectToDirectory` call). So a caller driving HexIDE through automation never sees a document that has no
file.

**Why that matters more than it sounds.** #489 measured that in real VB6 a document with no file is the
*ordinary* state: nothing is written until the project is saved. #273 names such a document
`untitled:<Project>/<Name>.<ext>` on the wire, and that branch was measured against five foreign language
servers precisely because a branch nothing exercises is a branch that rots. A model asked to verify it
through this surface will call `add_file`, see a `file:` URI, and reasonably conclude the `untitled:`
spelling is dead code.

**Workaround, and it is not obvious.** Drive the real menu: `dump_visual_tree` to find
`MenuItem[Project]`, `interact` with `expand`, `dump_visual_tree` again scoped to it, then `interact` with
`invoke` on `MenuItem[Add Module]`. That reaches `ProjectService.AddNewModule` — which, as of today, also
writes (hexide-io/HexIDE#500). The only genuinely pathless document reachable at all is the initial `Form1`
of a project created through `File > New Project` **in the UI**, which `--newproject` skips.

**Such a form also reads without its header, since #273 task 3.18.** Read in code, not run. `get_file_content`
returns a form's whole file, except a form created in this session and not yet saved or changed in the designer,
which has no header yet, so its text is its code alone; the tool's description says so. Its first committed designer
change or save gives it one (`HeaderRefresher.ApplyHeader`), its marks move down by the header's height, and from then
its lines count from the top of that header. So a line number read before then is out by the header's height after
it. **Workaround:** read the content again after any designer change or save before using a line number from it.
Where a created form's first header should come from, so that it holds one from the start, is
[#885](https://github.com/hexide-io/HexIDE/issues/885).

**Suggested fix.** Two parts, and the first is cheap. (1) `add_file` should say which of the two states it
leaves the document in, and gain a way to ask for the other — a caller cannot currently express "add it the
way the user's own menu does". (2) A launch flag that creates a project without saving it, so the dev loop
can reach the state the product will normally be in once #500 lands. Until then, any verification of
`untitled:` naming through this surface is verifying something the surface itself prevents.

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

**Since #273 task 3.18 the `content` half of that reply reads differently, and the defect does not.**
`get_file_content` now returns a module's whole file, so a new `Helpers` answers with its one header line,
`Attribute VB_Name = "Helpers"`, rather than `""`. `hasUnsavedChanges` still compares the code section with
the model's, which did not change, so it still reads `true`. The `""` above is what was measured, before
that change.

**Workaround.** Read `hasUnsavedChanges` on a document added in this session as "new", not as "edited".

**Suggested fix.** Undecided, and filed as needs-decision:
[#597](https://github.com/hexide-io/HexIDE/issues/597). Either record the render baseline when a new
document is written, or keep new documents unsaved on purpose and say so in `get_file_content`'s
description.

## A whole-window `dump_visual_tree` overflows the caller's output limit

**Symptom (2026-09-25).** `dump_visual_tree {"maxDepth":12}` with an IDE showing three document tabs and
the usual tool windows returned 58,264 characters. That is over the client's limit, so it was written to a
file instead of being returned. The only way to find the code editor's path was to parse that file outside
the tool. The description does say to scope a large dump with `root`, but a first-time caller does not yet
know any path to scope it with.

**Workaround.** Since #953, walk down instead: dump the whole window with a small `maxDepth` (3 or 4), then dump
again with a node's path as `root` wherever it carries `"truncated": true`. Each step stays small, and a cut-off
subtree is marked rather than silently dropped. Dumping once to a file and extracting paths with a script still
works when a question needs the whole tree at once.

**Suggested fix.** A filter on class name, view-model type or name, returning matching nodes with their
paths and no subtrees. This is the question a first call usually asks: where is the code editor? Filed as
[#699](https://github.com/hexide-io/HexIDE/issues/699).

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
folds one again. A `click` action on `interact` would cover this margin as well as the gutters.

**A member's Attribute lines fold too since #273 task 4.3**, into a box at the end of the line they describe, and
the workaround reaches them with one difference, read in code and not run. An offset strictly inside the run opens
it, as for any fold. An offset at the run's very end does not: the window moves a caret that lands after a folded
run's box to the end of the declaration, in front of the box (`CodeEditorView.KeepCaretOffFoldedRunsEnd`), and the
run stays folded.

## A write tool that needed no picker leaves an armed file-dialog answer pending, and says nothing

**Symptom.** On the project `--newproject` creates, Project > Add Form adds `Form2` with a file already, because
that project lives in a temp folder:

```
answer_next_file_dialog {}
add_control {"formName":"Form2","type":"CommandButton","x":20,"y":20,"width":81,"height":33}
→ {"success":true,"controlName":"Command0","note":"Command0 is at Left 20, Top 20, Width 81, Height 33, in pixels."}
clear_file_dialog_answers {}
→ {"success":true,"pending":0,"note":"Discarded 1 armed answer(s); the next file dialog will be shown for real."}
```

The save wrote the form's file and never opened a picker, so the cancel armed for it is still pending. The next
dialog to open takes it, however much later and whatever it is for. Only `clear_file_dialog_answers` showed it.

**Workaround.** After any write that was armed for, call `clear_file_dialog_answers`. A count above zero means
the write did not use the answer.

**Suggested fix.** When a write tool finishes with an answer armed before the call still pending, say so in its
note, naming why (the document already had a file) and that `clear_file_dialog_answers` discards it.
[#810](https://github.com/hexide-io/HexIDE/issues/810).

## No tool can close a window as a person does

**Symptom.** Verifying #820, a program's Yes/No message box had to be closed the way a person closes it, to show
that it refuses. Nothing on the surface does that:

```
dump_visual_tree {}
→ {"window":"YesNoBox","root":{"path":"Window", ... "children":[{"path":"Window/Custom", ... Button[Yes], Button[No]}]}}
interact {"target":"Window","action":"close"}      no such action
press_key {"target":"Window/Custom/Button[No]","key":"F4","modifiers":"Alt"}
```

The title bar is the platform's chrome, outside the control tree, and Alt+F4 is handled by Windows before
Avalonia raises a key, so a key event on a control closes nothing. `stop_project` and `shutdown_ide` close a dialog
programmatically, which is the close a guard deliberately lets through, so they cannot stand in for a person's.

**Workaround.** From PowerShell, find the window by title with `FindWindow` (passing `[NullString]::Value` as the
class: PowerShell turns `$null` into `""`, which matches nothing), check with `GetWindowThreadProcessId` that it
belongs to the IDE's process, and post `WM_SYSCOMMAND` with `SC_CLOSE`, which is what the title-bar button sends.
Windows only, and outside the surface a caller has.

**Suggested fix.** An `interact` action, `close`, on a top-level window, raising the platform's close for a person
(`WindowCloseReason.WindowClosing`, not programmatic), and replying whether the window is still open.
[#829](https://github.com/hexide-io/HexIDE/issues/829).

## A context-menu action bound to a method cannot be driven

**Symptom.** Live-checking #831 needed a project with no startup form, so Form1 had to be removed. Both of the UI's
routes are out of reach:

```
invoke_menu_item {"path":"Project/Remove Form1"}
→ {"success":false,"error":"'Project/Remove Form1' is a submenu or has no command"}
interact {"target":".../Pane[Project Group - Group1]/Custom/Tree/Pane/TreeItem[Project1 (Project1.vbp)]/TreeItem[Form1 (Form1.frm)]","action":"select"}
→ {"success":true, ... "selected 'Form1 (Form1.frm)'"}
interact {"target":".../Pane[Project Group - Group1]/Custom","action":"invoke_command","value":"DeleteForm"}
→ {"success":false,"mechanism":"reflection","error":"no public property 'DeleteForm' on ProjectToolViewModel"}
```

The Project menu's item is inert, which is a product defect (#835). The Project Explorer's context menu binds
**Remove Form1** to a method, `ProjectToolViewModel.DeleteForm()`, rather than to an `ICommand`. So `inspect_element`
does not list it and `invoke_command` cannot reach it. This entry first said that no tool opens a context menu; that
was wrong (below). The refusal also names a *property* when a command was asked for.

**Corrected 2026-10-13: `expand` opens the context menu.** `interact` `expand` on a control that owns a ContextMenu
or flyout opens it, and this tree's flyout was measured that way when #343 closed (`interact expand …/Custom/Tree →
"opened flyout on 'Tree'"`, in the archive). No tool description says so, which is how this entry missed it
([#949](https://github.com/hexide-io/HexIDE/issues/949)). Remove binds `Command="{CompiledBinding DeleteForm}"`,
which Avalonia turns into a command, so `invoke` on that item with the flyout open should run it and, since #947, close
the flyout as a click does. That is read in code, not run. Remove acts on the selected form
and asks nothing, so check that `select`'s reply names that form, and that the item's header reads **Remove** and its
name, before invoking it. Until the route is checked live, the workaround below stands.

**Workaround.** Load a `.vbp` whose `Startup` names no form in the project, by passing it as the positional argument
at launch.

**Suggested fix.** `invoke_command` falling back to a public parameterless method, listed by `inspect_element`, and
a refusal that names what it looked for rather than a property. An action to open a context menu is not needed:
`expand` is it. [#836](https://github.com/hexide-io/HexIDE/issues/836).

## Polling `list_lsp_messages` with the highest sequence seen never shows a waiting request answered

**Symptom.** While re-recording [`mcp-first-contact.md`](mcp-first-contact.md) for #273 on 2026-10-13, in a fresh
`--newproject` session, a request seen waiting never came back answered when polled as the description advises:

```
open_file {"name":"Form1"}
list_lsp_messages {}
→ six rows; sequence 4 is initialize, with no outcome; 6 is the highest
list_lsp_messages {"afterSequence":6,"limit":3}
→ rows 12, 13, 15 ("matched":7); initialize is not among them
list_lsp_messages {"afterSequence":3}
→ row 4 comes back "outcome":"Answered", "answerSequence":8
```

The description says to pass "the highest one already seen to poll for what is new". A reply does not add a row:
it completes its request's row and uses up a sequence. That row's sequence is below the one a poller passes, so
the filter (`CaptureQueries`, `envelope.Sequence <= after`) drops it. The poller sees what followed the request
and never sees the request answered.

**Workaround.** Pass a sequence below the oldest request still waiting, and read each request's row again.

**Suggested fix.** Match a row that was completed after `afterSequence` as well as one recorded after it, keyed
on the sequence its reply consumed, which exists whether or not the body was kept. Say in the description that
a completed request comes back on its own row. [#961](https://github.com/hexide-io/HexIDE/issues/961).

## A refused edit's reply sends a Backspace back to the caret it was refused at, and does not say whose lines are in the way

**Symptom.** Found in #273's live check (task 4.8) on 2026-10-14, with a class whose `Public Function Total()` has
its run `Attribute Total.VB_UserMemId = 0` folded into the declaration's row, and the caret put at the start of the
body line below it, offset 790:

```
interact {"target":".../Custom[Root]","action":"set_property","value":"CaretOffset=790"}
press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Back"}
→ {"success":false,"error":"press_key refused: offset 789 is in the file's header or a member's Attribute lines,
   which only the IDE changes (the designer, a save, a rename). Nothing was written. The nearest place that can be
   edited starts at offset 790; put the caret there with interact set_property CaretOffset=790. ..."}
```

The refusal is right: Backspace there would join the body line to the run's last line. The advice is not. Offset
790 is where the caret already was, so a caller that follows it presses the same key in the same place and is
refused again, which a first-contact model can loop on. The reply also says "the file's header or a member's
Attribute lines" although the IDE knows which, and since #273 task 4.5b names the member in an add-in's refusal
(`GuardedContent.MemberNamedBy`). `type_text` gives the same wording: at offset 766, inside the run, it said
"header or a member's Attribute lines" and pointed at 790.

**Workaround.** Read the refused offset, not the advice. A backward deletion refused at the start of the line under a
run has nowhere to go on that line; delete the declaration line whole instead (Ctrl+D, which takes the run with it).

**Suggested fix.** Name the region: "the file's header" or "the Attribute lines describing Total" (`MemberNamedBy`).
For a deleting key whose span ends at the caret, say that the text before the caret is the run's and that deleting
the declaration line takes both, rather than pointing at the caret's own offset; keep the "nearest place" advice for
an insertion or a forward deletion, where it moves the caller somewhere new.
[#1042](https://github.com/hexide-io/HexIDE/issues/1042).

## `type_text` writes beside a standing selection instead of over it

**Symptom.** Found by the review of #273 task 4.5, which reproduced it. The calls below are #1040's reproduction,
read in code and not run live. In a module with no `Attribute` lines, with `E` the code editor's path from
`dump_visual_tree` and the caret after `Debug.Print 1` on the line `    Debug.Print 1, 2`:

```
press_key {"target":E,"key":"End","modifiers":"Shift"}      selects ", 2"
type_text {"target":E,"text":", 3"}
→ success; the line now reads "    Debug.Print 1, 2, 3"
```

A person's typing replaces the selection. `UiAutomationDriver.TypeText` inserts at the caret on every surface (a
`TextEditor`, a `TextArea`, and a `TextBox` at its `CaretIndex`), never reads the selection, and, away from a
described line's end, replies only with how many characters it typed. Its description says it inserts "at the
caret", so it does what it says; what it does not do is what a person's typing does. `press_key` already judges a
key with a selection standing as a write over it, so the two tools disagree. The code window's own Enter has the
same shape, which is a product defect in the same issue.

**Workaround.** Delete the selection first with `press_key {"target":E,"key":"Delete"}`, then call `type_text`.

**Suggested fix.** Write over the selection, as a person's typing does and as `press_key` already assumes, and say
in the reply how much was replaced. [#1040](https://github.com/hexide-io/HexIDE/issues/1040).

## `set_file_content`'s reply does not say when its save rewrote a member's attribute qualifier

**Symptom.** `set_file_content` saves as the document's own Save does, and since #273 task 4.4 that save writes each
member's attribute run with its member's name, as VB6's own Save does whatever the module's state. Content holding
`Attribute Gone.VB_Description` under `Public Function Total()` is therefore written as `Attribute
Total.VB_Description`, and a no-op write-back of what `get_file_content` returned can change the file in that one
place. The reply says the file was written and nothing more, so a caller comparing the file with what it sent finds
a change it did not make, with no reason given.

**`set_control_property` and `add_control` save the same way, and say no more.** Read in code, not run: both save
through `ProjectService.SaveForm` or `SaveModule`, as `set_file_content` does, and both reach `SaveFiles` with
`ownCommand: true`, `SaveModule` through `SaveModuleCore`. So a property set on a form or UserControl, or a control
added to it, also writes a stale qualifier in its code with its member's name, and neither reply says so. #1043
names `set_file_content` alone.

**Workaround.** Read the file back with `get_file_content` after the write, and expect a stale qualifier to have been
re-qualified.

**Suggested fix.** Have the save report what it rewrote and say it in the reply's `note`, not the description, which
is within about 60 characters of its cap. [#1043](https://github.com/hexide-io/HexIDE/issues/1043).

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

## Replies that report or act on a document without taking its name do not say which project's it is

**Symptom.** Read in code, not run, at ec9bfc4. #273 made every tool that takes a document's name find it in any
loaded project and name it back as `project` and `document` (archived under the #575 and #467 entries). The tools
that report or act on a document without taking its name were left as they were. The shapes, from the code, with a
group holding a `Form1` in each of two projects:

```
step_over {}               → "note": "Paused at Form1 line 9 (Step)."
                             as run_project, break_program, continue_program, step_into, step_out, run_to_cursor
get_locals {}              → "context": "Form1.Form_Load", the Module.Procedure header, as the watch tools' context
invoke_designer_undo {}    → "note": "Undid '…'. Next undo: …", on the active designer, its form not named,
                             as invoke_designer_redo, get_undo_state and invoke_format_command
get_document_tabs {}       → each tab's title, which activate_document_tab and close_document_tab take
get_open_editors {}        → each editor's title, and 'activeWindow' as a title
```

None of these says which project's `Form1` it means. `get_debug_state` and `get_call_stack` now carry the running
`project`; the run-control notes beside them do not. A tab's title starts with its project's name
(`Second - Module1 (Code)`, measured in the #575 entry), but it is the IDE's display string, whose suffix follows
the IDE's language, and not the `project` and `document` every naming tool takes and returns.

**Workaround.** For a run, `get_debug_state {}` gives `project` beside `module`, and every frame `get_call_stack {}`
lists is in that project. For a designer, `view_designer` with `project` first makes the intended form the active
designer, and its reply names it. Address a tab by the title `get_document_tabs` returns, never by one composed from
a name.

**Suggested fix.** #886's: the running project beside a bare module name in the run-control notes and in the
locals and watch `context`; `project` and `document` for the document behind the active designer in the designer
tools' replies; and the tab tools reporting `project` and `document`, and accepting them in place of a title.
Until then the hexide-mcp-server requirement #273 added, that "a tool that accepts or reports a form, module or class
SHALL identify it by its project and its name", is only partly met.
[#886](https://github.com/hexide-io/HexIDE/issues/886).

## A control with an x:Name is reported by that name alone, so the Tools menu reads `ToolsMenuItem`

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
dump_visual_tree {"root":"Window/Custom[MainView]/None[#0]","maxDepth":4}
→ … MenuItem[File], MenuItem[Edit], MenuItem[View], … MenuItem[ToolsMenuItem], MenuItem[AddInsMenuItem] …
interact {"target":"Window/Custom[MainView]/None[#0]/Menu[MenuBar]/MenuItem[ToolsMenuItem]","action":"expand"}
→ opened menu 'ToolsMenuItem'
```

An x:Name wins over the label a person sees, in the node's `name`, in its path and in reply text, and no field
carries "Tools". A caller searching the dump for the text in a snapshot finds nothing. Four menu items have an
x:Name: `ToolsMenuItem`, `LanguageServersMenuItem`, `ProtocolInspectorMenuItem` and `AddInsMenuItem`. x:Name first
is deliberate, because it does not change with the IDE language, so the fix adds rather than swaps.
Cross-platform by code reading.

**Workaround.** Address the item by the x:Name the dump shows. `MenuItem[Tools]` should also resolve, through the
label fallback, but that is read in code and was not tried.

**Suggested fix.** A `label` field on a node whenever the peer's label differs from `name`, reply text that uses
it (`opened menu 'Tools' (ToolsMenuItem)`), and a description that says a segment may be addressed by either.
[#948](https://github.com/hexide-io/HexIDE/issues/948).

## `expand` also opens a control's context menu or flyout, and no description says so

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
dump_visual_tree {"maxDepth":3}
→ Window/Custom[MainView]/None[#0]   (the menu band's StackPanel)   providers ["expandCollapse"]
  TabItem[…]/None[PART_TabBody]      (a Border)                     providers ["expandCollapse"]
dump_visual_tree {"root":"Window/Custom/Custom","interactiveOnly":false,"maxDepth":2}   (Options > Editor > Formatting)
→ the Tab width TextBox                                             providers ["value","expandCollapse"]
```

The run read these as phantom tokens. They are not: each control owns a popup that `expand` opens, by design since
#343. The StackPanel owns the Toolbars context flyout, `PART_TabBody` Dock's document context menu, and every
TextBox the theme's Cut/Copy/Paste flyout. But `interact`'s `action` description defines expand and collapse as
"a dropdown, tree node or expander", and `dump_visual_tree` says only "expandCollapse → expand/collapse", which
is how #836 and its entry here came to say that no tool opens a context menu. `collapse` with the popup closed
answers "element does not support 'collapse'" while the token is still shown. Cross-platform by code reading.

**Workaround.** On a control with no expander of its own, read `expandCollapse` as "owns a context menu or
flyout": `expand` opens it, and its items then appear under it in `dump_visual_tree`.

**Suggested fix.** Say so in `interact`'s `action` description and, in a few words, in `dump_visual_tree`'s token
list, and have `collapse` with nothing open say there is nothing to close.
[#949](https://github.com/hexide-io/HexIDE/issues/949).

## `add_to_selection` names the new selection but not its reference, so a caller learns what Align measured against only afterwards

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
interact {"target":"Window/.../List[ControlsContainer]/ListItem[Command1]","action":"add_to_selection"}
→ {"detail":"added 'Command1' to the selection; 2 items are selected now: Command0, Command1"}
```

Only the next `invoke_format_command` reply, "…, aligned to Command0", named the reference, the control every
Align and Make Same command measures against. A snapshot between the two showed Command0 with the solid handles.
The reply lists the selection in index order, as does `inspect_element`'s `selection`, so no read names the
reference before a command runs, which the form-designer spec requires of the designer. The same goes for
`remove_from_selection`. Cross-platform by code reading.

**Workaround.** Select the intended reference first and `add_to_selection` the rest: by code reading, the
reference stays on the first item selected, and removing it moves it to the lowest-indexed item still selected.
A snapshot shows the reference with the solid handles.

**Suggested fix.** With two or more selected, the reply names the primary item, and when `remove_from_selection`
removes it, names the new one. [#951](https://github.com/hexide-io/HexIDE/issues/951). Which control should be
the reference is #928.

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

## `set_control_property` cannot set Font, and its refusal implies a text form that does not exist

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
set_control_property {"formName":"Form1","controlName":"Label0","property":"Font","value":"DejaVu Sans"}
→ {"success":false,"error":"Cannot set 'Font' to 'DejaVu Sans': it takes a value the Properties window accepts for a VBFont"}
```

The Properties window's Font row read "DejaVu Sans", the string refused, and that row is read-only beside a "..."
button, so the set of values it accepts is empty. The description promises that a refused value's reply lists
what the property takes, and here it does not. By code reading every `byte[]` property (Picture, Icon,
MouseIcon, DragIcon, OleObjectBlob, List, ItemData) falls through the same way, refused as "...accepts for a
Byte[]". Cross-platform by code reading.

**Workaround.** For Font, the Properties window: select the control, `interact` `invoke` on the Font row's
`PART_OpenFontWindowButton`, then drive the Font dialog, which the run did successfully. For the `byte[]`
properties none is known: the grid gives them only a generic text box, and what it does with a typed value was
not checked.

**Suggested fix.** Accept Font as the grid shows it (a family, optionally a size and style words), merged with the
current font so what is not given is kept. Never let a refusal promise a text form the parser refuses: name the
Font dialog for Font, and say that no automation route exists yet for the `byte[]` properties.
[#952](https://github.com/hexide-io/HexIDE/issues/952).

## No tool reads back a control property beyond eight, though `set_control_property` sends callers to `get_form_controls` for them

**Symptom.** (Linux, openSUSE Leap 16, Debug build of main at b15c7cf, 2026-10-13.)

```
get_form_controls {"formName":"Form1"}
→ Label0: name, type VB.Label, left 120, top 10, width 100, height 20, caption "Centred &label", visible, enabled
get_file_content {"name":"Form1"}
→ the code section only, not the form's property block
```

No Alignment, AutoSize, WordWrap, BorderStyle, UseMnemonic or Font, so none of them can be read back after
`set_control_property` sets them; the label's Alignment had to be inferred from its caption. `get_locals {}` while
paused listed Label0 without them too. `get_form_controls` describes itself as returning "key design-time
properties", which is accurate; what is not is `set_control_property`'s "Use get_form_controls to see available
controls and properties".

**Since #273 task 3.18 the `get_file_content` line reads differently, and the gap is narrower.** That run was on
main, where `get_file_content` returned a form's code alone. With #273 it returns the form's whole file, its
designer block included, and the block follows the designer: #273's live check on 2026-10-13 saw
`move_control {"formName":"Dialog","controlName":"OKButton","left":300}` reach the open code window's header as
`Left = 4500`, and after a save, which `set_control_property` makes, the block is the one written. So a property
the `.frm` records can now be read back, in the file's own spelling: twips for a position, a Font as its
`BeginProperty Font` block, a `Byte[]` property as a reference into the `.frx`. That is not the Properties
window's text, which is what `set_control_property` takes, and it is only what the file records.

**Workaround.** With #273, read the control's `Begin` block in `get_file_content {"name":"Form1"}`, as the file
spells it. For the Properties window's own spelling, not exercised in that run: select the control in the designer
and read the Properties window's rows with `dump_visual_tree` and `interactiveOnly: false`, or `inspect_element` on
a row.

**Suggested fix.** An optional `controlName` on `get_form_controls` that returns every property the Properties
window shows for that control, under its name there and in the text form `set_control_property` accepts, and a
corrected `set_control_property` description. [#923](https://github.com/hexide-io/HexIDE/issues/923).

## No tool types as a person does, so anything driven by TextInput is out of reach

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
type_text {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/None[TextEditor]","text":"Ms"}
→ didChange seq 151, and no completion request
```

A person typing `M` triggers one. `type_text` inserts through the document API by design (#649), and `press_key`
raises only KeyDown and KeyUp, which AvaloniaEdit does not turn into text. Nothing on the surface raises
TextInput, so these are out of reach: the list that opens as you type, parameter info on `(` and `,` and its
closing on `)`, the list closing on a character that cannot be part of a name, and the edit-while-running reset
prompt for a printable character. Both tools do what they say; this is a missing capability, as #829's was.
Cross-platform by code reading.

**Workaround.** For completion and parameter info, the explicit commands: `press_key` with Ctrl+Space, Ctrl+J or
Ctrl+Shift+I on the TextArea. A list opened that way cannot be seen either (next entry). There is no workaround
for the typed-character paths.

**Suggested fix.** A keyboard typing mode, a `type_keys` tool or `mode:"keyboard"` on `type_text`, that raises
KeyDown, TextInput and KeyUp per character on the focused TextArea and reports mechanism "keyboard".
[#943](https://github.com/hexide-io/HexIDE/issues/943).

## `take_snapshot` and `dump_visual_tree` cannot see the code editor's completion list or parameter-info popup

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
press_key {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/None[TextEditor]","key":"Space","modifiers":"Ctrl"}
take_snapshot {}                     → the code window, with no list
dump_visual_tree {"window":"auto"}   → MainWindow only
```

The run concluded that no list opens, and that was wrong: a Tab sent straight after was swallowed with no
didChange, which needs an open list, and a headless reproduction confirms the list is open there (#941). Both
tools answer the same whether or not a list is open. AvaloniaEdit's completion and insight windows are Popups
parented only logically to the TopLevel, and the routes the tools use find visual-child popups plus open
ContextMenus and flyouts. Cross-platform by code reading.

**Workaround.** Do not read an empty snapshot as "no list". Infer the list from its effect: `press_key` Down then
Tab, then read `get_file_content`, or the next `textDocument/didChange` in `list_lsp_messages`. Until #941 is
fixed, Tab appends the item instead of replacing the prefix.

**Suggested fix.** Expose CodeEditorView's completion and insight windows through an internal accessor, and return
the open one from `AttachedPopupOf` for a CodeEditorView owner, so the snapshot and the tree both reach it.
[#942](https://github.com/hexide-io/HexIDE/issues/942).

## `take_snapshot` returns only a path on the IDE's machine, so a client attached from elsewhere cannot see the image

**Symptom.** Driving a Debug build of b15c7cf in an openSUSE Leap 16 VM from a Windows host, through an SSH tunnel
to the VM's MCP port (2026-10-13):

```
take_snapshot {}
→ {"path":"/tmp/hexide_snapshot_20261013-004254-281_1.png","width":1920,"height":1011,"scale":1}
```

The path is in the VM's `/tmp`, which the client cannot open. The description says the tool "returns the file
path so the caller can read the image", which holds only for a caller on the same machine. The server answers
loopback only, so a tunnel is how another machine reaches it. Not Linux-specific: any remote attach has it.

**Workaround.** Copy the file across by hand; every snapshot in that run was fetched with `scp`.

**Suggested fix.** An optional `inline` parameter, default false, that returns the image as MCP image content
(`image/png`) beside the same JSON. Keep path-only as the default: an inline image costs every local caller that
does not need it. [#957](https://github.com/hexide-io/HexIDE/issues/957).

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

## `get_last_runtime_error` does not say which module or line raised the error

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
get_last_runtime_error {}
→ {"raised":true,"message":"Compile error:...","at":"2026-10-13T03:05:25.1331302+01:00","sequence":1}
```

The dialog read "Compile error: Method or data member not found (ListCount in Right(Control))", with Debug
disabled (#9), so once it closed neither a person nor a caller could reach the line. The reply carries text,
time and sequence only. A run-time error's text ends with the statement but no module or line, and a compile
error met mid-run carries neither, although the interpreter tracks both for the debugger. Cross-platform by code
reading.

**Workaround.** None beyond searching the project for an identifier the message names.

**Suggested fix.** Note the module and line on the exception on the way out of the statement walk, for run-time
and compile errors alike, and carry them through to `module` and `line` in the reply. Leave the dialog's text
alone: what VB6 shows for a compile error met mid-run is #833's measurement.
[#955](https://github.com/hexide-io/HexIDE/issues/955).

## `get_last_runtime_error` stamps local time, where every other timestamp on the surface is UTC

**Symptom.** On a machine at +01:00 (Linux, openSUSE Leap 16, Debug build of main at b15c7cf, 2026-10-13):

```
get_last_runtime_error {}
→ "at":"2026-10-13T03:05:25.1331302+01:00"
/health
→ "timestamp":"2026-10-13T04:07:50.0894348+00:00"
```

`list_lsp_messages`, the conversation export, the connection list and `/health` all stamp UTC, so lining an error
up against the LSP record means converting offsets by hand. Invisible on a machine whose zone is UTC, CI
included. Cross-platform by code reading, and the archived #342 entry quotes the same `+01:00`.

**Workaround.** Compare the stamps as instants, not as text: each carries its offset, so the comparison still
holds. This is an inconsistency, not wrong data.

**Suggested fix.** `DateTimeOffset.UtcNow` in `RuntimeErrorLog`, and a test asserting a zero offset.
[#956](https://github.com/hexide-io/HexIDE/issues/956).

## `list_lsp_messages` `failuresOnly` omits a message that could not be serialized

**Symptom.** With a serialization failure at seq 183 in the record (#944) (Linux, openSUSE Leap 16 under XWayland,
main at b15c7cf, 2026-10-13):

```
list_lsp_messages {"failuresOnly":true,"limit":20}
→ {"messages":[],"matched":0,"note":"118 envelope(s) are recorded, and none match this filter. Drop the filters to see what is there."}
```

The record held:

```
{"sequence":183,"direction":"Local","kind":"NeverSent","method":"$/cancelRequest","detail":"could not be serialized: ..."}
```

The Protocol Inspector's Failures only filter and its failure count miss it the same
way: both count only error responses and the Failed, Cancelled and Abandoned outcomes. So a filter for finding
failures hides the client-side defect the capture was built to expose. Not every NeverSent row is a failure: a
request withheld because the server did not advertise the capability is recorded as a Sent NeverSent row,
deliberately. Cross-platform by code reading.

**Workaround.** Call `list_lsp_messages {}` with no filter and look for `kind:"NeverSent"` with
`direction:"Local"`.

**Suggested fix.** Count a Local NeverSent row as a failure in `CaptureQueries.IsFailure` and
`ProtocolInspectorRowViewModel.IsFailure`, and say so in `failuresOnly`'s description.
[#945](https://github.com/hexide-io/HexIDE/issues/945).

## No reply mentions a modal dialog, whether it was already open or the action opened it

**Symptom.** (Windows 11, Debug build of #838's branch, merged with main at 2047857, 2026-10-13.) Paused at a
breakpoint on line 3 of a module, with the caret moved to line 2, an `Attribute` line:

```
invoke_menu_item {"path":"Debug/Set Next Statement"}
→ {"success":true,"note":"Invoked 'Set Next Statement'."}
take_snapshot {"window":"ide"}
→ {"path":"…_3.png","width":1920,"height":974,"scale":1.5}
dump_visual_tree {"maxDepth":3,"interactiveOnly":false}
→ {"window":"HexIDE","root":{… "className":"MessageBox" … "Can't set the next statement here — only a
   top-level statement of the currently paused procedure is supported …" … "Button[OK]"}}
take_snapshot {}
→ {"path":"…_4.png","activeDialog":"HexIDE","width":642,"height":172,"scale":1.5}
```

The command refused the move and opened a message box saying so. The first reply reads as if the move had
happened. The second is the main window with no `activeDialog`, and the box is not in the image, although
`window: "ide"` is the documented way to reach the IDE while a program is paused, and the tool's description
says an open modal is captured in preference to the main window. Only the default window finds it. #681
measured the first half with Tools/Add Procedure left open; this is a modal raised by the action itself, which
[its Linux comment](https://github.com/hexide-io/HexIDE/issues/681#issuecomment-6146516046) describes.

**Workaround.** After any action that might open a dialog, `dump_visual_tree {}` and read its `window`. A
`take_snapshot {}` with no `window` captures the dialog.

**Suggested fix.** #681's decision, for the replies. For `take_snapshot` with `window: "ide"`, prefer a modal
the IDE owns, as `auto` does, or at least name it in `activeDialog`.
[#681](https://github.com/hexide-io/HexIDE/issues/681).

**Seen again with #1011's halt** (2026-10-15, Debug build of `fix/1011-halt-on-error`). A click divides by zero, and the
program halts at the run-time error dialog. After Debug, a Continue without a fix retries the statement, which halts again:

```
continue_program {}
→ {"success":true,"note":"Running; no pause within 2 s. It raised Run-time error '11': Division by zero at 10 \ d;
   get_last_runtime_error has it."}
get_debug_state {}
→ {"running":true,"state":"Running"}
```

Both replies say Running. The program is held at a dialog that waits for End or Debug, and nothing of it runs. Naming
the error helps, but neither reply says the program is waiting for an answer. `dump_visual_tree {}` finds the dialog, as
above.

## A whole replacement that changes the header and a member's `Attribute` lines is told only about the header

**Symptom.** Read from the code, and recorded when #273 task 4.5b was built. `set_file_content` (and an add-in's
`TrySetContent`) compares the header first and returns on the first difference:

```
set_file_content {"name":"Ledger","content":"<a class header with one line changed, and Total's run changed>"}
→ refused: "The content would change the document's header (its designer block, class header or leading
   Attribute lines), which only the IDE changes. ..."
```

A caller that restores the header and sends again is refused a second time, now for `Total`'s run, a reason the
first reply did not give.

**Workaround.** After a header refusal, compare the members' `Attribute` lines with `get_file_content`'s before
sending again.

**Suggested fix.** Compare the runs whatever the header does, and refuse once with every finding.
[#1054](https://github.com/hexide-io/HexIDE/issues/1054).

## `press_key` refuses a Delete over a selection reaching protected lines, where the key deletes the editable part

**Symptom.** Read from the code (#273 task 4.5a records it as already so). With a selection running from code into
the header or a member's `Attribute` lines, a person's Delete deletes the parts the code window's read-only sections
allow. The tool deletes nothing:

```
press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Delete"}
→ {"success":false,"error":"press_key refused: offset N is in the file's header or a member's Attribute lines, ...
   Nothing was written. ..."}
```

`UiAutomationDriver` asks `DeletionChangesARegion` about the whole span, where the key's provider deletes the
deletable segments.

**Workaround.** Narrow the selection to the editable part with `interact set_property` (`SelectionStart`,
`SelectionLength`) before pressing the key.

**Suggested fix.** Delete what the deletable segments allow, as the key does, and say what was left and why.
[#1055](https://github.com/hexide-io/HexIDE/issues/1055).

## A Label on a running form is missing from the tree, so no tool can click it or read its caption

**Symptom.** Measured on 2026-10-15, driving the Battleship demo (#1071), whose board is 25 Labels and a status Label:

```
dump_visual_tree {"interactiveOnly":false,"maxDepth":20}
→ {"window":"Battleship - Admiral","root":{"path":"Window",...,"children":[{"path":"Window/Menu",...,"isHidden":true,
   "children":[]}]}}
```

None of the 26 Labels is there, with `interactiveOnly` false (the default is true). A pointer reaches them, so they
are on screen: `interact {"target":"Window","action":"click","value":"20,20"}` replies "landing on its part 'VBLabel'".
Nothing in the reply says controls were left out.

**Workaround.** Click the form at a point: `interact {"target":"Window","action":"click","value":"x,y"}`, with `x,y` worked
out from the control's twips at 15 to a pixel, in device-independent units from the client area's top-left. Read a
caption with `take_snapshot`.

**Suggested fix.** A peer for each built-in control that has none, `VBLabel` first, naming it by its caption. No control
in `BuiltinControls/` overrides `OnCreateAutomationPeer`, which is the likely cause and isn't verified.
[#1076](https://github.com/hexide-io/HexIDE/issues/1076).

## A running form's menu items are named with VB6's `&`

**Symptom.** Measured on 2026-10-17, driving the Minesweeper demo's menued version (#1123, caab5bdd):

```
dump_visual_tree {}
→ {"path":"Window/Menu/MenuItem[&Game]","controlType":"MenuItem","name":"&Game",...}
interact {"target":"Window/Menu/MenuItem[&Game]","action":"expand"}
→ ... {"path":"Window/Menu/MenuItem[&Game]/Pane/MenuItem[E&xit]","name":"E&xit",...}
```

The menu draws "Game" and "Exit", with the access key underlined. The name and the path carry the raw caption, so a
caller must know VB6's caption syntax to address what it can see.

**Workaround.** Put the `&` where the caption has it: `MenuItem[&Game]`, `MenuItem[E&xit]`.

**Suggested fix.** Name each run-time menu item by its caption as drawn, `&` removed and `&&` read as one `&`. The IDE's
own menus had the same fault with `_` (#578). [#1128](https://github.com/hexide-io/HexIDE/issues/1128).

### Breakpoint gutter hover target (#1176, fixed in this slice)

`dump_visual_tree {"maxDepth":24,"interactiveOnly":false}` did not expose the breakpoint margin.
`hover {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]","x":19,"y":229,"dwellMs":500}`
resolved to TextEditor and returned "no tip opened within 500ms": its synthetic move never reached
an unaddressable child margin's pointer handler. Clicking through the editor still hit-tests the gutter.
The fix in #1176 exposes the margin with a control automation peer and stable `BreakpointMargin` ID,
so callers can target its own hover handler. Declarative tooltip visibility remains subject to the
existing synthetic-pointer limitation; assert the reported declared text.

The same acceptance run found a product hit-testing defect: `interact` with the margin target,
`action:"click", value:"7,111"` reported that the press landed on `DockPanel`, not the margin.
Only painted marker shapes were hit-testable. F9 was a working workaround. The fix in #1176 paints
a transparent rectangle across the whole gutter so empty lines accept clicks and real pointer hover.

### Clipped Call Stack selection reports success without changing the frame (#1182)

During #1180 acceptance on Windows, this call reported success but Watches remained in Worker and refused
`callerValue`; the intended caller frame was not selected:

```json
{"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/TitleBar[Call Stack]/Pane[Call Stack]/Custom/List/Pane[PART_ScrollViewer]/ListItem[CallStackRow { Frame =    Main, Line = 6, Stop = DapStop { SessionId = 0734e6a8-bc5a-48fe-a5d4-b685c2a2aa05, Generation = 3, ThreadId = 1, Reason = breakpoint }, FrameId = 1 }]","action":"select"}
```

The path came from `dump_visual_tree` with `interactiveOnly:false`. Tool panes were clipped in a small window.
Workaround: hide unused panels, resize, then use
`press_key {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/TitleBar[Call Stack]/Pane[Call Stack]/Custom/List","key":"End"}`.
This selected Main and its watch returned 42. Investigate provider dispatch and virtualization; selection
replies should verify/report the resulting model selection. The underlying cause is not yet established.
