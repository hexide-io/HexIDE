## A control where a value is wanted is its default member (2026-10-12, #790)

Measured to decide what HexIDE should do with `s = Text1` until it has default members. Real `vb6.exe` 6.00.8176
(the original release), guest locale en-GB. Two `/make` runs of a form holding `Text1`, `Label1`, `Command1` and `Check1`, each
statement under `On Error Resume Next`, with the value, its `TypeName` and `Err.Number` logged.

| Statement | Result | Err |
|---|---|---|
| `s = Text1` / `s = Label1` (s As String) | `"Text1"` / `"Label1"`: the Text and the Caption | 0 |
| `v = Text1` (v As Variant) | `"Text1"`, String | 0 |
| `v = Command1` / `v = Check1` | `False`, Boolean / `0`, Integer: the Value | 0 |
| `n = Check1` (n As Long) | `0` | 0 |
| `n = Text1` (n As Long), Text1 holding `"Text1"` | unchanged | 13 |
| `i = Text1` / `d = Text1` (Integer / Double), Text1 holding `"42"` | `42` / `42` | 0 |
| `s = Command1` / `b = Command1` (String / Boolean) | `"False"` / `False` | 0 |
| `Set o = Text1 : s = o` (o As Object) | `"42"` | 0 |
| `v = o` | `"42"`, String | 0 |
| `Set v2 = Text1` | `TypeName(v2)` is `TextBox` | 0 |
| `v2 = Text1` after that Set (a Let) | `TypeName(v2)` is `String` | 0 |
| `Label1 = "x"` / `Text1 = "y"` / `Check1 = 1` | Caption `x` / Text `y` / Value `1` | 0 |

So a Let never stores the reference: the right-hand side is read through its default member, then converted to the
target's type as any value is (`n = Text1` is 13 because `"Text1"` is not a number). Only `Set` stores the
reference. The last row is the Let side: assigning to a control assigns its default member.

**HexIDE, after #790's guardrail.** HexIDE has no default members yet. It used to store the reference, which
answered wrong without a word: `s = Text1` held `"HexIDE.Runtime.BuiltinControls.VBTextBox"` and `v = Text1` kept
the control. A Let of a control or of an in-box object (`Err`, `App`, a control array) now stops the run with
`NotImplementedException` naming #790, whatever its target. `Set`, `For Each` and passing a control as an argument
are unaffected. `Label1 = "x"` is still a compile error ("Method or data member not found"). `n = Err` reads
`Err.Number` in VB6 by the documentation; that was not measured.

**Superseded for the in-box objects by #840**, which measured them ("An object used as a value"): `n = Err` stores
Err.Number, `s = App` raises 438, and `Debug` and a control array used as values are compile errors. The #790
refusal now covers controls only.

### Each intrinsic control's default member (2026-11-21, #790, SP6)

**The question.** The rows above covered a TextBox, a Label, a CommandButton and a CheckBox. Building default members needs every intrinsic control's.

