# MCP dev-server gaps (found in use)

Limitations of HexIDE's embedded MCP automation server, each hit while actually driving the IDE rather
than reasoned about in advance. The MCP server is a **dev/automation tool, `#if DEBUG`-only** (compiled
out of Release), so these are developer-workflow gaps, not product bugs. Recorded so they can be fixed
deliberately; the generic trio (`dump_visual_tree` / `interact` / `inspect_element`) otherwise works well
for non-modal surfaces.

## Where the entries are

Each gap is a file of its own, and the folder it is in is its status:

- **[`mcp-gaps/open/`](mcp-gaps/open/)** holds what still bites. Its length is a count of that, which is
  why fixed entries leave it.
- **[`mcp-gaps/closed/`](mcp-gaps/closed/)** holds entries retired once fixed. They are kept, rather than
  deleted, because each records a *measurement*: what the symptom looked like, what was tried, and which
  reading of it turned out to be wrong. That is the part which stops a fixed gap being rediscovered as a
  new one, and it is not recoverable from the commit that closed it.

Entries used to live in two shared files, this one and an archive beside it. Every pull request that recorded or
closed a gap edited the top of the same file, so any two of them conflicted, whatever else they changed
([#2510](https://github.com/hexide-io/HexIDE/issues/2510)). One file per entry means they never touch the same lines,
as `changelog.d/` already does for the changelog.

## Writing an entry

- **Name it after the issue filed for it**: `2513.md`. Every shortcoming is filed as well as recorded, so there
  is always an issue. Where one issue needs several entries, add a word: `2416-frame.md`. The entries written
  before this layout carry a name taken from their heading instead.
- **Start with a level-one heading** that states what goes wrong, as a caller would meet it.
- **Then the symptom, the workaround and the suggested fix**, each as its own paragraph.
- **Record the exact call an entry failed with, arguments included.** A conclusion drawn from a call with
  unstated arguments cannot be checked by the next reader, and it can be wrong without anyone noticing: one
  entry concluded that a document tab's content was "neither drivable nor readable" from a
  `dump_visual_tree` run with its default `interactiveOnly: true`, which filters out plain text. With
  `interactiveOnly: false` every field was there (#362). Write `dump_visual_tree(root: …, interactiveOnly: false,
  maxDepth: 14)`, not "`dump_visual_tree` returns only the tab chrome". Misspelled arguments such as
  `dump_visual_tree {"interactive_only": false}` now run with an explicit ignored-argument note and a suggestion
  of `interactiveOnly` (#1315); an omitted argument still uses its declared default.
- **To close one**, `git mv` it from `open/` to `closed/`, add what fixed it and what the live check showed,
  and say in its heading what closed it and when: `— **CLOSED** (#2460, 2026-10-28)`. Moving the file, rather
  than copying it, is what keeps an entry from being both open and closed. Three were, before this layout.

`McpGapsDocTests` checks the names, the headings, that no gap is both open and closed, and that no entry is
written back into this file.

## Numbered entries

The earliest entries were numbered, and code comments and other documents still cite them by number: "gap 12",
"entry 3". Two entries were numbered 7.

| Gap | Entry |
|---|---|
| 1 | [Blind to runtime modal dialogs layered over a running form](mcp-gaps/closed/blind-to-runtime-modal-dialogs-layered-over-a-running-form.md) |
| 2 | [`set_control_property` only handles string / number / bool](mcp-gaps/closed/set-control-property-only-handles-string-number-bool.md) |
| 3 | [Three tool calls while the IDE is down drop the tools until `/mcp` (narrowed 2026-10-12)](mcp-gaps/open/three-tool-calls-while-the-ide-is-down-drop-the-tools-until.md) |
| 4 | [Can't snapshot or drive the IDE while a form is running](mcp-gaps/closed/cant-snapshot-or-drive-the-ide-while-a-form-is-running.md) |
| 5 | [A designer control is reported under its view-model's type name, not its own](mcp-gaps/closed/a-designer-control-is-reported-under-its-view-models-type.md) |
| 6 | [Can't open a Debug-menu dialog (e.g. Add Watch) via MCP to snapshot it](mcp-gaps/closed/cant-open-a-debug-menu-dialog-add-watch-via-mcp-to-snapshot.md) |
| 7 | [A declarative `ToolTip.Tip` cannot be made to open (narrowed 2026-09-08)](mcp-gaps/closed/a-declarative-tooltip-tip-cannot-be-made-to-open.md) |
| 7 | [`hover`'s move through the window's input was measured headless only](mcp-gaps/closed/hovers-move-through-the-windows-input-was-measured-headless.md) |
| 8 | [`type_text` bypasses a read-only editor, and reports `mechanism: "keyboard"` while doing it](mcp-gaps/closed/type-text-bypasses-a-read-only-editor-and-reports-mechanism.md) |
| 9 | [`press_key` on a non-focusable container silently does nothing](mcp-gaps/closed/press-key-on-a-non-focusable-container-silently-does-nothing.md) |
| 10 | [A crashed IDE is indistinguishable from a slow tool call (narrowed 2026-09-22)](mcp-gaps/open/a-crashed-ide-is-indistinguishable-from-a-slow-tool-call.md) |
| 11 | [`add_control` mutated the designer but never persisted](mcp-gaps/closed/add-control-mutated-the-designer-but-never-persisted.md) |
| 12 | [A menu popup on a running form cannot be seen or driven](mcp-gaps/closed/a-menu-popup-on-a-running-form-cannot-be-seen-or-driven.md) |
| 13 | [`shutdown_ide` reports success but the process survives a runtime modal](mcp-gaps/closed/shutdown-ide-reports-success-but-the-process-survives-a.md) |
| 14 | [`set_file_content` silently dropped a form's `Attribute` header](mcp-gaps/closed/set-file-content-silently-dropped-a-forms-attribute-header.md) |
| 15 | [`dump_visual_tree` showed a hidden element as though it were on screen](mcp-gaps/closed/dump-visual-tree-showed-a-hidden-element-as-though-it-were.md) |

## Not an MCP gap (recorded to avoid confusion)

- **`Debug.Print` didn't reach the Immediate window** — that was an *interpreter* bug (the `Debug` object was
  seeded only in the test fixture, not the live F5 run), fixed this session (`VBDebugConsole`), not an MCP
  limitation. It's listed here only because gap #1 made it hard to *see* the resulting error.
- **`hexide` missing from `/mcp` entirely, even after resumes and reconnects**: the project-local
  `.claude/settings.local.json` had `"disabledMcpjsonServers": ["hexide"]`, so the client skipped the
  `.mcp.json` entry altogether (2026-09-25). Check that list before diagnosing the server.

## An empty reply that does not say why is a defect, not a null result

**The standing bar for this surface, recorded because it was stated as a correction.** The automation
surface is not internal scaffolding for whoever is building HexIDE. It ships to every developer who wants
it, driven by models nobody here chooses. **A suboptimal AI surface bites exactly as a bad UI/UX surface
bites a human user**, and "I found a way around it" is not the test — the person who found the way around
it had context a first-time caller does not.

**The worked example, and it was mine.** The first call to `list_lsp_messages` in a fresh session returned:

```
{"messages":[],"matched":0,"truncated":false,"framesDropped":0}
```

I knew why: servers start on the first document of a language they claim, and nothing was open. A caller
who did not know that cannot tell "nothing happened" from "nothing was configured" from "the tool is
broken" — **which is the exact ambiguity the protocol inspector exists to destroy**, reintroduced inside
the tool built to destroy it. That is the worst available place to put it.

It now answers:

```
"note": "No language server has connected yet, so there is nothing recorded. Servers start on the first
         document of a language they claim — open a file and ask again. Nothing needs arming for
         envelopes to be recorded."
```

Four states a bare zero collapses, each now named: no server has started; the connection id does not exist
(and here are the ones that do); connections exist with an empty record, so it was cleared; the filter
excluded everything. Six tests pin them, including one asserting the note is **absent** on an ordinary
reply — a field that is always populated stops being read.

**The checklist this generalises to**, for any tool added here:

- An empty or surprising reply explains itself, and says what to do next where there is an obvious next step.
- Check the generated schema's `required` array, not the C# signature. They disagree, and only one is what
  a caller sees.
- Enumerate the vocabulary a reply uses. A `kind` of `Unconsumed` or `Lifecycle` means nothing to somebody
  who was never told the set, so the description now lists all seven and says which three exist nowhere else.
- Explain anything that looks like a defect and is not. Sequence numbers have gaps, because a reply
  completes its request's envelope rather than adding one; beside a field called `framesDropped`, an
  unexplained gap reads as data loss.
- A reply that mutates reports the new state, and reports it even when the answer is empty.

**The mechanical half of this is now tested; whether the prose helps is not.** A description that misleads
a caller produces a wrong call and a green build. `ToolDescriptionParameterTests` and
`FirstContactTranscriptTests` (#535, closing #396) catch a parameter or reply field named in a spelling the
wire does not use, and a worked transcript that has drifted from the tools it shows.
`ToolDescriptionEnumTests` (#529) catches a vocabulary missing a member. #549 made all of them fail when
they meet source they cannot parse. None of them can say whether a description *helps*, and the author is
the one person who cannot judge that — the empty-reply defect above was caught by *being* the caller, not
by re-reading prose that had just been written. That half is the first-contact exercise with a fresh model,
[#534](https://github.com/hexide-io/HexIDE/issues/534), and it is still open.
