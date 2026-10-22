# Real RDCore DAP acceptance

This opt-in console check exercises HexIDE's session client against a real, separately built RDCore platform.
It does not change F5 or the debugger windows. No RDCore code or binaries are bundled in HexIDE.

Build RDCore's LanguageServer, ParseServer and CLI from the same revision, following that project's build
instructions. Supply its platform manifest (`rdcore.json`) and set `RDCORE_PLATFORM_ROOT` to the directory
containing it. The manifest must resolve `LangService`, `ParseServer` and `HostService` to the respective
executables (the runtime host is `rdc`). Keep its configuration and logs isolated from your normal platform.

Create an isolated workspace containing a `.rdproj` file:

```json
{
  "Version": "0.1.0",
  "Configuration": [],
  "ProjectInfo": {
    "Name": "DapSmoke",
    "References": [],
    "Modules": [{ "RelativeUri": "Module1.bas" }],
    "OtherFiles": [],
    "Folders": [],
    "PrecompilerConstants": {}
  }
}
```

Add `Module1.bas` with these exact lines:

```vb
Attribute VB_Name = "Module1"
Option Explicit
Public Sub Main()
    Dim answer As Long
    answer = 41
    answer = answer + 1
    Debug.Print answer
End Sub
```

Run from the HexIDE repository, substituting absolute paths:

```text
dotnet run --project IDE/HexIDE.Dap.Smoke -- <adapter-executable> <workspace-directory> Module1 <Module1.bas-path> 6
```

The check has a 90-second overall deadline, plus bounded process cleanup. It requires a verified breakpoint,
a breakpoint stop at the requested line, a stack frame, at least one visible variable, a terminated event
after continue, and adapter exit code zero without forced termination or cleanup failure. It prints stack,
variables and output for semantic inspection. Nonzero exit means acceptance failed; stderr is printed on failure.
The tool deliberately accepts an existing workspace rather than converting an IDE project. It can execute
that workspace's program, so use the isolated example for reproducible acceptance.

## Measured result

On Windows, 2026-10-18, against RDCore revision `b55abe3a0e9584807d917406976493e963b9b086`, built in Release:

- Real parsing server and runtime host launched successfully.
- Breakpoint verified at line 6; stop reason `breakpoint`.
- Stack: frame ID `0`, `Main`, `Module1.bas`, line 6, column 5.
- Locals: `answer`, type `Long`, value `41`.
- Continue produced output `42` and exited/terminated events.
- Explicit disconnect completed; adapter exit code `0`, forced termination `false`, no cleanup failure.

RDCore keeps the adapter alive after terminated until disconnect or transport closure. Waiting for process
exit without disconnect reached the smoke deadline; explicitly disconnecting completed cleanly. The session
must distinguish program termination from adapter process completion.

This is a Windows interoperability measurement, not a claim of Linux RDCore acceptance
or hosted form support. The independent session/wire tests run on Windows and Linux.

## IDE acceptance

The same revision and workspace were driven through the Windows IDE on 2026-10-18.
Open a VB6 project containing the example source beside the prepared `.rdproj`, then choose
**Run → Start External Debug Session…** and select a [JSON launch configuration](debug-launch.md).
Use the adapter executable above, arguments `--dap`, `--workspace`, and the absolute workspace path,
the prepared platform manifest environment, launch module `Module1` / entry point `Main`, and a
breakpoint on `Module1.bas` line 6.

Observed through live UI automation:

- Locals displayed `answer`, `Long`, `41`; Call Stack displayed `Main`, line 6.
- Debug → Step Over advanced to line 7 and Locals displayed `42`.
- The Start/Continue toolbar button completed the run and Immediate displayed `42`.
- Language & Debug Servers exposed a DAP row with initialize capabilities and stopped lifecycle state;
  Protocol Inspector displayed the session's traffic.
- End during a subsequent stopped session released the run, allowing another launch.
- Closing the IDE while stopped exited the IDE and left no adapter/runtime processes.
- A missing executable displayed its failure in Immediate and allowed another launch.
- A second workspace with a continuous `Do` loop enabled Break; clicking it produced stop reason `pause`
  and displayed the loop's `Double` counter in Locals. End released that session.
- Show Next Statement revealed the loaded source with an amber current-line highlight.

### Selected-frame acceptance

The same adapter revision was exercised on Windows on 2026-10-18 with a nested call:
`Main` sets `callerValue = 42` and calls `Worker`; `Worker` sets `calleeValue = 41`
and stops before printing it. The breakpoint is line 12, and the caller is line 6.

- The initial stack contained `Worker` (frame ID 0) and `Main` (frame ID 1).
- Selecting `Main` changed the Locals context to `Main`, displayed `callerValue`, `Long`, `42`,
  and placed the caret at line 6. Selecting `Worker` restored `calleeValue`, `Long`, `41`.
- Show Next Statement placed the caret at line 12 while retaining caller inspection.
- Step Over while inspecting the caller returned from `Worker` and stopped at `Main` line 7.
  The new stop selected its top frame (ID 0), with `callerValue = 42`.
- Continue cleared Locals and Call Stack; Immediate contained `41` followed by `42`.

