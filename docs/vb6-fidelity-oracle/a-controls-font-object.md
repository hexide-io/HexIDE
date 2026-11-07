## A control's Font object (2026-10-27, #934)

**The question.** In HexIDE, a Label's or TextBox's `Font` reads Null, and on every other control it reads the font's
name as a String, so `Me.Font.Size` cannot be read. In vb6.exe, which controls have a Font? What is it, what is its
default member, and does assigning one share the object or copy it?

**The probe.** A form holding one of each intrinsic control, none with a Font block, and a Label whose block says Arial
12, Weight 700, Italic. The program reads each control's `Font` into an Object and logs `TypeName`, its members, and
`FontName` and `FontSize`. Then it makes a series of writes. Built native and p-code, run in the probe runner's session,
and every row agreed.

| Control | `Font` |
|---|---|
| The form, Label, TextBox, CommandButton, CheckBox, OptionButton, ComboBox, ListBox, Frame, PictureBox, DriveListBox, DirListBox, FileListBox, Data | `TypeName` **"Font"**: Name "MS Sans Serif", Size the **Currency** 8.25, Bold and Italic False, Weight 400, Charset 0 |
| The same controls' `FontSize` | the **Single** 8.25 |
| The Label with the Arial block | "Arial", Currency 12, Bold True, Italic True, Weight 700 |
| HScrollBar, VScrollBar, Timer, Shape, Line, Image | **438**: they have no Font |

**The default member is Name, and a control's Font is one object:**

| Statement | vb6.exe |
|---|---|
| `v = Label1.Font`, `CStr(Label1.Font)` | the String "MS Sans Serif" |
| `Label1.Font Is Label1.Font` | True |
| `Label1.Font = "Arial"` | sets the name: `Font.Name` and `FontName` read "Arial" |
| `Label1.Font.Size = 12`, then `FontSize` and `Font.Size` | 12 and 12 |
| `Label1.FontSize = 10`, then both | 9.75 and 9.75: the whole-pixel size, as #1343 measured |
| `Label1.FontBold = True`, then `Font.Bold` and `Font.Weight` | True, 700 |
| `Set f = Label1.Font: f.Size = 14`, then `Label1.FontSize` | 14.25: `f` is the Label's own font |
| `Set Text1.Font = Label1.Font`, then `Text1.FontName`, `Text1.FontSize` | Arial, 14.25 |
| then `Label1.Font.Size = 9`, then `Text1.FontSize` and `Text1.Font Is Label1.Font` | **9, True**: the two now share one Font |
| `Set Command1.Font = sf`, a `New StdFont` set to Courier New 11 | "Courier New", 11.25 |
| then `sf.Size = 20`, then `Command1.FontSize` and `Command1.Font Is sf` | **20.25, True**: the control holds the StdFont itself |
| `TypeName(Me.Font)`, and `Me.Font Is Label1.Font` | "Font", False |
| `Set Label1.Font = Nothing` | **380**, and the font is unchanged |

So a Font is an object with Name as its default member, and its Size is a Currency. `Set` makes the control hold the
object it is given, not a copy, so a change through either name reaches every control that holds it. A Font is not
shared with the form until it is set: `form-font-inheritance.md` measured `Command1.Font Is Form1.Font` False too.
