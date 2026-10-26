## Identifier case (2026-10-12, #811)

**The question.** Does a name written in a different case from its declaration reach the same variable, constant,
procedure, control, property or method? HexIDE's environment and property tables compared ordinally, so `abc`
after `Dim Abc` was a new implicit variable, and a Function assigning `foo = 7` inside `Function Foo` returned 0.

**Probe.** A Standard EXE with `Module1.bas` and `Form1.frm`, built with `vb6.exe /make` on the Win10 guest
(session 0) and run, writing each value to a file. Every reference is in a different case from its declaration.

`Module1.bas`:
```vb
Option Explicit
Public Abc As Integer
Public Const KPI As Integer = 3
Public Function Foo() As Integer: foo = 7: End Function
Public Function Bar() As Integer: BAR = 8: bar = bar + 1: End Function
Public Sub Bump(ByVal Pa As Integer, ByRef Outv As Integer): pa = pa + 1: OUTV = PA: End Sub
```

`Form1.frm`, under `Option Explicit`, with `Private Xyz As Integer` at module level. Each value was written with
`TypeName` beside it:

| Statement(s) | Read back | Value | Type |
|---|---|---|---|
| `abc = 5` | `Abc` | 5 | Integer |
| (same) | `module1.abc` | 5 | Integer |
| `Foo()`, assigned as `foo = 7` | | 7 | Integer |
| `bar()`, assigned `BAR = 8` then `bar = bar + 1` | | 9 | Integer |
| `kpi * 2` | | 6 | Integer |
| `bump 10, o` | `o` | 11 | Integer |
| `Sub Shadow: Dim xyz: xyz = 3` | `xyz`, `XYZ` inside it | 3, 3 | Integer |
| `Xyz = 1`, then `Shadow` | `Xyz` after it | 1 | Integer |
| `text1.text = "lower"` | `Text1.Text` | lower | String |
| `LABEL1.CAPTION = "UP"` | `Label1.Caption` | UP | String |
| `list1.additem "a"`, `LIST1.ADDITEM "b"` | `List1.ListCount` | 2 | Integer |
| `cmd(0).caption = "c0"` | `Cmd(0).Caption` | c0 | String |
| `form1.caption = "fc"` | `Me.Caption` | fc | String |
| `me.CAPTION = "mc"` | `Form1.Caption` | mc | String |
| `err.raise 5` | `ERR.NUMBER` | 5 | Long |
| then `err.clear` | `Err.Number` | 0 | Long |
| `app.title` | | probe | String |
| `debug.print`, `DEBUG.PRINT` and `Debug.print` | | all three compile | |

**Result.** Case is ignored everywhere, `Option Explicit` included. A local that differs only in case from a
module variable is a new local that **shadows** it: inside `Shadow`, `xyz` and `XYZ` are both the local, and the
module's `Xyz` keeps 1.

**A procedure's `Const` shadows too.** A third probe declared `Private Limit As Integer` and
`Private Const Lim2 As Integer = 5` in the form, set `Limit = 1`, and called four Subs, each declaring a `Const`
over one of them:

| Sub declares | Read inside it | Module's value after it |
|---|---|---|
| `Const LIMIT As Integer = 10` | `LIMIT` 10, `limit` 10 | `Limit` 1 |
| `Const Limit As Integer = 20` | `Limit` 20 | `Limit` 1 |
| `Const lim2 As Integer = 7` | `lim2` 7 | `Lim2` 5 |
| `Const Lim2 As Integer = 8` | `Lim2` 8 | `Lim2` 5 |

Every value is an Integer, and calling the first Sub a second time gives the same rows. So a procedure's `Const`
is the procedure's own, in any case, over a module variable or a module `Const`. **HexIDE got this wrong before
#811 and worse during it:** a `Const` updated whatever slot its name found, and a procedure's scope is a copy of
its module's. So the same-case form wrote the module's `Limit` and left it at 20 after the call. Once lookup
ignored case, so did every other spelling. A `Const` inside a procedure now gets its own slot, as a `Dim` does.

**And in a `.frm`.** A second probe wrote property names in other cases into the form file itself: `caption`
and `backcolor` on a Label, `TEXT` on a TextBox. All three load (`lower key`, `255`, `upper key`).

A fourth wrote the form's **own** properties in lowercase: `caption`, `clientheight` 3000, `clientleft`,
`clienttop`, `clientwidth` 4000, `scaleheight` 3000, `scalewidth` 4000, and a Font block of `name` "Arial",
`size` 12, `weight` 700 and the rest. Everything loads:

| Read in `Form_Load` | Value | Type |
|---|---|---|
| `Me.Caption` | Probe | String |
| `Me.ScaleWidth` | 4005 | Single |
| `Me.ScaleHeight` | 3000 | Single |
| `Me.Width` | 4125 | Single |
| `Me.Height` | 3405 | Single |
| `Me.Font.Name` | Arial | String |
| `Me.Font.Size` | 12 | **Currency** |
| `Me.Font.Bold` | True | Boolean |

4005 rather than 4000 is the client width rounded up to whole pixels at 15 twips a pixel (267 pixels), so the
lowercase `clientwidth` was applied. The outer width adds the frame: 4125. `Font.Size` is a Currency, not a
Single or Double. That is VB6's own type for it, recorded here because a program that asks `TypeName` gets it.

**HexIDE, after #811.**
- **The tables are created case-insensitive:**
  - the execution environment: locals, parameters, return slots, module variables and constants, controls, the
    form, `Me`, `Debug`, `Err` and `App`;
  - the interpreter's control-property table;
  - each component's property table, which the `.frm` reader uses.
- **`Debug.Print`** matches in any case.
- **Locals** shows the first-declared spelling, which stays the key.
- **The `.frm` reader** maps each property name to its canonical spelling before anything compares it. Without
  that, a lowercase outer `width` on the root missed the branch that records the outer rectangle, and was
  applied as the client width. A Font block's fields are read in any case.

**Owned elsewhere.** Two rows above fail in HexIDE in *every* case on the form path F5 takes, so they are not
about case:
- **Reading or writing another module's `Public` variable or `Const` from a form (#256).** `kpi * 2` read from
  the form reads Empty, and a write to `module1.abc` does not reach it. The tests read and write within one
  module instead.
- **`ListCount` (#821).** The tests read the list's items instead. `ListCount` exists since #821, and its name is
  matched in any case, but these tests were not changed.

`Me` and the form's own name do resolve in any case on that path, where `VBLoader.RunForm` binds them, and are
tested there.

