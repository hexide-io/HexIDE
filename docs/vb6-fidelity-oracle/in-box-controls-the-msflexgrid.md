## In-box controls: the MSFlexGrid (2026-10-27, #1099)

**The probes.**
- **The software:** VB6 6.00.8176 SP6, with MSFLXGRD.OCX 6.01.9782.
  - The form line is `Object = "{5E9E78A0-531B-11CF-91F6-C2863C385E30}#1.0#0"; "MSFLXGRD.OCX"`.
  - The block is `Begin MSFlexGridLib.MSFlexGrid`.
- **The form:** written by hand, holding MSFlexGrid1 5000 × 3000 with only its position, extents and `_Version`, so
  every property starts at its default.
- **The programs:** each ran in a Sub Main and logged each value with its `TypeName`, or the error.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.

### The object model

- **Its members,** from the type library:
  - **The shape:** Rows, Cols, FixedRows, FixedCols.
  - **The cursor and the selection:** Row, Col, RowSel, ColSel, TopRow, LeftCol.
  - **Text:** Text, TextMatrix(Row, Col), TextArray(index), Clip, FormatString.
  - **Per row and column:** ColWidth, RowHeight, ColAlignment, FixedAlignment, MergeRow, MergeCol, RowData, ColData,
    RowIsVisible, ColIsVisible, RowPos, ColPos. RowPosition and ColPosition are write-only.
  - **The Cell properties,** over the selection: CellBackColor, CellForeColor, CellAlignment, CellTextStyle,
    CellPicture, CellPictureAlignment, CellFontName, CellFontSize, CellFontBold, CellFontItalic, CellFontUnderline,
    CellFontStrikeThrough, CellFontWidth, CellLeft, CellTop, CellWidth, CellHeight.
  - **Appearance:**
    - BackColor, ForeColor, BackColorFixed, ForeColorFixed, BackColorSel, ForeColorSel, BackColorBkg, GridColor,
      GridColorFixed;
    - GridLines, GridLinesFixed, GridLineWidth, TextStyle, TextStyleFixed;
    - FocusRect, HighLight, FillStyle, SelectionMode, MergeCells, AllowBigSelection, AllowUserResizing;
    - ScrollBars, ScrollTrack, WordWrap, RowHeightMin, Redraw;
    - BorderStyle, Appearance, Font, FontWidth, Picture, PictureType, RightToLeft;
    - MousePointer, MouseIcon, MouseRow, MouseCol, OLEDropMode.
  - **Write-only:** Sort.
  - **Methods:** AddItem, RemoveItem, Clear, Refresh, OLEDrag.
- **Its events:** Click, DblClick, KeyDown, KeyPress, KeyUp, MouseDown, MouseMove, MouseUp, SelChange, RowColChange,
  EnterCell, LeaveCell, Scroll, Compare(Row1, Row2, Cmp), and the OLE drag events.
- **`TypeName`** is `MSFlexGrid`.
- **Text is the default member.** `v = MSFlexGrid1` read the current cell's text, and `MSFlexGrid1 = "via default"` set
  it.
- **Version** reads 600.
- **The extender:** Index is 343 when the grid isn't in an array.
- **Errors carry the control's own numbers and messages:**
  - **30009** "Invalid Row Value";
  - **30010** "Invalid Column Value";
  - **30013** "Invalid Row Height Value";
  - **30014** "Invalid Column Width Value";
  - **30005** "Invalid Alignment value";
  - **30016** "FixedRows must be at least one less than Rows value", and **30017** for FixedCols;
  - **30000** "Cannot do a RemoveItem on a fixed row";
  - **30001** "Cannot use AddItem on a fixed row";
  - **30002** "Grid does not contain that row";
  - **30015** "Can not remove last non-fixed row";
  - **381** "Subscript out of range" for TextMatrix and TextArray.
  
  Read through `CallByName`, the numbers are the same but the message is "Application-defined or object-defined error".
  ColAlignment, FixedAlignment and MergeRow(index) past the end read 30004, 30004 and 30009.

