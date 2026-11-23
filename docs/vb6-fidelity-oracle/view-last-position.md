## View > Last Position (2026-11-21, #69, design-time, IDE, SP6)

**The question.** What does VB6's Last Position, on the View menu at Ctrl+Shift+F2, go back through? Which moves make
an entry, and how many entries does it keep? What happens to an entry when lines are inserted above it, and does it
cross code windows? In HexIDE the item is always disabled, and Ctrl+Shift+F2 is taken by Clear All Bookmarks (#69).

**The obvious guess was a history of every jump, like a browser's Back. VB6's holds edited lines, plus the line a
Shift+F2 or a Find left.** It lands on column 1, keeps eight entries and goes round them in a circle.

### The probe

VB6's IDE, SP6 (VB6.EXE 6.00.9782), in the console session of a clone of the oracle guest, driven through the agent
with SendKeys. Most cases used a Standard EXE project whose module was this, so `Foo` is on Ln 4, `Debug.Print 1` on
Ln 9 and `Debug.Print 2` on Ln 13:

```vb
Attribute VB_Name = "Module1"
Option Explicit

Sub Main()
    Foo
    Bar
End Sub

Sub Foo()
    Debug.Print 1
End Sub

Sub Bar()
    Debug.Print 2
End Sub
```

Each case started a fresh vb6.exe. Where the caret went was read three ways:
- the Procedure box after each step;
- at the end, by typing `Q` and copying the line;
- where the table says so, the toolbar's `Ln, Col` in a screenshot.

An edit was ` ' a` typed at the end of a line, and the caret then moved off the line.

| Case | What Ctrl+Shift+F2 did |
|---|---|
| A fresh IDE, before anything | nothing to do: the View menu shows Last Position greyed out |
| After an edit | the View menu shows it enabled, at Ctrl+Shift+F2 |
| Shift+F2 on `Foo`, at Ln 4, Col 5, which goes to `Sub Foo` | back to Ln 4, at column 1 |
| The same, then Ctrl+Shift+F2 again | stayed on Ln 4 |
| Find `Print 2` from Ln 4, with Enter and Esc, which goes to Ln 13 | back to Ln 4, at column 1 |
| Down 3, then Down 8: arrow keys only, no edit | nothing. Ln 12, Col 8, before and after |
| Ctrl+Down twice from Ln 4 (Next Procedure, to Ln 9 and then Ln 13) | nothing. Ln 13, Col 1, before and after |
| Edits on Ln 4, Ln 9 and Ln 13, in that order, then Ctrl+Home, then four presses | by the Procedure box, `Bar`, `Foo`, `Main`, then `Bar` again. The last read back as Ln 13, at column 1 |
| An edit on Ln 13, then Ctrl+Home, and Enter twice at the start of `Sub Main()` | Ln 13. That is now the blank line above `Sub Bar()`. The edited line had moved down to Ln 15 |
| Ten procedures, `P1` to `P10`, one edit in each in order, then Ctrl+Home and twelve presses | `P10`, `P9` and so on down to `P3`, then `P10` again: eight entries |
| An edit in Module1's code window, then one in Module2's | Module2's line, then Module1's, with its window brought to the front, then Module2's again |

**Edit > Bookmarks** lists Toggle Bookmark, Next Bookmark, Previous Bookmark and Clear All Bookmarks, with no shortcut
key against any of them, as a screenshot shows.

### Readings

- **An entry comes from an edit, or from the line that a Shift+F2 or a Find left.** Arrow keys and Next Procedure make
  none.
- **Last Position lands on column 1** of the entry's line, wherever the caret or the edit had been.
- **It keeps eight entries.** It starts from the most recent and steps back, and after the oldest it goes round to the
  most recent again.
- **An entry is a line number, not a line.** It stayed on Ln 13 when two lines were inserted above. The two Enters are
  edits too, but the first press didn't go to them.
- **It crosses code windows**, bringing the entry's window to the front.
- **Ctrl+Shift+F2 belongs to Last Position alone.** Clear All Bookmarks has no key.

**Not measured:**
- an entry whose window is closed, or whose module is removed;
- lines deleted above an entry;
- the Procedure box, Next Bookmark and the Call Stack as moves;
- whether an edit makes its entry when it is made or when the caret leaves the line;
- whether a Shift+F2 entry and edit entries share the eight;
- whether the history survives closing and reopening the project.

**In HexIDE.** #69 tracks Last Position and its key.

### A closed code window, and a run (2026-11-21, #69, SP6)

Two of the *Not measured* items above, measured the same way on the oracle guest with VB6.EXE 6.00.9782 (SP6).

**A closed window.** A project with Module1, the module above, and Module2, whose `Sub Two` holds `Debug.Print 22`:
1. An edit on Ln 4 of Module1, in its own code window.
2. An edit on Ln 4 of Module2, in its window.
3. Ctrl+Tab back to Module1's window, and Ctrl+F4 to close it. Module2's window was then active.

Three presses of Ctrl+Shift+F2, read by the active window's title and its Procedure box:

| Press | Active window | Procedure box |
|---|---|---|
| 1 | Module2 (Code) | Two |
| 2 | **Module1 (Code), reopened** | Main |
| 3 | Module2 (Code) | Two |

**A run.** An edit on Ln 4, then Down 8 left the caret on Ln 12, Col 10. F5 ran the program, which ends by itself, and
the IDE was back in design mode. The presses were read from the toolbar's `Ln, Col` in screenshots:

| | After the run | A control, the same with no run |
|---|---|---|
| Before any press | Ln 12, Col 10 | Ln 12, Col 10 |
| Press 1 | **Ln 12, Col 1** | Ln 4, Col 1 |
| Press 2 | Ln 4, Col 1 | Ln 4, Col 1 |
| Press 3 | Ln 12, Col 1 | Ln 4, Col 1 |

**Readings:**
- **An entry in a closed window reopens it.** Closing the window kept its entry.
- **Starting a run adds the caret's line as an entry, and the edit's entry stays.** With no run, the one entry was the
  edit's, and every press went there.

**Still not measured:** lines deleted above an entry; the Procedure box, Next Bookmark and the Call Stack as moves;
whether the history survives closing and reopening the project.
