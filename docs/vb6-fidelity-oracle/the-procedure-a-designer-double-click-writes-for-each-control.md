## The procedure a designer double-click writes for each control (2026-11-17, #4411, design-time, IDE, SP6)

**The question.** HexIDE's designer writes the first event in a control's list when a control is double-clicked, so a
TextBox gets `Text1_KeyDown` (#4411). VB6 is said to write each control's default event, `Text1_Change` for a
TextBox, but only a double-click on a designer's background had been measured (see "A UserControl's and a
PropertyPage's own events"). Which procedure does VB6 write for each in-box control?

**The probe.** SP6's IDE, in the guest's console session through the agent. `W4411.vbp` is a Standard EXE whose
`Form1` holds one of each of VB6's twenty in-box controls on a grid, each read from its file at a known twip position.
The steps, for each control and for an empty point of the form:
1. The form's designer was opened from the Project window and maximized.
2. The real pointer double-clicked the middle of the control: for the Line, the middle of a horizontal line with
   `BorderWidth` 3; for the Frame, inside its border below the caption.
3. The code window's Object and Procedure boxes were read with `CB_GETLBTEXT`, with the Properties window's object
   box read beside them to confirm which control was selected.
4. The code window was closed, and Shift+F7 brought the designer back before the next double-click.

At the end, Ctrl+S saved the form, and the saved file was read.

**A trap in the probe, recorded so the next one avoids it.** On the first pass, Ctrl+F4 sometimes left the code
window open. The next double-click then landed in the code window, selected a word there, and the boxes still named the
previous procedure: Picture1 read `Form` / `Load`, and HScroll1 `Command1` / `Click`. Four rows read stale like that. Each
was taken again with the designer confirmed in front and the Properties window naming the control, which is what the
table records.

**The double-click.**

| Control | Object box | Procedure box | The saved file gained |
|---|---|---|---|
| (the form's background) | `Form` | `Load` | `Private Sub Form_Load()` |
| PictureBox | `Picture1` | `Click` | `Private Sub Picture1_Click()` |
| Label | `Label1` | `Click` | `Private Sub Label1_Click()` |
| TextBox | `Text1` | `Change` | `Private Sub Text1_Change()` |
| Frame | `Frame1` | `DragDrop` | `Private Sub Frame1_DragDrop(Source As Control, X As Single, Y As Single)` |
| CommandButton | `Command1` | `Click` | `Private Sub Command1_Click()` |
| CheckBox | `Check1` | `Click` | `Private Sub Check1_Click()` |
| OptionButton | `Option1` | `Click` | `Private Sub Option1_Click()` |
| ComboBox | `Combo1` | `Change` | `Private Sub Combo1_Change()` |
| ListBox | `List1` | `Click` | `Private Sub List1_Click()` |
| HScrollBar | `HScroll1` | `Change` | `Private Sub HScroll1_Change()` |
| VScrollBar | `VScroll1` | `Change` | `Private Sub VScroll1_Change()` |
| Timer | `Timer1` | `Timer` | `Private Sub Timer1_Timer()` |
| DriveListBox | `Drive1` | `Change` | `Private Sub Drive1_Change()` |
| DirListBox | `Dir1` | `Change` | `Private Sub Dir1_Change()` |
| FileListBox | `File1` | `Click` | `Private Sub File1_Click()` |
| Shape | `(General)` | `(Declarations)` | nothing |
| Line | `(General)` | `(Declarations)` | nothing |
| Image | `Image1` | `Click` | `Private Sub Image1_Click()` |
| Data | `Data1` | `Validate` | `Private Sub Data1_Validate(Action As Integer, Save As Integer)` |
| OLE (empty) | `OLE1` | `Updated` | `Private Sub OLE1_Updated(Code As Integer)` |

Each stub VB6 wrote is the declaration, a blank line and `End Sub`.

**What the rows say:**
- **A double-click writes the control's default event, not the first in its list,** as the issue reasoned for the
  TextBox. The default isn't always Click: a TextBox, ComboBox, scroll bar, DriveListBox and DirListBox get Change,
  and a Timer gets Timer.
- **A Frame gets `DragDrop`.** The obvious guess is Click, which a Frame also raises; it isn't Click.
- **A Shape and a Line have no events,** so VB6 opens the code window at `(General)` `(Declarations)` and writes
  nothing.
- **An empty OLE container opens its code, not the Insert Object dialog,** and gets `Updated`.
- **The Data control gets `Validate`,** with VB6's two `Integer` arguments.

**Seen in passing, one probe only: VB6 files a new event procedure in alphabetical order.** The double-clicks went in
the order TextBox, form, Label, Frame, CommandButton, OptionButton, ComboBox, ListBox, VScrollBar, Timer, DriveListBox,
DirListBox, Image, Data, OLE, CheckBox, FileListBox, PictureBox, HScrollBar. The saved file holds the nineteen stubs
ordered by procedure name instead: `Check1_Click`, `Combo1_Change`, `Command1_Click`, `Data1_Validate`, `Dir1_Change`,
`Drive1_Change`, `File1_Click`, `Form_Load`, `Frame1_DragDrop`, `HScroll1_Change`, `Image1_Click`, `Label1_Click`,
`List1_Click`, `OLE1_Updated`, `Option1_Click`, `Picture1_Click`, `Text1_Change`, `Timer1_Timer`, `VScroll1_Change`.
One probe can't say whether the order compares with case or without, or where a stub goes beside code a person wrote.

**Not measured:** the release build; an MDIForm's background; a control array element (`Index` set); a UserControl
placed on a form; the ActiveX controls (ListView, TreeView and the rest); a second double-click on a control whose
default procedure already exists.
