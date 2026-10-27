## A multiple selection in the designer, and Align (2026-10-26, #2181, design-time, IDE)

**The question.** HexIDE's designer let a Ctrl+click add a control inside a Frame to a selection holding one on the
form, and Format > Align > Lefts then lined the two up in form coordinates. That left the Frame's control at a negative
Left, outside the Frame where it can't be seen. Does VB6's designer let one selection span two containers, and which
control does Align line the others up with?

**The probe.** VB6.EXE 6.00.8176 SP6, the IDE, in the guest's console session through the agent ("Reaching the VB6
*IDE*, not just the compiler" in the README). Form1 holds:

| Control | Container | Left |
|---|---|---|
| Command1 | the form | 600 |
| Command3 | the form | 240 |
| Frame1 | the form | 2040 |
| Command2 | Frame1 | 240 |
| Command4 | Frame1 | 480 |

For each scenario VB6 was started afresh on the same file, with the form open in its designer. The probe pressed real
mouse clicks near each control's top-left corner, holding Ctrl or Shift through `keybd_event` where the scenario says
so. Then it read the selection from the Properties window and pressed Alt+O, A, L for Format > Align > Lefts. Last, it
saved with Ctrl+S and read each Left from the saved `.frm`.

The selection was read two ways. One was the Properties window's object box, which names the control when one is
selected and is empty for several. The other was the row count of the window's list (`LB_GETCOUNT`): 33 for one
CommandButton, 23 for two, and 18 for a CommandButton with a Frame. A screenshot was kept at each step.

| Clicks | Selection | Lefts after Align Lefts |
|---|---|---|
| Command3, then Ctrl+click Command2 | **Command3 alone** | unchanged |
| Command2, then Ctrl+click Command3 | **Command2 alone** | unchanged |
| Command3, then Shift+click Command2 | **Command3 alone** | unchanged |
| Command3, then a plain click on Command2 | Command2 alone | unchanged |
| Command3, then Ctrl+click Command1 | both | **Command3 to 600**, Command1's |
| Command1, then Ctrl+click Command3 | both | **Command1 to 240**, Command3's |
| Command2, then Ctrl+click Command4, in the Frame | both | **Command2 to 480**, Command4's |
| Command3, then Ctrl+click Frame1 | both | **Command3 to 2040**, Frame1's |
| Command3, Ctrl+click Command1, then Ctrl+click Command2 | Command3 and Command1 | Command3 to 600; Command2 unchanged |
| Command3, Ctrl+click Command1, Ctrl+click Command2, then Ctrl+click Command1 again | Command3 alone | unchanged |

- **A selection never spans two containers.** A Ctrl+click or a Shift+click on a control in a different container
  from the selection is ignored. The selection stays as it was and isn't replaced. A plain click selects the clicked
  control as usual, in whichever container it is.
- **A Ctrl+click on a selected control removes it** from the selection. In the last row, Command1 was toggled out
  again, which leaves Command3. The ignored click on Command2 between them changed nothing.
- **Align lines the others up with the control added last.** In each order, the second control clicked kept its Left
  and the first moved to it. An ignored click doesn't count: in the three-click row, Command1 stays the reference.
  The screenshots show it with solid handles and the rest of the selection with hollow ones.
- **The Frame and a control on the form are one container's children,** so they can be selected together and
  aligned. The Frame's own position is the reference there; its children aren't selected with it.

### In HexIDE

A Ctrl+click added Command2, inside Frame1, to a selection holding Command3 on the form. Align Lefts then put Command2
at Left −120 inside the Frame (#2181). Under VB6's rule that selection can't be made, so the case doesn't arise.

The reference already follows VB6: the designer spec makes the control clicked last the reference. A dragged region is
a stated divergence there. VB6 confines it to the container the drag began in, and HexIDE's spans the whole form, so a
region can still return a selection across containers.
