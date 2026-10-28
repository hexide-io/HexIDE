# `get_bookmarks` did not say its lines count from 1 (#1261)

**Symptom.** Read in code at 4789f9a, not run. Since #1245, the bookmark tools count lines from 1 rather than 0.
`set_bookmarks`' note says so ("counting from 1 at the top of the file, as the gutter does"). `get_bookmarks`
returns `project`, `document`, `uri` and `lines` and no note, and its description says "1-based" without saying that
this changed; `LineNumberingDescriptionTests` refuses "0-based" or "from 0" in any description. A session that
loaded the tool's schema before the rebuild keeps "Lines are 0-based" (the schema entry in the live list), so it
reads `[5]` as the gutter's line 6, and nothing in the reply says otherwise.

**Workaround.** After a rebuild onto #1245, use the bookmark tools from a new session, as the schema entry advises.

**Suggested fix.** A `note` on a `get_bookmarks` reply that lists lines, stating the numbering in the words
`set_bookmarks` uses. [#1261](https://github.com/hexide-io/HexIDE/issues/1261).

> **Fixed.** `get_bookmarks` adds a `note` to a reply that lists lines, in `set_bookmarks`' words: "Project1/Form1 has
> bookmarks on lines 10, 11, counting from 1 at the top of the file, as the gutter does." An empty reply has none, and
> one line is "a bookmark on line 10", in both tools. The refusals that give lines, a line the file does not have or
> one in its header, now say the same, for every mark tool. The wording is `MarkLineRules`', where tests reach it.
> Measured through the server's HTTP endpoint on a new project, and guarded by `MarkLineRulesTests` and
> `LineNumberingDescriptionTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools:
>
> ```
> set_bookmarks {"name":"frmSetup","lines":[95,96]}          (a copy of the battleship demo)
> → BattleshipForms/frmSetup now has bookmarks on lines 95, 96, counting from 1 at the top of the file, as the gutter does.
> get_bookmarks {"name":"frmSetup"}
> → {…,"lines":[95,96],"note":"BattleshipForms/frmSetup has bookmarks on lines 95, 96, counting from 1 at the top of
>    the file, as the gutter does."}
> set_bookmarks {"name":"Form1","lines":[10,11]}             (a new project, an earlier run)
> → 10, 11 are in a read-only part of Project1/Form1: its header (1..17) or the Attribute lines that describe a
>    member. Neither ever runs, so no bookmark can go there; its code starts at 18, counting from 1 at the top of the
>    file, as the gutter does. Nothing was changed.
> set_bookmarks {"name":"Form1","lines":[18]}
> → Project1/Form1 now has a bookmark on line 18, counting from 1 at the top of the file, as the gutter does.
> ```
>
> **The `[10, 11]` measured above could not be reproduced on a new project.** Those lines were refused as header
> lines, and the header rule predates the fix. The two-line note was shown on `frmSetup` instead. A `get_bookmarks`
> reply with no lines has no note, as above.
