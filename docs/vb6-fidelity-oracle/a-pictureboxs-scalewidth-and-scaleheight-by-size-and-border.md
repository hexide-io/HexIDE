## A PictureBox's ScaleWidth and ScaleHeight, by size and border (2026-11-21, #5162, `/make`, SP6)

**The question.** Oracle PR #5161 saw in passing that a PictureBox saved with `Width = 3100` reads `ScaleWidth` 3045
in VB6, where HexIDE reads 3040. Where do the 5 twips come from? Does it depend on the border and appearance, and on
whether the size is a whole number of pixels?

**The probe:**
- **The software:** VB6 SP6, in the Win10 oracle guest at 96 dpi, where one pixel is 15 twips.
- **The form:** 12 PictureBoxes in twips scale.
  - **Widths:** 3090, 3100, 3105 and 3110. Each Height is its Width less 1500: 1590, 1600, 1605 and 1610.
  - **Borders:** each width three ways, with the default Fixed Single border and 3D appearance, Fixed Single and Flat,
    and no border.
- **What it wrote:** each box's Width, Height, ScaleWidth and ScaleHeight from `Form_Load`, and ScaleWidth in pixels.
  Then it assigned Width to a 3D box (3100, 3105, 3110, 3090, 3000) and to a borderless one (3105, 3110), reading each
  back.
- **The builds:** native and p-code wrote the same 19 lines.
- **Where it ran:** each EXE ran in the guest's session 0, through PowerShell Direct, where no form is shown. Every value was read in `Form_Load`, before a form would show anyway, from control windows that Windows creates hidden. That the values don't depend on display is reasoned, not checked on the console.

**At load**, ScaleWidth and ScaleHeight in twips, with pixels in brackets. Width and Height read back as saved in
every row.

| Saved Width, Height | Fixed Single, 3D | Fixed Single, Flat | No border |
|---|---|---|---|
| 3090, 1590 (206, 106 px) | 3030 (202), 1530 (102) | 3060 (204), 1560 (104) | 3090 (206), 1590 (106) |
| 3100, 1600 (206.67, 106.67 px) | 3045 (203), 1545 (103) | 3075 (205), 1575 (105) | **3105** (207), **1605** (107) |
| 3105, 1605 (207, 107 px) | 3045 (203), 1545 (103) | 3075 (205), 1575 (105) | 3105 (207), 1605 (107) |
| 3110, 1610 (207.33, 107.33 px) | 3045 (203), 1545 (103) | 3075 (205), 1575 (105) | 3105 (207), 1605 (107) |

**On assignment**, the same: a 3D box at 3100, 3105 and 3110 reads ScaleWidth 3045. At 3090 it reads 3030, and at
3000 it reads 2940. A borderless box at 3105 and 3110 reads 3105. Width reads back as assigned in every row.

**What it says:**
- **The inside is whole pixels.** It's the size in pixels rounded to the nearest whole pixel, less the border on both
  sides: 2 pixels a side for the 3D Fixed Single border, 1 for Flat, and 0 with no border.
  - 206.67 pixels rounds up to 207, and 207.33 rounds down to 207.
  - A half pixel, such as 3097.5 twips, wasn't tried, so which way it rounds isn't known.
- **Width and Height stay as saved or assigned.** Only the inside is rounded.
- **So without a border, ScaleWidth can exceed Width:** 3105 against 3100. That row rules out any rule taking the
  border off an unrounded width.

**Not measured:**
- a half-pixel size;
- a PictureBox in another ScaleMode, or on a form that isn't twips;
- a form's own ScaleWidth, and other containers such as a Frame;
- other dpi settings.

**Where main differs.** Main `3f3ad5742` ran the same `.frm` through a Debug `HexIDE.Standalone` on Windows.
- **Whole-pixel sizes (3090, 3105):** every row matches.
- **Other sizes:** main takes the border off the unrounded size.
  - At 3100 it reads 3040 (3D), 3070 (Flat) and 3100 (no border), against VB6's 3045, 3075 and 3105.
  - At 3110 it reads 3050, 3080 and 3110, against 3045, 3075 and 3105.
  - Assignments at 3100 and 3110 differ the same way.

