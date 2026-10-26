## What ZOrder does at run time (2026-10-23, #1503, p-code, IDE, release build)

**The question.** Where `ZOrder` moves a control, and whether it can move one across the windowless plane that "How a
running form stacks its controls" measured.

**The probe.** F5 in VB6's IDE, so this is p-code, on a form whose pairs overlap, each written first-named first.
`Form_Load` made the calls below, and a screenshot of the running form gave the result. A lone PictureBox then took
the out-of-range positions under `On Error Resume Next`, and `Err.Number` after each went to a file.

| Pair, in file order | Call | Result |
|---|---|---|
| LabelA (red), CmdA | `LabelA.ZOrder 0` | CmdA still over LabelA |
| PicR (red), PicB (blue) | `PicB.ZOrder` | blue over red |
| PicG (green), PicY (yellow) | `PicG.ZOrder 1` | yellow over green |
| PicC (cyan), Frame1 holding PicIn (magenta) | `Frame1.ZOrder 0` | Frame1 and PicIn over cyan |
| LabelE (yellow), ShapeE (green) | `ShapeE.ZOrder 0` | green over yellow |
| LineF (blue), LabelF (yellow) | `LineF.ZOrder 1` | the Label over the Line |
| PicK (orange), LabelK (yellow) | `PicK.ZOrder 1` | orange still over the Label |
| CmdArr(0) "Zero", CmdArr(1) "One" | `CmdArr(1).ZOrder 0` | One over Zero |

| Position | Err.Number |
|---|---|
| 2, -1 | 5 |
| 0.6, "1", -0.5, Empty | 0 |
| 1.5 | 5 |
| "x" | 13 |
| 32768 | 6 |
| Null | 13 |

**What it shows:**
- **`ZOrder` stays within its plane.** `ZOrder 0` (or no argument) puts a control over every control of its plane in
  its container, and `ZOrder 1` under them. A Label brought to the front stays under a windowed control, and a
  windowed control sent to the back stays over a Label.
- **A container takes its contents with it, and an array element moves alone.**
- **The position is an Integer, 0 or 1.** A half goes to the even number: 1.5 is 2, so 5, and -0.5 is 0.
  32768 overflows (6), and a string that is no number is 13. **Null is 13**, where `CInt(Null)` is 94.

**In HexIDE (#1503):** all of the above. A z-index change alone did not redraw the container: the control brought
forward stayed under the other in the next frame. So a restack now invalidates the container.

**Not measured:**
- `ZOrder` on a form.
- `ZOrder` on a Timer.
- `ZOrder` with two arguments.
- `ZOrder` on a control of a form that is loaded but not shown. `Form_Load` runs before the form is shown, and the
  calls there took effect.
- Native code.

