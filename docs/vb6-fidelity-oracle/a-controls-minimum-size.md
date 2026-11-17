## A control's minimum size (2026-11-16, #4071)

**Asked because** #589's measurement found that `Command1.Width = 0` and `= 30` both read back 75, and `Height = 0` and
`= 100` both read back 195. That fits a constant floor, but it fits other rules too. These rows were chosen to tell the
candidates apart: values either side of the floor, a larger font, a TextBox, and pixel `ScaleMode`.

**How.** The same harness as #589's measurement, "A negative or zero control size" (PR #4069): `Load Form1` from
`Sub Main`, then each assignment followed by a read, under `On Error Resume Next`, written to a file. The form held
`Command1` (MS Sans Serif 8), `Command4` (MS Sans Serif 24, set in the `.frm`), `Label1` and `Text1`. Built with
`VB6.EXE /make` native and p-code, SP6 (6.00.9782, MSVBVM60 6.00.9848), the guest at 96 dpi:
`Screen.TwipsPerPixelX` and `Y` read 15. **Native and p-code agreed on all 81 rows of the two runs.**

### What came back (twips unless stated)

| Control | Assigned | Read back |
|---|---|---|
| `Command1`, 8 pt, `Width` | 0, 1, 7, 8, 15, 30, 45, 60, 66, **67** | **75** |
| | **68**, 69, 74, 75, 76, 80, 89, 90, 91, 100 | as assigned |
| `Command1`, 8 pt, `Height` | 0, 1, 15, 150, 180, **187** | **195** |
| | **188**, 189, 194, 195, 196, 200, 209, 210, 211 | as assigned |
| `Command4`, 24 pt, `Width` | 0, 1, 210, 225, **232** | **240** |
| | **233**, 239 | as assigned |
| `Command4`, 24 pt, `Height` | 0, 1, 540, **547** | **555** |
| | **548**, 554 | as assigned |
| `Label1`, `Width` | 0, 1, **7** | **15** |
| | **8**, 14, 15, 16, 20 | as assigned |
| `Text1`, 8 pt | `Width = 0`, `Height = 0` | 150, 285 |
| `Command1` after `FontSize = 24`, then `Width = 0` | | 240 × **555**: its `Height`, 495 before, rose too |
| the same, with `Caption = ""`, then `Width = 0` | | 240 |
| `Form1.ScaleMode = 3` (pixels), 8 pt `Command1` | `Width = 0`, `Height = 0` | 5, 13 (pixels) |
| | `Width = 5.5` | 5.466667, which is 82 twips |
| | `Label1.Width = 0` | 1 |
| | `Command1.Width = -1` | Err 380 |
| `Form1.ScaleMode = 3`, `Command1` at 24 pt | `Width = 0`, `Height = 0`, `Width = 5.5` | 16, 37, 16 |

### The rule these rows support

**The size is rounded to whole pixels, half up. If the rounded size is below the control's minimum, the minimum is
stored. Otherwise the value assigned is kept exactly, without snapping.**

Every row above fits it:
- 67 twips is 4.47 px, which rounds to 4, below 5, so 75 is stored. 68 is 4.53 px, rounds to 5, and stays 68.
- Height 187 is 12.47 px against a minimum of 13; 188 is 12.53.
- At 24 pt, 232 and 233 straddle 15.5 px against 16; 547 and 548 straddle 36.5 against 37.
- The Label's 7 and 8 straddle half a pixel against 1.

**The minimum follows the control's font, not its caption.** A CommandButton's is 5 × 13 px at 8 pt and 16 × 37 px at
24 pt, and the empty caption gave the same 240. A TextBox's is 10 × 19 px at 8 pt, and a Label's is 1 px.

**Changing either dimension enforces both.** After `FontSize = 24`, assigning `Width` also raised a `Height` of 495
(33 px) to 555 (37 px).

**This overturns a reading of #589's rows.** "Raised to a floor of 75 × 195" was true of the values asked, but a
constant floor would have turned 74 into 75, and VB6 keeps 74. The values to implement are the minimum in pixels for
the control's font, not 75 and 195.

**Not measured:**
- how the minimum is computed from the font, beyond the two sizes of MS Sans Serif;
- other controls' minimums;
- other dpi settings;
- whether a `.frm` value below the minimum is enforced when the form loads. #589's `Height = 0` read 0 after loading,
  and only an assignment raised it.

The probe script is the harness above with these statements; the tables are its whole output.