## Half-twip sizes: a PictureBox's Width and Height are whole twips first (2026-11-22, #5162, `/make`, SP6)

**The question.** The rows above found that a PictureBox's inside is whole pixels, but left a half-pixel size untried,
so which way 206.5 pixels rounds wasn't known. Is it up, down, or to even?

**The probe.**
- **The rig:** as above. VB6 SP6 in a clone of the oracle guest at 96 dpi, where one pixel is 15 twips. Each EXE ran in
  the guest's session 0 through PowerShell Direct, and every value was read in `Form_Load`.
- **The form:** nine PictureBoxes saved with `Width` 3082.5, 3097.5 and 3112.5, which are 205.5, 206.5 and 207.5
  pixels. Each `Height` is its Width less 1500. Each size came three ways: Fixed Single and 3D, Fixed Single and Flat,
  and no border. The three half pixels tell half-up, half-down and half-to-even apart.
- **The assignments:** `Width` and `Height` (Width less 1500) assigned to a 3D box and a borderless one, at 3082.5,
  3097.5, 3112.5, 3090 and 3105.
- **What it wrote:** each box's `Width`, `Height`, `ScaleWidth` and `ScaleHeight` through `CStr`, and `ScaleWidth` in
  pixels.
- **The builds:** native and p-code wrote the same 21 lines.

**At load.** ScaleWidth and ScaleHeight in twips, with ScaleWidth in pixels in brackets:

| Saved Width, Height | Read back | Fixed Single, 3D | Fixed Single, Flat | No border |
|---|---|---|---|---|
| 3082.5, 1582.5 | **3082, 1582** | 3015 (201), 1515 | 3045 (203), 1545 | 3075 (205), 1575 |
| 3097.5, 1597.5 | **3098, 1598** | 3045 (203), 1545 | 3075 (205), 1575 | 3105 (207), 1605 |
| 3112.5, 1612.5 | **3112, 1612** | 3045 (203), 1545 | 3075 (205), 1575 | 3105 (207), 1605 |

**On assignment,** the same. Width, then ScaleWidth for the 3D box and the borderless one:

| Assigned | Width | 3D ScaleWidth | No border ScaleWidth |
|---|---|---|---|
| 3082.5 | 3082 | 3015 | 3075 |
| 3097.5 | 3098 | 3045 | 3105 |
| 3112.5 | 3112 | 3045 | 3105 |
| 3090 | 3090 | 3030 | 3090 |
| 3105 | 3105 | 3045 | 3105 |

Heights followed the same rule in every row.

**What it says:**
- **Width and Height are whole twips.** A half twip goes to the even neighbour: 3082.5 to 3082, 3097.5 to 3098 and
  3112.5 to 3112, from the file and on assignment alike. Half-up would give 3083 and 3113, and half-down 3097, so only
  half-to-even fits all three rows. *Width and Height stay as saved or assigned*, above, holds for whole twips.
- **So a half pixel never happens.** A whole number of twips is never 7.5 past a multiple of 15. The inside is the
  whole-twip size in pixels, rounded to the nearest pixel, less the border, and no tie is left to break.
- **What this overturned:** the probe was written to find which way 206.5 pixels rounds. That question has no answer,
  because the size never reaches the pixels with a half in it.

**Where main differs.** Main `ef65c5d96` ran the same `.frm` through a Debug `HexIDE.Standalone` on Windows.
- **Width and Height read back as VB6 reads them** in every row, 3082, 3098 and 3112 included.
- **At load, the inside keeps the half twip, and isn't whole pixels.** The 3D box at 3082.5 reads `ScaleWidth` 3022.5,
  where VB6 reads 3015, and the borderless box at 3097.5 reads 3097.5, where VB6 reads 3105.
- **On assignment, the inside is whole twips but not whole pixels.** The 3D box assigned 3082.5 reads 3022, where VB6
  reads 3015. Assigned 3090 and 3105, whole pixels, main matches.

**Not measured:**
- a half twip on another control, or on a form;
- `Left` and `Top` with a half twip;
- a half unit in another `ScaleMode`, such as 206.5 assigned in pixels;
- other dpi settings.
