## A Label's Alignment at run time (2026-11-18, #4604, SP6)

**The question.** Can a running program read and set a Label's `Alignment`? What does it refuse, and does a Label
added with `Controls.Add` take it too?

**The probe.**
- **The labels:** a form held Label1, 3000 by 375 twips with BorderStyle 1, so the text's place shows. It added a
  second Label, `lblAdded`, the same size, with `Controls.Add("VB.Label", "lblAdded")`.
- **The run:** a Timer ran the statements under `On Error Resume Next`, logging each value and error. Console
  screenshots were taken after its first tick and after its second.
- **The builds:** p-code and native code logged the same lines.

| Statement | Result |
|---|---|
| reading `Label1.Alignment` | Integer 0 |
| `Label1.Alignment = 2` | ok, and it reads 2 |
| `Label1.Alignment = vbRightJustify` | ok, and it reads 1 |
| `Label1.Alignment = 3` | **380**, Invalid property value. It still reads 1 |
| `Label1.Alignment = -1` | 380 |
| `Label1.Alignment = 1.5` | ok, and it reads **2**: rounded half to even |
| `Label1.Alignment = "2"` | ok, and it reads 2 |
| `lblAdded.Alignment = 2`, on the added Label | ok, and it reads 2 |

**On screen:**
- **After the first tick,** with both labels at 2, both captions were centred.
- **After the second tick,** which set Label1 to 1 and lblAdded to 0, Label1's caption sat at the right and lblAdded's
  at the left.

So a Label's `Alignment` is an ordinary run-time property: Integer, read and write, 0 to 2, with 380 outside that.
