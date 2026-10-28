# `press_key` refuses a Delete over a selection reaching protected lines, where the key deletes the editable part

**Symptom.** Read from the code (#273 task 4.5a records it as already so). With a selection running from code into
the header or a member's `Attribute` lines, a person's Delete deletes the parts the code window's read-only sections
allow. The tool deletes nothing:

```
press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Delete"}
→ {"success":false,"error":"press_key refused: offset N is in the file's header or a member's Attribute lines, ...
   Nothing was written. ..."}
```

`UiAutomationDriver` asks `DeletionChangesARegion` about the whole span, where the key's provider deletes the
deletable segments.

**Workaround.** Narrow the selection to the editable part with `interact set_property` (`SelectionStart`,
`SelectionLength`) before pressing the key.

**Suggested fix.** Delete what the deletable segments allow, as the key does, and say what was left and why.
[#1055](https://github.com/hexide-io/HexIDE/issues/1055).

**Closed (#1055).** Delete and Backspace now reach the real key handler whenever the selection has
editable segments. AvaloniaEdit's selection replacement asks the installed read-only section provider;
no automation code computes the deletion. Replies name the protected remainder and report the caret
and remaining selection. `type_text` uses that replacement API too, placing text in the last deletable
segment as a person's typing does. Wholly protected spans still refuse with #1042's region wording.
Verified by headless code-window integration tests, including unchanged protected text and post-edit selection.
