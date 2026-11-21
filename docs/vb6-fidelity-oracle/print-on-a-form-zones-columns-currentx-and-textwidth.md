## Print on a form and a PictureBox: zones, columns, CurrentX and CurrentY, TextWidth and TextHeight (2026-11-21, #989, SP6)

**The question.** #989 owed the oracle four things:
- the width of a `,` print zone in the default font;
- where CurrentX and CurrentY end after `Print "a"`, `Print "a";` and `Print "a",`, and after a list that runs past the right edge;
- `TextWidth` and `TextHeight` of `""`, of `"abc"` and of a string holding `vbCrLf`, with their types;
- FontTransparent's default.

**The probe:**
- **The form:** 9600 by 7200 twips with `AutoRedraw` True, shown in session 0, holding a borderless 4000 by 3000 PictureBox. Both were in MS Sans Serif 8.25, the default.
- **Each case:** it set ScaleMode 1 and the font, ran `Cls`, printed, and logged CurrentX and CurrentY.
- **A second project** read each font's `TEXTMETRIC` with `GetTextMetrics` on `Form1.hDC`.
- **The builds:** `/make` on vb6.exe SP6, as native code and as p-code. The two logged the same lines.

**FontTransparent** read True on the form, as [the drawing methods page](the-drawing-methods-line-pset-circle-cls-and-point.md) has it for a form and a PictureBox.

### Where CurrentX and CurrentY end

In MS Sans Serif 8.25, `TextWidth("a")` is 90, `TextHeight` 195, a column 75 and a zone 1050 twips.

| After `Cls`, then | CurrentX, CurrentY |
|---|---|
| `Print "a"` | 0, 195 |
| `Print "a";` | 90, 0 |
| `Print "a",` | **1050**, 0 |
| `Print "a", "b",` | 2100, 0 |
| `Print ,` and `Print , ,` | 1050 and 2100, 0 |
| `Print "abcdefghijklmnopqrstuvwxyz",`, wider than a zone | 2100, 0: the next zone after the text |
| `Print "a"; "b"` | 0, 195 |
| `Print` | 0, 195 |
| `Print "";` | 0, 0 |
| `Print Tab(10); "x";` | 750, 0: column 10 is at 675, and `x` is 75 wide |
| `Print Tab(1); "x";` and `Print Tab(0); "x";` | 75, 0: both are column 1 |
| `Print Tab; "x";` | 1125, 0: the next zone |
| `Print Spc(10); "x";` | 825, 0: **ten columns of 75**, not ten spaces of 45 |
| `Print Spc(0); "x";` | 75, 0 |
| `Print "abc"; Tab(2); "x";` | 150, **195**: column 2 is behind the text, so `Tab` moves to the next line |
| `Print 1; 2;` | 360, 0 |
| `Print -1; "x";` | 255, 0 |
| `Print "a" & vbCrLf & "bb";` | 180, 195 |
| `Print "a" & vbCr & "bb";` and `Print "a" & vbLf & "bb";` | 180, 195: **a lone CR or LF starts a new line too** |
| `Print String$(400, "W");` | 66000, 0: **no wrap** at the right edge, 9600 |
| twelve items, each followed by `,` | 12600, 0: the zones go on past the edge |

`Print 1; 2;` fits each number printed with a space before and after it, as `Str$` gives the sign's space: a digit is 90 wide and a space 45. That's reasoned from the widths, not read.

**Starting from CurrentX 1000 and CurrentY 500:**

| Statement | CurrentX, CurrentY |
|---|---|
| `Print "a"` | 0, 690 |
| `Print "a",` | 2100, 495 |
| `Print "a";` | 1095, 495 |
| `Print Tab(10); "x";` | 750, 690 |

- **A new line starts at x 0,** not at the x the line started from.
- **Print works in whole pixels.**
  - 1000 twips is 66.67 pixels and became 67. 500 is 33.33 and became 33.
  - So `Print "a";` ended at 67 + 6 = 73 pixels, 1095 twips, and CurrentY read 495 with no new line.
  - A new line added 13 pixels to 33, which is 690, not 500 + 195.

**A PictureBox prints the same way:** `Picture1.Print "a",` ended at 1050, 0, and a following `Print "b"` at 0, 195.

### The column, the zone and the font

`Print Tab(2);` reads one column after `Cls`, and `Print ,` one zone:

| Font, as set, and as read | `tmAveCharWidth` | Column | Zone | `tmHeight` | TextHeight |
|---|---|---|---|---|---|
| MS Sans Serif 8.25 | 5 pixels | 75 | 1050 | 13 | 195 |
| MS Sans Serif 10, read 9.75 | 7 | 105 | 1470 | 16 | 240 |
| MS Sans Serif 12 | 8 | 120 | 1680 | 20 | 300 |
| Arial 10, read 9.75 | 6 | 90 | 1260 | 16 | 240 |
| Arial 14, read 14.25 | 8 | 120 | 1680 | 22 | 330 |
| Courier New 10, read 9.75 | 8 | 120 | 1680 | 16 | 240 |
| Times New Roman 12 | 6 | 90 | 1260 | 19 | 285 |
| Tahoma 8, read 8.25 | 5 | 75 | 1050 | 13 | 195 |

- **A column is the font's `tmAveCharWidth`, and a zone is 14 columns,** in all eight fonts.
- **A column isn't any character's TextWidth.** In MS Sans Serif 8.25, `TextWidth("0")` is 90, and the mean over a to z and A to Z is 93.17, against a column of 75.
- **TextHeight is `tmHeight`, with no external leading.** Arial 14.25 and Times New Roman 12 have a pixel of it, and TextHeight leaves it out.
- **In ScaleMode 3,** a zone was 70 pixels and a column 5.

### TextWidth and TextHeight

In MS Sans Serif 8.25, in twips:

| Argument | TextWidth | TextHeight |
|---|---|---|
| `""` | 0 | **195**: an empty string is one line high |
| `"abc"` | 270 | 195 |
| `"a" & vbCrLf & "bbb"` | 270, the widest line | 390 |
| `"a" & vbCr & "bbb"` and `"a" & vbLf & "bbb"` | 270 | 390: a lone CR or LF breaks the line too |
| `vbCrLf` | 0 | 390 |
| `vbCrLf & vbCrLf` | 0 | 585 |
| `"a" & vbCrLf` and `vbCrLf & "a"` | 90 | 390 |
| `vbTab` | 600, eight columns | 195 |
| `" "` | 45 | 195 |
| `123`, a number | 270 | not read |
| `Empty` | 0 | not read |
| `Null` | **94** | **94** |

- **Every value was a Single.**
- **In other ScaleModes** `"abc"` was 18 by 13 in ScaleMode 3, and 0.1875 by 0.1354167 inches in ScaleMode 5: 270 and 195 twips, converted.

**Not measured:**
- a font changed partway along a line;
- a user scale;
- the Printer object;
- the console session.
