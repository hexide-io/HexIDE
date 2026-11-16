## A negative or zero control size (2026-11-16, #589)

**Asked because** HexIDE's `add_control`, `move_control` and `set_control_property` accepted a negative `Width`, saved it
to the `.frm`, and the project then refused to run (#589). The rule they should follow had to come from VB6.

**How.** A Standard EXE with `Sub Main` in a module, and a form holding CommandButtons `Command1` to `Command3`, a Label
and a Line. `Sub Main` runs `Load Form1`, then each statement below under `On Error Resume Next`, writing `Err.Number`
and the value read back to a file. Built with `VB6.EXE /make`, once native (`CompilationType=0`) and once as p-code
(`CompilationType=-1`), and run. VB6.EXE 6.00.9782 (SP6), MSVBVM60 6.00.9848, the `Win10` guest at 96 dpi (15 twips a
pixel). **Native and p-code gave the same answer on all 42 rows.** The Properties window at design time wasn't measured.

### Loading a `.frm`

| Saved in the `.frm` | What `/make` did |
|---|---|
| `Width = -150` on a CommandButton and on a Label | Refused. `Form1.log`: `Line 34: Property Width in Command2 could not be set.` and the same for `Label1` at line 18. The build log: `Errors during load. Refer to '…\Form1.log' for details`, then `'…\Form1.frm' could not be loaded.` and `Build of 'p589.exe' failed.` |
| `Height = 0` on a CommandButton | Accepted. `Form1.Command3.Height` read **0** after `Load Form1`. |

### Assigning at run time

| Statement | `Err.Number` | Read back afterwards |
|---|---|---|
| `Command1.Width = -10` | **380** | 1215, unchanged |
| `Command1.Height = -10` | **380** | unchanged |
| `Label1.Width = -10` | **380** | unchanged |
| `Form1.Width = -10` | **380** | 4800, unchanged |
| `Command1.Width = 0` | none | **75** |
| `Command2.Width = 30` | none | **75** |
| `Command1.Height = 0` | none | **195** |
| `Command3.Height = 0` (it read 0, from the `.frm`) | none | **195** |
| `Command3.Height = 100` | none | **195** |
| `Label1.Width = 0`, `Label1.Height = 0` | none | **15**, **15** |
| `Command1.Move 0, 0, -10` | **5** | unchanged |
| `Command1.Move 0, 0, 1215, -10`, after `Form1.Show` | **5** | unchanged |
| `Command1.Move 0, 0, 0, 0`, after `Form1.Show` | none | 75 × 195 |
| `Command1.Left = -50` | none | -50 |
| `Line1.X2 = -10` | none | -10 |

The same answers came before and after `Form1.Show`, where both were asked.

### What this settles, and what it doesn't

- **A negative size assigned to a property is Err 380, Invalid property value, and leaves the size as it was.** That
  held for a CommandButton, a Label and the form itself.
- **`Move` with a negative size is Err 5, not 380,** and moves nothing. That matches what #1071 measured for a Label's
  Move ([Move, ScaleWidth and ScaleHeight](move-scalewidth-and-scaleheight.md)), which noted that `Width = -10` was a
  different question it hadn't asked. This page asks it: a different error number.
- **A `.frm` holding a negative Width fails `/make`.** A HexIDE that writes one has produced a project VB6 can't
  build. This bullet first said such a form "doesn't load at all", from `/make` alone. VB6's own IDE overturned
  that: it loads the form with the width reading 0, and runs it
  ([below](#the-same-frm-in-vb6s-ide-2026-11-16-4179)).
- **Zero and small sizes are accepted and then raised to a floor:** 75 wide and 195 high for a CommandButton, which
  are 5 and 13 pixels here, and 15 by 15 for a Label, one pixel. Whether the floors are VB6's own or Windows' minimum
  for the window, and whether they scale with dpi, wasn't measured. The Label is windowless, which suggests at least
  its pixel floor is VB6's.
- **Resisted explanation:** a CommandButton whose `.frm` says `Height = 0` reads 0 after loading. Assigning 0, or 100,
  at run time reads 195. So the floor applies to an assignment and not to the value the form was loaded with. The
  probe didn't read it again after `Show` before reassigning it, so whether showing the form raises it is unknown.
- **Not measured:** what the Properties window does with `-10` at design time. A Line's coordinates are positions,
  not sizes, and take negatives.

**The probe's form,** for repeating it. `Command1` was 1215 × 495; `Command2` was 1215 wide, or -150 for the load
rows; `Command3` was 1215 wide with `Height = 0`; `Label1` was 1215 × 255, or -150 wide for the load rows; and `Line1`
ran from (120, 2400) to (1200, 2400). The statements are the tables' own, each followed by a read of the property.

### The same `.frm` in VB6's IDE (2026-11-16, #4179)

**Asked because** `/make` only says the build fails. HexIDE opens and runs a project in its IDE, so what decides its
behaviour is what VB6's own IDE does with that file: open it or not, run it or not, and save it how.

**How.** `vb6.exe Neg.vbp` on the `Win10` guest's desktop, driven by mouse and keys and read by screenshot. `NegForm.frm`
held, in this order, `Label1` (1215 → **-150** wide, 255 high), `Command3` (1215 wide, `Height = 0`) and `Command2`
(**-150** wide, 495 high), each control's `Begin` line at lines 11, 19 and 27 and its `Width` line six lines later.
`Form_Load` wrote `Command2.Width`, `Label1.Width` and `Command3.Height` to a file. A p-code project
(`CompilationType=-1`), and the same VB6.EXE 6.00.9782 (SP6).

| Step | What VB6's IDE did |
|---|---|
| Open the project | Opened with **no message**, and wrote no `NegForm.log`. The form isn't read until something needs it. |
| Open the form's designer | "Errors during load. Refer to '…\NegForm.log' for details", with OK and Help. The log: `Line 11: Property Width in Label1 could not be set.` and `Line 27: Property Width in Command2 could not be set.` |
| OK | The designer opened holding all three controls. The toolbar read Command2 at 120, 480, size **0 × 495**, and Label1 at 120, 1800, **0 × 255**. |
| F5 | **Ran.** `Form_Load` wrote `C2W=0 L1W=0 C3H=0`. |
| F5 straight after opening, the designer never opened | The same "Errors during load" box, over the IDE in `[run]`. After OK the program **ran**, writing `C2W=0 L1W=0 C3H=0` again, and the same two log lines. |
| Ctrl+S on that form, after the run | **Save File As**, suggesting `NegForm` in the IDE's current folder, not the form's own. |
| Saved as `NegSaved.frm` | Both widths written as **`Width = 0`**. The Project window then named `NegSaved.frm`; the `.vbp` wasn't saved. `NegForm.frm` kept its bytes and time. |
| **Control:** the same form with every width 1215, designer opened, Ctrl+S | **No dialog.** Saved in place, over `CleanForm.frm`. |

**What this settles:**
- **The line a load error names is the control's `Begin` line,** not the line of the property it couldn't set: 11
  and 27 here, where the `Width` lines were 17 and 33. The competing reading, the property's own line, was what this
  probe was laid out to tell apart. The earlier row's `Line 34` and `Line 18` came from a different layout and weren't
  re-read against it.
- **VB6's IDE loads the form anyway, with the size it couldn't set reading 0.** Not the default (1215), and not the
  value in the file. It then runs, and the program sees 0.
- **A form loaded with errors is never saved over itself.** Ctrl+S on it goes to Save As, and a clean form's Ctrl+S
  doesn't, so it's the load error that does it. What Save As writes is the 0 the form holds.
- **Not about the error:** both saves wrote the controls in a different order from the file's (Command3, Command2,
  Label1, where the file had Label1, Command3, Command2) and put a space after each `Begin` line. The clean save did
  both too, so neither comes from the load error. Why that order wasn't measured.
- **Not measured:** whether the IDE asks again on a second F5 in the same session, and whether `Height` behaves the
  same as `Width` for a negative value (only `Width` was negative here).
