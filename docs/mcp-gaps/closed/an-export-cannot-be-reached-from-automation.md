# An export cannot be reached from automation — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #398 (#395), which added `export_lsp_conversation` over `ConversationExporter`, always pseudonymised.**

**Symptom.** `get_lsp_message` returns a body raw, deliberately — it is the developer's own machine and
the live view is not redacted either. But there is no tool that produces the redacted, shareable form, so
an agent asked to attach a conversation to an issue has no safe path: it can read bodies it must not paste,
and cannot produce the form it should paste instead.

**Workaround used.** None needed yet; noted before it is.

**Suggested fix.** A tool over `ConversationExporter`, which already produces the JSON-lines form plus a
manifest and takes a redactor. It is listed as phase-four work in #369 (task 4.5, export and copy), so this
is a note that the automation half of it matters as much as the button — an agent is the likeliest thing to
be asked for an export, and it is currently the only consumer that cannot make one.

**Filed as [#395](https://github.com/hexide-io/HexIDE/issues/395) and closed by
`export_lsp_conversation`.** An entry here is a note to self; the surface ships either way, so an
ergonomics defect gets an issue exactly as a human-facing one does.

**The tool always pseudonymises, and the opt-out is deliberately NOT a parameter on it.** The design
records that a non-pseudonymising mode should exist and that its surface is an open question needing a
prominent warning wherever it lands. A boolean here would have settled that question quietly, in the one
place with nowhere to put a warning. The raw form stays reachable through `get_lsp_message`, so nothing is
inaccessible — only unshareable, which is the distinction the redaction boundary is made of.

Its first real run found a leak no test had: a workspace folder's `name` survives while its `uri` is
redacted, because body redaction is textual by design and a `"name"` beside a URI cannot be recognised
that way — [#397](https://github.com/hexide-io/HexIDE/issues/397).
