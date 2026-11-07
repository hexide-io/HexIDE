## Unload Me (2026-10-14, #973)

What does `Unload Me` raise on the startup form, what does Cancel keep, and what runs after it? Six `/make` probes, each
built native and p-code, all twelve builds agreeing on every line. A Timer drives each. Form_QueryUnload logs its
UnloadMode and Cancel on entry, Form_Unload its Cancel, and Form_Terminate itself.

| Probe | Log |
|---|---|
| `Unload Me`, then two more statements in the same handler | `queryunload, mode 1, cancel in 0`, `unload, cancel in 0`, **then both statements**, then `terminate` |
| QueryUnload sets `Cancel = True`; the next tick unloads again | `queryunload` and its refusal, **no Unload**, the handler goes on; the Timer ticks again; then QueryUnload, Unload, `terminate` |
| Form_Unload sets `Cancel = 1`; the next tick unloads again | QueryUnload, Unload and its refusal; the handler goes on; the Timer ticks again; then QueryUnload, Unload, `terminate` |
| `SendMessage Me.hWnd, WM_CLOSE`, refused in QueryUnload, then allowed | `queryunload, mode 0` twice; refused, the form stays and ticks; allowed, Unload, the sender's next statement, then `terminate` |
| `Unload Me`, then `Me.Caption` | QueryUnload, Unload, **`load` again** (no Initialize), the caption, and the run **carries on**: the next tick comes |
| `Me.Visible` before and after a refused Unload | False both times, in this headless session, where the startup form never reads as visible |

What these show:
- **The rest of the handler runs after `Unload Me`.** Terminate comes after the handler returns, as the run ends.
- **QueryUnload's Cancel stops the unload before Form_Unload is raised.** Unload's Cancel keeps the form loaded too.
  Either way the form goes on running its Timer.
- **UnloadMode is 1 from code and 0 from a close,** WM_CLOSE being what the window's close box sends.
- **A use after Unload loads the form again,** raising Load but not Initialize, and the run carries on. The obvious
  guess, that touching an unloaded form is an error, is wrong.

**An Unload that finds nothing to do.** Three more probes, the same twelve-way agreement, each with `On Error Resume
Next` so an error would be data:

| Probe | Log |
|---|---|
| `Unload Me` twice in a row | QueryUnload and Unload **once**, both statements after, `Err.Number` 0 each time, then `terminate` |
| `Unload Me` inside Form_QueryUnload | the nested statement returns at once with Err 0, then Unload, the handler's statements, `terminate` |
| `Unload Me` inside Form_Unload | the same: nothing raised again, Err 0, then the handler's statements, `terminate` |

So a second `Unload Me` does not load the form again, where `Me.Caption` does. An Unload of a form that is already
unloaded, or is being unloaded, does nothing at all.

**`Unload Me` inside Form_Load** (raised at review, #1046). Five probes, native and p-code, all ten agreeing. Form2's
Form_Load logs, unloads itself, and logs again.

| Probe | Log |
|---|---|
| The startup form, `Unload Me` in its own Load | `load`, QueryUnload mode 1, Unload, **the rest of Load**, `terminate`. No Resize, Activate or Paint, and the exe exits 0 with nothing shown |
| `Form2.Show` from Form1 | Form2's Initialize, Load, QueryUnload, Unload, the rest of its Load, then at Form1 **Err 364, "Object was unloaded"**. `Forms.Count` is 1 |
| `Load Form2` | the same, and the same **364** |
| `Form2.Show vbModal` | the same, and the same **364** |
| The startup form, QueryUnload refusing the unload made in Load | QueryUnload's refusal, the rest of Load, then **Resize and Activate**: the form is shown and the run goes on |

One more thing these show, which matters for #822. **Form2's Terminate is not raised by its Unload.** Each Form2 probe
ends with `End`, which raises no Terminate, and none came before it. A form's Terminate goes with its instance's
release. The startup form's comes as the run ends, which is where the first table sees it.

**In vb6.exe's IDE, the same.** Where the IDE and a compiled program differ, HexIDE matches the IDE (decided
2026-10-14, recorded on #822), with one exception: a class's error that its caller handles (#1011's c1, below). So the probes whose answer could depend on the IDE's host were run again in it, with
`VB6.EXE /runexit`:
- **`Unload Me`** and the statements after it;
- **a WM_CLOSE**, refused and then allowed;
- **`Unload Me` in the startup form's Load;**
- **the 364 at `Form2.Show`;**
- **a refusal in Load.**

All five logs match the compiled builds line for line, Terminate's place at the run's end included. VB6.EXE itself
exited with 0xC000041D every time, as it did in earlier `/runexit` runs from a PowerShell Direct session. Each log was
complete before that.

The rest weren't rerun, because they exercise the language engine rather than the host, and native and p-code already
agree on them:
- the two Cancel probes, which test a ByRef argument;
- reload on use;
- the three no-op shapes;
- the copies of the 364 under `Load` and `Show vbModal`;
- the headless `Visible` probe.

### HexIDE

Since #973 (its first slice), `Unload Me`, or `Unload Form1` naming the running startup form, raises Form_QueryUnload
with UnloadMode 1 and Form_Unload. Each gets a ByRef Cancel, and either keeps the form. They run inside the statement,
as a call does, so the statements after it run once they have. The window goes at once, and nothing more is delivered
to it. Once the handler has returned, Form_Terminate runs, the form's fields are released, and the run ends, its last
form gone. The window's close box raises the same pair with UnloadMode 0, and a refusal keeps the window open. Pinned
in `UnloadTests`, through the IDE's F5 path, with every statement yielding.

Also pinned there, after the review of #1046:
- **`Unload Me` in Form_Load** runs the rest of Load and then Terminate. The form is never shown, and the run ends
  with no report. A refusal there shows the form, as in VB6.
- **An Unload of a form already unloaded, or from inside its own QueryUnload or Unload,** does nothing.
- **Two presses of the close box** ask once. A press while an unload is deciding, or after it with Terminate still to
  come, keeps the window until Terminate has run.

What it still does not do:
- **Reload on use.** A use of the form after it has unloaded is refused as not implemented, naming #822, where VB6
  loads it again. Running on would end the run where VB6 carries on.
- **Another form.** Its Show and Load, and the 364 above, belong to #822's Load slice. So does keeping a form's
  Terminate for its instance's release. With only the startup form, that release is the run's end, which comes straight
  after.
- **`Unload Me` in Form_Initialize** is refused, naming #822. It is not measured.
- **The SingleView host's close box** still closes without asking. The desktop's asks.

