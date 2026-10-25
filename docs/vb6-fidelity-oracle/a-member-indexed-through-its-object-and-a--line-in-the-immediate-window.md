## A member indexed through its object, and a `?` line in the Immediate window (2026-10-22, #1386)

**The question.** Paused in the Notepad demo's Find dialog, the Immediate window answered "Syntax error" for
`? frmNotepad.txtEdit(0).Text`, and for any `?` with more than one item. Was that the Immediate window's parse, or the
interpreter? And what does VB6's Immediate window print for a `?` line?

It was the interpreter. The same expression in a program, `v = Form1.Text1(0).Text`, stopped the run with "Method or data
member not found". `Form1.Text1` found the control array as a field of the form and dropped the `(0)`. The Immediate
window reports every compile error as "Syntax error", which hid that.

### Through its object, in a compiled program

**The probe.** Form1 has a TextBox array, `Text1(0)` "first" and `Text1(1)` "second". Its `Form_Load` shows Form2 and
calls `Form2.Probe`, which reads Form1's controls under `On Error Resume Next` and logs each line. Class1 has `Public V As
Variant`. Made native and p-code, and run in the guest's session 0. The two logs were identical.

| Statement, in Form2's code | VB6 |
|---|---|
| `v = Form1.Text1(0).Text` / `Form1.Text1(1).Text` | `first` / `second` |
| `v = Form1.Text1(9).Text` | **340**, "Control array element '9' doesn't exist" |
| `v = Form1.Text1("1").Text` | `second`: a String index is taken as a number |
| `v = Form1.Text1.Count` / `Form1.Text1(1).Index` | `2` / `1` |
| `Form1.Text1(0).Text = "w"` | Err 0, and it reads `w` |
| `v = o.Text1(1).Text`, `Dim o As Object: Set o = Form1` | `second` |
| `v = k.V(1)`, `Dim k As Class1`, after `k.V = Array(10, 20, 30)` | `20` |
| `v = k.V(5)` | **9**, "Subscript out of range" |
| `v = o.V(2)`, `Set o = k` | **451**, and nothing read |
| `k.V(1) = 99` | Err 0, and `k.V(1)` still reads `20` |

**In the form's own code**, a second probe:

| Statement, in Form1's code | VB6 |
|---|---|
| `v = Text1(9).Text` / `Text1(9).Text = "x"` | **340**, "Control array element '9' doesn't exist", each |
| `v = Text1(300).Text` | **340**, "Control array element '300' doesn't exist" |
| `v = Me.Text1(9).Text` / `Unload Text1(9)` / `v = Text1("9").Text` | **340**, naming `9`, each |

**What it shows:**
- **A control array through its form, early- or late-bound, is the bare array.** The index picks the element, and a missing
  one is 340.
- **340's message names the index** it was given, read, written or unloaded. "Control arrays" above recorded the number
  alone.
- **A class's Variant field holding an array is indexed only early-bound.** Through an Object, the index goes to the
  property itself, which takes none: 451. Assigning an element writes into a copy, and nothing says so.

### A `?` line in the VB6 IDE's Immediate window

**The probe.** The same Form1, with a Form2 whose `Probe` sets `Dim n As Integer: n = 7` and then `Stop`s. In the guest's
console session, VB6 6.00.8176 opened the project, F5 ran it to the Stop, and a script typed each line into the Immediate
window, then copied the window through the clipboard. Each line below is copied as it was, between brackets so trailing
spaces show.

| Typed | Printed |
|---|---|
| `? 5` / `? 1 + 1` / `? n` | `[ 5 ]` / `[ 2 ]` / `[ 7 ]` |
| `? -3` / `? 1.5` | `[-3 ]` / `[ 1.5 ]` |
| `? "abc"` / `? True` / `? Null` | `[abc]` / `[True]` / `[Null]` |
| `? 1; "a"; 2` | `[ 1 a 2 ]` |
| `? 1, 2` | `[ 1             2 ]` |
| `? "a"; "b"` | `[ab]` |
| `? Form1.Caption` | `[One]` |
| `? Form1.Text1(0).Text` | `[first]` |
| `? Form1.Text1(1).Text; Len(Form1.Text1(1).Text)` | `[second 6 ]` |
| `? Form1.Text1.Count` | `[ 2 ]` |
| `? 6;`, then `? 7` typed | `[ 6 ]` with the caret left after it; `? 7` joined that line as ` 6 ? 7`, and Enter gave "Compile error: Invalid in Immediate pane" |
| `Print 4` / `?5` / `Debug.Print 9`, on SP6 | `[ 4 ]` / `[ 5 ]` / `[ 9 ]` |

**What it shows:**
- **A `?` line is Print, exactly as `Debug.Print` prints.** A number has a space where its sign would go and one after,
  `;` joins and `,` moves to the next 14-column zone (the table under "`Debug.Print` is the same Print").
- **It reads a control array through its form from another form's code.**
- **A trailing `;` leaves the line open**, and the Immediate window doesn't start a new line for what is typed next.

**On SP6 (2026-10-24, #1663),** the same lines typed again gave the same output, row for row, except the `? 6;` row, which
wasn't typed again. Three more were measured there for the first time, the last row of the table:
- **`Print`, with no `Debug.`,** is a `?` line.
- **So is `?5`,** with no space after the `?`.
- **So is `Debug.Print`.**

The release run couldn't measure the first two: in three runs of four, Ctrl+G left no caret in the Immediate window. On
SP6 a click inside the pane put it there. The first F5 went to the floating Data View window, until a click in the empty
MDI area gave the IDE the keyboard.

### In HexIDE (#1386)

- **`Form1.Text1(0)` is the element,** with the bare array's 340, now naming the index in every case.
- **A `?` line prints as Print does,** single items and lists. A bare expression, with no `?`, still answers the value
  alone, as it did. That's HexIDE's own, since VB6's Immediate window has no bare expression.
- **A `?` line ending in `;` prints its items,** and the next line still starts a line of its own (debugger divergence D28).
- **`k.V(1)` reads the element through its declared class** (#1435), with 9 past the bounds; through `As Object` it
  raises 451. An in-bounds indexed assignment leaves the field unchanged without error. Other bindings, other fields
  and out-of-bounds writes remain refused: these probes did not measure them. It used to read the whole array.

