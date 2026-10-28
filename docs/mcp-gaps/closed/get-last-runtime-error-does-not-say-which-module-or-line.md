# `get_last_runtime_error` does not say which module or line raised the error — **CLOSED** (#955, 2026-10-20)

> **Fixed.** An error carries the module and line it was raised at, the line counted as the code window numbers it,
> header included, and the one the debugger halts on: the innermost statement's, or a condition's own, as an ElseIf's
> or a Loop Until's. The innermost statement it passes out of notes them; a condition, a halt, and an error a statement
> loop raises itself (Resume without an error, a label that is not defined) note them where they are.
> `get_last_runtime_error` returns them as `module` and `line`, and a run tool's note names them. Run-time errors,
> compile errors met mid-run and constructs HexIDE does not implement carry them alike, from a form's event, a halt, or
> Sub Main. Measured with a cold agent on a Debug build, a click that divides by zero:
>
> ```
> continue_program {}
> → {"note":"Running; no pause within 2 s. It raised Run-time error '11': Division by zero at 10 \ d (in frmAsk,
>    line 53); get_last_runtime_error has it.", …}
> get_last_runtime_error {}
> → {"raised":true,"message":"Run-time error '11':\n\nDivision by zero\n\nat 10 \ d","at":"…","sequence":1,
>    "module":"frmAsk","line":53}
> ```
>
> Line 53 of the file is `txtResult.Text = CStr(10 \ d)`. Not covered: an error no statement raised, such as a syntax
> error found before the run began, which names its module in the text already; and the dialog's own text, left as it
> was (#833). Guarded by `ErrorLocationTests`.


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
