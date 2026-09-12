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

---

## 2. `set_control_property` only handles string / number / bool

**Symptom.** Setting an **enum** property fails — `set_control_property(Label0, "BackStyle", "1")` →
*"Property 'BackStyle' has type 'BackStyles' which is not supported by set_control_property"*. **Colour**
(`VBColor`) properties (`BackColor`/`ForeColor`) are likewise unsettable.

**How it bit.** I couldn't make a label opaque, nor set a control's colour, via the designer tool — so the
Phase-2 colour verification had to be done by *running code* (`Me.BackColor = &HC0FFC0` in `Form_Load`) and
snapshotting the result, rather than a designer property set.

**Fix.** Accept enum values (by member name or ordinal) and `VBColor` values (a hex `OLE_COLOR` string like
`"&H00FF0000&"`, or an `R,G,B` triple). Better: route the incoming string through the **same property-editor
coercion the designer's property grid uses**, so every editable property type is settable through one path.

---

## 3. MCP tools drop on IDE shutdown and don't re-attach mid-session

**Symptom.** MCP tools are discovered at session start. Shutting the IDE down — which is **required** to run
the `vb6.exe` oracle and for any rebuild that holds file locks on the runtime DLLs — disconnects the `hexide`
server and **removes its 38 tools from the session**; `ToolSearch("mcp__hexide__…")` then returns "no matching
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

## 5. Can't select / delete / reorder a designer control via MCP

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

## 6. Can't open a Debug-menu dialog (e.g. Add Watch) via MCP to snapshot it

