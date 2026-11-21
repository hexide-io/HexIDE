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
