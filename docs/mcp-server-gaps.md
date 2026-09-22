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

## 3. MCP tools drop on IDE shutdown and don't re-attach mid-session

> **The MCP client's behaviour, not a HexIDE defect** (#645). Tool discovery belongs to the client: it keeps
> a cached schema for a tool name it already knows, and does not attach at all when a session began with no
> server answering. Measured 2026-09-22 over about a dozen relaunches in one session that began attached:
> existing tools stayed callable every time, and a changed description was not picked up until a `/mcp`
> reconnect. Nothing observed points at the server. Kept here for the measurements.

**Symptom.** MCP tools are discovered at session start. Shutting the IDE down — which is **required** to run
the `vb6.exe` oracle and for any rebuild that holds file locks on the runtime DLLs — disconnects the `hexide`
server and **removes every one of its tools from the session**; `ToolSearch("mcp__hexide__…")` then returns "no matching
deferred tools." Relaunching the IDE (health 200) does **not** re-register them mid-session; it took a user
**session resume** to bring them back.

**How it bit.** The documented "MCP Dev Loop" (`shutdown_ide` → `dotnet build` → relaunch → verify via MCP)
breaks at the last step: after shutting down for the build, the tools are gone until a resume. This session's
colour verification stalled here until the user resumed.

**Distinction from the known caveat.** `CLAUDE.md` already says MCP **schema changes** need a session restart.
This is different — the schema is unchanged; the tools just need **re-attaching** after the server bounces on
the same port.

**Workarounds used.** (a) Prefer **headless integration tests** (`HexIDE.Integration.Tests`, `Avalonia.Headless`)
for verification that doesn't strictly need a live IDE — often more rigorous and lock-free anyway (this is how
the `TrySet` colour-boundary path got its permanent guard). (b) Ask for a session resume when live MCP is
genuinely required after a shutdown.

**Observed 2026-09-08, and it narrows this rather than closing it.** A NEW tool (`get_last_runtime_error`)
was added to the server, the IDE was stopped, rebuilt and relaunched, and the tool appeared to the running
session **without a restart** — the next call carried a `deferred_tools_delta` announcing it. That is a
different code path from the one this entry records (tools being *removed* while the server is down and not
restored), so the entry stands; but "MCP schema changes require a session restart" is at least too strong as
stated. A separate audit refuted an attempt to retire this entry on that basis, checking the transcript and
finding the earlier evidence was a full restart misread as a mid-session add. Worth one clean experiment
before either statement is trusted.

**That experiment was run on 2026-09-11, twice, and this entry's own symptom did not reproduce.** With the
IDE running and attached since session start: `shutdown_ide`, rebuild, relaunch, and the existing tools
were callable immediately with no resume — `open_file` answered on the first try. A tool added in the same
build (`get_lsp_capture_state`) arrived as a deferred-tool delta and worked on its first call. The cycle
was then repeated a second time with the same result.

**What that settles and what it does not.** It settles the schema half: "MCP schema changes require a
session restart" was too strong, and `CLAUDE.md` now states the condition instead — the attachment's state
when the SESSION began, not whether the process has restarted since. The same day supplied the negative
case: four tools added while no IDE was running at session start did **not** appear on launch, and needed a
resume, because there was no attachment to re-list from.

It does not settle whether this entry's original symptom is gone or merely unreproduced. The two runs above
differ from the one recorded here in that the IDE was attached at session start, which is exactly the
variable the schema half turned on — so the likeliest reading is that both observations are the same
mechanism seen from two starting states. Retiring it needs a session that begins with no IDE, which is the
condition nobody has deliberately arranged. **Left open on purpose**, since this entry has already survived
one wrong retirement.

**Fix consideration.** Auto-reconnect the MCP client when a known server reappears on its port, or a
lightweight "reconnect MCP" affordance — so the shutdown→build→relaunch→verify loop keeps the tools live
without a full resume.

---

## 5. A designer control is reported under its view-model's type name, not its own (narrowed 2026-09-22)

**Was:** *Can't select / delete / reorder a designer control via MCP.* Measured again for #362, and most of
it no longer holds. With default arguments, `dump_visual_tree(root: ".../Pane[#0]")` lists each canvas
control as a `ListItem` (`ControlItem`) with `selectionItem`. `interact select` on it selected it,
`press_key(key: "Delete")` on it deleted it (`get_form_controls` confirmed `Command0` gone; the Edit menu
then offered *Undo Delete: Command0*), and `invoke_designer_undo` / `invoke_designer_redo` both exist. The
`FormEditor.BringToFront` / `SendToBack` toolbar buttons are addressable by `automationId`; reordering was
not driven in this pass.

