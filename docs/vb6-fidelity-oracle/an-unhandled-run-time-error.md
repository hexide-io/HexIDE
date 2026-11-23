## An unhandled run-time error (2026-10-14, #1011)

What does VB6 do when an event procedure raises a run-time error that nothing handles? The probe has Timer1 logging a
stamped tick every 200 ms, and Timer2 dividing by zero once at 1 s with no `On Error`.

**In the IDE**, driven on the guest's console session (F5, then the dialog's buttons, a screenshot after each):
- **The dialog** is titled *Microsoft Visual Basic* and reads "Run-time error '11':" over "Division by zero". It offers
  **Continue** (greyed), **End**, **Debug** (the default button) and **Help**. The IDE's title stays *[run]* while the dialog
  is up; it is not break mode yet.
- **Nothing runs behind it.** No tick came in 18 s. A `/runexit` pass from session 0 agrees: no tick in 7 s.
- **Debug** enters *[break]*. The faulting statement, `x = 1 \ 0`, is the current statement, with its procedure
  live. When the error is raised in a standard-module Sub the handler called, the break is inside that Sub, on its own
  line. No tick runs in break mode.
- **F5 from break retries the faulting statement, all of it.** For `x = F() + 1 \ 0`, F logged a second call at
  the moment of the F5, and the same dialog came straight back.
- **End** returns to design mode, and raises no QueryUnload, Unload or Terminate.
- **Error Trapping** is the default, *Break in Class Module*. It was read off Tools > Options > General, since the
  registry holds no value for it.

**Compiled, native and p-code**, the dialog seen on the console:
- a message box titled with the program's name, with a warning icon, the same two lines, and OK only;
- nothing runs behind it: no tick in 29 s in session 0, nor in 20 s on the console;
- OK ends the process, and raises no QueryUnload, Unload or Terminate.

**An event procedure is a boundary for error handling.** A handler in the code that raised an event does not catch an
error the event's procedure leaves unhandled. The same in the IDE (`/runexit`) and in both compiled builds:

| probe | the handler | result |
|---|---|---|
| b1 | `On Error GoTo` in the code that sets `Text1.Text`; Text1_Change divides by zero | not caught |
| b2 | `On Error GoTo` in the form around `c.Fire`; `RaiseEvent` to a `WithEvents` sink that divides by zero | not caught |
| b3 | `On Error GoTo` inside `Fire`, directly around the `RaiseEvent` | not caught |

The obvious guess is that a handler further up the stack catches the error, as it would for an ordinary call. It is
wrong across an event, whether a control raised the event or `RaiseEvent` did.

**Where the IDE and a compiled program differ.** A class method with no handler divides by zero, and the form calls it
inside `On Error GoTo` (c1):
- **compiled, native and p-code:** the form's handler catches it, with Err 11;
- **in the IDE**, under the default *Break in Class Module*, the run stops in the class, and the form's handler never
  runs. Re-run on the guest's console (2026-11-18, #4768) with `/runexit`: within 15 s the IDE, titled *[run]*,
  showed the run-time error dialog, "Run-time error '11': Division by zero", with Continue, End, Debug and Help, and
  the form's handler hadn't run. Debug wasn't pressed, so where the break lands is from the first run. The registry
  held no Error Trapping value, so the default applied. Debug and Continue were measured on 2026-11-21, in
  [The Error Trapping option](the-error-trapping-option.md). Debug breaks on the class's statement, and Continue
  retries it. The form's handler still never runs.

The same call to a standard-module Sub (c2) is caught by the form's handler in the IDE and in both builds. So the IDE's
option decides c1, not the language.

**HexIDE follows the compiled program in c1** (decided 2026-10-15, recorded on #1011). The form's handler catches the
class's error, which is also what the IDE does under its *Break on Unhandled Errors* setting. This is a deliberate
exception to the rule that HexIDE matches the IDE where the two differ. The IDE's default stops code that handles its
own errors, and following it would mean building "Continue from a break hands the error to the caller's handler".
That premise was an assumption: VB6's own Continue from that break retries the class's statement, as
[The Error Trapping option](the-error-trapping-option.md) measured on 2026-11-21.
For an error that nothing handles, HexIDE keeps the default's location: the innermost faulting line, a class's
included.

**In HexIDE (#1011),** all of the above is pinned in `HaltOnErrorTests`:
- **The halt** comes before anything unwinds. The dialog offers End and Debug, and nothing runs behind it.
- **Debug** breaks at the statement, reason `RuntimeError`, and Continue runs it again, all of it: `F()` is called again.
- **The event boundary** holds for b1, b2 and b3. c1 and c2 go to the caller's handler.

**Chosen, not measured, when this page was written.** Only `x = F() + 1 \ 0` was measured for the retry. Elsewhere
HexIDE runs again only what raised: a condition on its own, an `ElseIf`'s or a `Loop Until`'s, and a single-line `If`'s
tail statement alone. Two more things weren't measured, and HexIDE takes a side on each. A dialog closed without an
answer takes Debug. `Err` is left as it was at the break.

**Measured since (2026-11-21):**
- **The retry** ([What F5 runs again after Debug](what-f5-runs-again-after-debug.md), #1079). VB6 runs again only what
  raised, as HexIDE does, for an `ElseIf`'s condition, a `Loop Until`'s condition and a single-line `If`'s tail. For a
  `Case`, VB6 runs only the faulting `Case`'s expression again, where HexIDE runs the whole `Select Case` again.
- **The dialog closed without an answer** ([Closing the run-time error dialog without End or
  Debug](closing-the-run-time-error-dialog.md), #607). It can't happen in VB6: the dialog has no close box, and Escape,
  `SC_CLOSE` and Alt+F4 leave it up.
- **`Err` at the break** is still unmeasured.

