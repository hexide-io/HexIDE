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
- **A `.frm` holding a negative Width doesn't load at all,** so `/make` fails. A HexIDE that writes one has produced a
  project VB6 itself can't open.
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
