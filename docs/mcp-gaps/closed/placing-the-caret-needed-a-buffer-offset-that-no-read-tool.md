# Placing the caret needed a buffer offset that no read tool returned — **CLOSED** (#273 task 3.18, 2026-10-13)

> **Closed by changing the read, not the offsets.** `get_file_content` returns a form's, module's or class's
> whole file, whether or not it is open: a form's `VERSION` line and designer block, a class's header and the
> leading `Attribute` lines, then the code. That is the text an open code window holds, so every offset a
> caller is given (a `type_text` or `press_key` refusal's `CaretOffset=N`, `inspect_element`'s `CaretOffset`,
> `SelectionStart` and `SelectionLength`) indexes the reply directly, and line N of the reply is line N to
> `set_breakpoints`, `get_diagnostics` and the debugger. `set_file_content` takes the same text back, so the
> two changed together: the whole file with its header unchanged, or the code alone, and only a header that
> differs is refused. Its reply counts the lines the document holds from the top of the file. The tool
> descriptions, and the refusal that gives an offset, now say what offsets and lines count from.
>
> **What is not measured yet.** This is closed on headless tests of the rule both tools relay
> (`DocumentContentTests`, including a read and write-back of every document in `demo/` and
> `corpus/designer/`). The live check, a refusal's `CaretOffset` indexing a `get_file_content` reply in the
> running IDE, is #273 task 3.22's, and its result belongs here when it is taken.
>
> **Taken in part on 2026-10-13 (#273 task 3.22)**, a Debug build of 5541621, on four files VB6 wrote. In the
> form whose code starts at line 74 (`frmLogin`), with the caret at offset 0, `type_text`, `press_key` Delete and
> `press_key` Enter were each refused, pointing at offset 2165 as the nearest place that can be edited. With the
> caret put there the status bar read `Ln 74, Col 1`. `get_file_content {"name":"clsDataSource"}`, not open,
> returned the file header first. What was not done is the comparison this entry asked for: reading the reply's
> text at the offset a refusal gives. That the reply is the open window's text rests on the code, where
> `DocumentContent.TextOf` returns the window's own `CodeWindowText`, and on `DocumentContentTests`.
>
> **Still open, and not a gap:** a way to put the caret at a line and column, or at a text match, would save
> every caller the offset arithmetic. That is [#700](https://github.com/hexide-io/HexIDE/issues/700). An offset
> counts every character of the text, so a client that turns `\r\n` into `\n` before counting comes up one
> short per line above the target.

**Symptom (2026-09-25, during #273 task 3.7's live check).** With a class module open,
`type_text {"target":"…/Custom[Root]/None[TextEditor]","text":"X"}` after `press_key {"key":"Home","modifiers":"Ctrl"}`
was refused with a good reply: the offset is in the header, and *"put the caret there with interact
set_property CaretOffset=371"*. That offset is into the editor buffer, which holds the whole file. But
`get_file_content {"name":"Order"}` answers with the code section only, header stripped, so no read tool
returns the text those offsets count into. To reach a given line the caller has to navigate with keys:
`press_key` Ctrl+End, then Up three times, then Home, to land on a member's `Attribute` line.

**Workaround, before the fix.** Take offsets from a refusal's reply, or navigate with `press_key`.
`inspect_element` on the code view reports `CaretOffset` and `BufferBody`, which is still the code section too.

**Do not compute offsets from the file on disk.** Tried on 2026-10-02 (#273 task 3.13): offsets taken from
`BillOfFare.frm`'s bytes landed four characters early in the editor, because the file is UTF-8 and its two
em dashes above that line are three bytes each and one character each in the buffer. A copy and a paste
then took different text from what was intended, which read like corruption until it was traced. This still
holds after the fix: count in the text `get_file_content` returns, never in the file's bytes.

**Suggested fix, as recorded.** #273 task 3.18 makes `get_file_content` return the whole file, which closes the
mismatch. A way to put the caret at a line and column, or at a text match, would still save every caller
the offset arithmetic. Filed as [#700](https://github.com/hexide-io/HexIDE/issues/700).
