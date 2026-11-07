# A native file dialog cannot be driven, so every flow that ends in one needed a person — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #402 (#369 phases 4 and 5), which added `answer_next_file_dialog`.**

**Symptom.** The protocol inspector's new *Export conversation…* button opens a save picker.
`dump_visual_tree` sees nothing of it — a native Win32 dialog is not in Avalonia's control tree at all —
and while a modal one is up the server does not answer. So the button could be found, enabled and invoked,
and what happened next could not be observed or completed.

**How far it reaches.** Not one button. Save As, Open Project, Add File, Make EXE, Make Project Group,
every export: each of them ends in `IStorageProvider`, and each has been verified up to the dialog and by
hand after it. This was already noted in passing inside a *closed* entry about carried files, which is
where a general gap goes to be forgotten.

**Fixed, and deliberately not by faking the dialog.** `answer_next_file_dialog(path?)` arms the answer the
picker would have returned; `clear_file_dialog_answers` discards what is armed. `WindowManager` consults
the armed answer before reaching for the storage provider, so everything below the picker — the writing,
the naming, the refusals — is the same code a real click reaches. Only the part a person performs is
skipped.

Three properties are load-bearing:

- **Single-shot.** A standing override would silently redirect the next unrelated save, and that damage
  shows up somewhere other than where it was caused.
- **Cancellation is expressible.** An empty path answers as cancelled, which is a distinct branch through
  most of these flows and the one least likely to have been exercised by hand.
- **DEBUG only.** The queue and both call sites compile out with the server, so a shipped build has no
  bypass rather than an unreachable one.

**It did NOT need a session restart, and that is worth recording because the expectation was wrong.** Two
brand-new tool schemas appeared to the already-attached client as soon as the IDE relaunched carrying them,
and were callable in the same session that added them. That matches the measured entry above about
mid-session relaunch rather than the standing advice, which is written for the case where the server was
not attached when the session began. Verified by using both tools to drive the export they were built for,
including the cancellation branch.
