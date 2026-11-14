# A native file picker that no armed answer covers stays open unseen, and a second save can start under it — **CLOSED** (#2870, 2026-11-14)

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
one under Save As or a scratch project user save, including `--newproject`), then the project, then the group. Outside the tools, a window listing of the IDE's process shows a
picker left open.

**Suggested fix.** Record when `WindowManager` falls through to the real storage provider, and say so in the replies
of `invoke_menu_item`, `interact`, `clear_file_dialog_answers` and `dump_visual_tree`'s note. Refuse a command that
would open another save while one is open. [#2870](https://github.com/hexide-io/HexIDE/issues/2870).

**Fixed (#2870, 2026-11-09).** WindowManager records an open native picker until it completes or fails. The menu,
control, tree and clear replies report it, and menu and control actions refuse underneath it without spending an
armed answer. The arming description includes scratch project saves. Headless tests cover the replies.

**Live (2026-11-14, Windows, a Debug build of main at `e6fb3b859`, `--newproject`).** The original sequence:
- `answer_next_file_dialog(path: "<folder>\Form1.frm")`, then `invoke_menu_item(path: "File/Save Project As")`.
  Form1's picker took the answer and the project's picker opened for real. The reply now says so:
  `{"success":true,"note":"Invoked 'Save Project As...'. An open native file picker is outside the control tree:
  title '(not specified)', suggested file name 'Project1.vbp'. Close or cancel it in the operating system before
  retrying. ..."}`. A window listing of the IDE's process showed one `#32770` "Save As".
- `dump_visual_tree(maxDepth: 2)` and `clear_file_dialog_answers()` gave the same note. The second replied
  `"pending":0`, "Nothing was armed.", and the note.
- `answer_next_file_dialog(path: "<folder>\Project1.vbp")`, then `invoke_menu_item(path: "File/Save Project As")`
  again: refused, `{"success":false,"error":"An open native file picker is outside the control tree: ..."}`.
  `clear_file_dialog_answers()` then discarded 1 armed answer, so the refusal spent nothing. Still one picker, and
  nothing written.
- `interact(target: "Window/Custom[MainView]/None[#0]/None[StandardToolbar]/Button[Standard.SaveProject]",
  action: "invoke", window: "ide")` was refused with the same error.
- With the picker cancelled in the operating system, the notes no longer mention it, and `get_project_info` still
  said the project had no chosen home. Arming `Form1.frm` and `Project1.vbp`, then Save Project As, wrote both files
  and left no picker open.

Two replies still mislead a little, filed as #3913. `answer_next_file_dialog` says "The next file dialog will
return …" for an answer queued behind another, and says nothing about an open picker.