**What remains.** The node's name is `HexIDE.VisualDesigner.ComponentInstanceViewModel`, the view model's
`ToString()`, so the path is `ListItem[HexIDE.VisualDesigner.ComponentInstanceViewModel]` for every
control. With more than one control on a form, a caller cannot tell from the tree which node is
`Command1`; it has to fall back on position or index. Filed as #526. The rest of this entry is the original record.

**Multi-selection (#661, closed by the PR that adds this note).** `interact select` replaces a selection, so
a caller could hold one control, or all of them through `press_key(key: "A", modifiers: "Ctrl")`, and nothing
in between. `interact add_to_selection` / `remove_from_selection` now reach a chosen group, and each canvas
control reports the `multiSelectItem` token that promises them. Measured with three buttons:
`select` Command0, then `add_to_selection` Command2 → `added 'Command2' to the selection; 2 items are selected
now: Command0, Command2`; `invoke_format_command(command: "AlignLefts")` then moved Command2 to Command0's
Left (120) and left Command1 at 600.


**Symptom.** A control placed with `add_control` is created and auto-selected, but there is no way to (a) select a
*different, existing* control, (b) delete a control, or (c) exercise undo/redo of a designer edit through MCP. The
controls drawn on the designer canvas are **not individual nodes in `dump_visual_tree`** (the canvas paints them; the
tree shows only the Properties-pane `ObjectSelector` combo and dock chrome), so `interact`/`press_key` have no path
to target a specific control, and there is no `select_control` / `delete_control` tool.

**How it bit (bug-hunt batch-2 designer fixes).** Verifying the four `FormEditViewModel` fixes — duplicate-name
avoidance (needs *delete then re-add*), multi-select **Delete** (needs a multi-selection), and undo **z-order**
restore (needs cut + undo) — was not drivable. Only the no-collision naming path was confirmed live (`add_control`
twice → `Command0`, `Command1`). The rest were verified by build + code review against the already-proven
`CutSelectedControls` path and the standard ascending-index restore invariant.

**Root cause.** The designer canvas is a custom-drawn surface; component VMs aren't surfaced as automation nodes, and
the designer's selection/delete/undo aren't exposed as `ICommand`s reachable via `interact invoke_command`.

**Fix consideration.** Small, high-leverage additions: `select_control(formName, controlName)` (drive the designer's
`SelectedComponent`/`SetSelectedComponents`), `delete_selected_controls`, and `designer_redo` to pair with the
existing `invoke_designer_undo` — plus surfacing each canvas control as a `dump_visual_tree` node with its name, so
`interact` can click/rubber-band it. That would make the whole designer edit loop (add → select → move → delete →
undo/redo) MCP-verifiable.

---

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

## MCP tools do not re-attach to a resumed session while the IDE keeps running

> **The MCP client's behaviour, not a HexIDE defect** (#645). Tool discovery belongs to the client: it keeps
> a cached schema for a tool name it already knows, and does not attach at all when a session began with no
> server answering. Measured 2026-09-22 over about a dozen relaunches in one session that began attached:
> existing tools stayed callable every time, and a changed description was not picked up until a `/mcp`
> reconnect. Nothing observed points at the server. Kept here for the measurements.

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

**Suggested fix.** Either make tool discovery retry against `.mcp.json` when the endpoint becomes reachable
mid-session, or have the launcher expose a readiness signal the client re-polls, so a restart with the IDE
already up is sufficient. Failing that, document that the IDE must be started *after* the session, not
before — which is the opposite of the intuitive order and worth stating explicitly.

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
then `dump_visual_tree(root=<combo>)` and read the `ComboBoxItem` children. Note the popup closes between
calls, so expand again immediately before `select`.

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

## A mid-session relaunch DOES pick up new tools, if the server was attached when the session started

**Measured, and it refines a rule this file and `CLAUDE.md` both state more strongly than is true.** The
documented rule is that MCP tools are discovered at session start and a schema change needs a session
restart. Both of these happened in one afternoon:

- Four new tools were added while **no IDE was running**. Building and launching did not surface them, and
  a session restart was needed. Consistent with the rule.
- A fifth was added later, with the IDE **running and attached since session start**. Shutting it down,
  rebuilding and relaunching surfaced the new tool immediately, as a deferred-tool notification, and it
  worked on the first call.

**So the distinguishing condition is whether the server was attached when the session began**, not whether
the process was restarted since. An attached server is re-listed when it comes back; a server that was
never attached has nothing to re-list from, and no amount of relaunching creates the attachment.

**Why it is worth writing down.** The stronger reading costs a round trip through the user every time a
tool is added, and the rule is the reason to stop and ask rather than improvise — so being wrong about it
in the cautious direction is not free. If the IDE was up and answering at session start, try the relaunch
before asking.

**Not contradicted:** the sibling entry above, about tools not re-attaching to a *resumed* session while
the IDE keeps running. That is the same mechanism seen from the other side — what matters is the state of
the attachment at the moment the session starts.

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

## A relaunch picks up a NEW tool but not a CHANGED one (measured 2026-09-20)

**Symptom.** Four mark tools gained an optional `project` argument. The documented rebuild cycle ran —
`shutdown_ide`, build, relaunch, `/health` — and the first call to `set_breakpoints` failed with
`An error occurred invoking 'set_breakpoints'.` and nothing else. The session's cached schema still carried
the old parameter list, so the request it built no longer matched what the server accepted.

**What this narrows.** The entry above records that a mid-session relaunch **does** pick up a tool that did
not exist before, measured both ways. This is the other half: a tool whose *signature* changed keeps the
schema the session already has. Added tools yes; changed tools no. The failure is a bare invocation error,
which reads as a broken tool rather than as a stale schema.

**The actual defect underneath it, which is the transferable part.** `string? project` with **no default
value** is `required` in the generated schema — a nullable C# parameter is not an optional JSON one. So the
new argument was mandatory on the wire and every existing caller broke, cached schema or not. Giving all six
`= null` made them genuinely optional, and the same call then worked on the first try after a rebuild.

**How to avoid paying for it again.** Read the generated schema's `required` array, not the C# signature —
this file and CLAUDE.md both say so, and it still cost a cycle. And when a *signature* changes rather than a
tool being added, expect the first call to fail against a stale schema and do not diagnose the server.

**Since #603.** A throw inside a tool is now reported with its exception type and message rather than the bare
line. Not re-measured for this case: a call that does not match the tool's parameters may fail in the SDK's
own argument binding, which can report through `McpException`, and #603 leaves that as the SDK reports it.

## The bookmark tools count lines from 0; every other line-taking tool, and the gutter, count from 1

**Symptom.** `set_bookmarks {"name":"Module1","lines":[1]}` (`project` left at null) answers `Carried/Module1
now has bookmarks on 1.`, and the bookmark is drawn on gutter line **2**. `get_breakpoints`,
`set_breakpoints`, `run_to_cursor`, `set_next_statement`, `get_debug_state` and `get_call_stack` all number
lines from 1, and so does the gutter. Both bookmark descriptions do say "0-based". A caller who has just used
any other line tool, or read a line number off a snapshot, is still one line out.

**Workaround.** Subtract one before calling `set_bookmarks`, and add one to what `get_bookmarks` returns.

**Suggested fix.** Undecided, and filed as needs-decision:
[#571](https://github.com/hexide-io/HexIDE/issues/571). Either convert at the tool boundary, which changes
the contract, or keep 0-based and have the descriptions say the gutter shows N+1.

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

**Workaround.** Read `hasUnsavedChanges` on a document added in this session as "new", not as "edited".

**Suggested fix.** Undecided, and filed as needs-decision:
[#597](https://github.com/hexide-io/HexIDE/issues/597). Either record the render baseline when a new
document is written, or keep new documents unsaved on purpose and say so in `get_file_content`'s
description.

## `open_file` does not give the editor keyboard focus, so a routed menu item refuses until something does

**Symptom.** Most built-in menu items are routed commands, which act on the control that has keyboard
focus. `open_file` brings a code window to the front but leaves focus where it was (measured: nowhere), so
`invoke_menu_item {"path":"Tools/Add Procedure"}` straight after `open_file {"name":"Form1"}` is refused.
After `press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Right"}` the same call opens the Add
Procedure dialog. A person who opens a code window has focus in it, so the tool is behind the person here.

**Narrowed by #678's PR.** The refusal used to say only `canExecute returned false`. It now says the item is a
routed command, names what has focus, and says `press_key` on the editor is the way in. What remains is the
focus itself.

**Workaround.** `press_key` on the editor with a key that changes nothing (`Right`) before invoking an item
that belongs to a document.

**Suggested fix.** Have `open_file` and `activate_document_tab` give the opened document keyboard focus, as
opening it by hand does: [#678](https://github.com/hexide-io/HexIDE/issues/678).
