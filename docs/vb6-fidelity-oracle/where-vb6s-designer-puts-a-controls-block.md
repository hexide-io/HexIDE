## Where VB6's designer puts a control's block (2026-10-23, #1481, design-time)

**The question.** `docs/vb6-designer-geometry.md` said that `Begin` order is z-order and that windowless controls have a
plane of their own. The direction was measured only for two Lines (`docs/vb6-fidelity-oracle/the-line-control.md`, "The Line control"),
and nothing recorded where VB6's designer writes a control it adds or reorders. HexIDE drew the last block on top and
wrote a new control's block last.

**The probe.** A form, opened in VB6's IDE through the console agent. Its blocks were Picture1 (red), Picture2 (blue),
then Frame1 holding Picture3 (green) and Picture4 (yellow). The two pictures in each pair overlapped. After each step
below I saved with File > Save, read the form-level and Frame-level block order from VB6's file, and took a
screenshot.

| Step | What VB6 showed | Blocks VB6 then wrote |
|---|---|---|
| Opened, then F5 | red over blue, green over yellow, at design time and at run time | Picture1, Picture2, Frame1 (Picture3, Picture4) |
| Bring to Front on Picture2 | blue over red | **Picture2**, Picture1, Frame1 (Picture3, Picture4) |
| Send to Back on Picture3 | yellow over green | Picture2, Picture1, Frame1 (Picture4, **Picture3**) |
| Click inside Frame1, then double-click the CommandButton tool | Command1 at the form's centre, over Frame1 | **Command1**, Picture2, Picture1, Frame1 (...) |
| Draw a CommandButton inside Frame1 | Command2 in the Frame | ..., Frame1 (**Command2**, Picture4, Picture3) |
| Copy Frame1, paste, No to each array prompt | the copy at 0, 0, its contents the other way up: green over yellow | **Frame2 (Picture6, Picture5, Command3)**, Command1, ... |
| Double-click the Label tool | Label1 at the form's centre, hidden under Command1 at the same place | ..., Frame1 (...), **Label1** |
| Draw a second Label | Label2 | ..., Frame1 (...), **Label2**, Label1 |
| Send to Back on Label2 | | ..., Frame1 (...), Label1, **Label2** |
| Bring to Front on Label2 | | ..., Frame1 (...), **Label2**, Label1 |
| Send to Back on Command1 | | Frame2 (...), Picture2, Picture1, Frame1 (...), **Command1**, Label2, Label1 |

In the pasted Frame2, Picture6 is the copy of Picture3, Picture5 of Picture4, and Command3 of Command2. Their TabIndexes
were 8 for Command3, 9 for Picture5 and 10 for Picture6. The array prompts came in the source's order: Frame1, Command2,
Picture4, Picture3.

**What it shows:**
- **The first block in a container is its top control,** in VB6's designer and in the running form alike.
- **Each container's blocks run windowed controls first, then windowless ones** (Label, Shape, Line, Image). A
  windowless control is drawn under every windowed control in its container, whatever the order: Label1, written
  last, was hidden under Command1. The VB98 template corpus agrees, 60 windowed blocks before any windowless sibling
  and none after one.
- **A new or pasted control goes first in its plane's run.** A windowed control goes first in its container, and a
  Label goes first among the Labels, after every windowed control.
- **Bring to Front and Send to Back move a block to the start or the end of its plane's run.** A Label stays behind
  the windowed controls, a windowed control sent to back stays ahead of the Labels, and a container takes what it
  holds. Neither changes the TabIndex.
- **A pasted container's contents come out the other way up.** VB6 pastes them one at a time, in the source's order,
  and each goes on top. So the copy stacks them in reverse, while its names and TabIndexes follow the source's order.
- **Double-clicking a tool after a click inside a Frame adds the control to the form**, not to the Frame. Whether the
  click had selected the Frame was not captured.

**In HexIDE (#1481):**
- **What follows the measurements:**
  - The designer draws each container's first block on top.
  - A new or pasted control is written first in its plane.
  - Bring to Front and Send to Back move a control within its plane, with what it holds.
  - A loaded form is saved in the order it was read.
- **One deliberate divergence:** a pasted container keeps its source's stacking. A copy that turns its contents over
  is an artefact of pasting them one at a time, not a design, so HexIDE does not reproduce it.
- **Two gaps, filed:**
  - The running form still draws the last block on top (#1497, since fixed: "How a running form stacks its
    controls").
  - A control added in front of one HexIDE carries as text is written after it (#1498, since fixed: "A control VB6
    knows and HexIDE carries stacks with the rest").

**Not measured:**
- A Shape, Line or Image beside a Label in the windowless plane. Only Labels were tried.
- Bring to Front with several controls selected.
- A paste while a container is selected.
- The `ZOrder` method at run time.

