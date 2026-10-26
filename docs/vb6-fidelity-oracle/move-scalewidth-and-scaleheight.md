## Move, ScaleWidth and ScaleHeight (2026-10-15, #1071)

**The question.** The Battleship demo's board lays out its grid with `Label.Move`, and fits the form to it with
`Width = (Width - ScaleWidth) + needed`. HexIDE had neither. What do they do?

**The probe.** VB6.EXE 6.00.8176 (the original release) in the oracle guest, native code, one compiled program. `Sub Main` loads a
form without showing it. The form has `BorderStyle = 1` (Fixed Single), `ClientWidth` and `ClientHeight` of 2520, and
one Label at Left 120, Top 120, 360 x 360. The program logs each step under `On Error Resume Next`:

| Statement | Logged |
|---|---|
| `Form1.ScaleWidth`, `Form1.ScaleHeight`, `TypeName(Form1.ScaleWidth)` | `2520 2520 Single` |
| `Form1.Width`, `Form1.Height` | `2610 2895` |
| `Label1.Move 100`, then Left, Top, Width and Height | `100 120 360 360` |
| `Label1.Move 200, 300` | `200 300 360 360` |
| `Label1.Move 400, 500, 600, 700` | `400 500 600 700` |
| `Label1.Move 450.5, 500, 600, 700` | `450 500 600 700` |
| `Label1.Move 400, 500, -10, 700`, then `Err.Number` | `5` |
| the Label afterwards | `450 500 600 700` |
| `Form1.Width = (Form1.Width - Form1.ScaleWidth) + 3000`, then `ScaleWidth` | `3000` |
| `Form1.Height = (Form1.Height - Form1.ScaleHeight) + 2000`, then `ScaleHeight` | `1995` |
| `Form1.Width`, `Form1.Height` afterwards | `3090 2375` |
| `Form1.ScaleMode = 3`, then `ScaleWidth`, `ScaleHeight` | `200 133` |

**What they show:**
- **Move sets what it is given:** Left alone, then Top, Width and Height in turn. A half twip rounds to even, as an
  assignment to `Left` does ("A control's position at run time").
- **Move checks before it moves.** A negative width is Err 5, Invalid procedure call or argument, and the Label kept
  Left 450, not the 400 the same Move gave first. So Move is not four assignments in a row. An assignment
  `Width = -10` would be a different question, and wasn't asked.
- **ScaleWidth and ScaleHeight are the client area, as a Single,** in the form's ScaleMode: 2520 twips, or 200 x 133
  pixels.
- **The client is whole pixels.** `Height` kept the 2375 assigned, but the client below it is 2375 - 375 = 2000
  twips, or 133.3 pixels. ScaleHeight reads 133 pixels, which is 1995 twips.
- **A form's Width and Height are its outer size:** 90 twips more than the client across, and 375 down, for a Fixed
  Single form without a menu.

**In HexIDE (#1071):**
- **Move on a control** does this, the check included. Move on a control with an omitted argument (`Move 1, , 3`) or
  with none is not measured, and stops the run. A form's Move was measured and built by #1569: [A form's
  Move](a-forms-move.md#a-forms-move-2026-10-26-1569).
- **ScaleWidth and ScaleHeight on a form** read the client area in whole pixels, in the form's ScaleMode. Setting them
  is #990. On a form with a menu bar they are the area below the bar, as in VB6 (#1127): the window is the form's
  ClientHeight plus VB6's 19-pixel bar tall, so the canvas gets the whole ClientHeight.
- **A form's Width and Height** read and set the client size, not the outer one: #1072. The fit-to-content idiom
  above still lands right, because here the difference between Width and ScaleWidth is 0.
- **#1072 then makes them the outer size**, with the themed desktop's frame: [A form's Width and Height: the frame round its
  client area](a-forms-width-and-height-the-frame-round-its-client-area.md#a-forms-width-and-height-the-frame-round-its-client-area-2026-10-23-1072). Session 0's frame, measured here, is
  smaller.

