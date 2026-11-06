# 8. `type_text` bypasses a read-only editor, and reports `mechanism: "keyboard"` while doing it — **CLOSED** (#649, 2026-09-22)

> **Fixed.** `type_text` refuses a read-only or disabled surface, naming which, and reports its mechanism as
> `"document"`; `"keyboard"` is left to `press_key`. Verified live on a form HexIDE opens read-only (a
> `CommandButton` nested in a `ListBox`): `type_text` answered `TextEditor is read-only, so a person could not
> type there either; nothing was inserted`, and `get_file_content` showed the code unchanged. Worth knowing
> when re-testing: the code window is read-only for a form HexIDE cannot save faithfully, not while a program
> runs, so a running program is not the way to reach this.
>
> **Since #273 (the review of task 3.20, 5541621), a file held read-only as a whole is refused with its reason.**
> The code window now asks first whether its file is one HexIDE will not save, and `type_text` and `press_key`
> answer `… refused: this file will not be saved, because HexIDE cannot yet reproduce it faithfully (…). Its code
> is part of the file, so typing in its code window is refused anywhere, as the window's banner says. Nothing was
> written.` On a code window that reply now comes before the one quoted above (`TextEditor is read-only, …`), which a
> form held read-only as a whole no longer reaches (`UiAutomationDriver.RefusedInReadOnlyRegion`, asked first). That
> review found the window's own Enter writing a line break into such a file, past the section provider that refuses
> typing, because its handler writes the document itself; Enter now refuses, and `press_key`
> says why rather than reporting the key pressed. Seen running on 2026-10-13 (#273 task 3.22), on a form whose
> `.frx` was missing: `type_text` and `press_key` Enter each refused with that text.

**Symptom.** Verifying the read-only editing gate (#22) against the running IDE, `type_text` successfully
inserted `XXX_SHOULD_NOT_APPEAR` into a code editor whose `TextEditor.IsReadOnly` was bound true. The result
reported `"mechanism":"keyboard"`, which reads as "a real key event went in" — so the first conclusion was
that the gate was broken. It was not.

**Cause.** The tool's own description says it inserts "at the caret **via the control's own API**", which is
a document mutation, not input. `IsReadOnly` on AvaloniaEdit guards the *editing UI*, so a direct
`Document.Insert` legitimately sidesteps it. The `mechanism: "keyboard"` label is the misleading part.

**Workaround.** Do not use `type_text` to test whether input is blocked. `press_key` raises real
`KeyDown`/`KeyUp`, but note gap #9 below before trusting a negative result from it either. The reliable
check is behavioural at a level the user cares about — here, invoking Save and confirming the file on disk
is byte-identical afterwards.

**Suggested fix.** Report `mechanism: "api"` (or `"document"`) when inserting programmatically, and reserve
`"keyboard"` for genuine key events. Optionally have `type_text` refuse, or warn, when the target editor is
read-only — silently mutating a read-only document is a surprising default for an automation tool.
