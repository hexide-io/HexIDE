## A form's Controls collection, and TypeOf on a control (2026-10-23, #1060)

**The question.** What a form's `Controls` holds and in what order, how `Controls(i)` takes its index, how it behaves
while controls are loaded and unloaded, and what `TypeOf` answers for a control. Real programs walk `Controls` to clear,
enable or resize every control of a kind, and pick the kind with `TypeOf`.

**The probe.** `/make` and a run of the EXE, native and p-code, with every probe line under `On Error Resume Next` and
written to a file. Form1's blocks, in file order: `Timer1`, `Command1`, `Text1` index 0, `Frame1` holding `Check1`,
`Text1` index 1, `Label1`, `Line1`, then `mnuFile` holding `mnuOpen`. A second form put `CommonDialog1` between
`Command1` and `Text1(0)`, with a menu array `mnuItem(0)` and `mnuFile` holding a separator `mnuSep` and `mnuLast`.
Four rounds.

**What it holds.** Native and p-code identical:

| Probe | Result |
|---|---|
| `Controls.Count`, `Me.Controls.Count` | 10, 10 |
| For Each, `Name` and `TypeName` | `Timer1 Timer`, `Command1 CommandButton`, `Text1 TextBox`, `Frame1 Frame`, `Check1 CheckBox`, `Text1 TextBox`, `Label1 Label`, `Line1 Line`, `mnuFile Menu`, `mnuOpen Menu` |
| `Controls(i).Name` for i from 0 | the same order |
| the second form's order | `Command1 CommonDialog1 Text1(0) Command2 mnuItem(0) mnuFile mnuSep mnuLast` |
| `TypeName(Controls)`, `TypeName(Me.Controls)` | Object, Object |
| `TypeName` of the CommonDialog, `mnuFile` and the separator | CommonDialog, Menu, Menu |
| `c.Index` on a control in no array | 343 |
| `Command1.ZOrder 1: Command2.ZOrder 0`, then For Each | the order unchanged |
| `Form1.Controls.Count` from Form1's own code | 10 |

So `Controls` is every control in the order its file holds the blocks. A container comes before what it holds, and each
element of a control array sits at its own block. The menus and their submenus are included, as is a control HexIDE
keeps as text. `ZOrder` doesn't reorder it.

**`Controls(i)` and `Controls("Name")`:**

| Index | Result |
|---|---|
| 0, `Controls.Item(0)` | Timer1 |
| `Controls.Count`, -1, 32767 | 9 |
| 1.6, 2.5, `CByte(2)`, `"1.5"`, `"2.5"` | Text1, element 0: position 2 |
| -0.4 | Timer1 |
| 100000 | 6 |
| `True` | 9 |
| `Null` | 13 |
| `"1"`, `"01"`, `"1 "`, `"1e0"` | Command1: position 1 |
| `"1,5"` | 9: 15 under en-US, where the comma groups thousands |
| `"-1"`, `"+1"`, `" 1"`, `"&H1"`, `"x1"`, `"Nope"` | 730, "Control 'x1' not found" with the name as given |
| `""` | 728, "'' is not a legal control name" |
| `"command1"`, `"Check1"`, `"mnuOpen"`, `"Label1"` by `Item` | each found: in any case, nested, a submenu |
| `"Command2 "` | 730: no trimming |
| `Controls!Command2` | Command2 |
| `"Text1"`, the array's name | the array: `TypeName` Object, `.Count` 2, and `(0)` a TextBox |
| `Controls("Command1") Is Command1`, `Controls(1) Is Command1` | True, True |
| `Set c = Controls: c.Count` | Object, 10 |
| `v = Controls`, `v` a Variant | 450, and `v` stays Empty |
| `Controls(0)` in a string | True: Timer1's default member, `Enabled` |

A number is converted as CInt converts it: halves to even, 6 past an Integer. A String is a position only when it
**starts with a digit**, and then converts as CInt converts that string. `"-1"`, `"+1"` and `" 1"` are names, though
CInt reads each as a number. That is the rule the rows fit, not one VB6 documents. Controls' names can't start with a
digit, so it never hides a control.

