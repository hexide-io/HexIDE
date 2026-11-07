# Every automation route to a new document saves it — **CLOSED** (#502, 2026-10-24)

**Symptom.** `add_file` says, in its own description, "Adds a new form or module to the startup project, or to
the loaded project `project` names …, **saves the new file to disk**, and returns its path." (Reworded by #596 and
by #273, which added `project`; this entry first quoted "saves it to disk", and the point is the same.)
`--newproject` — the flag the documented dev loop uses on every launch — goes further and saves the whole project
into `%TEMP%` before the first tool call
(`DesktopStartup.cs`, the `--newproject` hook's `SaveProjectToDirectory` call). So a caller driving HexIDE through automation never sees a document that has no
file.

**Why that matters more than it sounds.** #489 measured that in real VB6 a document with no file is the
*ordinary* state: nothing is written until the project is saved. #273 names such a document
`untitled:<Project>/<Name>.<ext>` on the wire, and that branch was measured against five foreign language
servers precisely because a branch nothing exercises is a branch that rots. A model asked to verify it
through this surface will call `add_file`, see a `file:` URI, and reasonably conclude the `untitled:`
spelling is dead code.

**Workaround, and it is not obvious.** Drive the real menu: `dump_visual_tree` to find
`MenuItem[Project]`, `interact` with `expand`, `dump_visual_tree` again scoped to it, then `interact` with
`invoke` on `MenuItem[Add Module]`. That reaches `ProjectService.AddNewModule` — which, as of today, also
writes (hexide-io/HexIDE#500). The only genuinely pathless document reachable at all is the initial `Form1`
of a project created through `File > New Project` **in the UI**, which `--newproject` skips.

**Such a form also reads without its header, since #273 task 3.18.** Read in code, not run. `get_file_content`
returns a form's whole file, except a form created in this session and not yet saved or changed in the designer,
which has no header yet, so its text is its code alone; the tool's description says so. Its first committed designer
change or save gives it one (`HeaderRefresher.ApplyHeader`), its marks move down by the header's height, and from then
its lines count from the top of that header. So a line number read before then is out by the header's height after
it. **Workaround:** read the content again after any designer change or save before using a line number from it.
Where a created form's first header should come from, so that it holds one from the start, is
[#885](https://github.com/hexide-io/HexIDE/issues/885).

**Suggested fix.** Two parts, and the first is cheap. (1) `add_file` should say which of the two states it
leaves the document in, and gain a way to ask for the other — a caller cannot currently express "add it the
way the user's own menu does". (2) A launch flag that creates a project without saving it, so the dev loop
can reach the state the product will normally be in once #500 lands. Until then, any verification of
`untitled:` naming through this surface is verifying something the surface itself prevents.

**Fixed.** #500 made `add_file` pathless: its description and reply explain the null path and the Save Project
pickers. #502 adds `--newproject-unsaved` (also `/newproject-unsaved`), which creates the initial project and
form without saving. It wins over `--newproject` and a positional path; `--newproject` keeps its existing save.
For an `untitled:` check launch with `--capture-lsp --newproject-unsaved`, then open Form1's code and inspect
its `didOpen`. No new VB6 behaviour is inferred: this uses #489's measured no-file state. The initial-header
limitation above remains open as #885 in the live gaps document.
