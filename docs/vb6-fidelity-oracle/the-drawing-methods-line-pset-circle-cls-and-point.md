## The drawing methods: Line, PSet, Circle, Cls and Point (2026-11-18, #988, SP6)

**The question.** What state do a running form and PictureBox draw with? Where do CurrentX and CurrentY end after
each drawing method? Which pixels does each method light, and what does `Point` read? What do the methods and their
properties refuse, at run time and at compile time? Where does a drawing sit among the controls?

**The probe:** the same programs, form and runs as [the coordinate-system page](the-coordinate-system-scalemode-scale-scalex-and-scaley.md).
- **Readback:** pixels were read back in the program with `Point`. The form's background read Long 15790320,
  &HF0F0F0.
- **The grids below:** each row was read pixel by pixel in `ScaleMode` pixels, with `#` for any pixel that isn't the
  background.
- **Two screenshots:** the layering rows also came from two console screenshots.
- **The builds:** p-code and native code logged the same lines.

### The drawing state on a new form and PictureBox

The form and Picture1 read the same:

| Property | Before anything is drawn |
|---|---|
| `CurrentX`, `CurrentY` | Single 0, 0 |
| `DrawWidth`, `DrawStyle`, `DrawMode` | Integer 1, 0, 13 |
| `FillStyle`, `FillColor` | Integer 1, Long 0 |
| `ForeColor`, `BackColor` | Long -2147483630 (&H80000012), Long -2147483633 (&H8000000F) |
| `AutoRedraw`, `FontTransparent` | Boolean False, True |

### Where CurrentX and CurrentY end

In twips, with AutoRedraw True, each row starting where the one before it left them:

| Statement | CurrentX, CurrentY after it |
|---|---|
| `Cls`, from (100, 200) | 0, 0 |
| `PSet (300, 400)` | 300, 400 |
| `PSet Step(10, 20)` | 310, 420 |
| `Line (100, 100)-(500, 700)` | 500, 700: the end |
| `Line -(900, 300)`, from there | 900, 300 |
| `Line Step(10, 10)-Step(100, 50)`, from (1000, 1000) | **1110, 1060**: the second Step counts from the first point |
| `Line -Step(5, 5)` | 1115, 1065 |
| `Line (100, 100)-(500, 700), , B` | 500, 700 |
| `Line (100, 100)-(500, 700), vbRed, BF` | 500, 700 |
| `Circle (1000, 1000), 300` | 1000, 1000: **the centre** |
| `Circle Step(100, 0), 300`, from there | 1100, 1000 |
| `Circle (2000, 1000), 300, , 0, 3.14`, an arc | 2000, 1000 |
| `Circle (2000, 1000), 300, , -1, -2`, a sector | 2000, 1000 |
| `Circle (2000, 1000), 300, , , , 0.5` | 2000, 1000 |

- **Every value was a Single.**
- **On Picture1, the same:** `Cls` from (50, 60) left 0, 0. `PSet (300, 400)` left 300, 400. `Line (10, 20)-(30, 40)`
  left 30, 40. `Circle (500, 500), 100` left 500, 500.
- **`Print`, for #989:** after the arcs, `Print "x";` left 2070, 1005, and a bare `Print` left 0, 1200. The 1005
  looks like CurrentY snapped to whole pixels: 1000 twips is 66.7 pixels, and 67 pixels is 1005. That's one row, not
  measured further.

### Which pixel a coordinate lights, and which one Point reads

The coordinate-system page shows coordinates kept as whole twips, rounded half to even. **A drawing method lights the
pixel nearest to those whole twips:**

| `PSet (t, t)`, in twips | Kept as | Pixel lit |
|---|---|---|
| 0, 7, 7.4 | 0, 7, 7 | 0 |
| 7.5, 7.6, 8, 14, 14.9, 15, 22, 22.5 | 8, 8, 8, 14, 15, 15, 22, **22** | 1 |
| 23, 29.9, 30 | 23, 30, 30 | 2 |
| 37.5, 52.5 | **38, 52** | 3 |

**`Point` converts straight to pixels and rounds half to even, with no whole-twips step.** So at an exact half pixel
the two disagree, and `Point` read the background right after `PSet` drew:

| `PSet (t, t)` then `Point(t, t)` | PSet lit | Point read |
|---|---|---|
| 7.5 twips | pixel 1 | pixel 0, the background |
| 22.5 twips | 1 | 2, the background |
| 37.5 twips | 3 | 2, the background |
| 52.5 twips | 3 | 4, the background |
| 0.5, 1.5 and 2.5, in `Scale (0, 0)-(312, 213)`, a pixel a unit | 1, 1, 3 | 0, 2, 2: each the background |
| 0.4, 0.6, 1.4, 1.6 there | 0, 1, 1, 2 | the same pixel, red |

