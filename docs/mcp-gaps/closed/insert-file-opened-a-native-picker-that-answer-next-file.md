# Insert File opened a native picker that `answer_next_file_dialog` could not answer — **CLOSED** (#532, 2026-09-22)

> **Fixed** by moving Insert File into `CodeEditorViewModel.InsertFileAsync`, which asks
> `IWindowManager.OpenFilePickerAsync`: the picker every other file dialog in the IDE uses, and the one an
> armed answer replaces.

**Symptom.** With an answer armed, `invoke_menu_item("Edit/Insert File...")` answered `{"success":true}`
and nothing was inserted. `clear_file_dialog_answers` then reported the answer still pending, which reads
as "the command never ran". It had run. Each attempt left a real native "Open Text File" picker open, three
of them by the time anyone looked, and they were found only by listing the process's top-level windows.
The automation server kept answering throughout, so nothing on the MCP surface showed that a modal dialog
was waiting.

**Cause.** `CodeEditorView.InsertFile` called `TopLevel.StorageProvider.OpenFilePickerAsync` itself, the
only picker in the IDE that did, so it never reached the hook in `WindowManager` that
`answer_next_file_dialog` arms.

**The wrong reading, recorded because it is the natural one.** The tool's description warns that routed
commands "may not execute correctly", and Insert File is a routed command, so a success with no effect
looked like exactly that. The next two attempts went into putting keyboard focus in the editor first. The
pending answer was the misleading evidence: it proved the IDE's picker had not been asked, not that the
command had not run.

**How to tell next time.** A native picker is invisible to every tool here. If an armed answer survives an
action that should have consumed it, list the process's visible top-level windows (a native dialog appears
there under its title) before concluding that the command did nothing.
