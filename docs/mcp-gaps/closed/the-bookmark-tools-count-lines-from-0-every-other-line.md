# The bookmark tools count lines from 0; every other line-taking tool, and the gutter, count from 1

**Symptom.** `set_bookmarks {"name":"Module1","lines":[1]}` (`project` left at null) answers `Carried/Module1
now has bookmarks on 1.`, and the bookmark is drawn on gutter line **2**. `get_breakpoints`,
`set_breakpoints`, `run_to_cursor`, `set_next_statement`, `get_debug_state` and `get_call_stack` all number
lines from 1, and so does the gutter. Both bookmark descriptions do say "0-based". A caller who has just used
any other line tool, or read a line number off a snapshot, is still one line out.

**Workaround.** Subtract one before calling `set_bookmarks`, and add one to what `get_bookmarks` returns.

**Since #273 both numberings count from the top of the file, header included** (task 3.12), as every line tool
now counts, and a line of the header or of a member's Attribute lines is refused, with where the code starts given
in the tool's own numbering. So being one out near the top is sometimes refused rather than placed. Measured in
task 3.16's live check (2026-10-01), on a class added through the IDE whose header is lines 1 to 13:
`set_breakpoints` with `[13]` was refused with "its code starts at 14", and `set_bookmarks` with `[13]` was
accepted, which counting from 0 is the gutter's line 14. One number, refused by one tool and accepted by the
other. Below the header the difference is as silent as it was.

**Suggested fix.** Undecided, and filed as needs-decision:
[#571](https://github.com/hexide-io/HexIDE/issues/571). Either convert at the tool boundary, which changes
the contract, or keep 0-based and have the descriptions say the gutter shows N+1.

> **Fixed.** Decided and done under [#571](https://github.com/hexide-io/HexIDE/issues/571): the bookmark tools count
> from 1, as the gutter and every other line tool do. `get_bookmarks` returns, and `set_bookmarks` takes and
> refuses, lines numbered 1..N from the top of the file, so one number means one line on every tool. The store
> and the `.user.hexproj` it writes still count bookmarks from 0; `MarkLineRules.BookmarkRequest` judges a request
> and converts it, the held set included, so a bookmark read off a header line can be written back. Both
> descriptions say 1-based, `get_file_content` no longer says the bookmark tools count from 0, and the reply's note
> says "counting from 1 at the top of the file", which reaches a caller still holding an old description. Guarded
> by `MarkLineRulesTests` and `LineNumberingDescriptionTests`, which refuses any tool description saying lines are
> counted from 0, in any of its usual spellings, and checks both tool bodies still convert.
