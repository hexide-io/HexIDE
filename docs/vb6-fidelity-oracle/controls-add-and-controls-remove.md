## Controls.Add and Controls.Remove (2026-10-26, #1556)

**The question.** What `Controls.Add` makes, where it puts it and what it refuses, and what `Controls.Remove` takes out.
They are VB6's way to make a control at run time without a design-time element to copy, and #1060 measured only that
`Add` returns the control and `Remove` refuses a design-time one with 729.

**The probe.** `/make` and a run of the EXE, native and p-code, in session 0, with every probe line under
`On Error Resume Next` and written to a file. Form1 is Arial 12 with a back colour of `&H00C0FFFF&`. Its blocks, in file
order: `Frame1`, `Picture1` (Courier New 9.75, back colour `&H00FFC0C0&`), `Text1`, `Command1`, `Option1(0)`. Seven
rounds: the form loaded and not shown; the same shown; one control's own `WS_VISIBLE` bit read with `GetWindowLong`;
six one-statement projects for the shapes the compiler might refuse; which handlers an added control's events reach; 
which error wins when more than one argument is wrong; and the list boxes' heights under fourteen fonts. Native and p-code agree on every line.

### What Add makes

| Class | Left, Top | Width × Height (twips) |
|---|---|---|
| `CommandButton`, `TextBox`, `Label`, `CheckBox`, `OptionButton`, `Frame`, `PictureBox`, `Image`, `Shape`, `DriveListBox`, `DirListBox` | 0, 0 | 1200 × 480 |
| `ListBox` | 0, 0 | 1200 × 330 |
| `ComboBox` | 0, 0 | 1200 × 315 |
| `FileListBox` | 0, 0 | 1200 × 360 |
| `HScrollBar` | 0, 0 | 1200 × 240 |
| `VScrollBar` | 0, 0 | 240 × 1200 |
| `Data` | 0, 0 | 1140 × 240 |
| `OLE` | 0, 0 | 3000 × 1125 |
| `Line` | `X1`, `Y1`, `X2`, `Y2` all 0 | |
| `Timer` | no position: `Left` is 393 | |

**The ListBox's height follows its font, and the ComboBox's does not.** A seventh round set the form's font before each
`Add`, since the new control takes it:

| Font | ListBox height | Rows × row (pixels) | ComboBox height |
|---|---|---|---|
| MS Sans Serif 8.25 | 450 | 2 × 13 | 315 |
| MS Sans Serif 9.75 | 300 | 1 × 16 | 315 |
| MS Sans Serif 12 | 360 | 1 × 20 | 315 |
| MS Sans Serif 13.5 | 420 | 1 × 24 | 315 |
| Microsoft Sans Serif 8.25 | 450 | 2 × 13 | 315 |
| Tahoma 8.25 | 450 | 2 × 13 | 315 |
| Tahoma 9.75 | 300 | 1 × 16 | 315 |
| Verdana 8.25 | 450 | 2 × 13 | 315 |
| Arial 8.25 | 480 | 2 × 14 | 315 |
| Arial 9.75 | 300 | 1 × 16 | 315 |
| Arial 12 | 330 | 1 × 18 | 315 |
| Times New Roman 12 | 345 | 1 × 19 | 315 |
| Courier New 9.75 | 300 | 1 × 16 | 315 |
| Segoe UI 9 | 285 | 1 × 15 | 315 |

Every ListBox height is 60 twips plus as many whole rows as fit in the 480 the other classes get, a row being a whole
number of pixels: the integral height of a 480-twip request. The row heights are inferred from the heights, and one
answer fits each: a row must be whole pixels and the next row must not fit. The ComboBox is 315 under every font tried.
The FileListBox's 360, with Arial 12, is one row of 20 pixels by the same rule, and was measured with that font only.

- **Hidden.** The control's own `WS_VISIBLE` is off when it is made, for every windowed class tried, and `Visible = True`
  sets it. A design-time control's is on. `Visible` itself read False throughout, design-time controls included,
  because session 0 never shows the form: it reads the window's visibility, parents and all.
