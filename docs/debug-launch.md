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
the Immediate window. Only explicitly listed breakpoints are installed; editor gutter marks are not imported.

During the session:

- F5 continues a stopped program. Break requests a pause, End disconnects and cleans up the adapter, and
  Step Into/Over/Out send the corresponding remote commands. Starting an interpreter run is disabled.
- Call Stack shows the stopped thread's frames; Locals shows the top frame's non-expensive scopes. Expanding
  a remote aggregate fetches its children asynchronously. Resuming clears inspection; late results are rejected.
- A stop opens Call Stack and Locals. When the source is already part of a loaded IDE project, its code
  window is revealed and marks the current line. Sources outside loaded projects are not opened automatically.
- Adapter output, breakpoint verification replies and failures appear in Immediate. A launch failure or End
  returns the IDE to its normal interpreter run commands. Closing the IDE also releases the adapter.
- Language & Debug Servers displays the current or most recent DAP connection, with capabilities and shared
  protocol capture. Its previous row is replaced at the next launch; retained conversations remain in the log.

This first integration uses one program thread and the top stack frame. It does not implement frame selection,
remote evaluation, watches, hover values, restart, Run To Cursor, Set Next Statement or live breakpoint editing.
The editor refuses live breakpoint changes during the external run. Change the launch file and start a new
session to change its breakpoints. Editing source does not update the running backend.

The existing debugger MCP commands remain interpreter-oriented. During an external run, `get_debug_state`
reports external state and `stop_project` stops it; other execution/inspection tools explicitly refuse remote
operations. Use the normal UI or generic MCP UI interaction for this slice. Remote command-specific MCP
routing is follow-up work.

See [the measured external adapter setup](dap-adapter-smoke.md) for a reproducible workspace and platform.