### Defaults

| Read | Logged |
|---|---|
| Rows, Cols | 2, 2 |
| FixedRows, FixedCols | 1, 1 |
| Row, Col, RowSel, ColSel, TopRow, LeftCol | 1 |
| Text, FormatString, Clip | empty |
| ColWidth(n) | **960** |
| RowHeight(n) | **240** |
| ColAlignment(n), FixedAlignment(n) | **9**, flexAlignGeneral |
| BackColor, ForeColor | `&H80000005`, `&H80000008` |
| BackColorFixed, ForeColorFixed | `&H8000000F`, `&H80000012` |
| BackColorSel, ForeColorSel | `&H8000000D`, `&H8000000E` |
| BackColorBkg | `&H808080` |
| GridColor, GridColorFixed | `&HC0C0C0`, `&H0` |
| GridLines, GridLinesFixed | 1 (flat), 2 (inset) |
| GridLineWidth | 1 |
| FocusRect, HighLight | 1 (light), 1 (always) |
| FillStyle, SelectionMode, MergeCells | 0 |
| AllowBigSelection | True |
| AllowUserResizing | 0 |
| ScrollBars | 3, both |
| ScrollTrack, WordWrap, RightToLeft | False |
| Redraw, Enabled | True |
| TextStyle, TextStyleFixed | 0 |
| BorderStyle, Appearance | 1, 1 |
| RowHeightMin | 0 |
| CellBackColor, CellForeColor | **0** |
| CellFontName, CellFontSize | MS Sans Serif, 8.25 |
| Font | MS Sans Serif 8.25 |
| CellLeft, CellTop, CellWidth, CellHeight at 1,1 | 1005, 285, 945, 225 |
| MergeRow(n), MergeCol(n) | False |
| RowData(n), ColData(n) | 0 |

**Colours read late-bound come back as unsigned Longs,** VarType 19, as the ListView's do. **CellBackColor reads 0 for a
cell never given one,** not the grid's BackColor.

### The shape, the cursor and the selection

| Assignment, on a 5 × 4 grid with one fixed row and column | Logged |
|---|---|
| `Rows = 0` | **taken.** FixedRows becomes 0, and Row and RowSel -1 |
| `Rows = -1` | **30009** |
| `Rows = 100000` | taken |
| `Cols = 0` | **taken.** FixedCols becomes 0, and Col and ColSel -1 |
| `Cols = -1` | **30010** |
| `FixedRows = Rows`, `FixedCols = Cols` | **30016**, **30017** |
| `FixedRows = -1` | 30009 |
| `FixedRows = n` | **Row, RowSel and TopRow move to n,** the first row below the fixed ones |
| `Row = 0`, a fixed row | **taken** |
| `Row = Rows`, `Row = -1` | 30009 |
| `Col = 0`, a fixed column | taken |
| `Col = Cols` | 30010 |
| `Row = n` | **RowSel becomes n too.** `Col = n` sets ColSel |
| `RowSel = 3`, `ColSel = 3` | taken, and Row and Col unchanged |
| `RowSel = 0` | taken: a selection running up into the fixed row |
| `RowSel = 9` | 30009 |
| `Rows = 3` with Row 4 | **Row and RowSel become 2, the new last row.** No event |
| `Cols = 2` with Col 3 | Col becomes 1 |
| `TopRow = 49` on 50 rows | **TopRow becomes 40,** the last row that leaves the window full. Scroll fires |
| `TopRow = 0`, a fixed row | 30009 |
| `LeftCol = 19` on 20 columns | LeftCol becomes 17 |
| `Row = 40` after TopRow 40 | taken. RowIsVisible(40) True |

**Events raised by code:**
- `Row = n` and `Col = n` raise **LeaveCell** (the old Row and Col), then **EnterCell** (the new ones), then
  **RowColChange**. Setting the cell already current raised nothing.
- **SelChange was not raised by code.** Setting Row, Col, RowSel or ColSel from code never raised it.
- Setting TopRow or LeftCol raised **Scroll**.

