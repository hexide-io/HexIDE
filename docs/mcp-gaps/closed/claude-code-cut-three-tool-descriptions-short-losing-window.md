# Claude Code cut three tool descriptions short, losing `window` "ide" among other things — **CLOSED** (#638, 2026-09-22)

> **Fixed.** Every description a caller reads, a tool's and each parameter's, is now at most 2048 characters,
> and `ToolDescriptionLengthTests` fails the build on one that is longer. Text about a single parameter moved
> into that parameter's own `[Description]`, which the SDK sends in the input schema.

**Symptom.** The `interact` schema Claude Code delivered to a session ended mid-word:
`…a TreeViewItem's own peer offers no prov… [truncated]`. The text before the marker is 2048 characters
exactly, counted from what was received. `list_lsp_messages` arrived cut the same way, at
`…Abandoned (the connection wen… [truncated]`.

**Measured lengths before the fix:** `list_lsp_messages` 3467, `interact` 3093, `dump_visual_tree` 2266, `hover`
2048. The first measurement missed `list_lsp_messages`: a quick script read only a `[Description]` directly after
`[McpServerTool]`, and that tool has three `[DescribesEnum]` attributes between them. The guard found it, since it
reads through the same parser as every other description check.

**What a caller lost.** From `interact` and `dump_visual_tree`, the only mention of passing `window` "ide" to reach
the IDE while a program runs or is paused, when the frontmost window is the program's form. From `interact` also
the dropdown advice and "use dump_visual_tree/inspect_element first". From `list_lsp_messages`, the end of the
`outcome` vocabulary, the sequence-gap explanation, the reply-on-the-request's-row rule, and the filters.

**Not verified in the session that fixed it.** A session caches tool schemas when it attaches, so the new text
could not be read back through the client that cut the old one. The text itself is guarded; that the client
delivers it whole needs a session attached after the change (`/mcp`, reconnect `hexide`).

**2048 is one client's limit, and the tightest known.** Other clients may cut elsewhere, which argues for keeping
a tool's description to what the tool does rather than for raising the limit.
