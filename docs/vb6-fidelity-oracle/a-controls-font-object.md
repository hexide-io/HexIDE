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

### A free-standing StdFont's assignments (2026-11-18)

**The probe.**
- **Build:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0. The two builds logged all 48 rows identically.
- **Each row:** a fresh `Set f = New StdFont`, the row's assignments under `On Error Resume Next`, then the error and every member read back.
- **The starting font** reads MS Sans Serif, 8.25, Weight 400, Charset 0, no flags. It's recorded in "A StdFont in a bag" on the PropertyBag page.
- **Every member is read in every row,** so only the members that changed are shown.

**Name.**

| Assignment | Error | Reads afterwards |
|---|---|---|
| `"Verdana"`, `"Times New Roman"` | none | that name, 8.25, Charset 0 |
| `"Wingdings"`, `"Symbol"` | none | that name, 8.25, **Charset 2** |
| `"Courier"` | none | Courier, **9.75** |
| `"No Such Font"`, `""`, `5` | none | **unchanged**: MS Sans Serif, 8.25 |
| `Null` | **13** | unchanged |

**Size.**

| Assignment | Error | Reads afterwards |
|---|---|---|
| `12` | none | MS Sans Serif, 12 |
| `9` | none | **Arial**, 9 |
| `14` | none | **Arial**, 14.25 |
| `7.5` | none | **Small Fonts**, 7.5 |
| `20` | none | **Arial**, 20.25 |
| `1` | none | **Arial**, 1.5 |
| `2048` | none | **Arial**, 2048.25 |
| `2160` | none | **Arial**, 2160 |
| `"11"` | none | **Arial**, 11.25 |
| `0`, `-1` | **380** | unchanged |
| `100000` | **6** | unchanged |
| `"x"`, `Null` | **13** | unchanged |
| `Name = "Arial"`, then `Size = 12` | none | Arial, 12 |
| `Name = "Arial"`, then `Size = 7` | none | Arial, **6.75** |

**Bold, Italic, Underline and Strikethrough.**

| Assignment | Error | Reads afterwards |
|---|---|---|
| `Bold = True`, then `Bold = False` | none | Bold False, Weight 400, and Name **Arial** |
| `Bold = 1`, `Bold = "True"` | none | Bold True, Weight 700, and Name **Arial** |
| `Bold = Null` | **13** | unchanged |
| `Italic`, `Underline` or `Strikethrough = True`, then `= False` | none | that flag False, Name MS Sans Serif |

**Weight.**

| Assignment | Error | Reads afterwards |
|---|---|---|
| `0`, `100`, `550` | none | Weight **400**, Bold False |
| `551`, `900`, `1000` | none | Weight **700**, Bold True, Name MS Sans Serif |
| `700` | none | Weight 700, Bold True, and Name **Arial** |
| `-1`, `32767` | **380** | unchanged |

**Charset.**

| Assignment | Error | Reads afterwards |
|---|---|---|
| `0`, `1` | none | Charset 0, MS Sans Serif |
| `128` | none | Charset 128, Name **MS PGothic** |
| `255` | none | Charset 255, Name **Arial** |
| `256`, `-1` | **380** | unchanged |
| `Name = "Verdana"`, then `Charset = 2` | none | Charset 2, Name **Wingdings** |

**What these say, read as measured:**
- **A failed assignment changes nothing.** Every error row reads the starting font afterwards.
- **A name with no font behind it is not an error:** the font keeps its name.
- **Size and Weight are rounded to what the font has,** and the font named can change with them. MS Sans Serif keeps 12, but 9, 14 and 20 read as Arial, and 7.5 reads as Small Fonts.
- **Weight reads only 400 or 700.** 550 and below read 400; 551 and above read 700.
- **Charset can change the name:** 2 gives Wingdings, 128 MS PGothic, 255 Arial.

**What resisted explanation.** `Weight = 700`, `Bold = 1` and `Bold = "True"` leave the name reading Arial, and so does `Bold = True` then `False`. But `Weight = 551`, `900` and `1000` leave it reading MS Sans Serif, with the same Weight 700 and Bold True. Measured, but no rule I would defend.

**Not measured:**
- the same assignments on a control's Font;
- Sizes between those above;
- names outside ASCII;
- a machine with another ANSI code page.
