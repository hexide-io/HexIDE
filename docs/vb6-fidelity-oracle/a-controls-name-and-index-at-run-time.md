## A control's Name and Index, at run time (2026-10-22, #1431)

**The question.** HexIDE stopped a program that read a control's `Name`, with a compile error `On Error` cannot trap. What
does each kind of control give, and what does assigning it do? The same for `Index`, which "Control arrays" above
measured only on an element.

**The probes.** A form with a ComboBox, TextBox, Label, CommandButton, Frame, Timer, Image, Shape, Line, an OptionButton
array `Option1(0)` and `Option1(1)`, a menu `mnuFile` and a menu array `mnuOpen(0)`, and a second, empty form, `Form2`.
`Form_Load` logs one line a read, under `On Error Resume Next`, then unloads. Each was made native and p-code, and run in
the guest's session 0; nothing here needs input or painting. The two logs were identical, row for row. An assignment
early-bound stops the make, so each of those had a probe of its own.

### Name

| Read | VB6 |
|---|---|
| `Label1.Name`, `Line1.Name`, `Shape1.Name`, `Image1.Name`, `Timer1.Name`, `Frame1.Name`, `Command1.Name`, `Text1.Name`, `Combo1.Name` | the control's name, Err 0 |
| `Option1(1).Name` | `Option1`, a **String**: the array's name, with no index |
| `mnuFile.Name` / `mnuOpen(0).Name` | `mnuFile` / `mnuOpen` |
| `o.Name`, `Dim o As Object: Set o = Combo1` / `c.Name`, `Dim c As Control` | `Combo1` |
| `c.Name` in `For Each c In Me.Controls` | each control's name |
| `Me.Name` / `Form1.Name` from Form1's code / `Form2.Name` | `Form1`, a String / `Form1` / `Form2` |
| `f.Name`, `Dim f As Form2: Set f = New Form2` / `frm.Name`, `Dim frm As Form: Set frm = f` | `Form2` / `Form2`: a new instance is named after its form |
| `o.Name`, `Set o = Me` / `Set o = Form2` | `Form1` / `Form2` |

| Assignment | VB6 |
|---|---|
| `o.Name = "zz"`, `o` an Object holding Combo1 | **382**, "'Name' property cannot be set at run time"; `Combo1.Name` still reads `Combo1` |
| `c.Name = "zz"`, `c` a **Control** holding Text1 | **382**, the same |
| `o.Name = "zz"`, `o` an Object holding `Me` / `Form2` | **382**, the same |
| `Combo1.Name = "zz"` | compile error, "Can't assign to read-only property" |
| `frm.Name = "zz"`, `frm` a **Form** | compile error, "Can't assign to read-only property" |
| `Me.Name = "zz"` | compile error, "Function or interface marked as restricted, or the function uses an Automation type not supported in Visual Basic" |

### Index

| Read | VB6 |
|---|---|
| `Option1(0).Index` / `mnuOpen(0).Index` | `0`, an **Integer** |
| `o.Index`, `o` an Object holding `Option1(1)` | `1` |
| `o.Index` on Combo1, `c.Index` on Label1 (a Control), `o.Index` on Line1 | **343**, "Object not an array" |
| `c.Index` in `For Each c In Me.Controls` | an element's index; **343** for every other control, a menu among them |
| `o.Index`, `o` an Object holding `Me` | **438**, "Object doesn't support this property or method" |

| Assignment | VB6 |
|---|---|
| `o.Index = 5`, `o` an Object holding `Option1(1)` | **382**, "'Index' property cannot be set at run time"; it still reads `1` |
| `Option1(0).Index = 3` | compile error, "Can't assign to read-only property" |

**What it shows:**
- **Every control has a Name, the lightweight ones and menus too.** An element's is its array's: nothing in `Name` tells
  two elements apart, and `Index` is what does.
- **A variable `As Control` binds late, and one `As Form` binds early.** The same assignment is a run-time 382 through
  a Control and a compile error through a Form. Through an Object, both are late.
- **So a program VB6 compiles can assign `Name` or `Index` only through an Object or a Control,** and always gets the
  382. The three early-bound refusals are the compiler's, and a program with one never runs.
- **`Index` outside an array is 343 for a control and 438 for a form.** A control has the member and no array to give it
  meaning; a form doesn't have the member.
