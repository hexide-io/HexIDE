# `add_control` on a document with no file opened a native picker and stopped the whole server — **CLOSED** (#514, 2026-09-22)

> **Fixed** by hexide-io/HexIDE#539, which refuses before anything is placed: a form with no file and no
> armed answer gets a reply naming `answer_next_file_dialog`, and the description now says so beside the
> sentence about saving. An armed answer, including an armed cancel, lets the save go ahead as before.
> `set_file_content` and `set_control_property` refuse a pathless document outright but only after the
> edit, with advice that leads back to the picker; that is #538.

**Symptom.** `add_control` on the `Form1` of a project created through `File > New Project` never returned.
The call was moved to the background after 120 s and every later MCP call hung behind it. Nothing on the
tool said a save was involved, let alone one that could stop the server.

**Cause.** `add_control` "places a control ... and SAVES the file" — its own description says so. A form with
no path takes the save picker (`ProjectService.WriteFormToDisk`), and a modal native dialog is outside the
control tree: the server cannot answer while one is up, which `answer_next_file_dialog` already documents
for the flows built around it. The two facts are each written down; the consequence of combining them is not.

**Why it misleads.** The save is a clause at the end of a sentence about placing a control, and it is
harmless on every form that has a file — which is every form the dev loop normally reaches, because a demo
project is opened from disk. The one case that hangs is the one a caller reaches deliberately when testing
the pathless state, having just been told by the entry above that it is hard to reach at all.

**Workaround.** Arm `answer_next_file_dialog` immediately before the call: with a path to let the save
happen, or with none to answer it as cancelled, which leaves the document pathless and is what a test of the
pathless state wants. The reply is then honest and useful —
`{"success":false,"controlName":"Command0","error":"'Command0' was added to the designer but the save
failed: The operation was canceled."}` — naming the control that is in the designer and not on disk.

**Suggested fix.** Any tool whose implementation can reach a file picker should say so in its description,
in the same sentence as the save, and name `answer_next_file_dialog`. Better still, refuse rather than block:
a save that would open a picker with no answer armed could return "this document has no file; arm
`answer_next_file_dialog` first", which is a reply instead of a hang. The list of such tools is knowable —
they are the ones that reach `SaveForm` or `SaveModule` with a null `AbsolutePath`.
