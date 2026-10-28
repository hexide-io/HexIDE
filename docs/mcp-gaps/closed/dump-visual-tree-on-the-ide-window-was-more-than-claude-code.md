# `dump_visual_tree {}` on the IDE window was more than Claude Code takes inline — **CLOSED** (#1982, 2026-10-26)

**Symptom (2026-10-25, #1330's check of #942, Windows).** `dump_visual_tree {}`, the call a first-time caller makes,
returned 66,822 to 83,944 characters on the IDE's main window. Claude Code saved each reply to a file and told the model
to read it in chunks, so nothing could be read from the reply itself.

**Fixed (#1982).** Claude Code (2.1.310) saves an MCP tool's text reply over 50,000 characters to a file, counting
characters, not tokens. So a tree over 40,000 characters is now cut at the most levels down that fit, and the reply's
`note` says how large the whole tree was, where it was cut and how to see the rest: a node marked `truncated` holds
more, so dump again with its path as `root`, or use `match`. A node with too many children to fit even one level down
says so. The note is written before the tree, so a client that keeps only the start of a long reply still has it. The
tool's description says so too. Guarded by `DumpFitsReplyTests`.

**Live (#1982, 2026-10-26).** Windows, a Debug build of the fix, from a Claude Code session's hexide tools, with Form1's
code and designer open:

- `dump_visual_tree {}` came back inline: `The tree to maxDepth 20 is about 75,967 characters, more than some clients
  take inline in a reply, so it was cut at maxDepth 7, about 33,631 characters.` Every `truncated` node was at level 7.
  After review the note says "cut 7 levels down" instead, since under `interactiveOnly` a cut is not the same tree as a
  dump with that `maxDepth`.
- `dump_visual_tree {"interactiveOnly": false}`: about 108,281 characters, cut at maxDepth 6, about 35,875, inline.
- A dump rooted at a truncated node, the designer's `Custom[Root]`, listed its children, with no note.
- `maxDepth: 3` came back whole, with no note.

The cut was one depth everywhere, so hidden toolbars came back whole while the open document was cut
([#2033](https://github.com/hexide-io/HexIDE/issues/2033)). The #2033 fix first omits children below hidden
nodes when a tree exceeds the budget, retaining their paths, `isHidden` and `truncated` markers. Only if
that still exceeds the budget does it cut levels in other branches, preserving the active document
subtree and its ancestors. The requested root keeps its children
so dumping from a hidden node makes progress. This applies with either `interactiveOnly` setting; trees
that already fit and `match` searches are unchanged. The note explains what was omitted and how to see it.
PR #2169's live check failed: `dump_visual_tree {}` still cut the designer at `Custom[Root]/Pane`,
eight levels down, with both 3 and 26 controls (supervisor, 2026-10-26). Hidden pruning alone was insufficient.
The follow-up preserves the active tab's content while cutting other branches; if that content alone
exceeds the budget the note says it is still over and advises `root`.
Covered headlessly by `DumpFitsReplyTests` and `ActiveDocumentDumpTests`; live verification of this
follow-up remains for supervisor review.
