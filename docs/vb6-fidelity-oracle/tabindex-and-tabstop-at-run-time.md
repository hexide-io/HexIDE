## TabIndex and TabStop at run time (2026-10-26, #1974)

**The probe.** A form of eight controls with TabIndex 0 to 7:
- Command1, Text1, Label1 and Check1;
- Frame1, with Option1 inside it;
- the control array `cmdA(0)` and `cmdA(1)`;
- a Timer, which has no TabIndex.

`Sub Main` loads the form and, under `On Error Resume Next`, makes one assignment per row. It prints the error, then
each control's TabIndex in the order listed above. Before each row the order is put back by setting each control's
TabIndex to its original value in turn. The program was built with `/make` as native code and as p-code in the oracle
guest's session 0, VB6 SP6, and both builds printed the same lines.

**Setting TabIndex.**

| Assignment | Error | Command1 … cmdA(1) |
|---|---|---|
| (as loaded) | | `0 1 2 3 4 5 6 7` |
| `Check1.TabIndex = 0` | 0 | `1 2 3 0 4 5 6 7` |
| `Command1.TabIndex = 7` | 0 | `7 0 1 2 3 4 5 6` |
| `Command1.TabIndex = 5` | 0 | `5 0 1 2 3 4 6 7` |
| `Text1.TabIndex = 8`, `= 99`, `= 32767` | 0 | `0 7 1 2 3 4 5 6`: past the last is the last |
| `Text1.TabIndex = 40000` | 6 | unchanged |
| `Text1.TabIndex = -1`, `= True` | 380 | unchanged |
| `Text1.TabIndex = 1.5`, `= 2.5` | 0 | `0 2 1 3 4 5 6 7`: each is 2, rounded half to even |
| `Text1.TabIndex = "3"` | 0 | `0 3 1 2 4 5 6 7` |
| `Text1.TabIndex = "abc"` | 13 | unchanged |
| `Text1.TabIndex = Null` | 94 | unchanged |
| `Text1.TabIndex = Empty` | 0 | `1 0 2 3 4 5 6 7`: Empty is 0 |
| `Text1.TabIndex = 1`, its own | 0 | unchanged |
| `Option1.TabIndex = 0`, inside Frame1 | 0 | `1 2 3 4 5 0 6 7` |
| `Frame1.TabIndex = 0` | 0 | `1 2 3 4 0 5 6 7` |
| `cmdA(1).TabIndex = 0` | 0 | `1 2 3 4 5 6 7 0` |
| `Label1.TabIndex = 7` | 0 | `0 1 7 2 3 4 5 6` |

So a form has **one tab order**, across its containers and its control arrays, and setting TabIndex moves the control
to that place in it. The controls between its old place and its new one each move by one. TabIndex reads as an
Integer, and is converted as one before anything moves. A Timer's is 438, late-bound.

**TabStop.** Each row is the value read back, its type and the error:

| Assignment to `Text1.TabStop` | Logged |
|---|---|
| none | `True Boolean 0` |
| `False`, `0`, `"False"`, `Empty` | `False Boolean 0` |
| `5` | `True Boolean 0` |
| `"abc"` | `True Boolean 13`: unchanged |
| `Null` | `True Boolean 94`: unchanged |

- **`Text1.TabStop = False` leaves the order as it was.**
- **Option1 and Check1 read True,** and Option1 reads False after `Option1.TabStop = False`.
- **A Label or a Frame has no TabStop:** reading or setting one is 438, late-bound.

**In HexIDE (#1974):**
- **Reading and setting work** on every control VB6 gives a TabIndex, for every row above.
- **A set renumbers the whole form,** whose order is the order the running form tabs in.
- **An option button reads the TabStop it was given,** not the stop its group allows it at that moment.
- **Not measured:**
  - the TabIndex a control array element loaded at run time takes;
  - a UserControl's TabIndex, which HexIDE doesn't bind yet.
