## The TabIndex a new or pasted control takes (2026-10-22, #925, design-time)

**The question.** HexIDE gave a new control the highest TabIndex of every component plus one, the Form's and a
Timer's included, and a paste kept its source's. VB6's own files show what it saves (every Label, Frame and focusable
control numbered densely from 0; no Timer, Image, Line or Shape numbered), but not what its designer hands out as a form
is built, nor what a paste takes.

**The probe.** A form holding Label1 (0), Command1 (1), Frame1 (2) with Check1 inside (3), and Timer1, opened in VB6's
IDE through the console agent. These steps were taken in order: double-click the CommandButton tool, the Timer tool,
then the CommandButton tool again; copy Command1 and paste it, answering No to "create a control array?"; copy Frame1
and paste it, answering No twice, once for the Frame and once for its CheckBox; then File > Save Form1.frm. The values
below are VB6's saved file.

| Control | How it arrived | TabIndex |
|---|---|---|
| Command2 | toolbox double-click | 4 |
| Timer2 | toolbox double-click | none |
| Command3 | toolbox double-click, after the Timer | 5 |
| Command4 | paste of Command1 (TabIndex 1) | 6 |
| Frame2 | paste of Frame1 (TabIndex 2) | 7 |
| Check2 | inside the pasted Frame2, from Check1 (TabIndex 3) | 8 |

**What it shows:**
- **A new control takes the next number after the highest.** A Timer takes none and uses none up. On a form numbered
  densely, as this one was, "the next after the highest" and "the count of numbered controls" agree; a form with gaps
  was not tried, and VB6 renumbers those when it loads one (`docs/vb6-designer-geometry.md`).
- **A pasted control takes a new number, not its source's.** A pasted container takes one, and then what it holds
  takes the ones after.
- **Two things outside #925, recorded so they are not lost:**
  - VB6 pasted both copies at the form's top-left, `Left = 0`, `Top = 0`, not offset from the original as HexIDE
    pastes (#1480).
  - VB6 wrote every control added in the session ahead of the ones the file already held, the newest first: Frame2,
    Command4, Command3, Timer2, Command2, then Frame1 and the rest as read. HexIDE appends a new control's block last
    (#1481).

