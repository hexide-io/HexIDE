## How a running form stacks its controls (2026-10-23, #1497, IDE, release build)

**The question.** "Where VB6's designer puts a control's block" measured windowed controls at run time, and the
windowless plane only in the designer. It left three things open:
- a windowless control written *before* a windowed one;
- a Line or a Shape beside a Label;
- where a control-array element loaded at run time goes.

**The probe.** A form, written by hand so that each pair overlaps and the first-named control comes first in the
file:

| Pair, in file order | Designer | Run time (F5) |
|---|---|---|
| LabelA (red), then CommandA | the button over the Label | the same |
| LabelB (yellow), then LineB (blue, 8 wide) | the Label over the Line; the Line shows only past the Label's ends | the same |
| LineC, then LabelC | the Line over the Label | the same |
| ShapeD (solid green), then LabelD (yellow) | the Shape over the Label | the same |
| LabelE, then ShapeE | the Label over the Shape | the same |
| CommandF, then CmdArr(0); `Form_Load` loads CmdArr(1) and moves it over both | | CmdArr(1) is under both |

**What it shows:**
- **A windowless control is drawn under a windowed one even when its block comes first.** VB6 never writes a
  file that way, so only a hand-written file shows it.
- **Among the windowless controls, the first block is on top, whatever the class.** A Label covers a Line or
  a Shape written after it, and is covered by one written before it.
- **A control-array element loaded at run time goes under every windowed control already there.** That covers its
  own array's elements and other controls alike. The designer puts a new control on top, so `Load` does the
  opposite.
- **Design time and run time agree on every pair.**

**In HexIDE (#1497):**
- **The running form stacks by these rules.** Each control placed on a container goes under every control of its
  plane already there. Placing a form's controls in file order therefore puts the first on top, and a `Load`ed
  element at the bottom.
- **Lines in the designer** are stacked like any other windowless control. They used to go under every other control,
  Labels included, and so under a Frame holding one.
- **Not done:** the `ZOrder` method is still not implemented, and a program calling it stops (#1503, since done: "What
  ZOrder does at run time").

**Not measured:**
- An Image beside a Label.
- A windowless element loaded at run time. HexIDE puts it at the bottom of the windowless plane, by the same rule.
- `ZOrder` at run time.

