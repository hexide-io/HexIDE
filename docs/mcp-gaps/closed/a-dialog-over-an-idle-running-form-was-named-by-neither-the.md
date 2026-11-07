# A dialog over an idle running form was named by neither the reply's note nor the wait (#1253)

`interact`, `type_text` and `press_key` on a running form leave the program's dialogs to the wait for the program
(#1232), and the dialog note (#681) skipped them, as the wait names them. But the wait names a dialog only while the
program is busy, and returns at idle naming only forms. A compile error reached from a click shows its dialog over the
form without holding the program, so `invoke` on the button replied `invoked Button 'Command1'`, clean, with the error
dialog open over the form, and the caller's next action landed behind it. Whether the note named it depended on which
window had focus, as that decides the dialog's owner.

> **Fixed.** The wait records the dialog it names, and the note leaves out exactly that one, every other dialog
> included, whatever the program is doing by the time the note looks. A first fix that left a program's dialogs to
> the wait only while the program was held still lost the dialog when a Timer's tick made the program busy as the note
> looked: the reviewer saw 5 of 15 replies with no note. It also hid a second dialog, as the wait names one at most.
>
> The wait now picks the window it names better:
> - a dialog open before the action is left out when the program was idle then, as it held nothing, so an error
>   dialog left open by an earlier call is not named in place of the MsgBox the action raised;
> - among windows the form already owned, the program's own box comes first, as that is what holds a handler;
> - a window that is not the program's box is said to *may* hold the program, not to be a MsgBox holding it.
>
> Measured on Windows through the server's HTTP endpoint, with a 40 ms Timer looping 3000 times:
> - a click reaching `NoSuchSub` was named in the note in 10 of 10 calls;
> - with that error dialog left open, a click raising a MsgBox named the MsgBox in the wait's text and the error dialog
>   in the note, as "still open".
>
> Guarded by `OpenDialogsTests`, `OpenDialogNoteTests` (through the filter) and `ProgramWaitTests` (the compile error
> shown over the form unawaited as the IDE shows it, a MsgBox beside a dialog left open, twice, and a window that is not
> a box).
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools.
> The form is the one above: a 40 ms Timer looping 3000 times, Command1
> calling `NoSuchSub`, and Command2 showing `MsgBox "Hello"`.
>
> - **Ten runs, each one click on Command1, answered with End.** The call was
>   `interact {"target":"Window/Button[Command1]","action":"click","window":"auto"}`.
>   - **The note named it in 8 of 10, against the 10 of 10 measured above:**
>     `'HexIDE' (the run-time error dialog: "Compile error:…", Continue/End/Debug/Help), … opened during this call`.
>   - In the other 2 only the wait gave it, and the note left it out, as above. The wait gave the title alone:
>     `(the program is still running Timer1_Timer, and has a window open over its form, 'HexIDE'; it may hold the
>     program until it is answered, …)`. So those two replies never said what it was, and the reset prompt has the
>     same title: [#1975](https://github.com/hexide-io/HexIDE/issues/1975).
> - **With that dialog left open, a click on Command2:**
>   - the wait's text named the MsgBox, as "a MsgBox holds its handler", by its title `'DialogProbe'`;
>   - the note named the error dialog as "still open and waits for an answer; this call reached past it, as a
>     person could not".
> - **Answering the MsgBox** gave a "still running" list of Command2_Click, Timer1_Timer, Command2_GotFocus and
>   Command2_LostFocus. The form has no handler for the last two:
>   [#1976](https://github.com/hexide-io/HexIDE/issues/1976).
