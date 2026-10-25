## `End` fires no `Class_Terminate` (interpreter-debugger reset)

Verified: a module-level object held live, then `End` — the class's `Class_Terminate` does **not** run (output is
`BEFORE-END` only; no `TERMINATE-FIRED`). VB6 `End` terminates abruptly and invokes **no** `Terminate` (nor
`Unload`/`QueryUnload`). So the debugger's reset ("Stop") must unwind the walk **without** firing `Class_Terminate`
— the interpreter suppresses it via an aborting-guard on `FireTerminate` while `IDebugController.IsAborting` is set.

### `End` itself, from three places (2026-10-12, #775)

The section above measured one object at module level, and stated the form events without measuring them. Three
probes now cover both. Each is a Standard EXE with a `Class1` whose `Class_Terminate` logs, and forms whose
`QueryUnload`, `Unload` and `Terminate` log. Every log line is written by opening, appending and closing the file,
so nothing waits in a buffer that `End` could discard. Real `vb6.exe` 6.00.8176 (the original release), run in session 0.

| Probe | Log |
|---|---|
| `Form1_Load` creates objects at module and form level and shows `Form2`; a 300 ms Timer creates a local object, logs `tick`, then `End`, then logs `after End` | `Form1_Load`, `Form2_Load`, `tick` |
| `End` inside `Form1_Load`, with a module-level and a local object live, then logs `after End` | `Form1_Load` |
| `Sub Main` creates a module-level and a local object, shows `Form1`, then `End`, then logs `after End` | `Main`, `Form1_Load` |

In every case the exe exited with code 0, and **nothing else was logged**:
- no line after `End`;
- no `QueryUnload`, `Unload` or `Terminate` from either form;
- no `Class_Terminate` for the module-level, the form-level or the local object.

So `End` is the same wherever it is reached, and it is not an unload. HexIDE's `End` (#775) stops the program at
the statement, suppresses `Class_Terminate` through its own `Ending` flag (which also works with no debugger
attached), and in the IDE ends the run as the End command does, reporting nothing.