- **Empty.** `Caption` and `Text` are `""`, not the control's name, which is what the designer gives a new control.
- **The form's font, as it stands.** Every class with a `Font` has the form's, Arial 12, even inside `Picture1`, whose
  own font is Courier New. Changing the form's font to Times New Roman 14 before an `Add` gives the new control Times New
  Roman 14.25, while `Command1` keeps Arial 12.
- **Its class's own colours, not the form's.** A `CommandButton`, `Label`, `CheckBox`, `OptionButton`, `Frame` and
  `PictureBox` have back colour `&H8000000F&`, the button face. A `TextBox`, `ListBox`, `ComboBox`, the three file
  lists, `Shape`, `Data` and `OLE` have `&H80000005&`, the window background. Neither takes the form's `&H00C0FFFF&`.
- **Last.** It is last in `Controls`, and its `TabIndex` is one past the highest already on the form. A `Remove` closes
  the gap: with added buttons at 5, 7, 8 and 9, and one inside `Picture1` at 6, removing the one at 7 makes 8 and 9 into
  7 and 8.
- **Under the rest.** `GetWindow` lists the form's child windows top first as `Frame1, Picture1, Text1, Command1,
  Option1`, and after an `Add`, the new button is last of them: under every control already there, where a loaded
  array element goes too (#1497). Inside `Picture1`, alone, it is the only child.
- **In the container's scale.** With the form's `ScaleMode` at 3, a new button is 80 × 32, the same 1200 × 480 twips.
- **Not an array element.** `Index` is 343.
- `TypeOf c Is CommandButton` and `TypeOf c Is Control` are True, and `c.Parent` is the form.
- `Me!a2`, `Controls!a2` and `Controls("A2")` all reach it, and `Controls("A2") Is c`.

### The container, Add's third argument

| Container | Result |
|---|---|
| omitted, or `Me` | the form |
| `Picture1`, `Frame1` | that container, at 0, 0 within it |
| a Frame or a PictureBox added at run time | that container |
| `Text1` | 721, `'TextBox' is not a valid control container` |
| `Option1(0)` | 721, `'OptionButton' is not a valid control container` |
| `Nothing` | 721, `'<Nothing>' is not a valid control container` |
| `"Picture1"`, a String | -2147352571, `Type mismatch` |

### What Add refuses

| Call | Result |
|---|---|
| a name already on the form, in any case: `Command1`, an added `a0`, `A0` | 727, `There is already a control with the name 'Command1'` |
| a control array's name, `Option1` | 727 |
| `""` | 728, `'' is not a legal control name` |
| `"1abc"`, `"a b"`, `"a.b"`, 40 and 41 characters, `"Next"`, `"Form1"`, the name of a `WithEvents` variable | accepted |
| `"vb.commandbutton"` | accepted |
| `"CommandButton"`, `"VB.Nope"`, `""` | 711, `Invalid class string.`, a line feed alone, then `Looking for object with ProgID: CommandButton` |
| `123`, a number | 711, the ProgID `123` |
| `Null`, as the class or the name | -2147352571, `Type mismatch` |
| `123` as the name | accepted, as `"123"` |
| `Empty` as the name | 728 |
| `"MSComctlLib.ProgressBar"` and `.ProgressBar.2`, with no `Object=` in the project | 711 |
| `"VB.Form"`, `"VB.MDIForm"`, `"VB.Menu"`, `"VB.UserControl"` | 720, `'Form' is not a valid control type` |
| `"Scripting.Dictionary"` | 720, `'Scripting.Dictionary' is not a valid control type` |
| no name, or four arguments | compiles, and is -2147352562, `Wrong number of arguments or invalid property assignment`, at run time |

**Which error wins.** The name is checked first, then the class, then the container: `Add("VB.Nope", "Command1")` and
`Add("VB.Form", "Command1")` are 727, `Add("", "")` is 728, `Add("VB.CommandButton", "Command1", Text1)` is 727, and
`Add("VB.Nope", "x1", Text1)` is 711.

The arity errors come back as the raw HRESULT, not as 450, because `Controls` is reached late-bound. The statement form
`Controls.Add "VB.Label", "lblStmt"` adds the control. `Controls.Add("VB.Label", "lblParen")` as a statement is a
syntax error.

### What Remove takes

| Call | Result |
|---|---|
| an added control's name, in any case, or the control itself | removes it |
| a position, `Controls.Count - 1` with an added control there | removes it |
| a position holding a design-time control, `0` or `0.6` | 729, `Controls.Remove can only remove controls added with Controls.Add` |
| `Command1`, or `Option1(0)` | 729 |
| `"Option1"`, a control array's name | -2147467262, `System Error &H80004002 (-2147467262). No such interface supported` |
| a name not there, or one already removed | 730, `Control 'nope' not found` |
| `""` | 728 |
| `999`, `-1` | 9, `Subscript out of range` |
| `Null` | -2147352571, `Type mismatch` |
| `"1abc"`, the name of a control added as `1abc` | -2147352571, `Type mismatch`: a String starting with a digit is a position, as `Controls(i)` reads one, so that control cannot be removed by name |
| no argument, or two | compiles, and is -2147352562 at run time |

- **A container goes with what it holds.** Removing an added Frame holding an added button and label takes all three
  out, and the button's name is then 730.
- **A reference outlives it.** A variable still holding a removed control keeps it: `TypeName` is still `Label`, and
  every property, read or written, is 735, `The control is no longer usable because it has been removed from the controls
  collection`. A `WithEvents` variable holding it is not `Nothing` either.
- **From its own event.** `Controls.Remove "cmdSelf"` inside `cmdSelf`'s Click succeeds, with no error.
- **A For Each still reaches it.** Removing a control ahead of the loop does not take it out of the loop: it is handed
  out, and using it is 735. A control added during the loop is not reached. This differs from a control array element
  unloaded during a For Each, which the loop passes over (#1060).
- **Unload drops them.** After `Unload Form1`, the next reference reloads the form with its five design-time controls,
  and an added name is 730.

### Events

**Only `WithEvents` hears an added control.** A form's `a0_Click` does not run for a control added as `a0`: the
handlers a form has by a control's name are its design-time controls'. A variable declared `Private WithEvents b As
VB.CommandButton` and set to the added control hears it.

| Held by | `Value = True` runs |
|---|---|
| an `Object` variable only | nothing |
| `b` | `b_Click` |
| `b` and then `b2`, both | `b2_Click`, then `b_Click`: the one set last first |
| `b2`, after `Set b = Nothing` | `b2_Click` |
| `b`, holding the design-time `Command1` | `Command1_Click`, then `b_Click` |

`Set o = Option1(0)`, a control array element into `Private WithEvents o As VB.OptionButton`, is 459, `Object or class
does not support the set of events`, and `Option1(0)`'s click then runs `Option1_Click` alone.


`Private WithEvents b As VB.CommandButton` and `Set b = Controls.Add("VB.CommandButton", "cmdEv")`: `b.Value = True`
raises `b_Click`. A TextBox added the same way raises `tb_Change` on `tb.Text = "changed"`.

`Dim x As VBControlExtender` and `Set x = Controls.Add("VB.CommandButton", "cmdX")` is 13, and `x` stays `Nothing`, but
the control is added all the same: the count goes up and `Controls("cmdX")` finds it. The extender is for an ActiveX
control, and an intrinsic one is not.

### Not measured

- Whether a new control is shown on a form that is shown. Its own `WS_VISIBLE` bit says not; the screen was not looked
  at, because session 0 has none.
- Where a windowless control (a `Label`, `Shape`, `Line` or `Image`) goes among the windowless ones: `GetWindow` cannot
  see them.
- The ListBox's height under a font not in the table.
- `Add` of an ActiveX control the project does reference, and a licensed one: the probe project had no `Object=`.
- A `Data` control's `Container`, which was an error for the one added and is unexplained.
