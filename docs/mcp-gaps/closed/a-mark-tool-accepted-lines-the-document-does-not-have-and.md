# A mark tool accepted lines the document does not have, and persisted them — **CLOSED** (#569, 2026-09-22)

> **Fixed.** `set_breakpoints` and `set_bookmarks` now refuse the whole call when any line is outside the
> document, name the lines and the valid range, and change nothing:
> `set_breakpoints {"name":"Module1","lines":[1,3]}` on a two-line module answers `3 is not a line of
> Carried/Module1, which has 2 lines: breakpoints are numbered 1..2. Nothing was changed.` The bookmark
> refusal adds "counting from 0", because the two tools number lines differently.

**Symptom.** `set_breakpoints {"name":"Module1","lines":[-1,0,99]}` (`project` left at null) on a two-line
module answered `{"success":true,"note":"Carried/Module1 now has breakpoints on -1, 0, 99."}`, and
`get_breakpoints` read the same three back. `set_bookmarks` did the same. The gutter showed none of the
breakpoints, because none is a line (breakpoints count from 1). The marks survived a relaunch too: they had
been written to the project's `*.user.hexproj` sidecar.

**Why it mattered.** The reply and the readback agreed with each other, and both were wrong. A caller who
gave bookmark numbering to the breakpoint tool, or the other way round, got a confident success and then a
run that never broke.

**Still open.** A sidecar that already holds such lines is loaded as-is, and a breakpoint on a line with no
executable statement is still accepted. Both are noted on #569.

**Since then.** The sidecar half was closed by #574's fix (#620): a mark on a line its document does not have is
dropped at load, and logged (`UserSidecarService.WithinDocument`). #273 then changed what the range counts and what
else is refused (task 3.12, 2026-10-01). The range counts from the top of the file, header included, and the
refusal says so: `… breakpoints are numbered 1..N, from the top of the file. Nothing was changed.` A line of the
header or of a member's Attribute lines is refused too, saying where the code starts (`MarkLineRules.Refusal`), and
so are `run_to_cursor` and `set_next_statement` aimed at one; a sidecar's mark on such a line is dropped at load
(task 3.17). Any other line with no executable statement, a blank or a comment, is still accepted.
