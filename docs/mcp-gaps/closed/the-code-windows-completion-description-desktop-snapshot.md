# The code window's completion description: desktop snapshot composition needed a live check — **CLOSED** (#1334, 2026-10-27)

**Original symptom.** Recorded when #942 closed (#1247), from the code and headless tests; no call was recorded.
The description tooltip beside a completion item was absent from `dump_visual_tree` and `take_snapshot`, and
`interact` `double_click` selected an item without committing it. [#1334](https://github.com/hexide-io/HexIDE/issues/1334).

**Fixed in code, with headless regression tests (#1678).** The dump reaches the open description popup through
`AttachedPopupsOf`, under the completion list, with its text readable and its path resolvable. `double_click`
selects the target and commits it through the list's own commit path. The snapshot composer discovers the
description's top-level root by the same route and composes it after the list, which headless rendering can't prove:
a headless popup renders in the window's own overlay.

**Why the check took a foreign server.** The bundled VB6 server sends no `detail` on any completion item, so with it
alone no item has a description, and the tooltip never shows. The carried-file editor asks no server for completion
(#266), so a C file carried by the project couldn't stand in either. clangd attached to `.bas` beside the bundled server
can, once it parses the module: its first attempt answered `twice` as `"kind":1,"deprecated":true` with no `detail`,
clangd's fallback for a file it can't parse, because `Attribute VB_Name = "Module1"` isn't C. A `.clangd` beside the
probe adding `-xc` and `-DAttribute=char*` made that line a declaration missing its semicolon, which clang recovers from.

**Live (2026-10-27, Windows at 150%, a Debug build of main at 8ae02d39 plus #2401, which touches neither path; clangd
22.1.6 from the foreign-server cache, attached for `.bas` in the session's own profile; headless `claude -p` through
the hexide tools, with `--capture-lsp`).** Module1 read `int twice(int x);` and `int main(void) { return tw; }`.

- `open_file {"name":"Module1","line":3,"column":27}` put the caret after `tw`, and `press_key` Ctrl+Space on the
  editor opened the list: clangd's item first, then the bundled server's keywords.
- `get_lsp_message` on clangd's completion response gave `{"detail":"int",…,"kind":3,"label":" twice(int x)",…}`.
- `interact` `select` on `…/List[PART_ListBox]/Pane[PART_ScrollViewer]/ListItem[twice(int x)]` answered
  `selected ' twice(int x)'`. `dump_visual_tree` rooted at the list then listed `…/List[PART_ListBox]/Text[int]`, a
  `TextBlock` named `int`, beside the list's scroll viewer.
- `take_snapshot {"window":"ide"}` drew the tooltip: a bordered box reading `int`, just right of the list, level with
  the selected row, whole, with the list unclipped beside it. Checked by eye on the image as well as by the caller.

**Found on the way.** `open_file` with a `line` refuses a carried file and opens nothing: #2403, in the live document.
