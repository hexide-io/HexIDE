## The name VB6's designer gives a new control (2026-10-23, #540, design-time, IDE, release build)

**The question.** HexIDE named the first CommandButton on a form Command0, and a pasted control took its number from
the count of controls on the canvas. The common recollection is that VB6 starts at 1, and #929's probe saw Command1.
Still open: whether VB6 fills a gap or counts on from the highest number, whether a number freed by a delete comes
back, and whether a paste follows the same rule.

**The probe.** A form saved holding Command1, Command3 and Text5, opened in VB6's IDE. Each step below was saved, and
the names are VB6's file. To add a control, I clicked an empty part of the form, then double-clicked the tool. To
paste, I selected the control, used Edit > Copy and Edit > Paste, and answered **No** to "You already have a control
named 'Command3'. Do you want to create a control array?".

| Form before | Step | New name |
|---|---|---|
| Command1, Command3, Text5 | add a CommandButton | **Command2** |
| Command1, Command2, Command3, Text5 | add a TextBox | **Text1** |
| Command1, Command3, Text5 | paste Command3 | **Command2**, caption still "Command3" |
| Command1, Command2, Command3, Text5 | paste Text5 | **Text1**, Text still "Text5" |
| Command1, Command2, Command3, Text1, Text5, after Command4 was pasted and deleted | add a CommandButton | **Command4** |
| Command1, Command3, Text5, after Command2 was added and deleted | add a CommandButton | **Command2** |

**What it shows:**
- **A new or pasted control takes its class's prefix and the lowest number from 1 that no control on the form has.**
  It fills a gap a file left (Command2 beside Command1 and Command3), and it does not count on from the highest
  number (Text1 beside Text5).
- **A number freed by a delete comes back**, in the middle (Command2) and at the top (Command4). So VB6 doesn't keep a
  per-session counter, which would have given Command5. The name depends only on what is on the form.
- **A paste answered No follows the same rule, and keeps the copied caption or text.** Only the name is new.

**In HexIDE (#540):**
- Both routes now name a control the same way, from 1. A new control used to start at 0, and a paste at the number of
  controls on the canvas.
- A name a preserved control holds still counts as taken (#854).
- HexIDE's paste never asks the array question. It does what VB6's No does.

**Not measured:**
- A paste answered **Yes**, which makes a control array.
- Several controls pasted at once, where each presumably takes the lowest free number in turn.
- A gap in a control array's indexes.