### Widths, heights and alignment

| Assignment | Logged |
|---|---|
| `ColWidth(1) = 500` | 495, rounded to whole pixels |
| `ColWidth(1) = 0` | 0 |
| `ColWidth(1) = -1` | **960, the default again** |
| `ColWidth(1) = -2` | **30014** |
| `ColWidth(1) = 100000` | 100005 |
| `ColWidth(-1) = 777` | **every column 780** |
| `ColWidth(Cols) =` | 30010 |
| `RowHeight(1) = 500, 0, -1, -2` | 495, 0, 240 (the default), **30013** |
| `RowHeightMin = 400` | every row reads 405 |
| `RowHeightMin = -1` | 30013 |
| `ColAlignment(1) = 0` to 9 | taken. 10 and -1 are **30005** |
| `CellAlignment = 10` | 30005 |
| `GridLineWidth = 1, 10, 11` | taken. **0 and -1 are 380** |

**The enumerated properties ignore a value out of range, raising nothing:**
- FillStyle, SelectionMode, MergeCells, HighLight, FocusRect, GridLines, TextStyle, AllowUserResizing, ScrollBars,
  BorderStyle, Appearance and PictureType were each set to -1 and 5. Each kept its value, with no error.
- 2 was taken by those whose range reaches it, and ignored by FillStyle, BorderStyle, Appearance and PictureType.

### Text

- **TextArray(index) is row-major:** index = Row × Cols + Col. On 4 × 3, TextArray(7) is cell 2,1.
- **TextMatrix and TextArray out of range are 381,** read or written.
- **Text sets the current cell when FillStyle is 0, and every cell of the selection when it is 1.**
  - With the selection 1,1 to 2,2 and FillStyle 0, `Text = "one"` changed 1,1 only.
  - With FillStyle 1, it changed all four.
  - **A selection running backwards counts the same:** Row 3, Col 2 with RowSel 1, ColSel 0 covered rows 1 to 3 and
    columns 0 to 2, the fixed column too.
- **Clip reads the selection** as tab-separated cells and CR-separated rows: `B0<TAB>B1<CR>C0<TAB>C1`. A backwards
  selection reads top to bottom, left to right.
- **Setting Clip fills the selection from its top left, cell by cell:**
  - Cells beyond the selection are dropped.
  - A cell the clip has no value for is left as it was. `1<TAB>2<TAB>3<CR>4<CR>5<CR>6` into a 2 × 2 selection set
    `1`, `2`, `4`, and kept the fourth.

### AddItem, RemoveItem and Clear

- **`AddItem "x<TAB>y<TAB>z<TAB>extra"`** appends a row with one tab-separated field per column. A field past the last
  column is dropped.
- **`AddItem "only"`** fills the first column and leaves the rest empty.
- **`AddItem item, n`** inserts the row at n:
  - 0, a fixed row, is **30001**;
  - n past Rows is **30002**;
  - n equal to Rows appends.
- **A CR in an item stays in the one cell.** `"a<CR>b"` made one row, with `a`, CR, `b` in its first cell.
- **RemoveItem n:**
  - 0 is **30000**;
  - 99 is **30002**;
  - removing the last non-fixed row is **30015**.
  - Removing row 1 with Row 2 current left Row at 2, now the next row down.
- **Clear empties every cell's text and formatting,** the fixed cells too: CellBackColor back to 0 and CellFontBold
  False. It keeps Rows, Cols, ColWidth and ColAlignment.
- **Rows reduced and then increased again** brings back empty rows. The text in the removed rows is gone.

### FormatString

**FormatString sets the fixed row's and the fixed column's text, alignment and widths.**
- Its columns are separated by `|`. An optional `;` starts a section of row headings, also `|`-separated.
- The widths it gives, in twips, for MS Sans Serif 8.25:

