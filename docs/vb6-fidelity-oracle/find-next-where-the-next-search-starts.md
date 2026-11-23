## Find Next: where the next search starts (2026-11-21, #704, design-time, IDE, SP6)

**The question.** After Find selects a match, where does Find Next start looking? HexIDE starts one character past
the caret, and a forward find leaves the caret at the end of the match. So HexIDE never finds a match that starts right
after the one selected: `ab` in `abab` goes back to offset 0 (#704). Does VB6 find that match? And does it find an
overlapping one, `aa` in `aaa` at offset 1?

**The obvious guess was that it starts one character past the start of the match, as some editors do. It starts at
the end of the match.** So it finds a match that starts right after the selected one, but never one that overlaps it.

### The probe

VB6's IDE, SP6 (VB6.EXE 6.00.9782), in the console session of a clone of the oracle guest, driven through the agent
with SendKeys. Each case started a fresh vb6.exe on a Standard EXE project whose only module was this, with `TEXT`
replaced by the case's text:

```vb
Attribute VB_Name = "Module1"
Option Explicit

Sub Main()
    Dim s As String
    s = "TEXT"
End Sub
```

1. The caret started at column 1 of the `Dim` line.
2. Ctrl+F opened Find with its defaults, as a screenshot shows them: Search *Current Module*, Direction *All*, and no
   box ticked.
3. The search text was typed into Find What, and Enter pressed, which is Find Next.
4. Then came either Enter again, with the dialog still open, or Esc and F3.

**How the result was read.** After the keys under test, `Q` was typed. It replaces the selection, so the line shows
where the selection was. Home, Shift+End and Ctrl+C then copied the line.

| Text | Find What | Keys after the first Find Next | The line read back | The second search |
|---|---|---|---|---|
| `aaa` | `aa` | none, the control | `s = "Qa"` | none; the first match is at offset 0 |
| `abab` | `ab` | Enter | `s = "abQ"` | found offset 2 |
| `abab` | `ab` | Esc, F3 | `s = "abQ"` | found offset 2 |
| `aaaa` | `aa` | Esc, F3 | `s = "aaQ"` | found offset 2, not 1 |
| `aaa` | `aa` | Enter | a message box over Find | found nothing |
| `aaa` | `aa` | Esc, F3 | a message box | found nothing |
| `aaa` | `aa` | Esc, F3, OK on the box | `s = "Qa"` | found nothing; the first match stayed selected |
| `aaa` | `aa` | Esc, F3, OK, F3 | `s = "Qa"` | no second box, and the same selection |

The message box is titled "Microsoft Visual Basic" and says "The specified region has been searched."

### Readings

- **Find Next starts at the end of the selection.** A match starting right after it is found (`abab`). One
  overlapping it is not: `aaaa` gives offset 2, not 1, and `aaa` gives no second match.
- **Enter in the open dialog and F3 after closing it agree.**
- **With Direction *All*, the search wraps round the module and stops where it began.** In `aaa` the search began on
  the `Dim` line, above the match. So it wrapped back to there without reaching offset 0 again, and said the region had
  been searched, instead of selecting the same match a second time.

**Not measured:**
- Direction *Down* or *Up*;
- *Use Pattern Matching*, *Find Whole Word Only* and *Match Case*;
- Replace and Replace All.

**In HexIDE.** With a selection, `FindReplaceViewModel.FindNext` starts a Down search at `startOffset + 1`
(`IDE/HexIDE/Forms/ViewModels/FindReplaceViewModel.cs:541`), and `FindNextRegex` does the same (`:615`). That is read
from main `3f3ad5742`, not run. #704 tracks the fix.

### Direction Down, and Use Pattern Matching (2026-11-21, #704, SP6)

Two of the *Not measured* items above, measured the same way on the oracle guest with VB6.EXE 6.00.9782 (SP6). Direction
was set with Alt+D and the key D, and pattern matching with Alt+U, using the access keys "Find and Replace: their access
keys" records. Alt+F then went back to Find What before Enter.

| Text | Find What | Options | Keys after the first Find Next | The line read back | The second search |
|---|---|---|---|---|---|
| `abab` | `ab` | Direction Down | Enter | `s = "abQ"` | found offset 2 |
| `aaaa` | `aa` | Direction Down | Esc, F3 | `s = "aaQ"` | found offset 2, not 1 |
| `aaa` | `aa` | Direction Down | Esc, F3 | a message box, then a second one | found nothing |
| `aaa` | `a` | Use Pattern Matching | Esc, F3 | `s = "aQa"` | found offset 1 |
| `aaaa` | `a?` | Use Pattern Matching | Esc, F3 | `s = "aaQ"` | found offset 2, not 1 |

- **With Direction Down,** F3 past the last match first asks "End of search scope has been reached; do you want to
  continue from the beginning?", with Yes, No and Help, and Yes the default. Enter on Yes then gave "The specified
  region has been searched." A screenshot shows the first box, with the match at offset 0 still selected.
- **The pattern rows started with the caret on the `s =` line.** Two first attempts started on the `Dim` line, as the
  rows above do. With Match Case off, `a` matched the `A` of `As` there, and `a?` matched `As`. So those attempts read
  the wrong match, and they're not counted.

**Readings:** Direction Down agrees with All, and pattern matching agrees with plain text. The next search starts at
the end of the selection: a one-character match finds its neighbour (`aaa`, offset 1), and a two-character match never
overlaps the last (`aaaa`, offset 2).

**Still not measured:** Direction Up, Find Whole Word Only, Match Case, and Replace.

### Direction Up (2026-11-22, #704, SP6)

One more of the items above, measured the same way with VB6.EXE 6.00.9782 (SP6), in a clone of the oracle guest. The
Search scope was the dialog's default, Current Module. Each case started with the caret at the end of the line
`s = "..."`, Ln 5, Col 14 for `aaa`. Direction was set with Alt+D and the key U.

| Text | Find What | Keys after the first Find Next | The line read back | Found |
|---|---|---|---|---|
| `abab` | `ab` | Esc | `s = "abQ"` | offset 2 |
| `abab` | `ab` | Enter, then Esc | `s = "Qab"` | offset 2, then 0 |
| `aaaa` | `aa` | Esc | `s = "aaQ"` | offset 2 |
| `aaaa` | `aa` | Esc, F3 | `s = "Qaa"` | offset 2, then **0, not 1** |
| `aaa` | `aa` | Esc | `s = "aQ"` | offset 1 |
| `aaa` | `aa` | Esc, F3 | a message box | offset 1, then none |

- **With Direction Up, F3 past the first match** asked "Beginning of search scope has been reached; do you want to
  continue from the end?", with Yes, No and Help.
- **Enter on that box** gave "The specified region has been searched.", with OK and Help, as Direction Down does.

**Readings:**
- **Up searches back from the caret,** and the first match is the last one before it.
- **The next Up search starts at the selection's start,** and takes only a match that ends there or before it. So
  matches never overlap, as with Down, where the next search starts at the selection's end.

**Still not measured:** Find Whole Word Only, Match Case, Replace, and the Yes of either prompt when another match lies
before the starting point.

### The wrap prompt's Yes and No (2026-11-22, #5307, SP6)

Measured the same way with VB6.EXE 6.00.9782 (SP6), in a clone of the oracle guest: scope Current Module, Direction
Down, Find What `ab`. The line was `s = "abab"`, and each case started with the caret before the second `ab`, at
string offset 2, by End and three Lefts.

| Keys after the first Find Next | The line read back, or the message | Found |
|---|---|---|
| Esc | `s = "abQ"` | offset 2, the match at the caret |
| Esc, F3 | "End of search scope has been reached; do you want to continue from the beginning?" | |
| Esc, F3, Yes | `s = "Qab"` | **offset 0**, before where the search began |
| Esc, F3, Yes, F3 | "The specified region has been searched." | **not offset 2 again** |
| Esc, F3, No | `s = "abQ"` | the selection unchanged, and no message |

**Readings:**
- **Yes wraps to the scope's start and finds a match before the starting point.**
- **The next F3 stops where the search began,** with "The specified region has been searched.", rather than finding
  the first match again.
- **No leaves the selection as it was.**

The caret's starting point and the first match were both at offset 2, so which of the two the search stops at isn't
told apart.

**Two other scopes,** with Direction Down and a screenshot of the dialog confirming each:
- **Current Procedure,** `aaa` and `aa`, from the line above: F3 past the first match gave the same "End of search
  scope has been reached; do you want to continue from the beginning?"
- **Current Project,** two modules, each with one `"ab"`, starting in Module2's procedure: Find Next found Module2's,
  F3 switched to Module1 and found its one, and the next F3 gave **"The specified region has been searched."**, with no
  prompt before it. So a project search goes on through the other modules without asking, and stops where it began.

**Still not measured:** Selected Text, and a project search that started with matches before the caret.
