# First-contact runs

Results of giving the automation surface to a model that has never seen it, with nothing else to go on: no
repository, no `CLAUDE.md`, no design record. [`mcp-first-contact.md`](mcp-first-contact.md) is the recorded walk a
first caller should be able to take; this file is what callers actually did. Each run is a row, so a later run can be
compared with an earlier one ([#534](https://github.com/hexide-io/HexIDE/issues/534)).

## How a run is made

- **Client.** Claude Code, headless (`claude -p`), from an empty directory, with `--setting-sources ""` and
  `--strict-mcp-config`. With those flags it reads no `CLAUDE.md`, and it sees no MCP server but `hexide`, which was
  measured before the first run. The prompt is the task and nothing else.
- **Tools.** `--allowedTools` names the hexide tools and `Read`, since a snapshot comes back as a path. Claude Code
  still allows its read-only tools without asking, and the models used `Glob`, `Grep` and read-only `Bash` and
  `PowerShell` to look around the empty directory, and found nothing there. Opus used `Read` on the project's `.frm`, whose
  path `get_project_info` gave it, which a caller with `Read` is entitled to. A later run should pass
  `--disallowedTools` for those.
- **IDE.** A fresh Debug build per run, started with `--newproject` in a profile directory of its own, nothing armed.
- **Outcome.** One of these:
  - *reached*: the right result, checked against the IDE afterwards by a separate read-only run;
  - *wrong*: it said it was done and was not;
  - *gave up*;
  - *asked*: it stopped and asked, recorded with whether the question was the right one.
- **Counting.** Calls include `ToolSearch` and the client's own tools. A failed call is one whose reply was an error
  or `success: false`.
- **Findings.** Every stumble is a finding. Findings go to [`mcp-server-gaps.md`](mcp-server-gaps.md) and are filed.

## The tasks

1. **Protocol record.** "What, if anything, has the VB6 language server said about Form1's code?" It starts from
   the empty reply #396 began with, and the answer needs `open_file`, which the task doesn't name.
2. **Designer.** "Put a command button on Form1 and make its background red, and check that it is."
3. **Run.** "Make Form1's title bar say Hello when the program starts, run it, and confirm it does."
4. **No direct tool.** "Turn on the option that makes new modules require variable declaration." It is reached
   only through the Options dialog.

## 2026-10-26: the baseline

Build: main at `d0014388`, as merged for batch 36 with #2044, #2048 and #2049, plus #2048's trim of
`dump_visual_tree`'s description (`b7965336`). Claude Code 2.1.310, Windows, each model at its default effort.

| Model | Task | Outcome | Calls | Failed | What happened |
|---|---|---|---|---|---|
| Haiku 5.5 | 1 | asked | 4 | 0 | `get_diagnostics` and `list_lsp_messages` said no server had started and to `open_file`; it answered "nothing yet" and offered to open the code. |
| Haiku 5.5 | 2 | wrong | 11 | 0 | Added the button, then made the *form* red, reading "its background" as the form's, and said so. |
| Haiku 5.5 | 3 | reached | 12 | 0 | Set the design-time `Caption`, ran, and called the snapshot blank: it holds no title bar (#2078). |
| Haiku 5.5 | 4 | reached | 15 | 0 | Options, Editor > General, the check box, OK; reopened the dialog to confirm. |
| Sonnet 5.5 | 1 | asked | 4 | 0 | `get_diagnostics` said no server had started, and `get_lsp_capture_state` showed no connections; it answered "nothing yet" and offered to open the code. |
| Sonnet 5.5 | 2 | reached | 8 | 1 | `Style` refused as not found (#913); set `BackColor` and confirmed by snapshot. |
| Sonnet 5.5 | 3 | reached | 7 | 1 | The failed call was a `PowerShell` listing. Set the `Caption`, ran, and read the title from `activeDialog` (#2078). |
| Sonnet 5.5 | 4 | reached | 10 | 1 | Tried `Editor/General` as a path, refused without a next step (#2080), then dumped. |
| Opus 5.5 | 1 | asked | 4 | 0 | `get_diagnostics`, `get_project_info` and `list_lsp_messages`; it answered as the others did and offered to open the code. |
| Opus 5.5 | 2 | reached | 10 | 1 | `Style` refused (#913); confirmed by property, snapshot and the saved `.frm`; warned that vb6.exe wouldn't show it red. |
| Opus 5.5 | 3 | reached | 8 | 0 | Wrote a `Form_Load` setting `Me.Caption`, ran, confirmed from `activeDialog`, stopped. |
| Opus 5.5 | 4 | reached | 8 | 0 | `match` found nothing on the page not showing (#2032), so it dumped the dialog and found it. |

**Read across the runs:**
- **No model failed on the surface.** The one wrong outcome is a reading of the task.
- **Task 1 never reached `open_file`.** Every model followed the empty reply's note far enough to explain it, and then
  asked before opening anything. That is the right question, since opening a file changes the user's IDE. The run did
  not test whether a model would go on to read the diagnostics.
- **Every model's task 3 ended without seeing the title bar.** Each took a snapshot to confirm it, and none could:
  the snapshot is the client area only (#2078).
- **Every model's task 4 took the `modalDialog` note's word for the Options dialog's buttons.** The note counts two
  check boxes among them (#2079), which no run tripped over.
