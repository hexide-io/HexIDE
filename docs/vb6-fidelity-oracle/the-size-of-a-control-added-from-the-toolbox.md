## The size of a control added from the toolbox (2026-10-23, #1473, design-time, IDE, release build)

**The question.** "Where the Format commands put a control" measured where VB6 puts a CommandButton added by
double-clicking its tool: 1215 x 495, centred, then on the grid. HexIDE gave every class 50 x 50 pixels (750 x 750
twips). Each class needed its own measurement.

**The probe.** An empty 6000 x 4500 form with the default 120-twip grid, in VB6's IDE. I double-clicked every intrinsic
tool except OLE, whose double-click opens a dialog, clicking an empty corner of the form first each time, then saved.
Every control went onto the form, none into another. The values are VB6's file.

| Class | Width x Height | Left, Top |
|---|---|---|
| CommandButton, Label, TextBox, Frame, CheckBox, OptionButton, PictureBox, Shape, Image | 1215 x 495 | 2400, 2040 |
| ComboBox | 1215 x 315 | 2400, 2040 |
| ListBox | 1215 x 450 | 2400, 2040 |
| HScrollBar | 1215 x 255 | 2400, 2160 |
| VScrollBar | 255 x 1335 | 2880, 1560 |
| DriveListBox | 1215 x 315 | 2400, 2040 |
| DirListBox | 1215 x 315 | 2400, 2040 |
| FileListBox | 1215 x 480 | 2400, 2040 |
| Data | 1215 x 300 | 2400, 2160 |
| Timer | none written | 2400, 2040 |
| Line | X1 2400, Y1 2040, X2 3600, Y2 2520 | |

**What it shows:**
- **Most classes take 1215 x 495.** The ComboBox, the list boxes, the scroll bars and the Data control each have a size
  of their own.
- **Each is centred by its own size, halves truncated, then put on the grid, a half going up.** For example, the
  VScrollBar: 3000 - 127 = 2873, on the grid 2880; and 2250 - 667 = 1583, on the grid 1560. The Data control is
  2250 - 150 = 2100, 17.5 units, which goes up to 2160.
- **A Timer writes no size, but is placed as a 1215 x 495 control would be.**
- **A Line is a diagonal** from the centred, gridded corner, 1200 across and 480 down.
- **A click with a tool selected and no drag adds nothing.** The CommandButton tool was pressed, a click on the form
  made no control, and the toolbox went back to the pointer.

**In HexIDE (#1473):**
- A control added by a double-click takes its class's size and is placed by it, for every class the toolbox holds.
- A click without a drag already added nothing.
- The DriveListBox, DirListBox, FileListBox, Data, Image and Line rows wait for those controls to reach the toolbox;
  the Image's is already the default.

**Not measured:**
- A form with another font, which may change the ComboBox's and the list boxes' heights.
- A grid other than 120 twips.
- A double-click while a container is selected, measured only as "after a click inside a Frame" ("Where VB6's designer
  puts a control's block").

**On SP6** (2026-10-24, VB6.EXE 6.00.9782, #1663). The same 19 double-clicks wrote the same file except for one line: the
Data control's `Connect` is `"Access 2000;"`, where the release build wrote `"Access"`. The Properties window shows
`Access 2000;` too. Every size and position agrees. HexIDE has no Data control yet. When it gains one, it takes SP6's
value, since SP6 is the build nearly every VB6 developer used.

