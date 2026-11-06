# A run that failed to start was reported as started, and left the debugger saying `Running` — **CLOSED** (#590, 2026-09-22)

> **Fixed.** When the startup form cannot be built, the start now tears down what it had claimed (the
> controller is stopped and no project is left running) and reports the failure through the existing
> runtime-error path: the person gets the runtime-error dialog and `get_last_runtime_error` returns the
> text. `run_project`, `run_to_cursor`, `step_into`, `step_over` and `step_out` answer the failure instead
> of success: `run_project {}` on a form whose button has `Width = -300` answers `Form 'Form1' could not
> be loaded, so the project did not start. -20 is not a valid value for 'Width'. Nothing is running. …`,
> and `get_debug_state` then reads `{"running":false,"state":"Stopped"}`. `get_debug_state` also reports
> `Stopped` on a freshly launched IDE, where it used to report the controller's pre-session `Running`.
> Whether a negative size should be accepted in the first place is #589, still open.

**Symptom.** With the startup form holding a control whose `Width` is -20 (the entry above):

```
run_project {}          → {"success":true}
get_debug_state {}      → {"running":false,"state":"Running"}
get_last_runtime_error  → {"raised":false,"sequence":0}
```

The form never appears. The `state` half is not only this failure's doing: a freshly launched IDE that has
run nothing answers `{"running":false,"state":"Running"}` too, and only reads `Stopped` after a run has
been stopped. The only record is an `ArgumentException` from `VBLoader.PlaceComponentTree`
in the IDE log. `run_to_cursor` takes the same path. A person pressing F5 sees nothing either.

**Workaround.** After a start, confirm with `get_debug_state` that `running` is true, and read the IDE log
(`%LOCALAPPDATA%/HexIDE/logs/ide/`) when it is not.

**Suggested fix.** Route a failed form load through the existing runtime-error path, so the person gets the
runtime-error dialog and `get_last_runtime_error` reports it, and reset the controller to `Stopped`:
[#590](https://github.com/hexide-io/HexIDE/issues/590). Any exception while the startup form loads
takes this path, not only #589's.