**Where the default is.** In VB6.OLB every intrinsic control has a member named `_Default` at member ID 0, the one a value context uses. The library doesn't say which property it duplicates, so each one was read and written at run time to find out. The library's dump is `corpus/inbox-controls/typelib/VB6-members.jsonl` (#1433), which a TLI walk of VB6.OLB matched.

**The probes.** A form holding one of each intrinsic control, with the Data control and the OLE container in a project of their own. Each statement ran under `On Error Resume Next`. A read was `v = ctl` into a Variant, directly and through `As Object`, and `CallByName(ctl, "_Default", VbGet)`. A write was `ctl = x`, then the candidate properties were read back. Built with `/make` on vb6.exe SP6 as native code and as p-code, the two agreed except where the value was a picture handle.

| Control | `_Default` in VB6.OLB | Stands for | Read as a value | Written, and read back |
|---|---|---|---|---|
| TextBox | get, let; `BSTR` | `Text` | `"T1"`, String | `= "y"`: `Text` is `y` |
| Label | get, let; `BSTR` | `Caption` | `"L1"`, String | `= "x"`: `Caption` is `x` |
| Frame | get, let; `BSTR` | `Caption` | `"F1"`, String | `= "f"`: `Caption` is `f` |
| CommandButton | get, let; `BOOL` | `Value` | False, Boolean | `= True`: `Value` reads False again. `Default`, `Cancel` and `UseMaskColor`, the other False Booleans, stayed False |
| CheckBox | get, let; `I2` | `Value` | 0, Integer | `= 1`: `Value` is 1 |
| OptionButton | get, let; `BOOL` | `Value` | False, Boolean | `= True`: `Value` is True |
| ListBox | get, let; `BSTR` | `Text` | `""` with nothing selected | `= "b"`, over `a` and `b`: `ListIndex` 1, `Text` `b` |
| ComboBox | get, let; `BSTR` | `Text` | `"B1"` (Style 0), `""` (Style 2) | `= "z"` (Style 0): `Text` is `z` |
| HScrollBar, VScrollBar | get, let; `I2` | `Value` | 0, Integer | `= 5` (HScrollBar): `Value` is 5 |
| PictureBox | get, let, set; `VARIANT` | `Picture` | a Long, the picture's `Handle`: 0 when empty, the icon's handle once `Form1.Icon` was assigned | `CallByName(..., "_Default", VbSet, Form1.Icon)`: `Picture.Handle` is the icon's |
| Image | get, let, set; `VARIANT` | `Picture` | as the PictureBox | `Image1 = Form1.Icon`, a Let and not a Set: `Picture.Handle` is the icon's |
| Shape | get, let; `I2` | `Shape` | 0, Integer | `= 3` through `As Object`: `Shape` 3; `BackStyle` 0 and `FillStyle` 1 unchanged |
| Line | get, let; `BOOL` | `Visible` | True, Boolean | `= False` through `As Object`: `Visible` False; `BorderStyle` unchanged |
| Timer | get, let; `BOOL` | `Enabled` | True, Boolean | `= False`: `Enabled` False; `Interval` still 0 |
| DriveListBox | get, let; `BSTR` | `Drive` | `"c:"` | `= "c:"`: `Drive` is `c:` |
| DirListBox | get, let; `BSTR` | `Path` | the current folder | `= "C:\"`: `Path` is `C:\` |
| FileListBox | get, let; `BSTR` | `FileName` | `""` | `= "*.txt"`: `FileName` stays `""` and `Pattern` becomes `*.txt`, as a pattern given to `FileName` does |
| Menu | get, let; `BOOL` | `Enabled` | True, Boolean | `= False`: `Enabled` False; `Checked` False and `Visible` True unchanged. `= True` changed nothing |
| Data | get, let; `BSTR` | `Caption` | `"D1"` | `= "D2"`: `Caption` `D2`; `DatabaseName` and `RecordSource` stayed empty |
| OLE | let only; `I2` | not run | **394** "Property is write-only" | not run: an OLE container's writes are actions, which can open a dialog |

**The form classes are different.** Form's `_Default` is a get returning an object, with no parameters, and so are MDIForm's, UserControl's, PropertyPage's and UserDocument's. `v = Form1` and `CallByName(Form1, "_Default", VbGet)` both raised **450**. That's reasoned, not checked: the object it returns has a default that needs an argument. The other four classes weren't run.

**Overturned:**
- **A PictureBox's default isn't read as an object.** Its `_Default` is typed `VARIANT` with a get, a let and a set, but read as a value it gave a Long, the picture's handle, never the picture.
- **`Image1 = Form1.Icon` assigns the picture without `Set`.**

**Not measured:** `Option1 = False` on a selected button; a ListBox write of text that matches no item; MDIForm and the other form classes.

### A control printed, written to a file, and joined with `&` (2026-11-23, #5385, SP6)

**The question.** The sections above read a control into a variable. #5385 found HexIDE printing a control's .NET type
name for `Debug.Print c`. What do `Print #`, `Write #` and `&` give for a control, and is it the same through
`Control`, `Object` and a Variant?

**The probes.**
- **Setup:** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle guest. Each project was built with `/make` as native code
  (`CompilationType=0`) and as p-code (`CompilationType=-1`), and each exe was run in session 0. The two builds wrote
  the same file, line for line, in every probe.
- **`Print #` stands in for `Debug.Print`,** which a compiled program drops.
- **The program:** each statement ran in `Form_Load` under `On Error Resume Next`, followed by a line logging
  `Err.Number`. Every row below read 0.
- **The first form** held `Label1` ("L1"), `Text1` ("T1"), `Command1`, `Check1` and an empty `Picture1`. Each was
  printed directly, then through `c As Control`, `o As Object` and `Set v =` a Variant.
- **The second form** held one each of the eleven controls in the table below. Before the probes,
  `List1.AddItem "a"`, `List1.AddItem "b"`, `Check1.Value = 1`, `Option1.Value = True`, `HScroll1.Value = 5`,
  `VScroll1.Value = 7` and `Shape1.Shape = 3`. Each control went through `Print #1, X`, `Print #1, o` with
  `Set o = X`, `Write #1, X`, `Write #1, o`, and `s = "" & X`.

| Control | `Print #1, X`, and through `Object` | `Write #1, X`, and through `Object` | `"" & X` |
|---|---|---|---|
| TextBox `"T1"` | `T1` | `"T1"` | `T1` |
| Label `"L1"` | `L1` | `"L1"` | `L1` |
| Frame `"F1"` | `F1` | `"F1"` | `F1` |
| ComboBox, Style 0, `"K1"` | `K1` | `"K1"` | `K1` |
| ListBox, nothing selected | an empty line | `""` | empty |
| ListBox, `ListIndex = 1` | `b` | `"b"` | — |
| CommandButton | `False` | `#FALSE#` | `False` |
| OptionButton, `True` | `True` | `#TRUE#` | `True` |
| CheckBox, 1 | ` 1 ` | **`"1"`** | `1` |
| HScrollBar, 5 | ` 5 ` | **`"5"`** | `5` |
| VScrollBar, 7 | ` 7 ` | **`"7"`** | `7` |
| Shape, 3 | ` 3 ` | **`"3"`** | `3` |

From the first form:
- **Through `Control`, `Object` and a Variant,** each control printed what it printed directly: `L1`, `T1`, `False`
  and ` 0 ` for the empty PictureBox.
- **`Print #1, Command1; Check1; Picture1`** gave `False 0  0 `.
- **`Print #1, Label1; Text1`** gave `L1T1`.
- **`Print #1, Label1, Text1`** gave `L1` and `T1` a print zone apart.
- **`Print #1, Label1;`** left the line open, as for any item.
- **`Write #1, Label1, Command1, Check1, Picture1`** gave `"L1",#FALSE#,"0","0"`.

**Readings:**
- **`Print #` and `&` use the default member,** the value `v = X` reads. A number prints as numbers do, with a leading
  space and a trailing one, and a Boolean as `True` or `False`.
- **`Write #` of a control quotes a numeric default.** A Boolean default is written as `#TRUE#` or `#FALSE#`, and every
  other default, numbers included, as a quoted string: `"1"`, `"5"`, `"0"`.
  - **The same values written once read out of the control are numbers:** `Write #1, Check1.Value,
    Picture1.Picture.Handle` gave `0,0`.
  - **So do these:** `v = Check1: Write #1, v` (`TypeName(v)` Integer), `i = Check1: Write #1, i`, and
    `Write #1, Check1 + 0, Picture1 + 0`.
  - **It's the control that's quoted,** reached directly, through `Object`, or through a Variant set to it (`Set v =
    Check1: Write #1, v` gave `"0"`). That's measured, but there's no rule I'd defend for why a Boolean escapes it.

**Not measured:** `Debug.Print` itself in the IDE's Immediate window; a PictureBox or Image holding a picture; the
Image, Line, Timer, Menu, the three file-system lists, Data and OLE controls.
