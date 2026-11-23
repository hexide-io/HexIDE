## The Line control (2026-10-15, #163)

Three probes, each compiled native and as p-code. Every line of output was the same both ways.
- **Probe A** reads members back. It ran in session 0, since nothing in it needs a painted window. It holds three
  forms: one in pixels (`ScaleMode = 3`), one with the About Dialog's user scale (`ScaleWidth = 5380.766`,
  `ScaleHeight = 2453.724` on a 5730-by-3555-twip client), and one in twips.
- **Probes B and C** read pixels with `Point` on a painted form, with `AutoRedraw` False, on the guest's **console**
  session. In session 0 `Point` answered -1 everywhere.

**What a Line holds (probe A):**
- **A Line with nothing set** reads X1, Y1, X2 and Y2 as 0, BorderColor `&H80000008` (Window Text), BorderStyle 1,
  BorderWidth 1, DrawMode 13 and Visible True. TypeName is Line.
- **The types:** X1 to Y2 are Single. BorderWidth, BorderStyle and DrawMode are Integer. BorderColor is Long.
- **The .frm gives the endpoints in the container's scale,** not in twips as it gives `Left` and `Top`. On the pixel
  form, a file X1 of 150 reads 150, and 2250 once the program sets `ScaleMode = 1`.
- **VB6 holds them as whole twips, half to even.** On the pixel form:

  | Assigned | Twips | Reads |
  |---|---|---|
  | 10.5 | 157.5, held as 158 | 10.53333 |
  | 10.4 | 156 | 10.4 |
  | 11.5 | 172.5, held as 172 | 11.46667 |

  In twips, 100.6 reads 101, and the file values 100.5, 2000.25 and 500.75 read 100, 2000 and 501. -5 and "12" are
  taken as they are.
- **The About Dialog's user scale.** Its file X1 of 84.515 is 90 twips, and its Y1 of 1687.583 is 2445. In pixels
  they read 6 and 163.
- **The user scale's read-back is in Single precision.** In the user scale itself they read **84.51466 and
  1687.583**. In Double, the scale's own arithmetic gives 84.51465 (90 × 5380.766 / 5730) and 1687.582 (2445 ×
  2453.724 / 3555), which was the obvious guess and is wrong in the last digit.
  - **What reproduces both:** twips divided by a twips-per-unit factor, where the factor is ClientWidth over
    ScaleWidth worked as Singles, and the quotient rounded to a Single. Of fifteen orderings tried, only that one and
    an equivalent route through pixels give both values.
  - **How firm it is:** two measured values pin it, so it is a rule that fits them, not one tested beyond them. The
    supervising session suggested trying Single arithmetic.
- **What it refuses:**
  - BorderWidth 0 is 380; 2.5 is taken as 2; 8192 and 8193 are taken.
  - BorderStyle 7 and -1 are 380, and 6 is taken.
  - DrawMode 0 and 17 are 380, and 16 is taken.
  - BorderColor -1 is 380, and `&H1000000` is taken, as for any colour.
- **Members a Line lacks:**
  - Early-bound, `LineP.Left` is the compile error "Method or data member not found".
  - Late-bound through an `Object`, Left, Move, hWnd and SetFocus are 438, while Refresh and ZOrder are taken.
  - Container and Parent name the form.
- **The control array:** Count 2 and UBound 1. `Load Line1(2)` gives an element that is not Visible, with element 0's
  X1 and Y1, and Count 3. `Unload` takes it back to 2.
