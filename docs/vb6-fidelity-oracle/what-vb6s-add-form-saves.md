## What VB6's Add Form saves (2026-10-23, #1532, design-time, IDE, release build)

**The question.** A form HexIDE created started with an empty `Private Sub Form_Load()` / `End Sub`, written to the
file at its first save. #1369 had already matched the rest of the file to VB6's template `Form1.frm`, which has no
code, but nobody had looked at what VB6's own Add Form writes.

**The probe.** VB6.EXE 6.00.8176, the IDE, in the guest's console session, driven with SendKeys and a screenshot at
each step. `probe.vbp` is a Standard EXE holding `Module1.bas` alone (`Sub Main`). Opened in the IDE, then Project >
Add Form, which showed the Add Form dialog on its New tab with Form selected; Enter. Then Ctrl+S, which showed Save File
As with `Form1` in the project's folder; Enter. VB6 was then stopped, without saving the project, and the file read
back.

**What it wrote**, 485 bytes, every line CRLF:

```
VERSION 5.00
Begin VB.Form Form1 
   Caption         =   "Form1"
   ClientHeight    =   3015
   ClientLeft      =   120
   ClientTop       =   465
   ClientWidth     =   4560
   LinkTopic       =   "Form1"
   ScaleHeight     =   3015
   ScaleWidth      =   4560
   StartUpPosition =   3  'Windows Default
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
```

The last bytes are `... 46 61 6C 73 65 0D 0A`: the file ends at `Attribute VB_Exposed = False` and its line break.

**What it shows:**
- **A new form has no code.** VB6 adds an event procedure when the developer asks for one, by double-clicking the form
  or picking it in the Procedure box. HexIDE's seed `Form_Load` was its own invention.
- **The designer block differs from HexIDE's in more than the code.** VB6's toolbar read the form as 4800 × 3600
  outside. Its client area, 4560 × 3015, and its `ClientLeft` 120 and `ClientTop` 465, are that outer size less the
  borders and caption of this guest's theme, so those numbers are this machine's, not constants. `LinkTopic = "Form1"`
  is written too. HexIDE's new form has a client area of 6000 × 4500 and writes neither `ClientLeft`, `ClientTop` nor
  `LinkTopic`. That is #1538.

**A double-click on the form.** With the new form gone empty, a double-click is how it gets its `Form_Load`, so that
was measured too. Same rig: the project opened, Ctrl+R, the form, Shift+F7 for its designer, a real double-click of the
mouse inside it, then Ctrl+S at once, with nothing typed. Two forms, each the file above plus a code section:

| Form's code before | What follows `Attribute VB_Exposed = False` after the save |
|---|---|
| none | `Private Sub Form_Load()` CRLF, CRLF, `End Sub` CRLF |
| `Option Explicit`, a blank line, `Private Sub Zeta()` … `End Sub` | that code, then CRLF, `Private Sub Form_Load()` CRLF, CRLF, `End Sub` CRLF |

- **The procedure is `Private`, CRLF, with one blank line inside**, and the caret on that line (the code window read Ln 8,
  Col 1 in the second row).
- **It goes at the end**, one blank line below the code, not sorted among the procedures. Below a header with no code,
  there is no blank line.
- **An empty procedure is kept.** Saved at once, with nothing typed, it is written.

HexIDE's double-click wrote `Public Sub Form_Load()` in bare LF, after two bare line feeds, into a CRLF file. That had
been hidden, because a new form already had its seeded `Form_Load` to navigate to. An event picked in the Procedure box
was `Private` already, but ended at `End Sub` with no line break, its line breaks the host's, and its caret after `End Sub`.

**In HexIDE (#1532):** the `FormDefinition` constructor's seed is now empty, so a form HexIDE creates is written ending at
its `Attribute` block, as VB6's is. Both ways of adding an event procedure now write it as the table shows: `Private`, in
the file's line breaks, at the end, one blank line below any code, with the caret on the blank line inside.

**Add User Control and Add Property Page** (#1538). The same rig, with the Project menu's Add User Control (`u`) and
Add Property Page (`p`), Enter on the dialog's default, then Ctrl+S and Enter. Each file ends at
`Attribute VB_Exposed = False` and its CRLF, with no code:

| | UserControl1.ctl | PropertyPage1.pag |
|---|---|---|
| `Caption` | none | `"PropertyPage1"` |
| `ClientHeight` × `ClientWidth` | 3600 × 4800 | 3600 × 4800 |
| `ClientLeft`, `ClientTop` | 0, 0 | 0, 0 |
| other properties | none | `PaletteMode = 0 'Halftone` |
| `VB_Creatable` | **True** | False |
| `VB_PredeclaredId` | **False** | False |
| `VB_GlobalNameSpace`, `VB_Exposed` | False, False | False, False |

- **Neither has a border**, so its client area is VB6's 4800 × 3600 window itself, at 0, 0. These are constants, where a
  form's are its machine's.
- **A UserControl has no `Caption`.** HexIDE's had one, from the form constructor it shared.
- **The `Attribute` block is not a form's.** A UserControl is creatable and neither has a default instance. HexIDE wrote
  `VB_Name` alone for both.
- Not measured: an ActiveX Control project, where `VB_Exposed` may be True.

**In HexIDE (#1538):** `FormDefinition.NewForm` takes VB6's 4560 × 3015 client area at 120, 465, and its `LinkTopic`, which
HexIDE does not model and keeps as a loaded form's raw line. `ModuleDefinition.NewDesigner` makes a new UserControl or
PropertyPage as the table shows, its `PaletteMode` kept the same way. Each new file is now VB6's byte for byte.

