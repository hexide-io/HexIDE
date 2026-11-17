## A name in another case in a .vbp or a .frm (2026-11-17, #823, #824, SP6)

**The question.** #819 measured that a `.frm`'s property names are matched without regard to case. Two other names in
VB6's files were not measured:
- **#823:** a `.vbp`'s `Startup=`, when it names the startup form in another case than the form's own;
- **#824:** a control's type name on its `Begin` line, as in `Begin vb.label Label1`.

For each, does VB6 accept the name, and what does it write back?

**Which VB6.** SP6 (`VB6.EXE` 6.00.9782) in the oracle guest. Every project was built as p-code (`CompilationType=-1`).

### `Startup=` in another case (#823)

**The probe.** Three Standard EXE projects, each with one form, `Form1`. The form's `Begin VB.Form Form1` and
`Attribute VB_Name = "Form1"` spell its name the usual way. Its `Form_Load` writes `"loaded " & Me.Name` to a file, then
runs `End`. The projects differ only in their `Startup=` line.

Each `.vbp` also set `AutoIncrementVer=1`, with `RevisionVer=0`. With that set, `vb6.exe /make` writes the project file
back after a build to save the new revision number, which shows what VB6 writes for `Startup=` with no IDE needed.

| `Startup=` in the file | `/make` | The program wrote | `Startup=` VB6 wrote back |
|---|---|---|---|
| `"Form1"` (control) | `Build of 's823a.exe' succeeded.` | `loaded Form1` | `"Form1"` |
| `"form1"` | succeeded | `loaded Form1` | **`"Form1"`** |
| `"FORM1"` | succeeded | `loaded Form1` | **`"Form1"`** |

**What it shows:**
- **VB6 finds the startup form without regard to case,** and the built program starts it.
- **It writes the form's own spelling back,** not the file's.

**Seen in passing:** each rewritten `.vbp` gained a line, `IconForm="Form1"`, above `Startup=`, which the probe's file
didn't have. `RevisionVer` went from 0 to 1. Nothing else changed.

**Not measured:**
- **Save Project from the IDE.** The write-back here is `/make`'s, from the same `vb6.exe`; the IDE's own save
  wasn't driven.
- **A `Startup=` that names no form in any case.** That's "When a document gets a file, and what a dangling
  `Startup=` does" (#489).

### A control's type name in another case (#824)

**The probe.** Five Standard EXE projects, each with one form holding one Label, `Label1`, with Caption `L1` and Left
120. The projects differ only in how the Label's `Begin` line spells its type. `Form_Load` runs under
`On Error Resume Next` and writes `TypeName(Label1) & "|" & Label1.Caption & "|" & Label1.Left & "|" & Err.Number`.

| `Begin` line | `/make` | The program wrote |
|---|---|---|
| `Begin VB.Label Label1` (control) | succeeded | `Label\|L1\|120\|0` |
| `Begin vb.label Label1` | succeeded | `Label\|L1\|120\|0` |
| `Begin VB.LABEL Label1` | succeeded | `Label\|L1\|120\|0` |
| `Begin vb.Label Label1` | succeeded | `Label\|L1\|120\|0` |
| `Begin VB.label Label1` | succeeded | `Label\|L1\|120\|0` |

None of the builds wrote a load log beside the form.

**What VB6's IDE writes back.** The `vb.label` and `VB.LABEL` projects were each opened in SP6's IDE on the guest's
console. Each opened with no message. Then:
1. select `Form1` in the Project window, then F7;
2. Ctrl+End and Enter, type `Dim zz` and Enter;
3. Ctrl+S.

| `Begin` line in the file | After VB6's save |
|---|---|
| `Begin vb.label Label1` | **`Begin VB.Label Label1`** |
| `Begin VB.LABEL Label1` | **`Begin VB.Label Label1`** |

Apart from that line and the added `Dim zz`, both files were byte-identical to what went in.

**What it shows:**
- **VB6 finds a control's class without regard to case,** in the library part and the class part alike.
- **Its save writes the class's own spelling,** `VB.Label`.

**Not measured:**
- **A control from an OCX,** such as `Begin mscomctllib.progressbar`.
- **The form's own `Begin` line,** as in `Begin vb.form Form1`.

**A trap in the probe, recorded so the next one avoids it.** The console was driven through Hyper-V's synthetic
keyboard, which sends virtual keys. The guest's layout is English (UK), so the US virtual key for `\` typed `#`, and a
path typed into the Run dialog went wrong. Forward slashes don't open a `.vbp` from Run either. A one-line launcher on the
guest user's `PATH` took the project's name, which needs no backslash. The code typed into VB6 was kept to letters and
spaces for the same reason.
