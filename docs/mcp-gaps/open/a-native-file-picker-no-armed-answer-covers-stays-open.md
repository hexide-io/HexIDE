# A native file picker that no armed answer covers stays open unseen, and a second save can start under it

**Symptom (2026-11-02, #2540's live check, Windows).** A first-contact caller armed one answer,
`answer_next_file_dialog(path: "<folder>\Form1.frm")`, then called `invoke_menu_item(path: "File/Save Project As")`,
which replied `{"success":true,"note":"Invoked 'Save Project As...'."}`. Save Project As asks for each file in turn:
Form1's picker took the answer, and the project's picker opened for real. `dump_visual_tree` showed only the main
window, and `clear_file_dialog_answers` replied `"Nothing was armed."`. The caller read that as no picker being open.
It armed `<folder>\Project1.vbp` and ran Save Project As again. The server answered under the open picker, the second
save's form picker took `Project1.vbp` as Form1's path, and a second real project picker opened. The IDE's process
then had two native `#32770` "Save As" windows, both offering `Project1.vbp`. Nothing was written, since a save
collects every destination first, but confirming the second would have written Form1 as `Project1.vbp`.

`answer_next_file_dialog`'s description says a modal native picker stops the server answering. Here it answered every
call.

**Workaround.** Arm one answer per picker the command raises, in order: each form or module without a path (every
one, under Save As), then the project, then the group. Outside the tools, a window listing of the IDE's process shows a
picker left open.

**Suggested fix.** Record when `WindowManager` falls through to the real storage provider, and say so in the replies
of `invoke_menu_item`, `interact`, `clear_file_dialog_answers` and `dump_visual_tree`'s note. Refuse a command that
would open another save while one is open. [#2870](https://github.com/hexide-io/HexIDE/issues/2870).