The same held in a scale flipped upside down, `Scale (0, 213)-(312, 0)`: y 0.5 lit row 213, y 0.6 row 212, and y 1.5
row 211.

**What this explains.** In `Scale (0, 100)-(10, 0)`, a blue `PSet (5, 50)` on a red line read back red. The PSet kept
y as 1598 twips and lit row 107. `Point(5, 50)` read row 106.5, rounded half to even: row 106, where the line was.

### Lines, boxes and circles, in pixels (DrawWidth 1)

- **A line leaves out its last pixel.** `Line (10, 10)-(20, 10)` lit 10 to 19, and not 9 or 20.
  `Line (10, 10)-(20, 15)`:

  ```text
    y010 ..##...........
    y011 ....##.........
    y012 ......##.......
    y013 ........##.....
    y014 ..........##...   (x 8 to 22; nothing at (20, 15))
  ```

- **A box includes its far corner.** `Line (30, 30)-(40, 35), vbRed, B` lit all four corners, (30, 30), (40, 30),
  (30, 35) and (40, 35), and not (41, 35), (40, 36) or the inside (35, 32).
- **A filled box too.** `Line (50, 50)-(60, 55), vbBlue, BF` filled through (60, 55), and not (61, 55) or (60, 56).
- **`FillStyle` fills a box:** with FillStyle 0 and FillColor vbGreen, `Line (70, 50)-(80, 55), vbRed, B` drew a red
  border round a green inside.
- **A circle reaches its radius on both sides.** `Circle (100, 100), 10` lit 90 and 110 across and down, and not 89
  or 111. Its centre stayed empty.
- **The radius is the longer half-axis:**
  - **Aspect 0.5:** reached 10 across, to 150, and 5 up and down, to 95 but not 94.
  - **Aspect 2:** reached 10 up and down, to 90 but not 89, and 5 across, to 185 and 175, not 184.

### Thick pens

Each `PSet (10, 10)` and line in red, read as grids starting at the x named:

```text
DrawWidth 2, PSet (10, 10), x 6 to 14      DrawWidth 3, PSet (10, 10), x 6 to 14
  y010 ...##....                             y009 ....#....
  y011 ...##....                             y010 ...###...
                                             y011 ...###...
                                             y012 ....#....

DrawWidth 4, PSet (10, 10), x 6 to 14      DrawWidth 5, PSet (10, 10), x 5 to 15
  y009 ...###...                             y008 ....###....
  y010 ..####...                             y009 ...#####...
  y011 ...###...                             y010 ...#####...
  y012 ...###...                             y011 ...#####...
                                             y012 ...#####...
                                             y013 ....###....

DrawWidth 3, Line (10, 10)-(20, 10), x 6 to 24
  y009 ....###########....
  y010 ...#############...
  y011 ....###########....

DrawWidth 4, Line (10, 10)-(20, 10), x 6 to 24
  y008 .....##########....
  y009 ...#############...
  y010 ...#############...
  y011 ...#############...

DrawWidth 3, Line (10, 10)-(20, 15), vbRed, B, x 6 to 24
  y009 ...#############...
  y010 ...#############...
  y011 ...#############...
  y012 ...###.......###...
  y013 ...###.......###...
  y014 ...#############...
  y015 ...#############...
  y016 ...#############...
```

- **At a width above 1, a line reaches past both its ends, and its last pixel is drawn.** At width 3 it ran from 9
  to 21. At width 1 it stops at 19.
- **The dots aren't centred squares.** They look like GDI's round pen, but no rule is defended here. The grids are
  the record.

### Colours

- **A method with no colour draws in ForeColor.** With the default &H80000012, `PSet`, `Line` and `Circle` drew black,
  0. After `ForeColor = vbBlue`, `PSet` and `Line ..., , B` drew blue, 16711680.
- **A method's colour argument takes what the properties refuse:**
  - `PSet (7, 7), -1` drew white, 16777215;
  - `PSet (8, 8), &H1000000` drew black, 0;
  - `PSet (9, 9), &H80000005`, a system colour, drew white.
  - `ForeColor = -1` and `FillColor = -1` each raised 380 and left the property as it was.

### Errors

| Statement | Result |
|---|---|
| `DrawWidth = 0`, `DrawWidth = -1` | 380, Invalid property value; DrawWidth stays 1 |
| `DrawWidth = 32767` | accepted |
| `DrawWidth = 32768` | 6, Overflow |
| `DrawStyle = 7`, `DrawStyle = -1` | 380 |
| `DrawMode = 0`, `DrawMode = 17` | 380 |
| `FillStyle = 8` | 380 |
| `Circle (1000, 1000), -50` | 5, Invalid procedure call or argument |
| `Circle (1000, 1000), 0` | accepted |
| aspect 0, aspect -1 | accepted |
| start 7, start -7, beyond 2π | 5 |
| `PSet (1E+30, 1E+30)`, `Line (0, 0)-(1E+10, 1E+10)` | 6, Overflow, and CurrentX and CurrentY stay as they were |
| `Point(-100, -100)`, `Point(1E+7, 0)` | Long -1 |
| `Point(1E+30, 0)` | 6, Overflow |

