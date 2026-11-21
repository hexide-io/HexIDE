## The 3-D and flat edges of a CheckBox, OptionButton, TextBox and CommandButton (2026-11-21, #918, SP6)

**The question.** [A Label's border](a-labels-border.md) measured the Label's 3-D and flat edges. What do a CheckBox,
an OptionButton, a TextBox and a CommandButton draw at Appearance 1 (3D) and Appearance 0 (Flat)?

**The probe.** One form held:
- Check1, Option1 and Text1 at Appearance 1;
- Check2, Option2 and Text2 at Appearance 0;
- Command1 at its default, Appearance 1.

It was built with `/make`, run on the guest's console, and opened in VB6's designer (VB6.EXE 6.00.9782, SP6, 96 dpi),
and the host's framebuffer was captured.

**About the colours.** The framebuffer is RGB565, so each colour comes back as its nearest RGB565 value. The grids
use one letter per colour read:

| Letter | Read as | Consistent with |
|---|---|---|
| `.` | `F7F3F7` | `F0F0F0`, the button face |
| `W` | `FFFFFF` | white |
| `l` | `E7E3E7` | `E3E3E3` |
| `s` | `A5A2A5` | `A0A0A0` |
| `d` | `6B696B` | `696969` |
| `f` | `636563` | `646464` |

Five of these are the RGB565 readings of the colours the Label's border measured exactly: `A0A0A0`, `696969`,
`E3E3E3`, `FFFFFF` and `646464`. That page doesn't measure `F0F0F0`. Each grid starts a little above and to the left
of the control.

**Check1 (3D) and Check2 (Flat): the box, 13 × 13.**

```
Check1          Check2
ssssssssssssW   fffffffffffff
sddddddddddlW   fffffffffffff
sdWWWWWWWWWlW   ffWWWWWWWWWff
sdWWWWWWWWWlW   ffWWWWWWWWWff
   ... nine white rows in all ...
sdWWWWWWWWWlW   ffWWWWWWWWWff
slllllllllllW   fffffffffffff
WWWWWWWWWWWWW   fffffffffffff
```

**Option1 (3D) and Option2 (Flat): the circle, top half.**

```
Option1            Option2
....ssss...        ....ffff...
..ssddddss.        ..ffffffff.
.sddWWWWddW        .fffWWWWfff
.sdWWWWWWlW        .ffWWWWWWff
sdWWWWWWWWl        ffWWWWWWWWf
```

**The TextBox and the CommandButton:**
- **Text1 (3D),** read across its top-left corner: `s` then `d`, then white. Its right edge, read inward from outside,
  is `W` then `l`.
- **Text2 (Flat):** one `f` line, on the top, left and right.
- **Command1:** its top and left are `W` outermost, then `l`. Its bottom and right are `d` outermost, then `s`.

So:
- **3D is a sunken pair for the box and the circle.** The outer line is `A0A0A0` and the inner `696969` on the top and
  left. The inner line is `E3E3E3` and the outer white on the bottom and right. That is the Label's 3-D edge, Win32's
  `EDGE_SUNKEN`. The TextBox has the same pair on its top, left and right; its bottom wasn't read.
- **Flat is one colour, `646464`.** It is two pixels deep for the CheckBox's box and the OptionButton's circle, and one
  pixel for the TextBox.
- **The CommandButton is raised:** white then `E3E3E3` on the top and left, and `696969` then `A0A0A0` on the bottom
  and right, read from outside in.
- **The designer draws the same.** Check1's, Check2's and Command1's grids were identical in the designer and the run.

### Not measured

- **The TextBoxes' bottom edges,** and the whole of the OptionButtons' circles.
- **A CommandButton at Appearance 0,** and any control disabled or holding the focus.
- **A BorderStyle 0 TextBox.**
- **The exact colours.** The RGB565 readings fit the values above, but don't prove them.