- **The default member is Visible:** `"" & LineP` printed False after `LineP.Visible = False`.
- **Visible on the console** (2026-11-18, #4768; probe A above ran in session 0). A Line read True whether its form
  was loaded, shown or hidden, in session 0 and on the console alike, and `Load Line1(2)` read False, with Count 3
  and element 0's X1 and Y1. A Line has no window, so unlike a CommandButton's, its Visible doesn't follow the
  form's (#4829).

**How a Line draws (probes B and C):**
- **An endpoint is on the pixel nearest its twips.** 3007 twips (200.47 pixels) is row 200 and 3008 (200.53) is row
  201. The same holds for X.
- **One pixel wide, it is GDI's line.**
  - It covers every pixel from the first endpoint, the second left out: 20 to 220 covers 20 to 219.
  - A diagonal steps as a midpoint line: (300,200) to (310,205) covers 300 and 301 on row 200, then two to a row, and
    ends at 309 on row 204.
  - Its BorderStyle dashes it from the first endpoint, in pixels on and off:

    | BorderStyle | Pattern |
    |---|---|
    | 2, Dash | 18, 6 |
    | 3, Dot | 3, 3 |
    | 4, Dash-Dot | 9, 6, 3, 6 |
    | 5, Dash-Dot-Dot | 9, 3, 3, 3, 3, 3 |
- **Wider, it is solid whatever the BorderStyle, and covers both endpoints.**
  - Across a horizontal line, 2 covers the endpoint's row and the one above; 3 covers the rows above and below too.
  - A vertical line of 3 covers the endpoint's column and the columns either side.
  - On the top row of a 2-wide line the first pixel is left out (21 to 220). On its bottom row, and on a 3-wide line,
    every pixel is covered (20 to 220).
- **Inside Solid (6) draws as Solid, at every width. Transparent (0) draws nothing.**
- **A Line earlier in the .frm is drawn over one later in it.** Two lines on the same pixels, red then blue in the
  file, show red.
- **The About Dialog's etched rule** is its two lines in that order: grey, then white and 2 wide, on a user scale.
  - Row 163 is grey from 6 to 376.
  - Row 164 is white from 7 to 377.
  - At 377, row 163 shows white: the white line covers its endpoint and the grey one, drawn over it, leaves out its
    own.

**Not measured:**
- Even widths on a vertical line;
- a wide diagonal's end caps;
- DrawMode other than 13;
- a Line beside a Label, Shape or Image, which share its windowless plane (measured since for a Label: "How a running
  form stacks its controls", #1497);
- what VB6's designer writes when it saves a Line, including DrawMode's comment;
- a Line in a PictureBox with a user scale.

**In HexIDE (#163):**
- **All of the above runs:** the loading, the whole twips, the reads, writes and refusals, the control array, the
  user scale's Single factor, and the drawing, pixel for pixel, including the About Dialog's rule.
- **The divergences:**
  - every DrawMode draws as Copy Pen;
  - a Line is drawn under every other control, Labels included (until #1497, which stacks it among the windowless
    controls by file order, as measured there);
  - a wide diagonal's ends are round caps, not measured;
  - the default member is not implemented.
- **Refused:** a Line on a form whose ScaleLeft or ScaleTop is not 0, such as a y-up graph's, since HexIDE
  applies no origin; and a Line in a PictureBox with a user scale.


## What VB6's designer saves for a Line (2026-11-21, #163, SP6)

**The question.** The probes above read a Line from code. #163 also asks what the designer offers and what a save
writes: the property set, its order, and which values are left out.

**The rig.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console through Hyper-V's synthetic
keyboard and mouse. Files were read in the guest over PowerShell Direct. Each project was a one-form Standard EXE, the
form in twips. Each hand-written form was dirtied by a Replace All of a comment in its code, then saved with Ctrl+S.
VB6 loaded each one with no message and no `.log`.

**The Properties window** lists twelve rows for a Line, with these defaults:

| Property | Default shown |
|---|---|
| `(Name)` | |
| BorderColor | `&H80000008&` |
| BorderStyle | `1 - Solid` |
| BorderWidth | `1` |
| DrawMode | `13 - Copy Pen` |
| Index | empty |
| Tag | empty |
| Visible | `True` |
| X1, X2, Y1, Y2 | the endpoints |

**What a save writes.**
- **A Line added with a double-click on its toolbox button,** on a 6000-by-4500 client, was saved as `X1 = 2400`,
  `X2 = 3600`, `Y1 = 2010`, `Y2 = 2490`, and nothing else. That is a diagonal across a 1200-by-480 box.
- **A Line drawn with the mouse** was saved with its four endpoints only.
- **Properties at their defaults are left out,** even when the file names them. A Line whose file gave
  `BorderColor = &H80000008&`, `BorderStyle = 1  'Solid`, `BorderWidth = 1`, `DrawMode = 13  'Copy Pen` and
  `Visible = -1  'True` was saved with its four endpoints only.
- **The order is alphabetical,** whatever order the file gave. The hand-written lines were in reverse order, and VB6
  saved them like this:

```
   Begin VB.Line LineA 
      BorderColor     =   &H000000FF&
      BorderStyle     =   2  'Dash
      DrawMode        =   7  'Invert
      Tag             =   "tag"
      Visible         =   0   'False
      X1              =   300
      X2              =   3000
      Y1              =   300
      Y2              =   900
   End
   Begin VB.Line LineB 
      BorderWidth     =   3
      ...
   End
   Begin VB.Line LineC 
      Index           =   2
      X1              =   300
      ...
```

- **`Index` came before the endpoints** on a Line with nothing else set (LineC). A Line with `Index` and other
  properties together wasn't tried.

**BorderStyle's comments** are the Properties window's names:

| Value | Saved as |
|---|---|
| 0 | `0  'Transparent` |
| 1 | left out |
| 2 | `2  'Dash` |
| 3 | `3  'Dot` |
| 4 | `4  'Dash-Dot` |
| 5 | `5  'Dash-Dot-Dot` |
| 6 | `6  'Inside Solid` |

**DrawMode's comments are one name behind the value.** The obvious guess is that a saved `DrawMode = 7` reads
`'Xor Pen`, the name VB6's Properties window gives 7. It doesn't: the file says `'Invert`, the name of 6.

Sixteen Lines, with DrawMode 1 to 16 in the file and no comment, were saved as:

| Value | Saved comment | The Properties window's name |
|---|---|---|
| 1 | `'Blackness` | Blackness |
| 2 | `'Blackness` | Not Merge Pen |
| 3 | `'Not Merge Pen` | Mask Not Pen |
| 4 | `'Mask Not Pen` | Not Copy Pen |
| 5 | `'Not Copy Pen` | Mask Pen Not |
| 6 | `'Mask Pen Not` | Invert |
| 7 | `'Invert` | Xor Pen |
| 8 | `'Xor Pen` | Not Mask Pen |
| 9 | `'Not Mask Pen` | Mask Pen |
| 10 | `'Mask Pen` | Not Xor Pen |
| 11 | `'Not Xor Pen` | Nop |
| 12 | `'Nop` | Merge Not Pen |
| 13 | left out | Copy Pen |
| 14 | `'Copy Pen` | Merge Pen Not |
| 15 | `'Merge Pen Not` | Merge Pen |
| 16 | `'Merge Pen` | Whiteness |

- **The Properties window's names are right:** its drop-down reads `6 - Invert`, `7 - Xor Pen`, `8 - Not Mask Pen`,
  matching the `vb` constants.
- **The shift isn't the Line's own.** A form's `DrawMode = 7` and a Shape's were saved as `7  'Invert` too.
- **No value is saved as `'Whiteness`,** and `'Blackness` is written twice.
- **The comment is only a comment.** The value before it is what VB6 reads back, so the files load as saved.
- **The spacing is two spaces before the `'`** for one digit and two alike (`7  'Invert`, `10  'Mask Pen`).
  `Visible = 0   'False` has three.

### Not measured on SP6

- **The release build's comments,** so whether the DrawMode shift is SP6's or VB6's from the start.
- **A Line inside a container,** whose endpoints are in the container's scale (above).

## A Line's ends follow its container's current scale, whatever unit it was loaded in (2026-11-23, #5530, SP6)

**The question.** The ends of a Line loaded in centimetres follow its PictureBox's ScaleMode as it changes. That's
#4693's row, oracle PR #5514. Does a Line loaded in another unit do the same? Or does it keep the unit it was loaded
in? HexIDE's runtime assumed the second for every unit but centimetres: a Line loaded in twips read 300 after its
container went to pixels (#5530).

**The rig.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM. `/make` built native code and p-code, and each EXE
ran on the guest's console. The IDE ran the p-code project through `vb6.exe /runexit`. The project was one form and
one module. `Sub Main` showed the form, called its `Probe`, and wrote each line to `out.txt` beside the EXE. All
three runs wrote the same 42 lines.

The form, 9200 × 4800 twips, held three borderless PictureBoxes of 2835 × 2835 twips:

| Box | Saved ScaleMode | Its Line, as saved |
|---|---|---|
| `Picture2` | twips (the default, no line) | `X1 = 300`, `Y1 = 300`, `X2 = 1500`, `Y2 = 1500` |
| `Picture3` | `3 'Pixel`, ScaleWidth and ScaleHeight 189 | `X1 = 20`, `Y1 = 20`, `X2 = 100`, `Y2 = 100` |
| `Picture4` | `5 'Inch`, ScaleWidth and ScaleHeight 1.969 | `X1 = 0.25`, `Y1 = 0.5`, `X2 = 1`, `Y2 = 1.25` |

A fourth Line, on the form itself, was saved at (300, 3200)-(1200, 4000) twips.

Each box ran this, with `R` returning `CStr` of `X1`, `Y1`, `X2` and `Y2` joined by `|`:

```vb
Private Sub Box(ByVal nm As String, p As PictureBox, ln As Control)
    Dim m As Variant
    L nm & " init " & R(ln) & " " & TypeName(ln.X1)
    For Each m In Array(1, 3, 5, 6, 7, 4, 2)
        p.ScaleMode = m
        L nm & " mode" & m & " " & R(ln)
    Next
    p.Scale (0, 0)-(100, 50)
    L nm & " user " & R(ln)
    p.ScaleMode = 3
    ln.X1 = 10
    L nm & " wrote10px px " & R(ln)
    p.ScaleMode = 1
    L nm & " wrote10px twips " & R(ln)
End Sub
```

**What came back.** Each read was a Single (`TypeName` printed `Single` for all four Lines).

| Container's ScaleMode now | Line loaded in twips | Line loaded in pixels | Line loaded in inches |
|---|---|---|---|
| as loaded | 300\|300\|1500\|1500 | 20\|20\|100\|100 | 0.25\|0.5\|1\|1.25 |
| 1, twips | 300\|300\|1500\|1500 | 300\|300\|1500\|1500 | 360\|720\|1440\|1800 |
| 3, pixels | 20\|20\|100\|100 | 20\|20\|100\|100 | 24\|48\|96\|120 |
| 5, inches | 0.2083333\|0.2083333\|1.041667\|1.041667 | the same as twips | 0.25\|0.5\|1\|1.25 |
| 6, millimetres | 5.291672\|5.291672\|26.45836\|26.45836 | the same as twips | 6.350006\|12.70001\|25.40002\|31.75003 |
| 7, centimetres | 0.5291672\|0.5291672\|2.645836\|2.645836 | the same as twips | 0.6350006\|1.270001\|2.540003\|3.175003 |
| 4, characters | 2.5\|1.25\|12.5\|6.25 | the same as twips | 3\|3\|12\|7.5 |
| 2, points | 15\|15\|75\|75 | the same as twips | 18\|36\|72\|90 |
| `Scale (0, 0)-(100, 50)` | 10.58201\|5.291005\|52.91005\|26.45503 | the same as twips | 12.69841\|12.69841\|50.79365\|31.74603 |
| `ScaleMode = 3`, then `X1 = 10` | 10\|20\|100\|100 | the same as twips | 10\|48\|96\|120 |
| then `ScaleMode = 1` | 150\|300\|1500\|1500 | the same as twips | 150\|720\|1440\|1800 |

The form's own Line read the same way through `Me.ScaleMode`:

| Form's ScaleMode now | X1\|Y1\|X2\|Y2 |
|---|---|
| 3, pixels | 20\|213.3333\|80\|266.6667 |
| 5, inches | 0.2083333\|2.222222\|0.8333333\|2.777778 |
| 7, centimetres | 0.5291672\|5.64445\|2.116669\|7.055562 |
| 1, twips | 300\|3200\|1200\|4000 |
| `Scale (0, 0)-(100, 50)` | 3.262643\|33.33333\|13.05057\|41.66667 |

**What it shows.**
- **The unit a Line was loaded in only converts its saved numbers to twips.** After that, every read and write goes
  through the container's scale as it stands. That's the twips row of each column, divided by the current mode's
  twips per unit.
- **A Line loaded in pixels and one loaded in twips are the same Line once loaded.** Both columns agree in every row,
  because 20 pixels is 300 twips.
- **The character mode's two axes differ:** 120 twips across and 240 down, so 300 twips reads 2.5 across and 1.25
  down.
- **A write in pixels lands in whole twips.** 10 pixels reads back as 150 in twips.
- **The obvious guess was right this time, but HexIDE's code had made the other one:** only a centimetre-loaded Line
  followed the container's current scale (#5530).
