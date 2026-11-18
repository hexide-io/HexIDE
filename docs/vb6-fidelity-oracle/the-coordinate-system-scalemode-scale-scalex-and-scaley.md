## The coordinate system: ScaleMode, Scale, ScaleX and ScaleY (2026-11-18, #990, SP6)

**The question.** What do a running form's and PictureBox's Scale properties read? What do `Scale`, `ScaleMode` and the
four Scale properties do to one another? How are coordinates rounded and kept? What do `ScaleX` and `ScaleY` return,
and what do they refuse?

**The probe:**
- **The software:** VB6 SP6 (6.00.9782) on Windows 10 at 96 dpi, where `Screen.TwipsPerPixelX` and `TwipsPerPixelY`
  are both Single 15. The user locale was en-GB.
- **The form:** a Standard EXE form with ClientWidth 4680 and ClientHeight 3195. It held Picture1, with Left 2160, Top
  1200, Width 2415, Height 1815 and the default border, and a Timer.
- **The program:** the Timer's first tick ran every probe once, under `On Error Resume Next`. It logged each value as
  `TypeName` and value, and each error as `Err.Number`, to a file.
- **The runs:** two such programs in the guest's console session, with the form showing. Each was built as p-code and
  as native code, and each pair logged the same lines.
- **Compile-time questions:** small projects, each read from `/make`'s error log.

The drawing methods themselves are on [their own page](the-drawing-methods-line-pset-circle-cls-and-point.md).

### On a new form and PictureBox

| Property | Form | Picture1 |
|---|---|---|
| `ScaleMode` | Integer 1 | Integer 1 |
| `ScaleLeft`, `ScaleTop` | Single 0, 0 | Single 0, 0 |
| `ScaleWidth`, `ScaleHeight` | Single 4680, 3195: the client area | Single 2355, 1755: inside its 2-pixel border |
| `Width`, `Height` | Single 4920, 3780 | Single 2415, 1815 |

### Each ScaleMode, from twips

| `ScaleMode =` | `ScaleWidth` | `ScaleHeight` |
|---|---|---|
| 0, user | 4680 | 3195: the current scale, kept as a user scale |
| 1, twips | 4680 | 3195 |
| 2, points | 234 | 159.75 |
| 3, pixels | 312 | 213 |
| 4, characters | 39 | 13.3125: 120 twips a character across, 240 down |
| 5, inches | 3.25 | 2.21875 |
| 6, millimetres | 82.55008 | 56.3563 |
| 7, centimetres | 8.255008 | 5.635631 |

`ScaleLeft` and `ScaleTop` stayed 0 in every mode, and each value was a Single.

### One Scale property at a time

| From twips | Then |
|---|---|
| `ScaleWidth = 1000` | ScaleMode **0**, ScaleWidth 1000, ScaleHeight still 3195, ScaleLeft and ScaleTop 0 |
| then `ScaleMode = vbTwips` | ScaleWidth 4680 and ScaleHeight 3195 again |
| `ScaleLeft = 50` | ScaleMode **0**, ScaleLeft 50, ScaleWidth 4680, ScaleHeight 3195 |
| `ScaleWidth = -100` | accepted: ScaleMode 0, ScaleWidth -100 |
| `Scale`, with no arguments | ScaleMode 1, back to twips: 0, 0, 4680, 3195 |

### Scale (x1, y1)-(x2, y2)

- **`Scale (0, 100)-(10, 0)`:** ScaleMode 0, ScaleLeft 0, ScaleTop 100, ScaleWidth 10, **ScaleHeight -100**.
  - A red `Line (0, 0)-(10, 100)` drawn in it ran from the bottom left to the top right. Read back in pixels after
    `ScaleMode = vbPixels`, `Point(0, 213)` was red. That's the row just below the client area's last, row 212. The
    four corner pixels of the client area were the background.
  - So switching ScaleMode afterwards didn't move what was drawn: it was where the user scale had put it.
- **`Scale (10, 10)-(110, 60)`:** ScaleLeft 10, ScaleTop 10, ScaleWidth 100, ScaleHeight 50.
  - **The controls on the form are read in the same scale.** Picture1 read Left 56.15385, Top 28.77934 and Width
    51.60257: its 2160, 1200 and 2415 twips, as `10 + 2160 * 100 / 4680` and so on.
  - In pixels, they read 144, 80 and 161.

### A resize

| Mode | Change | Then |
|---|---|---|
| twips | `Width + 1500`, `Height + 750` | ScaleWidth 6180, ScaleHeight 3945: the client area, grown |
| user, `Scale (0, 0)-(100, 100)` at that size | the form shrunk back by the same | ScaleMode still 0, ScaleWidth **75.72816**, ScaleHeight **80.98859** |
| pixels | `Width + 1500` | ScaleWidth 412 |

