# `answer_next_file_dialog` armed one path, so a multi-select Open could not be answered with several files — **CLOSED** (#2408, 2026-10-27)

**Symptom (2026-10-27, hexide-frm's live check of #2395, the CommonDialog).** A ShowOpen with AllowMultiselect and
Explorer (`Flags = &H80200`) had to return two files, as in the oracle's row where `"a.txt" "b.txt"` is typed and
FileName comes back as the folder, Chr(0), `a.txt`, Chr(0), `b.txt`. No call could give that answer.
`answer_next_file_dialog(path: "<folder>\a.txt")` armed one path, which the Open picker returned as a one-element
list. Calling it twice armed two answers, each spent by its own dialog, so the second went to the next picker rather
than to the same pick. Only a person or the headless tests could reach a program's multi-file result. The IDE's own
Add File, which allows several, had the same limit.

**Fixed (#2408).** `answer_next_file_dialog` takes `paths`, every file of one answer in order, beside `path`; the reply
lists them in `paths`. Only an Open that allows several takes them all. A save, or an Open that takes one file, answers
the answer as cancelled, not with its first file, which no person could have chosen there; the answer is spent all the
same, so it cannot redirect a later dialog. The save's own reply fails and says so. Where nothing replies for the
dialog, as with a single-select Open from a menu or a running program, the next `answer_next_file_dialog` or
`clear_file_dialog_answers` reply does, once. Both `path` and `paths`, an empty `paths` or a blank entry is refused, with
nothing armed. Guarded by `ScriptedFileDialogsTests` and `AnswerNextFileDialogPathsTests`. A program's multi-file
FileName is #2395's to format; this gives it the files.

**Live (2026-10-27, a Debug build of the fix, headless `claude -p` through the hexide tools, a new Standard EXE).**

- **Add File with two files.** `answer_next_file_dialog {"paths":["…\\Alpha.bas","…\\Beta.bas"]}`, then
  `invoke_menu_item {"path":"Project/Add File"}`: `get_project_info` listed `"modules":["Alpha","Beta"]`, and
  `clear_file_dialog_answers` then said `Nothing was armed.`
- **A save.** `add_file` a module Gamma, arm two paths, then `set_file_content` Gamma: `{"success":false,"error":"The
  new code is in the IDE. Module 'Gamma' still has no file: the save picker answered as cancelled, because the armed
  answer named several files and it takes one (answer_next_file_dialog's paths is for an Open that allows several), so
  nothing was written. …"}`, re-checked against the final build. The `clear_file_dialog_answers` after it said
  `Nothing was armed.`: the save took the answer, and its reply had already reported the refusal.
- **An Open that takes one file, from a menu.** Arm two paths, then `invoke_menu_item {"path":"Run/Start External
  Debug Session"}`, which replied only `Invoked`. The next `answer_next_file_dialog {}` said `The next file dialog will
  answer as cancelled. Earlier, an Open picker that takes one file took an answer of 2 paths, which it cannot take, so
  it answered as cancelled.`
- **Refused at arming.** `path` and `paths` together: `Pass path for one file or paths for several, not both.`; `paths`
  empty: `paths must name at least one file, and none of them blank. …`; both with `Nothing was armed.`
