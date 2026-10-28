# `open_file` couldn't put the caret in a carried file — **CLOSED** (#2403, 2026-10-27)

**Symptom (2026-10-27, Windows, #1334's live check).** `open_file {"name":"twice.c","line":3,"column":27}`, on a
project carrying `twice.c`, answered `{"success":false,"error":"'twice.c' is a carried file, which opens in the
plain-text editor, where this does not put the caret; nothing was opened"}`. The refusal is deliberate: #700 placed the
caret in the code window only.

**Workaround, until the fix.** `open_file` with no `line`, then walk the caret there with `press_key`: Ctrl+Home, Down per line, End or
Right. That is a call per keystroke, and a wrapped line or a refused key misplaces it.

**Suggested fix.** Place the caret in the carried-file editor, an AvaloniaEdit `TextEditor` like the code window's, with
`CaretPlacement.Place` and the same rules, and read it back in the note. If a carried file must be treated differently,
open it and say so, rather than refuse and open nothing. [#2403](https://github.com/hexide-io/HexIDE/issues/2403).

**Fixed (#2403).** `open_file` places the caret in a carried file as in a code window. It checks the place against the
text `get_file_content` returns, the editor's buffer or else the file on disk, and refuses one off the text before
anything opens. It then opens the file, places the caret with `CaretPlacement.Place`, which now takes any
`ISearchableDocument`, and reads it back in the note. The carried-file view gained what #700's review gave the code
window's: a fold the caret lands at the end of is opened, which AvaloniaEdit does not do for itself, and the editor
scrolls to it. It also takes a caret placed before it was laid out, as open_file places one in a file it has just
opened; the editor's own caret, at 0 when its document arrived, used to overwrite it. Guarded by
`CaretPlacementTests` and `OpenFileCaretTests`.

**Live (2026-10-27, Windows at 150%, a Debug build of the fix, headless `claude -p` through the hexide tools).** A
project carried `notes.md`, 80 lines, `line NN of the notes` from line 2.

- `open_file {"name":"notes.md","line":99}`: `Nothing was opened: in notes.md, line 99 is not one of its lines,
  numbered 1 to 81`, and `get_document_tabs` then listed no tab.
- `open_file {"name":"notes.md","line":70,"column":6}`: `… The caret is at line 70, column 6: offset 1510 in the text
  get_file_content returns.` The snapshot, read by eye, shows lines 59 to 81 with the caret just before `70` on line 70:
  the editor scrolled there.
- `type_text` `XY` in keyboard mode on the editor's TextArea, then `get_file_content`: line 70 read `line XY70 of the
  notes`.
- `open_file` at line 2, column 1 on the file already open: `The caret is at line 2, column 1: offset 9 …`.
