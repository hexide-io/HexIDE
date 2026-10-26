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

