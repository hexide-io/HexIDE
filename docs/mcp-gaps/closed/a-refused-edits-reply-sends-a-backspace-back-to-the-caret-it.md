# A refused edit's reply sends a Backspace back to the caret it was refused at, and does not say whose lines are in the way

**Symptom.** Found in #273's live check (task 4.8) on 2026-10-14, with a class whose `Public Function Total()` has
its run `Attribute Total.VB_UserMemId = 0` folded into the declaration's row, and the caret put at the start of the
body line below it, offset 790:

```
interact {"target":".../Custom[Root]","action":"set_property","value":"CaretOffset=790"}
press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Back"}
→ {"success":false,"error":"press_key refused: offset 789 is in the file's header or a member's Attribute lines,
   which only the IDE changes (the designer, a save, a rename). Nothing was written. The nearest place that can be
   edited starts at offset 790; put the caret there with interact set_property CaretOffset=790. ..."}
```

The refusal is right: Backspace there would join the body line to the run's last line. The advice is not. Offset
790 is where the caret already was, so a caller that follows it presses the same key in the same place and is
refused again, which a first-contact model can loop on. The reply also says "the file's header or a member's
Attribute lines" although the IDE knows which, and since #273 task 4.5b names the member in an add-in's refusal
(`GuardedContent.MemberNamedBy`). `type_text` gives the same wording: at offset 766, inside the run, it said
"header or a member's Attribute lines" and pointed at 790.

**Workaround.** Read the refused offset, not the advice. A backward deletion refused at the start of the line under a
run has nowhere to go on that line; delete the declaration line whole instead (Ctrl+D, which takes the run with it).

**Suggested fix.** Name the region: "the file's header" or "the Attribute lines describing Total" (`MemberNamedBy`).
For a deleting key whose span ends at the caret, say that the text before the caret is the run's and that deleting
the declaration line takes both, rather than pointing at the caret's own offset; keep the "nearest place" advice for
an insertion or a forward deletion, where it moves the caller somewhere new.
[#1042](https://github.com/hexide-io/HexIDE/issues/1042).

**Fixed (#1042).** Refusals now name the header or the member through the same name reader as add-in refusals. A deletion ending at the caret explains that the preceding text belongs to that region; for a member, deleting its whole logical declaration removes its Attribute lines too. Insertions and forward deletions retain a new editable offset. Headless editor regressions cover all three cases and fail against the previous wording.
