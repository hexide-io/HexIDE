# External debug sessions

The title shows `[break]` while the external session is stopped and the status bar shows Ready,
as for an interpreter break. While the program runs they show `[run]` and the running message;
ending the session immediately returns the title to `[design]` and the status to Ready, even while
the adapter is still disconnecting (#2965).

When a stop opens Locals and Call Stack, re-docking the code editor does not interrupt frame and
variable inspection or print an error in Immediate. Editor callbacks from a previous attachment are
ignored (#3048). Inspection failures retain their exception and stack in the Debug-level log
(`HEXIDE_LOG_LEVEL=Debug`), as well as reporting the message in Immediate.

Choose **Run → Start External Debug Session…** and select a JSON launch configuration. The selected file
starts a separate adapter process; opening a project never executes such a file automatically. One debug
session runs at a time. The configuration does not convert or save the open VB6 project: prepare an existing
backend workspace and save its source files before launching.

```json
{
  "executable": "../adapter/adapter.exe",
  "arguments": ["--dap", "--workspace", "/absolute/path/to/workspace"],
  "workingDirectory": ".",
  "environment": {},
  "adapterId": "external",
  "launch": { "module": "Module1", "entryPoint": "Main" },
  "breakpoints": [{ "path": "Module1.bas", "lines": [6] }]
}
```

The executable, working directory and breakpoint source paths are resolved relative to the configuration
file. The executable must be a file path, not a shell command or a PATH lookup. Arguments stay separate
strings; no shell expansion or variable substitution occurs. Argument contents and launch properties are
adapter-specific and passed unchanged, so use absolute workspace paths there. Environment entries override
inherited variables; a null value removes a variable. Lines are one-based and include source file headers.
Unknown top-level properties and invalid breakpoint lines are rejected. A breakpoint entry containing
`condition`, `hitCondition` or `logMessage` is refused before the adapter starts, with a localized diagnostic naming the launch file and
the one-based breakpoint entry. Set conditions through **Condition…** in the gutter context menu; hit conditions and log messages remain unsupported. Configuration failures appear in
the Immediate window. The initial set combines explicitly listed breakpoints with saved IDE gutter marks and their conditions.
External breakpoint edits are session-only and never rewrite the launch file or saved IDE marks.

During the session:

- F5 continues a stopped program. Break requests a pause, End disconnects and cleans up the adapter, and
  Step Into/Over/Out send the corresponding remote commands. Starting an interpreter run is disabled.
- Once launch configuration completes, gutter clicks and F9 replace the external breakpoint set for that
  source, while running or stopped. Clear All removes the external session's sets. Saved interpreter marks
  reappear after the external session ends; the next launch rebuilds its initial set from JSON and saved IDE marks.
  External markers stay on requested lines: a solid circle is verified, a diamond means the adapter moved
  it, a hollow circle is pending, and a cross is unverified. Hover for requested/adapter lines and the adapter's
  message. A rejected removal keeps the uncertain marker visible; inspect the failure in Immediate and retry.
- Call Stack initially selects the stopped thread's top frame. Select a caller to inspect its non-expensive
  scopes in Locals and reveal its loaded source. Locals clears while the new request is pending; late results
  from older selections are rejected. Expanding a remote aggregate fetches its children asynchronously.
  An accepted resume clears inspection, and the next stop selects its top frame again. While a resume is
  pending, inspection remains visible but cannot be evaluated. A refused continue or step keeps the stack,
  selected frame, locals and current-line mark, and reports the adapter's refusal in Immediate
  ([#2912](https://github.com/hexide-io/HexIDE/issues/2912)).
- HexIDE advertises `supportsInvalidatedEvent`. An `invalidated` event while stopped refreshes Call Stack,
  scopes, Locals and Watches without resuming. The selected caller is retained if its frame ID still exists;
  otherwise the top frame is selected. Pending replies from before invalidation are discarded. This single-thread
  integration refreshes the whole stopped inspection even when area/thread/frame hints narrow the event.
  Events while running do not inspect frames; the next stop refreshes normally (#2914).
  See the [DAP invalidated event definition](https://microsoft.github.io/debug-adapter-protocol/specification#Events_Invalidated)
  for these refresh hints.
- A stop opens Call Stack and Locals. When the source is already part of a loaded IDE project, its code
  window is revealed and marks the current line. The amber execution highlight and Show Next Statement
  continue to identify the top frame even when inspecting a caller. Sources outside loaded projects are not opened automatically.
- Adapter output and failures appear in Immediate. Breakpoint verification appears in the gutter. A launch failure or End
  returns the IDE to its normal interpreter run commands. Closing the IDE also releases the adapter.
- While stopped, type an Immediate command and press Enter to evaluate it in the selected Call Stack frame.
  The line is sent unchanged with DAP's `repl` context: expression/statement syntax and permitted side effects
  belong to the adapter. For example, an adapter may accept `? callerValue` and `callerValue = 99`.
  Results and adapter errors appear in Immediate; output events are appended independently. Locals refreshes
  after completion, including a refused command that may have partially changed state. Only one Immediate
  request is pending at a time. Switching frames or resuming replaces its eventual reply with an expired-context
  message; this does not undo execution. Requests are bounded and are never retried automatically.
- Language & Debug Servers displays the current or most recent DAP connection, with capabilities and shared
  protocol capture. Its previous row is replaced at the next launch; retained conversations remain in the log.

During an external run, Add/Edit/Quick Watch uses a separate expression list retained until the IDE closes.
The Watches window evaluates it in the selected Call Stack frame using the adapter's expression syntax,
refreshes after stops, frame changes, edits and Immediate completion, and clears values on resume.
Expandable results fetch children on demand. Adapter errors appear in the affected row (hover a truncated
value to read it). Break When True/Changed is disabled for external watches. End restores the interpreter
watch list; another external launch restores the external list. Neither list is copied into the other,
and external expressions are not saved in launch JSON. These are adapter evaluations, not a guarantee
that every adapter evaluates expressions without side effects.

While stopped, dwell over an identifier in the code editor to see its value in the selected Call Stack
frame. This requires the adapter's hover-evaluation capability. Unresolved names show no value tip;
frame changes, Immediate evaluation, stepping and End clear the tip. Tips inspect individual identifiers,
not selected expressions or member chains. While the external program runs, hovering retains language-server
quick info.

This integration inspects one stopped program thread. It does not implement thread selection,
restart or Run To Cursor. Sources without a saved file path cannot receive external breakpoints.

The adapter determines which operations it can perform. RDCore `84278aea` advertised goto targets and
conditional breakpoints and applied saved edits in the measurements recorded in [#2914](https://github.com/hexide-io/HexIDE/issues/2914):

- **Set Next Statement** while stopped sends `gotoTargets` for the active editor's saved source path and
  caret line, then `goto` with the first returned target and the stopped thread. This requires
  `supportsGotoTargetsRequest`; without it, the command reports that it is unavailable and sends nothing.
  Empty targets or a refused request report the adapter's message in Immediate and retain inspection and
  the current line, without moving the caret or scrolling the editor on refusal. A successful move displays the location from the following `stopped` event (#2914).
- **Breakpoint conditions** come from **Condition…** on a breakpoint's gutter context menu. They are sent
  only when the adapter advertises `supportsConditionalBreakpoints`. Otherwise the breakpoint still stops
  every time, and Immediate explains this once per session. Launch-file `condition`, `hitCondition` and
  `logMessage` remain refused before launch (#2914).
- **Edits reach the program only through the adapter.** HexIDE sends nothing when you edit. An adapter that
  reads the saved sources when the program resumes, as RDCore's does, runs an edit you saved while stopped,
  so save before you step. Ctrl+S saves the module you are in (#244), and **Save Project** saves them all.
  Where no blank line separates a module's declarations from its first procedure, Save Project inserts one,
  which moves every statement below it. An adapter that
  can't apply an edit refuses the step and leaves the program stopped. HexIDE prints its message in
  Immediate, and keeps Call Stack, Locals, the selected frame and the current line as they were, without moving the caret or scrolling the editor (#2912, #2914).
  Undo the edit and save again, or End. When an adapter sends `invalidated` while the program is stopped,
  HexIDE refreshes Call Stack, Locals and Watches, keeping the selected frame where it still exists
  (#2914). RDCore's adapter sends it only after it applies a saved edit, just before that resume's
  `stopped`, so with RDCore the refresh arrives with the stop (measured against RDCore 84278ae, 2026-11-03).

Two behaviours of RDCore's adapter affect these commands. Both were measured against RDCore `84278aea` on
2026-11-03. Both are RDCore's own, so a later RDCore build may change either:

- **Break doesn't interrupt a running program.** RDCore handles one request at a time, so `pause` and
  `disconnect` wait behind the request that runs the program. In a loop that ran for about 7 s, Break's
  `pause` was answered only when the loop finished. No `stopped` event followed, and the program ran to its
  end ([#2976](https://github.com/hexide-io/HexIDE/pull/2976)). End still ends a program that never
  finishes. RDCore doesn't answer its `disconnect`, so HexIDE stops waiting after 5 s and closes the
  connection, then ends the adapter's process tree if it hasn't exited 5 s later. With an endless loop, the
  adapter and its host process were gone 10.1 s after End
  ([#2980](https://github.com/hexide-io/HexIDE/issues/2980)).
- **Set Next Statement can land somewhere other than the target line.** RDCore moves to the first statement
  of the stopped procedure at or after the target line. So it refuses a line below the stopped procedure,
  and moves a line above it to that procedure's first statement. RDCore's own probe, reported in #2964,
  found that it reads only the line number, not the file, so a line in another file is treated as the same
  line of the stopped procedure's file. RDCore's `gotoTargets` reply labels such a target with the requested
  line, so HexIDE can't tell it from an ordinary move, and shows the line that RDCore's next `stopped` event
  reports ([#2964](https://github.com/hexide-io/HexIDE/pull/2964)).

Breakpoints stay on the line numbers they were set on. An edit that adds or removes lines above one leaves it
on a different statement.

During an external run, `get_debug_state` reports the adapter's state and `stop_project` stops it.
The inspection tools (`get_locals`, `get_call_stack`, the watch tools and `evaluate`) still refuse
external sessions; use the IDE's inspection windows through generic MCP UI interaction instead.

See [the measured external adapter setup](dap-adapter-smoke.md) for a reproducible workspace and platform.

The MCP tools `step_into`, `step_over`, `step_out`, `continue_program` and `break_program`
use these same external commands. Their replies wait up to two seconds for a new stop and its
stack, then report the top frame's source path and line, running, or ended. An adapter refusal
includes its message and says whether stopped inspection was kept. `set_next_statement` uses
`gotoTargets` then `goto` when supported, resolving its module name to the saved document's file
path; an unsaved target is refused. The interpreter's top-level-statement restriction does not
apply to the adapter. `run_to_cursor` refuses external sessions because the external route has
no one-shot breakpoint support; set a breakpoint in the editor and continue instead.

If an adapter does not answer **End** and must be killed after cleanup waits, Immediate shows one
localized notice with its name and elapsed wait in seconds. Any nonempty standard-error tail follows
separately. Exit codes and forced-termination details remain in the log and Protocol Inspector (#2980).

### Breakpoint conditions (#2914)

Right-click an existing breakpoint in the gutter and choose **Condition…**. The dialog prompts for an
expression that must be True to break at that line. Enter an expression in the
adapter's language; an empty field removes the condition, keeping the breakpoint. Cancel keeps the previous
expression. A square around the breakpoint glyph marks a condition, and hovering shows the expression
alongside any adapter verification or message.

IDE breakpoints and their conditions survive in the per-user `.user.hexproj` sidecar, including document
renames and header shifts. They join the launch file's initial source breakpoints; editing them during an
external session follows the existing session-only breakpoint behavior.

A capable adapter receives the expression with `setBreakpoints`. An adapter without conditional breakpoint
support receives only the line. Interpreter runs also stop unconditionally: conditions are not evaluated
there yet. Immediate reports either fallback once per session, including when the first condition is added
while running.

## A launch an add-in contributes

An add-in can contribute a launch through `IHexIdeHost.RegisterDebugLaunch`: an `AddinDebugLaunch` with a title, which
projects it runs (`Claims`), and what makes its configuration for a run (`CreateConfigurationAsync`). The configuration,
an `AddinLaunchConfiguration`, holds a launch file's fields without its breakpoints: the executable, arguments, working
directory, environment, adapter id and `launch` arguments. Its executable and working directory are fully qualified,
since there is no file for them to be relative to; the working directory is the executable's folder when none is given.

The project is described to the add-in as `IProjectAccess` describes it, with its `StartupObject` too: `"Sub Main"`, the
startup form's name, or null.

Nothing runs because an add-in is installed or a project opens. Whether a launch runs the project is asked as the Run menu
opens and when a run of it starts. For whether F5 can start a project the user chose the launch for, its answer is kept
until what it is told of the project changes (its name, file, startup object, references, or its members and their names
and files), so it is never asked at each edit. Its configuration is asked for only when a run of it starts. That is under
the session's cancellation, and End before the session has started ends it even if the add-in never looks at its token.
The session is then the same as a launch file's: one at a time, with the project's saved gutter marks, and conditions
refused in the configuration.

**What F5 runs.** F5 runs the project in the IDE until its user chooses otherwise. **Run > F5 Runs** lists In the IDE
and each contributed launch that runs the startup project, with the current choice checked. The choice is per project
and per user, kept in the `.user.hexproj` sidecar as `runWith` by the add-in's id and the launch's title; choosing In the
IDE removes it. **Run > Start Once With** runs a launch once without changing what F5 runs, and is shown while an add-in
offers a launch for the startup project. F5 Runs is also shown while the project has a choice whose launch is gone.

If the chosen launch is gone, because its add-in was removed or turned off, or no longer runs the project, F5 runs the
project in the IDE and the status bar says so. The choice stays, for when the add-in is back, and shows under F5 Runs as
not available, beside In the IDE, which sets it back. Only F5 and **Run > Start** follow the choice: stepping in from idle
(F8), Run To Cursor, Restart and Start With Full Compile run in the IDE, whatever F5 runs.

Disposing the handle `RegisterDebugLaunch` returns takes the launch out, as do a failed initialisation of its add-in,
turning the add-in off and revoking its consent. A run starts only while the add-in may start what it contributed,
checked before it is asked for the configuration and again before what it made is started; a run refused there ends with
the reason and starts no adapter.