- **`Me.Controls` runs in the order the .frm lists its controls,** and then its menus: Timer1, Frame1, Command1,
  `Option1(1)`, `Option1(0)`, Combo1, Text1, Image1, Shape1, Line1, Label1, mnuFile, `mnuOpen(0)`. The .frm here lists
  `Option1(1)` before `Option1(0)`, and the collection kept that order. This is for #1060: HexIDE has no Controls
  collection yet.

### A member no object has, late-bound

Measured alongside, on a form with a TextBox and a class `Class1` with one Public field, under `On Error Resume Next`,
native and p-code identical:

| Statement | VB6 |
|---|---|
| `v = o.Foo` / `o.Foo = 1` / `o.Foo`, `o` an Object holding Text1 | **438**, "Object doesn't support this property or method", each |
| `v = c.Foo` / `c.Foo = 1`, `c` a Control holding Text1 | **438**, each |
| `v = o.Foo`, `o` holding `Me` | **438** |
| `v = o.Foo` / `o.Foo = 1`, `o` holding a `Class1` | **438**, each |

So late-bound, a member that isn't there is always 438, read, written or called, on a control, a form or a class.
Early-bound it is the compile error "Method or data member not found", which is why HexIDE raises that. HexIDE at the
time of measuring, for comparison: the reads were that compile error, which `On Error` cannot trap; the writes, a trappable
461; the call, refused as not implemented (#1433).

### In HexIDE (#1431)

- **Every row above reads and refuses as measured,** but those for a form made with `New` (#822) and for `Me.Controls`
  (#1060), neither of which HexIDE has yet.
- **An early-bound assignment is the 382 too, when the statement runs.** HexIDE doesn't tell early binding from late, so
  it gives every assignment what VB6 gives the only kind it compiles. That's the translation `interpreter-core` prescribes
  for a compile-time refusal, and #1340's for a TextBox's MultiLine.
- **Not measured:** a UserControl's Name, which VB6 is recalled to give through its Extender. A control added at run time
  with `Controls.Add` reads the name Add gave it, and has no Index (343): "Controls.Add and Controls.Remove" (#1556).

### Each intrinsic control's members, from VB6.OLB (2026-11-21, #1433)

**What #1433 needs.** It needs to tell a name VB6's control lacks, which raises 438 late-bound, from one VB6 has and HexIDE doesn't implement. That takes each control's real member list. HexIDE's own component metadata only says what HexIDE implements, and `vb6-form-members.json` covers only Form.

**Where the list is.** `corpus/inbox-controls/typelib/VB6-members.jsonl` holds the members of the 21 intrinsic controls. `scripts/dump-typelib-members.ps1` read them from VB6.OLB (SP6) on the oracle VM, in its 32-bit PowerShell, run as its own example shows:

```powershell
dump-typelib-members.ps1 -Path '<VB98>\VB6.OLB' -Class 'PictureBox,Label,TextBox,Frame,CommandButton,CheckBox,OptionButton,ComboBox,ListBox,HScrollBar,VScrollBar,Timer,DriveListBox,DirListBox,FileListBox,Menu,Shape,Line,Image,Data,OLE'
```

It has the same row shapes as the OCX dumps beside it: a `CLASS` row per interface, and a `MEMBER` or `EVENT` row per member, with `invoke`, `params`, `type`, `hidden` and `restricted`.

**Checked against a second reader.** A `/make` probe walked the same library with TLI (`TypeLibInfoFromFile`). For each class it listed every member of the default interface, built as native code and as p-code. The number of distinct member names matched the dump in all 21 classes:

| Control | Members | Events |
|---|---|---|
| PictureBox | 89 | 26 |
| Label | 60 | 18 |
| TextBox | 71 | 24 |
| Frame | 45 | 13 |
| CommandButton | 54 | 17 |
| CheckBox | 59 | 18 |
| OptionButton | 54 | 19 |
| ComboBox | 65 | 19 |
| ListBox | 65 | 21 |
| HScrollBar, VScrollBar | 37 each | 10 each |
| Timer | 10 | 1 |
| DriveListBox | 49 | 16 |
| DirListBox | 50 | 20 |
| FileListBox | 59 | 22 |
| Menu | 11 | 1 |
| Shape | 27 | 0 |
| Line | 21 | 0 |
| Image | 38 | 13 |
| Data | 53 | 15 |
| OLE | 79 | 16 |

A property's get, let and set count as one member name here.

**Worth knowing before building on it:**
- **`Name`, `Index`, `Parent` and `Tag` are in all 21 controls' own interfaces.** The other extender-style names vary:
  - `Container`: all but the Timer, the Menu and the Data control.
  - `Left`: all but the Timer, the Menu and the Line.
  - `Visible`: all but the Timer.
  - `Enabled`: all but the Shape and the Line.
  - `TabIndex`: not on the Timer, the Menu, the Shape, the Line, the Image or the Data control.
  - `hWnd`: not on those six, nor on the Label.
- **Fifteen controls have hidden `AddItem`, `RemoveItem` and `Clear` methods,** a Timer and a TextBox included. The Shape and the Line also have a hidden `Drag`. Late-bound, these names exist, so they won't raise 438 for being missing. What calling one does wasn't measured.
- **No Timer, HScrollBar or VScrollBar has `BackColor`**, which is #801's case from the other side.
- **`Print` isn't in the library.** VB6 compiles it as its own syntax on a PictureBox, as on a Form.
- **Every control's member at ID 0 is `_Default`.** Which property it stands for is in "Each intrinsic control's default member" (#790), on `a-control-where-a-value-is-wanted-is-its-default-member.md`.

**Not measured:** whether `GetIDsOfNames` on a running control accepts exactly these names and no others. The 438 rows above were measured for `Foo` only.

### Does a running control answer a name its list leaves out? (2026-11-23, #5443, SP6)

**The question.** The section above left one thing unmeasured: whether a running control answers exactly its VB6.OLB
names late-bound. #1433 relies on it, raising 438 for a name absent from the control's list. If the running object
answered a name its type library omits, VB6 would return a value there, and HexIDE would raise 438.

**The probe.**
- **Setup:** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle guest. The project was built with `/make` as native code
  and as p-code, and each exe was run in session 0. The two logs matched line for line.
- **The form** held one control of each of the 21 classes, and a menu.
- **Each control** was passed to a procedure as `o As Object`. Under `On Error Resume Next`, the procedure read every
  name that's in another class's list in `VB6-members.jsonl` but not in the control's own.
- **Each read** was `v = o.Name` into a Variant. Where that raised 438, `Set ob = o.Name` was tried too, so that a name
  answering with an object without a default member couldn't pass as absent.
- **`Line`, `Circle`, `PSet` and `Scale`** don't parse as `v = o.Line`, so those four went through
  `CallByName(o, "Line", VbMethod)` and `CallByName(..., VbGet)`.
- **As positive controls,** each class's own `Name` and `Tag` were read the same way first, and the Timer's `Left`,
  `Top`, `Enabled`, `Index`, `Parent` and `Container`.

**What VB6 gave:**

| Class | Unlisted names read | Answered |
|---|---|---|
| PictureBox | 121 | none: 438 for every one, read and `Set` |
| Label | 150 | none |
| TextBox | 139 | none |
| Frame | 165 | none |
| CommandButton, OptionButton | 156 each | none |
| CheckBox, FileListBox | 151 each | none |
| ComboBox, ListBox | 145 each | none |
| HScrollBar, VScrollBar | 173 each | none |
| DriveListBox | 161 | none |
| DirListBox | 160 | none |
| Shape | 183 | none |
| Line | 189 | none |
| Image | 172 | none |
| Data | 157 | none |
| OLE | 131 | none |
| Menu | 199 | none |
| Timer | 200 | **`Left` and `Top`: 393**, "'Left' property cannot be read at run time". The other 198 are 438 |

The positive controls:
- **Every class's `Name` and `Tag`** read back with no error: `Picture1`, `Label1` and so on, and `""`.
- **The Timer:** `Enabled` read True, `Index` raised 343 "Object not an array", and `Parent` raised 450.
  `Container`, absent from its list, raised 438.

**Readings:**
- **For the 20 classes other than the Timer,** the running control answers none of the names its list leaves out, among
  the 210 names any intrinsic class has. So a name absent from a control's list is 438 late-bound, as #1433 has it.
- **The Timer's `Left` and `Top`** are the exception the list's dump flagged: they're answered, with 393, as
  `a-controls-position-at-run-time.md` already records.

**Not measured:**
- **Names in no intrinsic class's list,** other than `Foo` above: a Form's own `Hide` or `Show`, say.
- **Writes and calls** of the unlisted names. The reads above were value reads and `Set`s.
