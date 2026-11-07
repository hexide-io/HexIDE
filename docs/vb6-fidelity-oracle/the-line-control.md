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

