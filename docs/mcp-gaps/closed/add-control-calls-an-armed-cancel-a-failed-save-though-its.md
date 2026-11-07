# add_control calls an armed cancel a failed save, though its description offers the cancel — **CLOSED** (#784 for #755, 2026-10-12)

> **Fixed by #784, verified live on main a816e0d.** On a never-saved Standard EXE from File > New Project,
> with `answer_next_file_dialog {}` armed before each call, `add_control {"formName":"Form1","type":"CommandButton","x":20,"y":20,"width":81,"height":33}`,
> `set_control_property {"formName":"Form1","controlName":"Command0","property":"Caption","value":"x"}` and
> `set_file_content {"name":"Form1","content":"..."}` each answered `success:true`, with a note saying the form
> still has no file and how to write it. `get_form_controls` then listed one control. The cancel is recognised
> from the dialog's state (`ScriptedFileDialogs.WatchForCancel`), not from the exception, which an encoding
> refusal shares (#751). `ScriptedFileDialogsTests` covers that decision.
>
> **Checking it needs a form with no file, and Project > Add Form does not give one** on the project
> `--newproject` creates, because that project already lives in a temp folder. There the save needs no
> picker, the armed cancel is left pending, and the reply does not say so (#810, still open).

**Symptom.** On a new Standard EXE project from File > New Project, so `Form1` has no file:

```
view_designer {"name":"Form1"}
answer_next_file_dialog {}
add_control {"formName":"Form1","type":"CommandButton","x":20,"y":20,"width":81,"height":33}
→ {"success":false,"controlName":"Command0","error":"'Command0' was added to the designer but the save failed: The operation was canceled."}
get_form_controls {"formName":"Form1"}
→ lists Command0 at 20,20, 81 × 33
```

`add_control`'s description says to "arm it with no path to keep the form without a file".
`ScriptedFileDialogs.PickerRefusal`, the refusal `add_control`, `set_file_content` and
`set_control_property` all give for a pathless document, gives the same advice. The call did exactly
that: the control is placed and the form still has no file. The reply says `success:false` and "the
save failed", so a caller cannot tell whether to retry, and a retry adds `Command1` beside `Command0`.

`set_file_content` in the same state answered:

```
{"success":false,"error":"The new code is in the IDE but was not written: The operation was canceled."}
```

That wording is better, because it says where the edit is. It is still `success:false` for the outcome
the caller asked for.

**Which contract is intended.** The description's. The description, `PickerRefusal`, the comment over
`add_control`'s refusal and `WouldShowPicker`'s remarks all present an armed cancel as the way to keep a
document without a file, and the spec's *Driving a cancellation* scenario has the flow proceed as though
the dialog was cancelled. The reply comes from the write tools' catch-all, which predates that advice and
reports any exception as a failure, including the bare `OperationCanceledException` the save throws when
its picker returns no path. `set_control_property` has the same catch, read in code and not measured.

**Workaround.** After arming a cancel for a pathless document, read `success:false` ending "The operation
was canceled." as the requested outcome: the edit is in the IDE and the document still has no file.
Confirm with `get_form_controls` or `get_file_content`, and do not retry.

**Suggested fix.** In those three tools, answer `success:true` with a note giving the state, for
example that the control is in the designer and the form still has no file because the armed answer
cancelled the picker. Decide that it is the cancel before the save, from the document being pathless and
the next armed answer being a cancel. Do not decide it from the exception: #729 adds an encoding refusal
that is also an `OperationCanceledException`, and that one must stay a failure.
[#755](https://github.com/hexide-io/HexIDE/issues/755).