| FormatString | Fixed text | ColWidth | ColAlignment, FixedAlignment |
|---|---|---|---|
| `<Name\|^Age\|>Amount` | `Name`, `Age`, `Amount` | 510, 375, 630 | **1, 4, 7** |
| `Plain\|Two` | `Plain`, `Two` | 435, 405 | 9, 9 |
| `\|Col1\|Col2;\|Row1\|Row2` | row 0: ``, `Col1`, `Col2`; column 0: ``, `Row1`, `Row2` | **510**, 405, 405 | 9 |
| `;Only rows\|R2\|R3` | column 0: `Only rows`, `R2`, `R3`, from row 0 | 780, and 960 untouched | 9 |
| `A\|\|C` | `A`, ``, `C` | 195, **90**, 195 | 9 |
| `Name     \|Wide` | `Name`, `Wide` | **735**, 465 | 9 |
| `<a\|<b;>x\|>y` | `>x`, `b` in row 0; `>y` in row 1 | 255, 180 | 1, 1 |

- **Each width is the segment's TextWidth plus 90, rounded to pixels.**
  - `Name` measures 420 and gets 510.
  - **Trailing spaces count toward the width but are trimmed from the text:** `Name     ` measures 645, gets 735, and
    shows `Name`.
  - An empty segment gets 90.
  - **Column 0 takes the wider of its column segment and its row headings:** `Row1`'s 420 + 90 = 510.
- **`<`, `^` and `>` set left, centre and right,** 1, 4 and 7, both ColAlignment and FixedAlignment. Without one, the
  column stays 9.
- **The row section is taken literally.** `>x` stayed `>x`, and its first entry overwrote row 0's column-0 text, `a`.
- **Cols grows to fit the column segments, but never shrinks.** `X|Y` on a 5 × 4 grid set columns 0 and 1 and left 2
  and 3 as they were. Five segments on two columns made Cols 5.
- **With no fixed rows or columns, FormatString sets no text,** and raises nothing.
- **FormatString reads back as last assigned.** Changing a fixed cell's text afterwards doesn't change it.

### The Cell properties

- **With FillStyle 0, a Cell property sets the current cell only,** whatever the selection.
- **With FillStyle 1, it sets every cell of the selection.**
- A fixed cell takes them too: cell 0,0 took CellBackColor blue.
- **Read, a Cell property reports the current cell,** Row and Col, whatever FillStyle and the selection.
- **CellFontSize 12 doesn't grow the row.** RowHeight(3) stayed 240.

### Sort

**Sort sorts the rows from Row to RowSel, keyed on the columns from Col to ColSel, left to right.**
- The whole row moves, the fixed column's text too.
- The fixed rows don't move.
- The keys were the non-fixed column 1, holding `10`, `9`, `b`, `B` and `a` in rows r1 to r5:

| Sort | Order of column 1 |
|---|---|
| 1, generic ascending | `9`, `10`, `a`, `B`, `b`: **numbers first, by value**, then text ignoring case |
| 2, generic descending | `b`, `B`, `a`, `10`, `9` |
| 3, numeric ascending | `b`, `a`, `B`, `9`, `10`: text counts as 0 |
| 4, numeric descending | `10`, `9`, `B`, `a`, `b` |
| 5, string ignoring case, ascending | `10`, `9`, `a`, `B`, `b` |
| 6, string ignoring case, descending | `b`, `B`, `a`, `9`, `10` |
| 7, string, ascending | `10`, `9`, `a`, **`b`, `B`**: the locale's order, a lower-case letter before its capital |
| 8, string, descending | `B`, `b`, `a`, `9`, `10` |
| 9, custom | by the **Compare** event's Cmp. By length, it raised Compare 4 times for 5 rows |
| 10 | **380** |

- **Rows whose keys tie aren't kept in their order.** Each run started from the previous one's order, and tied keys
  came out swapped as often as not: in run 1, `B` (r4) came before `b` (r3).
- **Col 2 with ColSel 1** keyed on columns 1 then 2, left to right, as Col 1 with ColSel 2 would.

### In HexIDE

HexIDE doesn't model the MSFlexGrid (#1099). The designer draws it as an empty box (#157), and running a form that holds one is refused, as for any control HexIDE can't create (#857).