The live check also exposed a source-reveal defect: moving the caret in an already active editor
did not scroll it into view. The editor's view-model caret synchronization now scrolls to that line.
A rebuilt IDE was rechecked: selecting the caller scrolled to line 6, and Show Next Statement revealed the amber line 12 while Locals retained the caller. End released the stopped session.

### Immediate acceptance

On Windows, 2026-10-18, the same adapter revision and nested-call sample were driven through the
Immediate editor using live UI automation:

- With `Worker` selected, `? calleeValue` printed `41`.
- Selecting `Main` and entering `? callerValue` printed `42`.
- `callerValue = 99` completed without extra result text and refreshed Locals to `99`.
- `? missingValue` displayed the adapter's refusal, "the interpreter reached something it cannot run yet".
  A subsequent `? callerValue` printed `99`, confirming error recovery.
- The execution highlight remained at `Worker` line 12 while inspecting and evaluating the caller.
- Continue printed `41` followed by `99`, confirming the assignment changed the running program.

These lines use the adapter's REPL syntax. Printed values arrived as DAP output events; the corresponding
successful evaluate responses had empty result text, which the IDE did not duplicate. Independent fixture
tests also exercise nonempty result text, delayed replies, frame changes, resume and End during evaluation.

### Session-only breakpoint acceptance

On Windows, 2026-10-18, the same RDCore revision and nested-call sample were driven through live
UI automation with a saved interpreter breakpoint at line 5 and launch JSON requesting line 12.

- Launch hid the interpreter mark and displayed the pending hollow marker, then the verified solid
  marker at line 12. Hover reported requested line 12, adapter line 12 and verified status.
- F9 and a gutter click each installed a verified breakpoint at line 7. Continuing from Worker stopped
  in Main at line 7 with stop reason `breakpoint`, proving the replacement reached the backend.
- Clicking blank line 14 displayed an unverified cross. Hover reported the adapter message
  "no statement begins on this line".
- Clicking line 7 again removed it. Debug → Clear All Breakpoints removed the remaining external marks;
  Continue completed with output `41` then `42`.
- At idle the saved interpreter set was still exactly `[5]`. The launch JSON SHA-256 was unchanged.
- Relaunch restored the JSON breakpoint at line 12 without the session-added line 7. End again
  restored the interpreter set `[5]`.
- A separate fixture session displayed the relocation diamond at requested line 10; hover reported
  adapter line 11. Relocation and breakpoint-ID events are fixture evidence, not RDCore behavior.

The live run exposed and fixed gutter hit-testing: empty margin space now paints a transparent hit area.
The margin also has a control automation peer, allowing direct hover targeting. Tooltip text was checked
through the generic hover tool's declared-tip result; synthetic pointers did not then open declarative tooltips
(#1265 has since changed that).

### External Watches acceptance

On Windows, 2026-10-18, the same RDCore revision and nested-call sample were driven through live UI automation:

- An interpreter-only watch was created before launch. External launch hid it and used an independent list.
- Debug → Add Watch exposed only the expression watch type; both break-condition radio buttons were disabled.
- `calleeValue` evaluated to `41`, type `Long`, context `Worker` at the line-12 stop.
- Editing the watch to `callerValue` while still in Worker displayed the adapter refusal
  "'callerValue' is not defined". Selecting Main refreshed the same expression to `42`, context `Main`.
- Entering `callerValue = 99` in Immediate refreshed the watch to `99`.
- Continue restored the interpreter-only watch unchanged. Relaunch restored the external `callerValue`
  expression; it again displayed the refusal in Worker, rather than retaining the old frame's value.
- A separate fixture session supplied an expandable watch result. Toggling its row chevron set IsExpanded
  and fetched a child named `callerValue` with value `42` and type `Long`. Aggregate expansion is fixture
  evidence; the real-adapter sample above contains scalar locals.

Screenshots checked the visible expression/value/type/context layout; structured inspection checked values,
radio-button enabled states and fetched children. A clipped Call Stack selection automation issue found
in this run is tracked in #1182; keyboard selection reached the intended frame.

### External hover acceptance

On Windows, 2026-10-19, the same RDCore revision and nested-call sample were driven through live UI automation:

- At the line-12 stop in Worker, hovering `calleeValue` displayed `calleeValue = 41`, including with the editor vertically scrolled.
- Hovering `callerValue` in Worker displayed no tip. Selecting Main displayed `callerValue = 42`.
- After entering `callerValue = 99` in Immediate and waiting for evaluation completion, hovering again displayed `callerValue = 99`.
- Continue cleared the attached value tip and printed `41` then `99`; relaunch did not retain the old tip.
- Protocol capture confirmed `evaluate` with `context: hover` and the selected frame ID. Adapter refusal was suppressed in the editor.

The live check found an existing hover hit-testing defect: viewport coordinates were passed directly to a document-coordinate API, so scrolling could evaluate a different word. Adding the TextView scroll offset fixed the reproduced case. A screenshot checked the editor and selected-frame layout; the generic hover tool checked transient tip text.

Wire/service regression tests separately cover missing/false/non-boolean hover capability, frame zero, caller-frame routing, cancellation, adapter refusal and late replies after stepping, frame selection or Immediate evaluation.
