# A runtime error dialog could be missed entirely, and its text could not be read — **CLOSED** (#342, 2026-09-08)

> **Fixed, in both halves.**
>
> *Its text* is readable because `inspect_element` now reports property values (see the entry below):
> `ErrorText` comes back as a string instead of pixels.
>
> *Missed entirely* is fixed by `get_last_runtime_error`, backed by a `RuntimeErrorLog` recorded **before**
> the dialog is shown and deliberately not tied to its lifetime. `run_project` clears the message, so a
> result belongs to the most recent run; the `sequence` is **not** cleared and only increases, because a
> caller needs to tell "no error this run" from "the same error again" — comparing message text cannot,
> since a loop can raise the identical error twice.
>
> Measured live, in the exact rhythm that used to destroy the evidence — raise, dismiss, stop, then look:
>
> ```
> run → End → stop_project → get_last_runtime_error
>   {"raised":true,"message":"Run-time error '11': Division by zero, at 1 / 0",
>    "at":"2026-09-08T04:12:13+01:00","sequence":1}
>
> (clean program) run → get_last_runtime_error
>   {"raised":false,"sequence":0}
> ```
>

**Symptom.** Running a project that raises a runtime error opens a modal dialog over the running form.
`take_snapshot` does report it (`"activeDialog": "HexIDE"`) and captures it — but only while it is still up.
`stop_project` and `shutdown_ide` both close open dialogs, so the ordinary automation rhythm of run → stop →
snapshot destroys the evidence before it is ever seen. A run whose form silently did nothing looks identical
to a run whose error dialog was dismissed a moment earlier.

Separately, once the dialog IS captured, its text is only readable as pixels. `inspect_element` on the
`RuntimeErrorView` lists `ErrorText` among the DataContext members but returns no value for it, so the
message has to be read off a PNG.

**Consequence.** This is how a real defect stayed hidden: a runtime error raised inside a `.bas` module was
being swallowed (the handler threw before it could show the dialog), and the automated symptom — a form that
runs and does nothing — was indistinguishable from success. It was found only because a human happened to
see a dialog flash on screen.

**Workaround.** `take_snapshot` BEFORE `stop_project`, always, on any run that might raise. Treat
`activeDialog` in the snapshot result as the signal — it names the dialog even when the image is hard to
read. To confirm a swallowed error, check the IDE log at
`%LOCALAPPDATA%\HexIDE\logs\ide\ide-*.log`; an exception thrown inside the error handler lands there.

**Suggested fix.** Expose string property values in `inspect_element`'s `dataContextMembers` (at least for
simple scalars), so a dialog's message is assertable rather than only legible. And consider a
`get_last_runtime_error` that survives the dialog being dismissed — the interesting state currently exists
only for as long as a modal is on screen.