**`Point` just outside the client area doesn't read -1 here.** With AutoRedraw True, `Point(0, 213)` read the line drawn
there, and `Point(312, 0)` read the background, in a client area of 312 by 213 pixels. So the edge where -1 starts
isn't the client area's, and wasn't measured.

### AutoRedraw and Paint

In order, in pixels:

| Step | Then |
|---|---|
| the form shows | Paint, once, before the first Timer tick |
| AutoRedraw True, `PSet (10, 10), vbRed` | `Point(10, 10)` red |
| `AutoRedraw = False` | **Paint, at once,** before the next statement. `Point(10, 10)` still red |
| `PSet (20, 20), vbRed` | `Point(20, 20)` red |
| `Refresh` | Paint. Afterwards (10, 10) is red and **(20, 20) is the background** |
| `AutoRedraw = True` | (10, 10) red, (20, 20) the background |
| `Cls` | (10, 10) the background |
| `AutoRedraw = False` | Paint |
| `PSet (30, 30), vbRed`, then `Cls` | (30, 30) the background |

So turning AutoRedraw off keeps the picture it held, and the window repaints from it. What's drawn while it's off is
lost at the next repaint.

### Image, and Picture = Image (2026-11-21, #991, SP6)

**The question.** #991 owed three things:
- `TypeName(Me.Image)`;
- whether `Picture1.Picture = Picture1.Image` copies the drawing;
- every moment Paint is raised.

**The probe.** A form, 320 by 240 pixels in `ScaleMode` 3, held a borderless PictureBox. It ran in session 0, built with `/make` on vb6.exe SP6 as native code and as p-code, and the two logged the same lines.

**`Image` is a bitmap Picture.** On the form and on the PictureBox, with AutoRedraw True or False:
- `TypeName(o.Image)` is `Picture`;
- `o.Image.Type` is 1;
- its Handle isn't 0.

**A form's Image is the screen's size, whatever the form's.** It read 1024 by 768 pixels (27093 by 20320 HIMETRIC) on a 1024 by 768 screen, for forms with insides of:
- 192 by 140;
- 320 by 247;
- 392 by 306.

