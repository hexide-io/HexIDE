## A bookmark through edits above it, an Undo, and its own line's deletion (2026-11-23, #5334, IDE, SP6)

**The question.** HexIDE stores a bookmark as a bare line number, and nothing moves it when lines are added or removed
above it (#5334). [A breakpoint through edits above it](a-breakpoint-through-edits-above-it.md) measured what VB6
does with a breakpoint, and left bookmarks unmeasured. Does a VB6 bookmark do the same?

**The probe:**
- **The IDE:** VB6.EXE 6.00.9782 (SP6), in the console session of a clone of the oracle guest, driven through the
  console agent as before.
- **The program and the edits** are the breakpoint page's, row for row: a Standard EXE whose `Sub Main` calls
  `L "one"` to `L "four"`, with the bookmark set on `L "two"`, line 6.
- **Each row was a fresh IDE:** open the project, open the module's code window, put the caret on `L "two"`, and set
  the bookmark with Edit > Bookmarks > Toggle Bookmark. Then make the row's edit in design mode.
- **Reading it back:** Ctrl+Home, then Edit > Bookmarks > Next Bookmark. The caret's line is where the next
  bookmark is. The toolbar's `Ln` and a screenshot, which shows the bookmark's mark in the margin, confirm it.
- **The keys** went through `SendKeys`. The menu was reached with Alt+E, Up and Right. Alt+E then B opens Table,
  not Bookmarks: B is Table's access key in that menu.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.

| Edit, after Toggle Bookmark on `L "two"` (line 6) | Next Bookmark from line 1 | The breakpoint, in the same row |
|---|---|---|
| **Control:** none | line 6, `L "two"` | kept |
| A blank line inserted above it: End on `L "one"`, then Enter | line 7, `L "two"` | kept |
| The line above deleted: from the end of `Sub Main()`, Shift+Down and Shift+End, then Delete | line 5, `L "two"` | kept |
| A copy of `L "one"` pasted below `L "one"` | line 7, `L "two"` | kept |
| A blank line inserted above it as before, then Ctrl+Z | line 6, `L "two"` | kept |
| Enter at its own line's first character, which moves the statement down a line | line 7, `L "two"` | kept |
| Its own line deleted: Home, Shift+Down, Delete | **none.** Next Bookmark was greyed out, and the caret stayed on line 1 | removed |
| Home on `L "one"`, Shift+Down, Delete, which joins `L "two"` onto `L "one"`'s line | **line 5, `L "two"`**, the joined line | removed |

The screenshots agree with every row. The mark sits on `L "two"` in the first six rows and in the last. After the
seventh, there is no mark on any line of `Sub Main`.

**Readings:**
- **A VB6 bookmark follows its statement as a breakpoint does,** through an insert, a paste or a deletion above it,
  Enter at its start, and Undo. Deleting its statement's line removes it.
- **The join is where the two differ.** The same edit removed the breakpoint but kept the bookmark, which moved with
  `L "two"` onto the joined line.
- **One reading fits all eight rows:** the bookmark goes only when the edit deletes its statement. Only one join was
  measured, though, so I would not defend that as a rule for other joins.

### In HexIDE

`BookmarkService` keeps each document's bookmarks as a set of line numbers (`IDE/HexIDE/Bookmarks/BookmarkService.cs:10`),
and #5334 records that nothing moves them when text changes. That wasn't checked again for this page.

### Not measured

- a join that cuts into the bookmarked statement, or one made from the bookmarked line's end;
- a bookmark on a blank line or a comment, several bookmarks, and bookmarks in two modules;
- an edit made in break mode, and whether bookmarks survive a save or reopening the project.
