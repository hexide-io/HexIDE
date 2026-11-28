# A control without a Font block takes the form's font (#2062)

Provenance: the implementation brief comment on hexide-io/HexIDE#2062 reports measurements on
vb6.exe SP6, with native and p-code agreeing, from #1556's "Found along the way" probe. That
probe's subject file was on `feat/1556-controls-add` when this change was written; this record
preserves the supplied measurements without claiming a new local oracle run.

The .frm declares Form1's font as Arial 12. Picture1 declares Courier New 9.75;
Frame1 declares Times New Roman 9.75 with Weight 700. All other controls omit their Font block.

| Control | Location | Font.Name / FontName | Font.Size / FontSize | Font.Bold |
|---|---|---|---|---|
| Command1 | Form1 | Arial | 12 | False |
| Label1 | Form1 | Arial | 12 | False |
| Command2 | Picture1 | Arial | 12 | False |
| Frame2 | Picture1 | Arial | 12 | False |
| Command4 | Frame2 inside Picture1 | Arial | 12 | False |
| Command3 | Frame1 | Arial | 12 | False |
| Picture1 | Form1, explicit font | Courier New | 9.75 | False |
| Frame1 | Form1, explicit font | Times New Roman | 9.75 | True |

The issue body additionally reports Text1 without a Font block reading Arial 12.
The missing block takes the form's font at any container depth, rather than the nearest
container's font. HexIDE previously supplied MS Sans Serif 8.25 instead.

`Command1.Font Is Label1.Font` and `Command1.Font Is Form1.Font` both returned False.
After `Form1.Font.Name = "Tahoma"`, Command1 and Command2 still read Arial: the inherited
font is a copy made at load, not a continuing link. HexIDE's font descriptor is a value type;
control Font object member access remains separate work (#934).

Designer rendering, UserControl contents, and a form omitting its own Font block were not
measured by this probe and are not pinned here.

## What VB6's IDE saves for a control without a Font block (2026-11-28, #5836, IDE, SP6)

**The question.** The table above is what a control without a Font block reads when the program runs. #5836 asks what
VB6's IDE writes when it saves such a form: does the control's inherited font go into the file? HexIDE's save wrote it
into the control, so a round trip of such a form wasn't byte-identical.

**The rig.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console by the IDE step driver. Files
were read in the guest over PowerShell Direct. The project was a one-form Standard EXE holding #5836's `Form2`, in CRLF,
exactly as the issue gives it:
- **`Form2`'s own Font block:** Arial, Size 8.25, Charset 0, Weight 700, and Underline, Italic and Strikethrough False.
- **`Text1`, a TextBox on the form:** Height 315, Left 120, TabIndex 0, Text "0", Top 120 and Width 975, with no Font
  block.

VB6 opened it with no message. Each case below ran in a fresh vb6.exe, which opened `Form2`'s code window, and ended
with Ctrl+S, which saved `Form2.frm`.

| Case | The edit before Ctrl+S | `Form2.frm` written | The saved file |
|---|---|---|---|
| Code | Ctrl+End in the code window, type `x`, Backspace | 9.5 s after it was copied in | Identical to the input but for one empty line at the end of the code, which the edit left |
| Designer | Shift+F7 to the designer, Tab, which selected `Text1`, then Ctrl+Right and Ctrl+Left | 12.4 s after it was copied in | Byte-identical to the input, 1,002 bytes |

**Result: VB6's save writes no Font block into `Text1`.** Its block is written exactly as it was read, in both cases.

**At design time the control reads the form's font too, and the save still leaves it out.** In the designer case, with
`Text1` selected, the Properties window showed its Font as `Arial`, and the toolbar read `120, 120` and `975 x 315`.

**So the inherited font is something a control reads, not something it holds.** A save writes a control's Font block
only from what the control itself has. The expected answer was this one. The issue's guess, that HexIDE stores the
inherited font on the control after #2062, is about HexIDE's model and wasn't measured here.

**Not measured:**
- whether Ctrl+Right moved `Text1` before Ctrl+Left moved it back. No screenshot was taken between them, and the readout
  after both read `120, 120`;
- a control whose own Font block matches the form's, and whether a save keeps that block;
- a change to the form's Font in the designer: whether a control without a block follows it, and what a save then
  writes;
- a form with no Font block of its own;
- controls other than a TextBox, and controls inside a container.
