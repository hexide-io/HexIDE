## Drawing a control into a Frame or PictureBox (2026-10-26, #2039, design-time)

**The probe.** VB6 SP6's designer on the oracle VM's console, with the default 120-twip (8-pixel) grid. CommandButtons
were drawn with the mouse (press, move, release) by a drag helper, and the form was saved with Ctrl+S. The form held:
- **Frame1:** Left 600, Top 600, 3600 × 2400;
- **Picture1, inside Frame1:** Left 1800, Top 600, 1500 × 1200, with a 2-pixel border;
- **Command0, on the form.**

A second run made Picture1 1395 × 1095, which puts its client area off the grid both ways.

| Case | Where it went | Saved |
|---|---|---|
| a: pressed and released inside Frame1 | Frame1 | Left 240, Top 1200, 1215 × 735 |
| b: pressed inside Frame1, released on the form beyond it | **Frame1** | Left 480, Top 1920, 3135 × 495 |
| c: pressed on the form, released inside Frame1 | **the form** | Left 240, Top 240, 1455 × 1455 |
| d: inside Picture1, which is inside Frame1 | Picture1 | Left 240, Top 240, 855 × 615 |
| e: Frame1 selected, then the tool double-clicked | **the form**, centred | Left 2400, Top 2040 |
| g: Picture1 selected, then the tool double-clicked | **the form**, centred | Left 2400, Top 2040 |
| i: pressed inside Picture1, released far outside Frame1 | Picture1 | Left 240, Top 240, 1215 × 975 |
| j: as i, Picture1 1395 × 1095 | Picture1 | Left 240, Top 240, 1095 × 855 |
| k: pressed inside Picture1, dragged up and left out of it, over Frame1 | Picture1 | **Left 0, Top 0**, 1095 × 855 |
| f: Command0 dragged from the form into Frame1 | **the form** | Left 1320, Top 840 |

**The rules:**
- **The press point picks the container:** the innermost Frame or PictureBox under it. Where the drag ends doesn't matter
  (b and c).
- **The drag is held to that container.** Measured from the container's client origin, the far corner stops at the
  container's **outer** `Width` and `Height`, snapped down to the grid:

  | Container | Outer size, px | Snapped down | Far edge saved |
  |---|---|---|---|
  | Frame1 (b) | 240 × 160 | 240 × 160 | 241 × 161 |
  | Picture1 (i) | 100 × 80 | 96 × 80 | 97 × 81 |
  | Picture1 (j) | 93 × 73 | 88 × 72 | 89 × 73 |

- **Up and left, the drag stops at the container's client origin** (k): the control starts at 0, 0 there.
- **The corners snap on the container's own grid,** measured from its client origin rather than from the form. In k the
  pressed corner, at 71, 58 from Picture1's client origin, became 72, 56.
- **Each corner snaps to the nearest grid line,** not the one below it. In k, 71 became 72. In c, the press 14 pixels
  into the form became 16, which `Left = 240` records.
- **A drawn control ends one pixel past its far grid line,** in every case, held or not: a is 81 × 49 px, 80 + 1 by
  48 + 1, and c is 97 × 97. That's the same pixel that makes the default CommandButton 1215 × 495 twips (81 × 33 px). It
  explains the "+ 1" in the table above.
- **A double-click on the toolbox places on the form,** whatever container is selected (e, g).
- **Moving a control never changes its container** (f).

**What was overturned:**
- **I guessed the stop was the container's client area.** It isn't. Picture1's client height was 76 and then 69 px,
  yet the drags reached 80 and 72. The stop uses the outer size measured from the client origin, so a control drawn in a
  bordered PictureBox can run a few pixels past its client area, where it's drawn clipped.
- **A rule I proposed from the first run is also wrong:** "the extent rounded **up** to the grid, plus a pixel". It fitted
  the four edges then measured, two of which were already on the grid. The off-grid Picture1 rules it out.

**The grid off, and the rounding (#2058).** Three more drags on the same form, whose client origin the saved positions
put at (126, 179) on screen:

| Case | Grid | Pressed, released (client px) | Saved | px |
|---|---|---|---|---|
| n1 | on | 5, 14 → 50, 30 | Left 120, Top 240, 615 × 255 | 8, 16; 41 × 17 |
| n2 | off | 5, 14 → 50, 31 | Left 75, Top 210, 690 × 270 | 5, 14; 46 × 18 |
| n3 | off | 314, 16 → 335, 53 | Left 4710, Top 240, 330 × 570 | 314, 16; 22 × 38 |

- **n1 decides the rounding.** 5 went to 8 and 14 to 16; snapped down, they'd have been 0 and 8.
- **n2 and n3 show the extra pixel doesn't come from the grid.** With the grid off, a 45 × 17 span is 46 × 18, and a
  21 × 37 span is 22 × 38.

**In HexIDE.** A drawn control:
- goes into the innermost container under the press;
- has its corners snapped to the nearest line of that container's grid;
- is held on every side;
- ends one pixel past its far corner.

`add_control` behaves as a drag begun at its top-left, and keeps the size it's given.

**Not measured:**
- a corner exactly half way between two lines (HexIDE sends it up, as a double-click's placement goes, #1473);
- a container that isn't on the grid itself.
