## A form whose two names disagree (2026-10-22, #473)

**The question.** A `.frm` names its form twice: on the designer block's `Begin` line and in `Attribute VB_Name`. VB6
writes the two equal. HexIDE renamed only the first until #1059 fixed #473, so files saved before that can disagree.
Which name does VB6 honour? #473 recorded that for a module VB6 binds on `VB_Name` (see the `Folder1\Module1.bas`
probes), and nobody had asked for a form.

**The probe.** A Standard EXE starting from `Sub Main`, built with `vb6.exe /make` as p-code (6.00.8176, the original release). Its form
was `Begin VB.Form frmRenamed` with `Attribute VB_Name = "Form1"`, in `Form1.frm`, listed as `Form=Form1.frm`. The
module had no `Option Explicit`, so whichever name was not the form compiled as an implicit Variant. `Sub Main`
printed `TypeName` of each name, each `.Name`, `Forms(0).Name`, and the `Me.Name` that `Form_Load` saw.

**The `Begin` line wins, and VB6 says so.**
- The build succeeds and exits 0. The `/out` log reads `Conflicting names were found in '…\Form1.frm'.  The name
  'frmRenamed' will be used.`, then that the build succeeded.
- At run time:
  - `TypeName(frmRenamed)` is `frmRenamed`, its `.Name` is `frmRenamed`, and `Me.Name` in `Form_Load` is `frmRenamed`;
  - `Forms(0).Name` is `frmRenamed`;
  - `TypeName(Form1)` is `Empty`: it is an implicit variable.
- The VB6 IDE, opening the project, shows the same message in a dialog with OK and Help. Its Project Explorer lists
  `frmRenamed (Form1.frm)`.
- After OK, a one-line code change, and Ctrl+S, the IDE wrote `Attribute VB_Name = "frmRenamed"`. It repaired the
  attribute to the `Begin` line's name. `LinkTopic = "Form1"` was left as it was.

So for a form, the opposite of a module: the designer block's name is the form's name, and `VB_Name` follows it.

**Case alone is a disagreement.** With `Begin VB.Form frmRenamed` and `Attribute VB_Name = "FRMRENAMED"`, `/make`
logs the same warning, "The name 'frmRenamed' will be used.", and builds.

**A UserControl and a PropertyPage follow the same rule.** The probe was an ActiveX control project:
- `UserControl=Gauge.ctl` holding `Begin VB.UserControl ucBegin` and `Attribute VB_Name = "Gauge"`;
- `PropertyPage=Page1.pag` holding `Begin VB.PropertyPage ppBegin` and `Attribute VB_Name = "Page1"`.

`/make` logs `Conflicting names were found in '…\Gauge.ctl'.  The name 'ucBegin' will be used.`, and the same for the
page naming `ppBegin`, and builds the OCX. Which name a form must use for such a control is measured below.

HexIDE takes a form's name from the `Begin` line too, so a program runs the same. It neither warns nor repairs on
load, and that is #1407. A UserControl or PropertyPage it names after its file instead, which is #1411.

**Which name a form uses for a UserControl (#1411).** The probes were five Standard EXEs called `probe`, each with a
private UserControl and a form holding one instance of it, `Begin probe.<name> uc1`, built with `/make` as p-code:

| File | `Begin` | `VB_Name` | The form says | `/make` |
|---|---|---|---|---|
| `Gauge.ctl` | `ctlDial` | `ctlDial` | `probe.ctlDial` | builds |
| `Gauge.ctl` | `ctlDial` | `ctlDial` | `probe.Gauge` | fails |
| `Dial.ctl` | `ucBegin` | `Gauge` | `probe.ucBegin` | builds, after the conflicting-names warning |
| `Dial.ctl` | `ucBegin` | `Gauge` | `probe.Gauge` | fails |
| `Dial.ctl` | `ucBegin` | `Gauge` | `probe.Dial` | fails |

Each failure logs `Line 11: Class probe.<name> of control uc1 was not a loaded control class.` in `Form1.log`, and the
form "could not be loaded". So the control's class is its `Begin` line's name and nothing else: not its file's, and
not its `VB_Name`, even when the two agree with each other.

**The IDE repairs a UserControl as it repairs a form.** It opened the third project with the dialog "Conflicting names
were found in '…\Dial.ctl'.  The name 'ucBegin' will be used." After a one-line change to the control's code, Ctrl+S
wrote `Attribute VB_Name = "ucBegin"` into `Dial.ctl`, which kept its file name. The project file still read
`UserControl=Dial.ctl`.

