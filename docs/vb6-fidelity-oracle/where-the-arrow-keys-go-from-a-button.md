## Where the arrow keys go from a button (2026-10-23, #1625)

**The question.** "Tab and the arrow keys among option buttons" found that an arrow on an option button moves the focus to
the previous or next control in its container, by TabIndex, wrapping. It left open:
- an arrow onto a control whose TabStop is False, or one that's disabled;
- what an arrow passes over when the next control is a Label or another container;
- the arrow keys on a CheckBox and a CommandButton;
- whether "by TabIndex" was TabIndex at all, since in that probe the TabIndex order and the order of the controls in the
  file were the same.

**The probes.** Two compiled forms, native and p-code, run on the oracle guest's console session through the agent, as
#1583's were. A Timer took one step each 300 ms, by `SetFocus` or by `SendKeys`, and logged `Screen.ActiveControl`, the
options selected and the check boxes' Values after each.

The first form had a Frame for each case, with its controls in TabIndex order:

| Frame | Controls |
|---|---|
| A | optA1, optA2 (selected), txtA1 with `TabStop = False`, txtA2 |
| B | optB1, optB2 (selected), txtB1 with `Enabled = False`, txtB2 |
| C | optC1, optC2 (selected), lblC, a Label, txtC2 |
| D | txtD1, optD1, optD2 (selected) |
| E | optE1, optE2 (selected), picE, a PictureBox, txtE2 |
| F | chkF1, chkF2, two CheckBoxes, txtF |
| G | optG1, optG2 (selected), FrameG2 holding txtG, txtG2 |
| H | cmdH1, cmdH2, two CommandButtons, txtH |

The second form's one Frame held, in the file's order, optI1 at TabIndex 0, txtI at TabIndex 2, and optI2 at TabIndex 1.
By TabIndex, Down from optI1 reaches optI2; by the file's order, txtI. Two steps then moved txtI, and later optI2, to the
front with `ZOrder 0`, which changes the order the controls are drawn in and not their TabIndex.

Native and p-code logged the same lines, line for line: 64 for the first form and 22 for the second.

| From | Key | Focus went to | Selected after |
|---|---|---|---|
| optA2 | Down | **txtA1**, whose TabStop is False | optA2 |
| optA2 | Up | optA1 | **optA1** |
| optA1 | Up | **txtA2**: it wraps to the last | optA1 |
| optB2 | Down | **txtB2**, past the disabled txtB1 | optB2 |
| optC2 | Down | **txtC2**, past the Label | optC2 |
| optD2 | Down | **txtD1**: it wraps to the first | optD2 |
| optD1, by `SetFocus` | Up | txtD1 | optD1 |
| optE2 | Down | **picE** | optE2 |
| picE | Down | picE: the PictureBox keeps the key | optE2 |
| chkF1 | Down | **chkF2** | both Values 0 |
| chkF2 | Down | txtF | both Values 0 |
| chkF2 | Up | chkF1 | both Values 0 |
| optG2 | Down | **txtG2**, past FrameG2 and not into it | optG2 |
| optG1, by `SetFocus` | Up | txtG2: it wraps to the last | optG1 |
| cmdH1 | Down | **cmdH2** | |
| cmdH2 | Right | txtH | |
| optA2, by `SetFocus` | Right | txtA1, as Down | optA2 |
| optI1 | Down | **optI2**, TabIndex 1, not txtI, which is next in the file | **optI2** |
| optI1 | Up | **txtI**, TabIndex 2: it wraps to the last | optI1 |
| optI1, after `txtI.ZOrder 0` | Down | optI2 | optI2 |
| optI1, after `optI2.ZOrder 0` | Down | optI2 | optI2 |

**What it shows:**
- **An arrow moves by TabIndex**, not by the order of the controls in the file or on screen. `ZOrder` changed nothing.
- **TabStop doesn't matter to an arrow.** Down reached a TextBox whose TabStop is False, which the Tab key passes over.
- **A control that can't take the focus is passed over:** a disabled TextBox, a Label, and a Frame, whose controls are not
  reached either. A PictureBox can take the focus, and does. Then it keeps the arrow keys, as a TextBox does.
- **A CheckBox and a CommandButton move the same way.** An arrow changes no check box's Value.
- **It wraps both ways** within the container, to a control that isn't a button too: Down from optD2 reached txtD1.
- **`SetFocus` on an option selects it**, as "Tab and the arrow keys among option buttons" found. optD1 and optG1 were
  selected by the step that focused them.

**Not measured:**
- an arrow with Shift, Ctrl or Alt held;
- an invisible control as the next one;
- an arrow onto a ListBox, a ComboBox or a scroll bar, though from one of them the arrow keys are its own;
- an arrow on a control in a UserControl, or on the UserControl itself.

### In HexIDE (#1625)

- **The arrow keys on an option button, a check box and a command button follow this:**
  - Up and Left go to the previous control in the container, and Down and Right to the next, by TabIndex, wrapping;
  - whatever its TabStop;
  - past controls that can't take the focus: disabled ones, Labels, Frames, and anything hidden;
  - not into a nested container.
- **An option it lands on is selected** through the focus, with Click after GotFocus, as #1583 has it. A check box isn't
  changed.
- **Not followed: an arrow with a modifier held**, which isn't measured. It does nothing.