**Loading and unloading.** Native and p-code identical, except as noted:
- `Load Text1(2)` adds the element at the end, and `Load mnuItem(1)` adds the menu element at the end too. Count goes up
  by one.
- `Unload` takes the element out, and a later `Load` of the same index goes at the end again. Loads in the order 3 then 1
  list 3 before 1.
- **A For Each goes on from where it is.** A loop that unloads the element it is on reaches the next. A loop on
  `Command1` that unloads a later element doesn't reach it. One that loads an element doesn't reach the new one either:
  a Collection's loop does (#1471).
- **One row resisted explanation.** In p-code only, a loop that unloaded an element it had already passed, followed by an
  `Unload` of the rest and a `Load` of indexes 1, 2 and 3, then listed 2, 3, 1. Native code listed 1, 2, 3, as both did
  in every other sequence. Measured, but no rule I would defend.

**`TypeOf`.** Native and p-code identical:

| Expression | Result |
|---|---|
| `TypeOf Text1(0) Is TextBox`, `Is VB.TextBox`, `Is Control`, `Is Object` | True each |
| `TypeOf Text1(0) Is CommandButton` | False |
| `TypeOf CommonDialog1 Is CommonDialog`, `Is MSComDlg.CommonDialog`, `Is Control` | True each |
| `TypeOf mnuLast Is Menu`, `Is Control`; the separator `Is Menu` | True each |
| A variable `As Object` holding `Text1(0)`, `Is TextBox`, `Is Control` | True, True |
| `TypeOf` a variable holding `Controls`, `Is Object`, `Is Collection` | True, False |
| `TypeOf Text1 Is Object`, the array | True |
| `TypeOf Me Is Form`, `Is Form1`, `Is Object`, `Is Control` | True, True, True, False |
| `For Each c In Controls: If TypeOf c Is TextBox Then …` | reaches each TextBox |
| `For Each t In Controls`, `t As TextBox`, under Resume Next | 13 at the first control, the body runs once, and `t` is Nothing |

**`Controls.Add` and `Remove`.** `Set c = Controls.Add("VB.CommandButton", "cmdNew")` gives a CommandButton, enumerated
last, and `Controls.Remove "cmdNew"` takes it out. `Controls.Remove "Command1"`, placed at design time, raises 729.
HexIDE refuses both, naming #1556.

### In HexIDE (#1060)

`FormControls` is the collection. The loader builds it in file order and gives it to the form instance. A preserved
block, the CommonDialog, takes its place among its container's children by its ordinal, as the writer puts it back.
A control-array element loaded or unloaded at run time joins or leaves it. Its For Each starts from the controls it
held and passes over any since unloaded. `Controls`, `Me.Controls` and `Form2.Controls` reach it, and so do
`Controls(i)`, `Controls!Name` and a variable holding it.

**`TypeOf` was False for every control**, whatever the type named, so the usual loop did nothing, silently. It now answers
as the table shows, from the class `TypeName` gives a control. A form is a `Form` too.

**`Unload c`**, with `c` holding an array element, unloads it, as the cleanup loop needs. Only `Unload Text1(i)` used
to.

**Not followed:**
- The p-code order after the row that resisted explanation: HexIDE lists 1, 2, 3, as native code does.
- `For Each t In Controls` with `t As TextBox`: HexIDE binds every control and raises nothing, where VB6 raises 13 at the
  Timer. A missing error, filed as #1563.

**Not measured:** `Controls` on a UserControl, and on a form while its Initialize runs.


## Reading a Frame caption (#2063)

Provenance: issue hexide-io/HexIDE#2063, reporting measurements made while testing #1556.
With `Set c = Controls.Add("VB.Frame", "z")`, reading `c.Caption` returns the empty string
in both native and p-code VB6 builds. Reading `Frame1.Caption` returns the caption saved
in the `.frm`. The issue does not record a caption-assignment probe or its result.
