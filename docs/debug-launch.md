# External debug sessions

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
Unknown top-level properties and invalid breakpoint lines are rejected. Configuration failures appear in
the Immediate window. Only explicitly listed breakpoints are installed initially; saved interpreter gutter
marks are not imported. External breakpoint edits are session-only and never rewrite the launch file.

During the session:

- F5 continues a stopped program. Break requests a pause, End disconnects and cleans up the adapter, and
  Step Into/Over/Out send the corresponding remote commands. Starting an interpreter run is disabled.
- Once launch configuration completes, gutter clicks and F9 replace the external breakpoint set for that
  source, while running or stopped. Clear All removes the external session's sets. Saved interpreter marks
  reappear after the external session ends; the next launch reads its initial set from JSON again.
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

These three depend on what the adapter offers. Each was measured against RDCore `84278aea` on 2026-11-02,
whose adapter supports all three:

- **Set Next Statement is not offered.** During an external session the command says it is not available
  and sends nothing, even to an adapter that advertises `gotoTargets` (#2914).
- **Breakpoint conditions are not sent.** An external breakpoint carries a line only, and nothing in the IDE
  gives it a condition or a hit count. A launch file whose breakpoint entry has a `condition` is refused
  before launch (#2914).
- **Edits reach the program only through the adapter.** HexIDE sends nothing when you edit. An adapter that
  reads the saved sources when the program resumes, as RDCore's does, runs an edit you saved while stopped,
  so save before you step. Use **Save Project**: Ctrl+S does not save a module (#244), and where no blank
  line separates a module's declarations from its first procedure it inserts one, which moves every
  statement below it. An adapter that
  can't apply an edit refuses the step and leaves the program stopped. HexIDE prints its message in
  Immediate, and keeps Call Stack, Locals, the selected frame and the current line as they were (#2912).
  Undo the edit and save again, or End. When an adapter sends `invalidated` while the program is stopped,
  HexIDE refreshes Call Stack, Locals and Watches, keeping the selected frame where it still exists
  (#2914). RDCore's adapter sends it only after it applies a saved edit, just before that resume's
  `stopped`, so with RDCore the refresh arrives with the stop (measured against RDCore 84278ae, 2026-11-03).

Breakpoints stay on the line numbers they were set on. An edit that adds or removes lines above one leaves it
on a different statement.

The existing debugger MCP commands remain interpreter-oriented. During an external run, `get_debug_state`
reports external state and `stop_project` stops it; other execution/inspection tools explicitly refuse remote
operations. Use the normal UI or generic MCP UI interaction for this slice. Remote command-specific MCP
routing is follow-up work.

See [the measured external adapter setup](dap-adapter-smoke.md) for a reproducible workspace and platform.