That held with AutoRedraw True and False. It's the size the form's AutoRedraw surface has when `Point` is searched for its -1 edge (#988).

**A PictureBox's Image grows with the box and doesn't shrink.** In order, in pixels:

| AutoRedraw | Box inside | Image |
|---|---|---|
| False | 900 by 900 | 900 by 900 |
| False | 1800 by 1200 | 1800 by 1200 |
| False | 2100 by 1200 | 2100 by 1200 |
| False | 3000 by 1500 | 3000 by 1500 |
| False | 1500 by 1200 | **3000 by 1500** |
| True | 900 by 900, then 1800 by 1200, 2100 by 1200, 3000 by 1500 and 1500 by 1200 | 3000 by 1500 each time |

**`Picture = Image` copies the drawing,** by a Let or a Set, on the form and on the PictureBox, with AutoRedraw True. After `PSet (5, 5), vbRed`, `o.Picture = o.Image` and then `Cls`:
- (5, 5) read red again, and (6, 6) the background;
- `Picture.Type` was 1.

`Cls` clears to the Picture, and the Picture now holds the drawing.

**Not evidence here:**
- **With AutoRedraw False, `Point` read -1 at every pixel in session 0,** so those `Picture = Image` rows say nothing.
- **No Paint was raised at all in session 0,** for the form or the PictureBox. It wasn't raised on Show, `Refresh`, `Cls`, Hide and Show again, a resize, a minimize and restore, or a change of AutoRedraw. "AutoRedraw and Paint" above has Paint on Show and on `Refresh`, so session 0 can't measure when Paint is raised. #991's list of those moments is still open for the console.

### Where a drawing sits among the controls

Two diagonal 5-pixel lines across a form holding a TextBox, a Label with an opaque yellow back, and a Shape with an
opaque green back, from console screenshots:
- **AutoRedraw True,** drawn in a Timer: the lines pass **under** all three.
- **AutoRedraw False,** drawn in a Timer after the form showed: **under the TextBox, over the Label and the Shape.**

### When Paint is raised, on the console (2026-11-21, #991, SP6)

**The question.** #991 owed every moment Paint is raised, with AutoRedraw False and True. Session 0 raises none, so this was run on the console.

**The probe:**
- **The form:** 320 by 240 pixels in `ScaleMode` 3, holding a borderless PictureBox. It logged `P` in `Form_Paint`, `R` in `Form_Resize`, `A` in `Form_Activate`, `p` in `Picture1_Paint` and `r` in `Picture1_Resize`.
- **Each step:** DoEvents twenty times, clear the log, make the change, and read the log at once. Then DoEvents twenty times more and read it again.
- **The builds:** `/make` on vb6.exe SP6, as native code and as p-code, run on the guest's console session by its agent. The two logged the same lines.

**With AutoRedraw False on both,** in order:

| Step | At once | After DoEvents |
|---|---|---|
| `Show`, the first time | R | R, A, **P, p** |
| `Width` 600 twips narrower | R | R: **no Paint** |
| `Width` 600 wider | R | R, **P** |
| `Height` 600 shorter | R | R: no Paint |
| `Height` 600 taller | R | R, P |
| `Move` 300 to the right | nothing | nothing |
| narrower and then wider in one step | R, R | R, R, P |
| `Hide` | nothing | nothing |
| `Show` again | nothing | **P, p** |
| `Refresh` | **P** | P |
| `Cls`, `PSet`, `Print`, `Caption =`, `BackColor =` | nothing | nothing |
| `WindowState = 1`, minimized | R | R |
| `WindowState = 0`, restored | R | R, P, p |
| `Picture1.Refresh` | **p** | p |
| `Picture1.Cls`, `Picture1.BackColor =` | nothing | nothing |
| `Picture1.Width` 300 pixels wider | r | r, p |
| `Picture1.Visible = False` | nothing | P |
| `Picture1.Visible = True` | nothing | p |

**With AutoRedraw True on both,** the same steps raised Resize as above and **no Paint at all**: not on a resize, `Show`, `Refresh`, a restore or anything else. Turning AutoRedraw on, and later off again for the form and then the PictureBox, raised nothing.

**What it shows:**
- **Paint follows the area Windows asks to be redrawn.** A resize that only shrinks the client area raises Resize and no Paint. One that grows it raises both. Showing, restoring, and hiding a control on top all raise Paint for what becomes visible.
- **`Refresh` raises Paint at once,** before the next statement, for the form or the PictureBox it's called on, and only for that one.
- **Drawing and changing properties don't,** `BackColor` included.
- **With AutoRedraw True, no Paint is raised at all.**

**What resisted explanation.** Here, `AutoRedraw = False` raised no Paint. "AutoRedraw and Paint" above has Paint at once for it. The two sequences differ:
- there, a `PSet` with AutoRedraw True came just before;
- here, a `BackColor` change, a minimize and a restore came between.

They weren't separated.

**`Picture = Image` with AutoRedraw False copies the persistent picture, not the screen.** After `PSet (5, 5), vbRed` with AutoRedraw False, `Point(5, 5)` read red. But after `o.Picture = o.Image` and `Cls`, (5, 5) read the background, by a Let or a Set and on the form or the PictureBox. With AutoRedraw True the same steps kept the red, as in session 0. On the console the form's Image read 50800 by 31750 HIMETRIC, 1920 by 1200 pixels: the console's screen.

**Not evidence here:**
- **Activate after the second `Show`.** None was logged, but whether the form could become the foreground window there wasn't checked.
- **A `Picture1.Width` 300 pixels narrower** raised 380, since the box was narrower than that. It isn't a row.

### At compile time

| Code | `/make` |
|---|---|
| `Circle (100, 100), 50, Aspect:=0.5` | Syntax error: no named arguments |
| `Line (1, 1)-(2, 2), , F` | Syntax error: F alone isn't a box |
| `PSet (1, 1), vbRed, B` | Syntax error |
| `Line (1, 1)-(2, 2) B`, with no comma | Syntax error |
| `Line (0, 0)-(1, 1)`, `Circle (1, 1), 1` or `PSet (1, 1)` in a standard module | Method not valid without suitable object |
| `Cls` in a standard module | **Sub or Function not defined** |
| `Form1.Line`, `Form1.Circle` and `Form1.Scale` in a module | builds |
| `o.Line`, `o.Circle` and `o.Scale` with `o` As Object | builds; not run |
| two lines reading `Cls: Debug.Print 1` in one procedure | **Duplicate declaration in current scope**, at the second |
| the same with `Me.Cls:` | builds |

- **`Cls:` at the start of a line is a label,** not a call followed by another statement. A bare name and a colon make
  a label, so a program that means to clear and continue on one line writes `Me.Cls: ...`.
- **`Circle` and `PSet` are reserved words.** Neither can be a variable, in a module or a form's procedure, a Sub, or a
  label: Syntax error, or "Expected: identifier" for the Sub.
- **`Line` and `Cls` aren't reserved.** `Dim Line As Integer`, `Public Sub Line()` and a label `Line:` all build, and
  so do the same with `Cls`.

### Not measured

- `DrawStyle` at any width, and `DrawMode` other than 13;
- an arc's or a sector's pixels;
- `FillStyle` other than 0 and 1, and a filled circle;
- where `Point` starts reading -1;
- a late-bound `o.Line` at run time;
- `Print`, `TextWidth` and `TextHeight`, beyond the one row above. They're #989's.

**Since measured:** an arc, a sector and a filled circle on [the circles page](circles-arcs-sectors-and-thick-pens-pixel-by-pixel.md). DrawStyle, DrawMode, FillStyle 2 to 7, where `Point` reads -1, and late-bound drawing are in the section below (2026-11-21).

## DrawMode, DrawStyle, FillStyle, where Point reads -1, and late-bound drawing (2026-11-21, #988, SP6)

**The question.** "Not measured" above left open:
- DrawStyle at any width;
- DrawMode other than 13;
- FillStyle 2 to 7;
- where `Point` starts reading -1;
- a late-bound `o.Line`.

**The probe.** It used the circles page's setup: a form shown in session 0, `ScaleMode` 3, `AutoRedraw` True.
- **The form:** 260 by 150 pixels, with two borderless 80 by 60 PictureBoxes, one with AutoRedraw and one without.
- **Each case:** it reset DrawMode 13, DrawStyle 0, DrawWidth 1, FillStyle 1 and ForeColor black. Then it set BackColor, ran `Cls`, drew, and read pixels back with `Point`. In the grids, `.` is BackColor, `#` black, `R` red and `G` green.
- **A second project** measured the dash and hatch gaps, and the late-bound methods.
- **The builds:** `/make` on vb6.exe SP6, as native code and as p-code. The two logged the same lines.

### DrawMode is the Windows raster operation, for the pen and for the fill

**The colours:** the background D was &H336699 after `Cls`, ForeColor P &H55AA0F, and FillColor F &H0F55AA.
- **One value for every pen pixel:** `PSet`, `Line`, the edge of `Line ..., , B`, both the inside and the edge of `Line ..., , BF`, and a `Circle`'s outline each read the formula's value for D and P.
- **A box's inside with FillStyle 1** kept D.
- **A filled circle** (FillStyle 0):
  - its inside read the formula for D and F;
  - its edge pixel took the fill and then the outline, each through the formula.

Every value is masked to &HFFFFFF.

| DrawMode | Formula | Pen pixels | Filled circle's inside | Its edge |
|---|---|---|---|---|
| 1 `vbBlackness` | 0 | &H000000 | &H000000 | &H000000 |
| 2 `vbNotMergePen` | Not (D Or P) | &H881160 | &HC08844 | &H2A55B0 |
| 3 `vbMaskNotPen` | D And Not P | &H224490 | &H302211 | &H200010 |
| 4 `vbNotCopyPen` | Not P | &HAA55F0 | &HF0AA55 | &HAA55F0 |
| 5 `vbMaskPenNot` | P And Not D | &H448806 | &H0C1122 | &H51AA0D |
| 6 `vbInvert` | Not D | &HCC9966 | &HCC9966 | &H336699 |
| 7 `vbXorPen` | D Xor P | &H66CC96 | &H3C3333 | &H69993C |
| 8 `vbNotMaskPen` | Not (D And P) | &HEEDDF6 | &HFCBB77 | &HAB55F8 |
| 9 `vbMaskPen` | D And P | &H112209 | &H034488 | &H010008 |
| 10 `vbNotXorPen` | Not (D Xor P) | &H993369 | &HC3CCCC | &H69993C |
| 11 `vbNop` | D | &H336699 | &H336699 | &H336699 |
| 12 `vbMergeNotPen` | D Or Not P | &HBB77F9 | &HF3EEDD | &HFBFFFD |
| 13 `vbCopyPen` | P | &H55AA0F | &H0F55AA | &H55AA0F |
| 14 `vbMergePenNot` | P Or Not D | &HDDBB6F | &HCFDDEE | &H75AA1F |
| 15 `vbMergePen` | D Or P | &H77EE9F | &H3F77BB | &H7FFFBF |
| 16 `vbWhiteness` | all ones | &HFFFFFF | &HFFFFFF | &HFFFFFF |

**`PSet` twice on the same pixel gives D back** with DrawMode 6, 7, 10 and 11, as the formulas say.

### DrawStyle

**At DrawWidth 1, `Line (10, 20)-(73, 20)` lit x 10 to 72:**

| DrawStyle | Lit, from x 10 |
|---|---|
| 0 `vbSolid`, 6 `vbInsideSolid` | all 63 pixels |
| 1 `vbDash` | 18 on, 6 off, repeated |
| 2 `vbDot` | 3 on, 3 off |
| 3 `vbDashDot` | 9 on, 6 off, 3 on, 6 off |
| 4 `vbDashDotDot` | 9 on, 3 off, 3 on, 3 off, 3 on, 3 off |
| 5 `vbInvisible` | nothing |

**At DrawWidth 2 and 3, DrawStyle 1 to 4 and 6 lit the same pixels as 0.** A pattern is drawn only at width 1. DrawStyle 5 drew nothing at any width.

**The gaps follow FontTransparent.** Over a green box:
- with `FontTransparent` True, the default, a dash's gaps showed the green;
- with `FontTransparent` False, they were BackColor.

The obvious guess was that FontTransparent affects only text.

**DrawStyle 6 keeps a thick box inside its corners:** `Line (20, 20)-(40, 35), , B`:

| DrawWidth | DrawStyle 0 lit | DrawStyle 6 lit |
|---|---|---|
| 1 | x 20 to 40, y 20 to 35 | the same |
| 4 | x 18 to 41, y 18 to 36: two pixels out from each corner and one in | x 20 to 40, y 20 to 35 |
| 5 | x 18 to 42, y 18 to 37: two pixels each way | x 20 to 40, y 20 to 35 |

DrawStyle 1 at width 4 drew the same as 0.

<details><summary>A circle of radius 15 at (40, 40) with DrawStyle 1 to 4, DrawWidth 1</summary>

DrawStyle 1:
```text
x from 23
 26 ...........####......###...........
 27 ..........#.............#..........
 28 ........##...............##........
 29 .......#...................#.......
 30 ......#.....................#......
 31 .....#.......................#.....
 32 .....#.......................#.....
 33 ....#.........................#....
 34 ...#...........................#...
 35 ...#...........................#...
 36 ...#...........................#...
 37 ...#...........................#...
 38 ..#.............................#..
 39 ..#.............................#..
 40 ................................#..
 41 ................................#..
 42 ................................#..
 43 ...............................#...
 44 ...............................#...
 45 ...............................#...
 46 ...#...........................#...
 47 ....#.........................#....
 48 .....#.......................#.....
 49 .....#.......................#.....
 50 ......#.....................#......
 51 .......#...................#.......
 52 ........##................#........
 53 ..........#........................
 54 ...........####....................
 55 ...............#####...............
```

DrawStyle 2:
```text
x from 23
 25 ..................##...............
 26 ............###.....#..............
 27 ........................#..........
 28 ........#................##........
 29 .......#...........................
 30 ......#............................
 32 .............................#.....
 33 ..............................#....
 34 ...#...........................#...
 35 ...#...............................
 36 ...#...............................
 38 ................................#..
 39 ................................#..
 40 ..#.............................#..
 41 ..#................................
 42 ..#................................
 44 ...............................#...
 45 ...............................#...
 46 ...#...........................#...
 47 ....#..............................
 48 .....#.............................
 50 ............................#......
 51 ...........................#.......
 52 ........##................#........
 53 ..........#........................
 54 ..............#.....###............
 55 ...............##..................
```

DrawStyle 3:
```text
x from 23
 26 ...........####......###...........
 27 ..........#........................
 28 ........##.........................
 29 .......#...........................
 30 ......#............................
 32 .............................#.....
 33 ..............................#....
 34 ...............................#...
 35 ...............................#...
 36 ...............................#...
 37 ...#...........................#...
 38 ..#.............................#..
 39 ..#.............................#..
 40 ................................#..
 44 ...............................#...
 45 ...............................#...
 46 ...#...........................#...
 47 ....#.........................#....
 48 .....#.......................#.....
 49 .....#.......................#.....
 50 ......#.....................#......
 51 .......#...................#.......
 52 ........##................#........
 53 ..........#........................
 55 .................###...............
```

DrawStyle 4:
```text
x from 23
 25 ..................##...............
 26 ...........####.....#..............
 27 ..........#.............#..........
 28 ........##...............##........
 29 .......#...........................
 30 ......#............................
 32 .............................#.....
 33 ..............................#....
 34 ...#...........................#...
 35 ...#...........................#...
 36 ...#...........................#...
 37 ...............................#...
 38 ................................#..
 39 ................................#..
 40 ..#.............................#..
 41 ..#................................
 42 ..#................................
 44 ...............................#...
 45 ...............................#...
 46 ...#...........................#...
 47 ....#.........................#....
 48 .....#.......................#.....
 49 .....#.......................#.....
 50 ......#.....................#......
 51 .......#...................#.......
 52 ........##................#........
 53 ..........#........................
 54 ..............#.....###............
 55 ...............##..................
```

</details>

### FillStyle 2 to 7

**The hatches are drawn in FillColor on an 8-pixel grid fixed to the surface, not to the shape.**
- **Horizontal lines** fell at y 11, 19, 27 and 35, and **vertical ones** at x 12, 20, 28 and 36, for a box drawn from (10, 10).
- **A box from (13, 11)** had them at the same y and x.

**The gaps follow FontTransparent, as a dash's do.** Over a green box, they showed the green while it was True and were BackColor while it was False.

**FillStyle 4, `vbUpwardDiagonal`, draws lines that fall to the right**, `\`, and **5, `vbDownwardDiagonal`, lines that rise to the right**, `/`. The names suggest the reverse.

<details><summary>The grids: <code>Line (10, 10)-(41, 41), , B</code> over a green box, FillColor red, rows 10 to 19 (the pattern repeats every 8)</summary>

FillStyle 2:
```text
x from 8
 10 ..################################..
 11 ..#RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR#..
 12 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 13 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 14 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 15 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 16 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 17 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 18 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 19 ..#RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR#..
```

FillStyle 3:
```text
x from 8
 10 ..################################..
 11 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 12 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 13 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 14 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 15 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 16 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 17 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 18 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 19 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
```

FillStyle 4:
```text
x from 8
 10 ..################################..
 11 ..#RGGGGGGGRGGGGGGGRGGGGGGGRGGGGG#..
 12 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 13 ..#GGRGGGGGGGRGGGGGGGRGGGGGGGRGGG#..
 14 ..#GGGRGGGGGGGRGGGGGGGRGGGGGGGRGG#..
 15 ..#GGGGRGGGGGGGRGGGGGGGRGGGGGGGRG#..
 16 ..#GGGGGRGGGGGGGRGGGGGGGRGGGGGGGR#..
 17 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 18 ..#GGGGGGGRGGGGGGGRGGGGGGGRGGGGGG#..
 19 ..#RGGGGGGGRGGGGGGGRGGGGGGGRGGGGG#..
```

FillStyle 5:
```text
x from 8
 10 ..################################..
 11 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 12 ..#RGGGGGGGRGGGGGGGRGGGGGGGRGGGGG#..
 13 ..#GGGGGGGRGGGGGGGRGGGGGGGRGGGGGG#..
 14 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 15 ..#GGGGGRGGGGGGGRGGGGGGGRGGGGGGGR#..
 16 ..#GGGGRGGGGGGGRGGGGGGGRGGGGGGGRG#..
 17 ..#GGGRGGGGGGGRGGGGGGGRGGGGGGGRGG#..
 18 ..#GGRGGGGGGGRGGGGGGGRGGGGGGGRGGG#..
 19 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
```

FillStyle 6:
```text
x from 8
 10 ..################################..
 11 ..#RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR#..
 12 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 13 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 14 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 15 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 16 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 17 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 18 ..#GRGGGGGGGRGGGGGGGRGGGGGGGRGGGG#..
 19 ..#RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR#..
```

FillStyle 7:
```text
x from 8
 10 ..################################..
 11 ..#RRGGGGGGRRGGGGGGRRGGGGGGRRGGGG#..
 12 ..#RRGGGGGGRRGGGGGGRRGGGGGGRRGGGG#..
 13 ..#GGRGGGGRGGRGGGGRGGRGGGGRGGRGGG#..
 14 ..#GGGRGGRGGGGRGGRGGGGRGGRGGGGRGG#..
 15 ..#GGGGRRGGGGGGRRGGGGGGRRGGGGGGRR#..
 16 ..#GGGGRRGGGGGGRRGGGGGGRRGGGGGGRR#..
 17 ..#GGGRGGRGGGGRGGRGGGGRGGRGGGGRGG#..
 18 ..#GGRGGGGRGGRGGGGRGGRGGGGRGGRGGG#..
 19 ..#RRGGGGGGRRGGGGGGRRGGGGGGRRGGGG#..
```

FillStyle 2, the box from (13, 11):
```text
x from 11
 11 ..################################..
 12 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 13 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 14 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 15 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 16 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 17 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 18 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
 19 ..#RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR#..
 20 ..#GGGGGGGGGGGGGGGGGGGGGGGGGGGGGG#..
```

FillStyle 6, the box from (13, 11):
```text
x from 11
 11 ..################################..
 12 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 13 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 14 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 15 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 16 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 17 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 18 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
 19 ..#RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR#..
 20 ..#GGGGGGRGGGGGGGRGGGGGGGRGGGGGGG#..
```

FillStyle 7, the box from (13, 11):
```text
x from 11
 11 ..################################..
 12 ..#GGGGGRRGGGGGGRRGGGGGGRRGGGGGGR#..
 13 ..#GGGGRGGRGGGGRGGRGGGGRGGRGGGGRG#..
 14 ..#RGGRGGGGRGGRGGGGRGGRGGGGRGGRGG#..
 15 ..#GRRGGGGGGRRGGGGGGRRGGGGGGRRGGG#..
 16 ..#GRRGGGGGGRRGGGGGGRRGGGGGGRRGGG#..
 17 ..#RGGRGGGGRGGRGGGGRGGRGGGGRGGRGG#..
 18 ..#GGGGRGGRGGGGRGGRGGGGRGGRGGGGRG#..
 19 ..#GGGGGRRGGGGGGRRGGGGGGRRGGGGGGR#..
 20 ..#GGGGGRRGGGGGGRRGGGGGGRRGGGGGGR#..
```

`Circle (40, 40), 12` with FillStyle 2:
```text
x from 26
 28 ............#####............
 29 .........###.....###.........
 30 .......##...........##.......
 31 ......#...............#......
 32 .....#.................#.....
 33 ....#...................#....
 34 ....#...................#....
 35 ...#RRRRRRRRRRRRRRRRRRRRR#...
 36 ...#.....................#...
 37 ...#.....................#...
 38 ..#.......................#..
 39 ..#.......................#..
 40 ..#.......................#..
 41 ..#.......................#..
 42 ..#.......................#..
 43 ...#RRRRRRRRRRRRRRRRRRRRR#...
 44 ...#.....................#...
 45 ...#.....................#...
 46 ....#...................#....
 47 ....#...................#....
 48 .....#.................#.....
 49 ......#...............#......
 50 .......##...........##.......
 51 .........###RRRRR###.........
 52 ............#####............
```

`Circle (40, 40), 12` with FillStyle 6:
```text
x from 26
 28 ............#####............
 29 .........###.....###.........
 30 .......##.R.......R.##.......
 31 ......#...R.......R...#......
 32 .....#....R.......R....#.....
 33 ....#.....R.......R.....#....
 34 ....#.....R.......R.....#....
 35 ...#RRRRRRRRRRRRRRRRRRRRR#...
 36 ...#......R.......R......#...
 37 ...#......R.......R......#...
 38 ..#.......R.......R.......#..
 39 ..#.......R.......R.......#..
 40 ..#.......R.......R.......#..
 41 ..#.......R.......R.......#..
 42 ..#.......R.......R.......#..
 43 ...#RRRRRRRRRRRRRRRRRRRRR#...
 44 ...#......R.......R......#...
 45 ...#......R.......R......#...
 46 ....#.....R.......R.....#....
 47 ....#.....R.......R.....#....
 48 .....#....R.......R....#.....
 49 ......#...R.......R...#......
 50 .......##.R.......R.##.......
 51 .........###RRRRR###.........
 52 ............#####............
```

</details>

### Where Point reads -1

`Point` was searched along x at y 5, and along y at x 5, for the first coordinate that reads -1:

| Surface | Last x that reads | Last y that reads | Its size |
|---|---|---|---|
| The form, AutoRedraw | **1023** | **767** | 260 by 150; the screen was 1024 by 768 |
| A PictureBox, AutoRedraw | 79 | 59 | 80 by 60 |
| A PictureBox without AutoRedraw | none: -1 everywhere, (0, 5) included | none | 80 by 60 |

- **A form's AutoRedraw surface is the screen's size, not the form's.** That's why "Point just outside the client area" read the background above. A PictureBox's is its own size.
- **The edge rounds as `Point` does elsewhere,** half to even: the last + 0.4 read the background and the last + 0.5 read -1. -0.5 read pixel 0 and -0.6 read -1.
- **The PictureBox without AutoRedraw is a session 0 result.** It read -1 at every point here, which fits a window nothing is painted on (reasoned, not checked). The console wasn't run.

### Late-bound, and inside With

- **Late-bound drawing works** as early-bound drawing does. With `Dim o As Object: Set o = Form1`, these each drew and moved CurrentX and CurrentY to the same place:
  - `o.PSet (5, 5), vbRed`;
  - `o.Line (20, 10)-(40, 10), vbRed`;
  - `o.Line (50, 10)-(60, 20), vbGreen, BF`;
  - `o.Circle (80, 30), 10, vbRed`;
  - `o.Line -(90, 40), vbRed`.

  `o.Point(5, 5)` and `o.Cls` worked too.
- **Inside `With Form1`, `.PSet (5, 5), vbRed`, `.Line (0, 0)-(10, 10), vbRed` and `.Circle (20, 20), 5, vbRed` are compile errors,** "Syntax error". `.Cls` and `.Point(5, 5)` compile and work. So the special syntax doesn't need a typed object. It needs an object named before the dot.

**Not measured:**
- DrawMode with DrawWidth above 1, or with a pattern;
- FillStyle with a DrawMode other than 13;
- FillStyle on a PictureBox;
- the console, for the PictureBox without AutoRedraw.
