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
