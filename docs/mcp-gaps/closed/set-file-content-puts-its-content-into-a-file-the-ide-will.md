# `set_file_content` puts its content into a file the IDE will not save, and its refusal says only that the file was not written — **CLOSED** (#701, 2026-11-10)

**Symptom.** Read in code, not run, at ec9bfc4. On a form HexIDE holds read-only as a whole because it cannot save it
faithfully, such as the `frmAbout` with no `.frx` that #273's live check used on 2026-10-13 (archived under the #649
entry):

```
get_file_content {"name":"frmAbout"}
set_file_content {"name":"frmAbout","content":"<that text, with one line of code changed>"}
→ {"success":false,"error":"HexIDE cannot reproduce this file faithfully, so it was not written and the copy on
   disk is unchanged.", "project":…, "document":"frmAbout"}
get_file_content {"name":"frmAbout"}
→ the text that was sent
```

The content is applied before the save is refused. `DocumentContent.Write` asks `GuardedContent` about the header
and members' Attribute lines, never about the file as a whole, so the open code window's buffer takes the content, or
with no window open the model's code does; then `ProjectService.SaveForm` refuses the form (`RefusesUnfaithful`). The
reply says nothing of the IDE's copy, where the same tool's encoding refusal says "The new code is in the IDE, but
the file was not written." A caller reading `success:false` takes it that nothing changed. The window's banner says
typing in it is refused, and `type_text` and `press_key` are refused there with that reason, yet its text has
changed, and no save will write it.

**Workaround.** Before sending content, open the file's code window and `inspect_element` its code view, which lists
the view model's `IsReadOnly` and `ReadOnlyReason` (read in code; #273's check read `BufferBody` there). When
`IsReadOnly` is true the file is held read-only as a whole: send nothing. After this refusal, read it as "the IDE
holds the content and no save will write it", and put the IDE's copy back by sending the text `get_file_content`
returned before the write, which is applied the same way.

**Suggested fix.** #701's: refuse in the guarded write path, before anything changes, with the reason the banner and
`type_text` give. The issue lists this tool among the writers that still edit such a file.
[#701](https://github.com/hexide-io/HexIDE/issues/701).

**Fixed (#701).** `set_file_content` now checks the form's whole-file fidelity verdict before write
preparation, editing or saving, including when no code window is open. It returns the existing localized
code-window banner. Open-window whole-content and range writes were already guarded by #3408.
`WorkingCopyReplyTests.UnopenedUnsaveableFormRefusesAutomationBeforeChangingTheModel` exercises the tool
headlessly on Linux. Live verification remains for the supervising session: send changed code to an
unsaveable form both before and after opening its code window, and confirm the refusal and unchanged content.