**A user scale's unit keeps its size when the form resizes,** so the width counts more or fewer of them:
4680 / 6180 * 100 = 75.728.

### CurrentX and CurrentY are kept in whole twips, rounded half to even

| In twips, set | Read back |
|---|---|
| 7.4 | 7 |
| 7.5 | 8 |
| 7.6 | 8 |
| 0.49 | 0 |
| 0.5 | **0** |
| 1.25 | 1 |
| 1.5 | 2 |
| 2.5 | **2** |
| -0.5 | 0 |
| -1.5 | -2 |

CurrentX and CurrentY read the same every time.

| In pixels, set | Read back | Because |
|---|---|---|
| 0.37 | 0.4 | 5.55 twips kept as 6 |
| 0.5 | 0.5333334 | 7.5 twips kept as 8 |
| 1.5 | 1.466667 | 22.5 kept as 22 |
| 2.5 | 2.533333 | 37.5 kept as 38 |
| 0.0333 | 0 | 0.4995 kept as 0 |

- **The same rounding meets a user scale.** After `Scale (0, 100)-(10, 0)`, `PSet (5, 50)` left CurrentY at
  **49.98435**: y 50 is 1597.5 twips from the top, kept as 1598.
- **A change of scale doesn't move them.** CurrentX and CurrentY set to 7 in twips read 0.4666667 in pixels. After
  `Scale (0, 0)-(100, 100)` they read 0.1495727 and 0.2190923, and back in twips, 7 and 7 again.
- The drawing methods' pixels follow the same whole twips, and `Point` doesn't. That's on the drawing page.

### ScaleX and ScaleY

| Call, with the form in twips unless it says | Result |
|---|---|
| `ScaleX(1, vbInches, vbTwips)` | Single 1440 |
| `ScaleX(100, vbPixels, vbTwips)`, `ScaleY(100, vbPixels, vbTwips)` | Single 1500, 1500 |
| `ScaleX(1000)`, `ScaleX(1000, vbHimetric)` | Single 566.9291, both |
| `ScaleX(1, vbCentimeters, vbMillimeters)` | Single 10 |
| `ScaleX(1, vbCharacters, vbTwips)`, `ScaleY(1, vbCharacters, vbTwips)` | Single 120, 240 |
| `ScaleX(1, vbPoints, vbTwips)` | Single 20 |
| `ScaleX(1, vbUser, vbTwips)` | **error 5** |
| the same in `Scale (0, 0)-(100, 100)` | Single 46.8 |
| `ScaleX(10)` in that scale | Single 0.1211387 |
| `ScaleX(1, 11, vbTwips)` | error 5 |
| `vbContainerPosition` or `vbContainerSize`, either argument, on the form or Picture1 | **error 5**, all 14 calls |

- **The arguments' defaults:** with both omitted, the value is HiMetric, converted to the object's current scale.
  `ScaleX(1000)` gave twips with the form in twips, and `ScaleX(10)` gave user units in a user scale.
- **`vbUser` needs a user scale:** with the form in twips, error 5.
- **The container scales:** the 14 calls ran with the form in twips, in pixels and in `Scale (0, 0)-(100, 100)`. Each
  raised 5, on the form and on a PictureBox placed on it.

### Errors

| Statement | Result |
|---|---|
| `ScaleMode = 8`, `ScaleMode = -1` | 380, Invalid property value |
| `ScaleWidth = 0`, `ScaleHeight = 0` | 380 |
| `ScaleWidth = -100` | accepted |
| `Scale (0, 0)-(0, 10)` | **11, Division by zero**, and the scale is left as it was |
| `CurrentX = 1E+30` | 6, Overflow, and CurrentX is left as it was |

### At compile time

- **`Scale` is a reserved word.** Each of these fails to compile:
  - `Dim Scale As Integer`, in a module or in a form's procedure: Syntax error;
  - `Public Sub Scale()`: "Expected: identifier";
  - a label `Scale:`: Syntax error.
- **`Scale (0, 0)-(1, 1)` in a standard module:** "Method not valid without suitable object".
- **What does build:** `Form1.Scale (0, 0)-(1, 1)` in a module, and `o.Scale (0, 0)-(1, 1)` with `o` declared As
  Object. Neither was run.

### Not measured

- a `.frm` that saves a ScaleMode other than 1, as it loads;
- a PictureBox resized;
- `ScaleX` between two modes other than those above;
- a display other than 96 dpi;
- a late-bound `o.Scale` at run time.