**Symptom.** Verifying debugger P6a's **Add Watch** dialog rendering couldn't be driven through MCP. The dialog opens
from a Debug-menu item / keyboard shortcut backed by a *routed* command (AvaloniaLabs CommandManager), not a VM
`ICommand`: `interact invoke_command AddWatchCommand` finds no such property on `MainViewViewModel`; `interact invoke`
on `MenuItem[Debug]` reports "element does not support 'invoke'" (a top-level menu exposes no invoke provider until
opened, and its children aren't in the tree until then). The Watches rows render as `TreeViewItem`s but don't
advertise a `selectionItem` provider, so selecting a row to enable `EditWatchCommand` (which opens the same dialog)
wasn't reachable either.

**How it bit (debugger P6a).** The Watches *window* was fully verified live — it renders (columns + rows in
`dump_visual_tree`, snapshot after Stop) and evaluates correctly while paused (`get_watches`: x=42, s=hello, x*2=84,
arr expandable). Only the **Add Watch dialog's** live render couldn't be reached; deferred to P6b (where its
Break-type radios become functional and it's exercised again), backed meanwhile by the dialog-VM unit tests.

**Fix consideration.** Best: (a) surface a `selectionItem` provider on tool-window TreeView rows so `interact select`
can set the selection (unlocking the row's context-menu commands like Edit/Delete); also useful: (b) make a top-level
`MenuItem` invoke open the menu and realize its children, or (c) a thin MCP action to open a named debugger dialog.

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

## 8. `type_text` bypasses a read-only editor, and reports `mechanism: "keyboard"` while doing it

**Symptom.** Verifying the read-only editing gate (#22) against the running IDE, `type_text` successfully
inserted `XXX_SHOULD_NOT_APPEAR` into a code editor whose `TextEditor.IsReadOnly` was bound true. The result
reported `"mechanism":"keyboard"`, which reads as "a real key event went in" — so the first conclusion was
that the gate was broken. It was not.

**Cause.** The tool's own description says it inserts "at the caret **via the control's own API**", which is
a document mutation, not input. `IsReadOnly` on AvaloniaEdit guards the *editing UI*, so a direct
`Document.Insert` legitimately sidesteps it. The `mechanism: "keyboard"` label is the misleading part.

**Workaround.** Do not use `type_text` to test whether input is blocked. `press_key` raises real
`KeyDown`/`KeyUp`, but note gap #9 below before trusting a negative result from it either. The reliable
check is behavioural at a level the user cares about — here, invoking Save and confirming the file on disk
is byte-identical afterwards.

**Suggested fix.** Report `mechanism: "api"` (or `"document"`) when inserting programmatically, and reserve
`"keyboard"` for genuine key events. Optionally have `type_text` refuse, or warn, when the target editor is
read-only — silently mutating a read-only document is a surprising default for an automation tool.

## 10. A crashed IDE is indistinguishable from a slow tool call

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

## A Project Explorer node that is not a form or module cannot be selected or opened

**Symptom.** There is no way to drive "select this tree node, then open it" for any node kind beyond forms
and modules. Verified against a related-document node (a file the project carries but does not compile):

- `interact select` on its `TreeViewItem` → `element does not support 'select'`; the item exposes only a
  `scroll` provider, no `selectionItem`.
- `press_key` on the `TreeView` does nothing useful — `inspect_element` reports the tree as
  `isKeyboardFocusable: false`, so arrow keys never reach a selection model.
- `interact set_property` cannot help either: `ProjectToolViewModel.SelectedItem` is typed `Object`, and the
  reflection fallback coerces a **string** to the property type. There is no way to name an existing
  view-model instance as a value.
- `open_file(name)` opens a form or module by name only, so it cannot reach anything else.

**Consequence.** The double-click-to-open gesture is unverifiable through MCP for any new node kind. That
matters because CLAUDE.md requires a UI feature be confirmed against the running IDE, and here the *tree*
can be confirmed by snapshot while the *gesture* cannot be driven at all.

**Workaround.** Split the verification and say which half was driven. The node's presence, icon and caption
are visible in `take_snapshot` and assertable via `dump_visual_tree` (the node reports its
`dataContextType`, so its type is checkable). The routing — selection to the right editor — and the editor's
own behaviour are covered by tests instead. State plainly that the gesture itself was not driven.

**Suggested fix.** Either surface a `selectionItem` provider on tree items so `interact select` works, or
add a selection-by-path action (`interact select_node` taking the same `path` the tree dump already
returns). The second is probably better: paths are already the addressing scheme everywhere else in this
server, and it would work for every current and future node kind rather than needing a provider per
control. A narrower `open_file` that accepts any project member would help too, but would not fix
selection, which is what several context-menu commands key on.

## take_snapshot renders DIPs while Win32 coordinates are physical pixels

**Symptom.** Driving a synthetic mouse click from a `boundingRect` needs a scale conversion that nothing in
the tool output mentions. On the machine this was hit on, `GetClientRect` reported 987 × 560 physical pixels
while `take_snapshot` returned a 1481 × 840 image and `inspect_element` reported bounds in that same 1481-wide
space — a factor of 0.666. Clicking at the raw `boundingRect` coordinates lands roughly 50% off, far enough
to hit a different control and look like "the click did nothing".

**Consequence.** Any fallback that leaves the MCP surface for real input — the only route left when a
control has no usable provider — silently targets the wrong place, and the resulting no-op is easy to
misread as the feature being broken.

**Workaround.** Derive the factor before clicking: `GetClientRect` width ÷ snapshot image width, then
multiply the DIP coordinate by it and pass through `ClientToScreen`. Do not assume 1.0, and do not assume
the usual Windows 1.25/1.5 either — measure it.

**Suggested fix.** Report the scale explicitly. `take_snapshot` returning the render scale alongside the
path (and `inspect_element` naming the space its `boundingRect` is in) would remove the guesswork; the
values are already known to the server.

## get_project_info omits every project member that is not a form or module

**Symptom.** `get_project_info` returns `forms` and `modules` only. A project carrying a related document (a
file it does not compile) reports it nowhere, so after adding one the tool's output is byte-identical to
before.

**Consequence.** The obvious check after an add — call `get_project_info` and see the new member — silently
answers "nothing happened" for a whole member kind. It reads as a failed feature rather than a blind tool.

**Workaround.** Confirm through the Project Explorer instead: `take_snapshot` shows the node and its icon,
and `dump_visual_tree` reports the node's `dataContextType`, which distinguishes a related document from a
module.

**Suggested fix.** Add a `relatedDocuments` array, and prefer a shape that will not need this edit again the
next time a member kind is added — a single `members` array of `{name, kind, path}` would cover forms,
modules, related documents and whatever follows.

## A scrolled tool window can only be verified down to its first screenful

**Symptom.** The Language Servers window (#259) lists every attached server, one card each, so its content
is routinely taller than the pane. `take_snapshot` captures what is painted, and there is no way to scroll
the content, so every server below the fold is unverifiable. Hiding a bottom-docked tool via
`set_tool_window_visible` buys one more card and no more.

**What was tried.** `press_key` with `End`/`PageDown` needs a `target` path, and the only addressable node
in that region is the `TabItem` — pressing a key there does not reach the `ScrollViewer` inside the tab's
content. `dump_visual_tree` returns the tab chrome (the `TabItem`, its close `Button`, its header `Text`)
but not the realised card content beneath it, so the values could not be asserted structurally either.
Two shapes of the same limit: the content of a document tab is neither drivable nor readable.

**Consequence.** Verification stopped at "the first two groups render correctly, with real servers and the
right fields". The case that most wanted checking — a server further down the list whose row shows
`Running` with nothing advertised — was covered by a view-model test instead. That is a reasonable place
for it, but it means the *rendering* of the most diagnostic row in the window is unverified, which is
exactly the substitution the visual-verification rule exists to prevent.

**Workaround used.** Assert the projection in `LanguageServersToolViewModelTests` and snapshot only what
fits. Note this is weaker than it sounds: a binding typo renders an empty row and passes every view-model
test.

**Suggested fix, cheapest first.** A `scroll` action on `interact` (`value` = `up`/`down`/`home`/`end`,
resolving to the nearest ancestor `ScrollViewer`) would close it for every scrolled surface at once, not
just this one — the Object Browser, the Translation Editor and the Locals tree have the same shape. Failing
that, `take_snapshot` could accept an optional element `path` and capture that element at its full desired
size rather than clipped to the viewport, which would also make long content diffable.

---

## take_snapshot of the IDE fails while a menu is open, and the default capture picks a modeless dialog

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

## Every parameter of a capture tool was required, including the ones that mean "no filter"

**Symptom.** The first call to `list_lsp_messages` had to pass five arguments to ask the simplest possible
question. `connectionId`, `method`, `failuresOnly`, `afterSequence` and `limit` were all in the schema's
`required` array, so "list everything" could not be expressed as an empty call.

**Cause, and it is a C# detail with a schema consequence.** A nullable parameter with no default value is
still a *required* parameter to the MCP schema generator. `string? connectionId` is optional-looking in C#
and mandatory on the wire. `dump_visual_tree` got this right by accident of having defaults
(`string? root = null, int maxDepth = 20`), and the new tools did not.

**Workaround used.** Pass `null` explicitly for each. It works, and it is five arguments of noise on every
call, which is exactly the friction that makes an agent reach for a different tool.

**Fixed** by giving every optional parameter a C# default. Worth knowing for the next tool: check the
generated schema's `required` array, not the C# signature — they disagree, and only one of them is what an
agent sees.

## The capture state went blank after a clear, which is the one thing it existed to prevent

**Symptom.** `clear_lsp_capture` replied:

```
{"envelopesDiscarded":13,"state":{"armsEveryConnection":true,"connections":[]}}
```

The connection was alive and armed. `arm_lsp_capture` a moment earlier had listed it correctly.

**Cause.** Both mutating tools returned a state whose connection list was derived from the envelopes
present in the record — the same answer as the real list right up until somebody empties the record.

**Consequence, and why it is worse than a cosmetic wrong field.** The state is returned by those two tools
specifically so that arming is not invisible: a tool answering only "done" would leave an agent unable to
tell an armed connection from one whose id it had misspelled. After a clear it answered exactly that.
An agent clearing `hexide.vb6` and one clearing `hexide.vb` got identical replies.

**Fixed** by having the log name its own connections (`ConversationLog.ConnectionIds`) rather than
inferring them from traffic, which also makes a connection armed before it has started visible — the case
the launch flag depends on. Two tests pin it.

**Found on the first real use of the tools**, by driving them rather than by reading them. Both defects had
passing unit tests around them; neither could have been caught by one, because both are about what the
*schema* and the *reply* look like to a caller.

## There is no way to ask what is being recorded without changing it

**Symptom.** To find out which connections existed and which were armed, the only tools were
`arm_lsp_capture` and `clear_lsp_capture` — both of which mutate. Reading the state meant arming something
first.

**Workaround used.** Call `arm_lsp_capture` with the state it already had, and read the reply.

**Fixed** by adding `get_lsp_capture_state`, which is the same reply with nothing changed.

## An export cannot be reached from automation

**Symptom.** `get_lsp_message` returns a body raw, deliberately — it is the developer's own machine and
the live view is not redacted either. But there is no tool that produces the redacted, shareable form, so
an agent asked to attach a conversation to an issue has no safe path: it can read bodies it must not paste,
and cannot produce the form it should paste instead.

**Workaround used.** None needed yet; noted before it is.

**Suggested fix.** A tool over `ConversationExporter`, which already produces the JSON-lines form plus a
manifest and takes a redactor. It is listed as phase-four work in #369 (task 4.5, export and copy), so this
is a note that the automation half of it matters as much as the button — an agent is the likeliest thing to
be asked for an export, and it is currently the only consumer that cannot make one.

**Filed as [#395](https://github.com/hexide-io/HexIDE/issues/395) and closed by
`export_lsp_conversation`.** An entry here is a note to self; the surface ships either way, so an
ergonomics defect gets an issue exactly as a human-facing one does.

**The tool always pseudonymises, and the opt-out is deliberately NOT a parameter on it.** The design
records that a non-pseudonymising mode should exist and that its surface is an open question needing a
prominent warning wherever it lands. A boolean here would have settled that question quietly, in the one
place with nowhere to put a warning. The raw form stays reachable through `get_lsp_message`, so nothing is
inaccessible — only unshareable, which is the distinction the redaction boundary is made of.

Its first real run found a leak no test had: a workspace folder's `name` survives while its `uri` is
redacted, because body redaction is textual by design and a `"name"` beside a URI cannot be recognised
that way — [#397](https://github.com/hexide-io/HexIDE/issues/397).

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

**None of this is tested, and the descriptions are the largest part of the surface.** A description that
misleads a caller produces a wrong call and a green build, and the author is the one person who cannot
evaluate it — the empty-reply defect above was caught by *being* the caller, not by re-reading prose that
had just been written. Filed as [#396](https://github.com/hexide-io/HexIDE/issues/396).

---

## get_document_tabs reported three fewer tabs than the user could see

**Symptom.** A newly built Protocol Inspector tab was visibly open in the tab strip, and:

```
get_document_tabs      -> only the form designer
activate_document_tab  -> "No document tab with title 'Protocol Inspector'"
```

The Object Browser and the language-server connection list were missing too. Three real tabs, in the same
strip, invisible to automation.

**Cause.** Both tools read `IDocumentDockService.OpenDocuments`, which is typed
`IReadOnlyList<BaseEditorWindowViewModel>` and tracks only the editors the service was asked to open. The
Object Browser, the connection list and the inspector are documents the shell adds straight to the dock, so
they were never in that list. Nothing was wrong with the tools' logic; they were answering a narrower
question than the one asked, and the difference was invisible from outside.

**How it bit.** It blocked verification of the very feature being built. Worse, the failure was
*affirmative*: not "I cannot see that kind of tab" but "no document tab with title X", which reads as the
tab not existing. I had to take a screenshot to establish that the thing I had just built was on screen.

**Fixed.** `IDocumentDockService` gained `AllTabs`, `ActiveTab`, `TryActivateAny` and `TryCloseAny`, reading
the dock's own `VisibleDockables`. The three tools now answer about the strip the user sees, `type` gained
a third value `tool` for documents that are not editors, and the description enumerates all three.

**And the error now names what IS open.** A bare "no tab called X" cannot be told from a typo, and cost a
second call to find out. The tabs were already in hand:

```
No document tab with title 'Protocl Inspector'. Open tabs: Object Browser,
Language & Debug Servers, Project1 - Form1 (Form), Protocol Inspector.
```

---

## A DataGrid row could be read but not selected, which makes a master-detail window undrivable

**Symptom.** With the protocol inspector's grid on screen and its rows enumerated by
`dump_visual_tree`:

```
interact(".../DataGrid/DataItem[#2]", "select")
-> {"success": false, "mechanism": "peer", "error": "element does not support 'select'"}
```

`inspect_element` on the same row reported `"providers": []` and `"selectionItems": []`.

**Cause.** A `DataGridRow`'s automation peer exposes no `ISelectionItemProvider`, and `DoSelect` refused
when there was no provider. There was no second route either: the reflection actions set a view-model
property by name and coerce the value from a string, and the property that holds a selection is a row
object no string can name. So the grid was fully readable and completely inert.

**How it bit.** Selecting a row is not a detail of this window, it is the window: click a row, read the
body that crossed the wire. Every master-detail surface in the IDE has the same shape, so the gap was one
control wide and the whole pattern deep. It surfaced while verifying the detail pane, which could not be
verified at all until it was fixed.

**Fixed.** `UiAutomationDriver.DoSelect` falls back to selecting through the grid that owns the row —
found by walking up the visual tree, because `DataGridRow.OwningGrid` is internal — and reads
`SelectedItem` back rather than assuming the grid accepted it. `DescribeProviders` now advertises
`selectionItem` on a row, so the verb is discoverable instead of being a thing a caller has to try.
Covered headlessly in `UiAutomationDriverTests`.

---

## A native file dialog cannot be driven, so every flow that ends in one needed a person

**Symptom.** The protocol inspector's new *Export conversation…* button opens a save picker.
`dump_visual_tree` sees nothing of it — a native Win32 dialog is not in Avalonia's control tree at all —
and while a modal one is up the server does not answer. So the button could be found, enabled and invoked,
and what happened next could not be observed or completed.

**How far it reaches.** Not one button. Save As, Open Project, Add File, Make EXE, Make Project Group,
every export: each of them ends in `IStorageProvider`, and each has been verified up to the dialog and by
hand after it. This was already noted in passing inside a *closed* entry about carried files, which is
where a general gap goes to be forgotten.

**Fixed, and deliberately not by faking the dialog.** `answer_next_file_dialog(path?)` arms the answer the
picker would have returned; `clear_file_dialog_answers` discards what is armed. `WindowManager` consults
the armed answer before reaching for the storage provider, so everything below the picker — the writing,
the naming, the refusals — is the same code a real click reaches. Only the part a person performs is
skipped.

Three properties are load-bearing:

- **Single-shot.** A standing override would silently redirect the next unrelated save, and that damage
  shows up somewhere other than where it was caused.
- **Cancellation is expressible.** An empty path answers as cancelled, which is a distinct branch through
  most of these flows and the one least likely to have been exercised by hand.
- **DEBUG only.** The queue and both call sites compile out with the server, so a shipped build has no
  bypass rather than an unreachable one.

**It did NOT need a session restart, and that is worth recording because the expectation was wrong.** Two
brand-new tool schemas appeared to the already-attached client as soon as the IDE relaunched carrying them,
and were callable in the same session that added them. That matches the measured entry above about
mid-session relaunch rather than the standing advice, which is written for the case where the server was
not attached when the session began. Verified by using both tools to drive the export they were built for,
including the cancellation branch.

---

## `list_lsp_messages` described a vocabulary it does not use, and promised data that was not there

**Symptom.** The tool's own description enumerates what a `kind` can be, because a caller who reads
`Unconsumed` in a reply has no other way to learn what it means. `ConversationEntryKind` has **nine**
members; the description named **seven** of them, and got one of those seven wrong:

- **`StandardError` and `Note` were absent entirely.** A caller shown `"kind": "StandardError"` had been
  told the set and it was not in the set, which reads as a bug in the tool rather than a gap in the
  sentence.
- **`Lifecycle` was described as "a process starting, stopping, its standard error and exit code".** It
  covers none of standard error and, at the time the sentence was written, no exit code either — nothing
  in the tree read one. So the description was the only place in the repository claiming the record held
  data it did not hold, and a caller who trusted it would have concluded the *server* was silent.
- **`direction` said "Sent, Received, or Local for the entries that are not messages at all"**, which
  puts every non-message under `Local`. A standard error line is `Received`, and the vocabulary's own
  definition says so.

**Why it survived.** The two omissions were kinds the enum had and the wire never produced — `StandardError`
because nothing raised it, `Note` because the only path to it was an undecodable frame that the recorder
never saw. A description drifts exactly where the code is unreachable, so the sentence agreed with the
observable behaviour and disagreed with the design. Nothing checks a `[Description]` string against the
enum it enumerates; the coverage guard that would have caught it is the one this repository applies to
`docs/lsp-client.md` and to the language packs, and tool descriptions have no equivalent.

**Fixed** by rewriting both sentences to the full set of nine — four for wire traffic, five that are not
messages — and to what each kind actually carries. The
underlying data gaps were closed in the same change: standard error and the exit code now reach the record
(`ILspTransport.Notice`), and an undecodable frame is recorded as a `Note` with its bytes rather than
dropped. Filed as hexide-io/HexIDE#400 for the general problem — a tool description that enumerates
a C# enum should be guarded against it, the way the LSP coverage table is guarded against the
specification.

