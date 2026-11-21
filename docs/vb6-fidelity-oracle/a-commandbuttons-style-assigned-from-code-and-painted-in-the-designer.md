## A CommandButton's Style: assigned from code, and painted in the designer (2026-11-21, #913, SP6)

**The question.** A running form has already been measured (#752): a Style 0 CommandButton given a BackColor paints the
button face, and only a Style 1 (Graphical) button paints its BackColor. Does VB6's designer paint them the same way?
And what does assigning Style at run time do? #913 recorded its error number as unmeasured.

**The probe.** One form with two buttons, both with `BackColor = &H000000FF&`. Command1 has no Style line, and Command2
has `Style = 1 'Graphical`. Three projects were built with `/make`, each as native code and as p-code, on VB6.EXE
6.00.9782 (SP6). Native code and p-code gave the same answer on every row.

**Assigned without naming the button's type.** In Form_Load, under `On Error Resume Next`, each assignment was followed
by `Err.Number`, `Err.Description` and Style read back. Reading first gave `Command1.Style` 0, `Command2.Style` 1,
and `TypeName(Command1.Style)` "Integer".

| Assignment | Err | Description | Style after |
|---|---|---|---|
| `o.Style = 1`, `o` an Object holding Command1 | 382 | 'Style' property cannot be set at run time | 0 |
| `c.Style = 0`, `c` a Control holding Command2 | 382 | 'Style' property cannot be set at run time | 1 |
| `v.Style = 1`, `v` a Variant holding Command1 | 382 | 'Style' property cannot be set at run time | 0 |
| `CallByName Command1, "Style", VbLet, 1` | 382 | **Set not supported at runtime** | 0 |
| `Me.Controls("Command2").Style = 0` | 382 | 'Style' property cannot be set at run time | 1 |

- **Every late-bound assignment raises 382 and leaves Style as it was.** This is the same as Name and Index, in
  [A control's Name and Index at run time](a-controls-name-and-index-at-run-time.md).
- **CallByName raises the same number with different words.** The text is "Set not supported at runtime", not the
  property-named message the other four give.

**Assigned directly, `Command1.Style = 1`, the build fails.** The `/out` reads `Compile Error in File '…\Form1.frm',
Line N : Can't assign to read-only property`, then `Build of '…' failed.`.
- **In Form_Load:** N is 1.
- **In Command1_Click,** below a Form_Load of eight lines: N is 9.
- Both are the assignment's line, counted from 0 in the code view, as
  [What `/make` writes to `/out`](what-make-writes-to-out-and-what-line-n-counts.md) records. It is not the `Sub` line.

**In the designer.** The same form opened in VB6's designer on the guest's console was captured through the host's
framebuffer, which is RGB565.
- **Command1 (Style 0):** most of its face read `F7F3F7`, which is how `F0F0F0`, the button face, reads through RGB565.
- **Command2 (Style 1):** its face read `FF0000`.

So the designer paints as a run does: a Style 0 button's BackColor is kept, but it isn't painted.

### Not measured

- **A Style 1 button with a Picture,** DownPicture or DisabledPicture, in either place.
- **A BackColor assigned to a Style 0 button at run time.** #752's rows are from a form loaded with the value.
