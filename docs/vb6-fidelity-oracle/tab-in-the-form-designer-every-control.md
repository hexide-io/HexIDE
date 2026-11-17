## Tab in the form designer: every control, in TabIndex order, and from a multiple selection (2026-11-16, #4211, design-time, IDE, SP6)

**The question.** #3399's rows, on oracle PR #4158's IDE page, had three top-level controls that were all visible,
enabled tab stops. #4211 asked what the designer's Tab does with the rest: TabStop False, Visible False, Enabled False,
a Frame's children, a Label, and two controls selected.

Measured on vb6.exe SP6's IDE, in the oracle guest's console session. The designer had the focus after a click on an empty part of the form. Each Tab was a real key. The selection was read after each one from the Properties window's object box, through Win32 (`CB_GETLBTEXT`).

### The first form: every case in one TabIndex run

| TabIndex | Control | What's special |
|---|---|---|
| 0 | Command1 | |
| 1 | Text1 | `TabStop = False` |
| 2 | Check1 | `Visible = False` |
| 3 | Option1 | `Enabled = False` |
| 4 | Frame1 | holds Text2 (5) and Command2 (6) |
| 7 | Label1 | |
| 8 | Command3 | |

**Tab from the form:** Command1, Text1, Check1, Option1, Frame1, Text2, Command2, Label1, Command3, then Command1 again.

**The rule:** every control is a stop, and nothing is skipped.
- That includes TabStop False (1), Visible False (2), Enabled False (3), the Frame and its children (4 to 6), and the Label (7).
- It wraps, and the form isn't in the cycle, as #3399's rows had it.

### The second form: containment against TabIndex

The Frame's children were given TabIndexes that don't follow it:

| TabIndex | Control |
|---|---|
| 0 | Command1 |
| 1 | **Text2, inside Frame1** |
| 2 | Text1 |
| 3 | Frame1 |
| 4 | Command3 |
| 5 | **Command2, inside Frame1** |
| 6 | Label1 |
| 7 | Check1, `Visible = False` |
| 8 | Option1, `Enabled = False` |

**Tab from the form:** Command1, **Text2**, Text1, Frame1, Command3, **Command2**, Label1, Check1, Option1, then Command1 again.

**The rule: pure TabIndex order across the whole form.** A control inside a Frame is visited at its own TabIndex, before its Frame if its number is lower, and not grouped with the Frame. The first form couldn't tell these two rules apart; this one does.

### Tab with two controls selected

Two selections were made, each by a click on one control, then Ctrl+click on another. The object box goes blank while two are selected.

| Selected, primary last | Tab selects |
|---|---|
| Command1 (0), then Command3 (8) | Command1 alone: Command3's next, wrapping |
| Command3 (8), then **Text1 (1)** | **Check1** alone (2): Text1's next |

**The rule:** Tab drops the multiple selection and selects the control after the **primary**, the one added last, drawn with solid handles. The second row is the one that tells this apart from "start again at TabIndex 0".

**What this overturned.** HexIDE's first implementation (#3399, PR #4212) kept TabIndex order to forms whose controls
were all top-level, visible, enabled tab stops, guessing the designer would skip the rest. It skips none of them.
