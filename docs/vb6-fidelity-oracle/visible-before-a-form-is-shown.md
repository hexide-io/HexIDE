## Visible before a form is shown, by kind of control (2026-11-19, #4829)

**Asked because** #4829 measured that a CommandButton's `Visible` reads False while its form is loaded but not shown,
and again after `Hide`, though its own `WS_VISIBLE` bit stays on, while a Line reads True throughout. That fits "a
control with a window follows its form, and one without keeps its own value". It fits other rules too, and HexIDE had
two: one that left only the Line, Label, Image and Shape their own value, and one that read False for every control.
These rows were chosen to tell those apart: the other lightweight controls, a menu, and a UserControl both with a window
and without.

**The probe.** A `Sub Main` loads FormV and reads each control's `Visible`, then `FormV.Show` and `DoEvents`, then
`FormV.Hide` and `DoEvents`, then `Show` again, reading after each. `Form_Load` reads them as well. FormV holds:
- `Command1`, a CommandButton;
- `Label1`, and `Label2`, which has `Visible = False` in the .frm;
- `Image1` with no picture, `Shape1` and `LineP`, a Line;
- `mnuGame`, a top-level menu with `mnuNew` under it;
- two of the project's own UserControls, each empty: `UCW1`, whose UserControl is `Windowless = True`, and `UCN1`,
  whose isn't.

It ran on the Win10 guest, VB6 SP6, as a p-code EXE, a native EXE and in the IDE with `/runexit` on the console, and as
the p-code EXE in session 0.

**On the console**, the three agreeing on every row:

| Control | `Form_Load` | loaded | shown | hidden | shown again |
|---|---|---|---|---|---|
| `Command1` | False | False | True | False | True |
| `UCN1`, a UserControl with a window | False | False | True | False | True |
| `UCW1`, a windowless UserControl | **False** | **False** | True | **False** | True |
| `Label1`, `Image1`, `Shape1`, `LineP` | True | True | True | True | True |
| `mnuGame`, and `mnuNew` under it | True | True | True | True | True |
| `Label2`, False in the .frm | False | False | False | False | False |
| the form | False | False | True | False | True |

**In session 0** the form is never shown, so `Command1`, `UCN1` and `UCW1` read False in every column, the two shown
ones included. Every other row is as on the console.

**So the rule is not "has a window".** A Label, Image, Shape and Line, and a menu at either level, read their own value
whether or not the form is shown. A CommandButton and a UserControl read False until the form is shown and again after
`Hide`. That includes **a windowless UserControl, which has no window either**. The obvious guess from #4829's first
rows would have put it with the Label. It goes with the CommandButton.

**Not measured:**
- a control inside a hidden Frame or PictureBox on a shown form;
- `Visible` read straight after `Visible = True` on a control of a hidden form;
- other OCX controls than the ProgressBar, which #4829 measured following its form as the CommandButton does.

**HexIDE.** #4846 makes `Visible` follow these rows. Label, Image, Shape, Line and menus keep their own value, and any
other control reads False while its form isn't shown. Before it, main read False for every control there (since batch
389, from #4520's measured TextBox).
