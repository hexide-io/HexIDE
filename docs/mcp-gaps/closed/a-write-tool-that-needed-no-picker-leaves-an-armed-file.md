# A write tool that needed no picker leaves an armed file-dialog answer pending, and says nothing

**Symptom.** On the project `--newproject` creates, Project > Add Form adds `Form2` with a file already, because
that project lives in a temp folder:

```
answer_next_file_dialog {}
add_control {"formName":"Form2","type":"CommandButton","x":20,"y":20,"width":81,"height":33}
→ {"success":true,"controlName":"Command0","note":"Command0 is at Left 20, Top 20, Width 81, Height 33, in pixels."}
clear_file_dialog_answers {}
→ {"success":true,"pending":0,"note":"Discarded 1 armed answer(s); the next file dialog will be shown for real."}
```

The save wrote the form's file and never opened a picker, so the cancel armed for it is still pending. The next
dialog to open takes it, however much later and whatever it is for. Only `clear_file_dialog_answers` showed it.

**Workaround.** After any write that was armed for, call `clear_file_dialog_answers`. A count above zero means
the write did not use the answer.

**Suggested fix.** When a write tool finishes with an answer armed before the call still pending, say so in its
note, naming why (the document already had a file) and that `clear_file_dialog_answers` discards it.
[#810](https://github.com/hexide-io/HexIDE/issues/810).

> **Fixed.** `set_file_content`, `set_control_property` and `add_control` say so when answers armed before the call
> are still pending after their save and it took none: in the note on success, and in the error when the save
> failed. The sentence states what happened, not what the answer was for, since a caller may have armed it for a
> later step: how many are pending, that the save opened no picker because the document already has a file, or
> stopped before its picker opened, that the next dialogs will take them, and that `clear_file_dialog_answers`
> discards them if they were armed for this save. Answers are numbered as they are armed, so one armed while the
> save ran is not put down to it. A refusal before the save, such as a name not found, leaves the answer for the
> retry and says nothing. Not covered: a save reached through a menu, by `invoke_menu_item` or `interact`, which
> does not wait for the save to finish; `answer_next_file_dialog`'s description says so and points to
> `clear_file_dialog_answers`. For a document without a file, an answer a dialog elsewhere takes while the save runs,
> as a running program's can, reads as the save's. Guarded by `ScriptedFileDialogsTests`, which also checks that
> every reply a saving tool builds after its save goes through the sentence.
