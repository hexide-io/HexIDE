## A CommandButton's caption: where it sits, where it's clipped, and how it draws disabled (2026-11-20, #4551, #4586, SP6)

**The question.** HexIDE draws a short CommandButton's caption low and clipped (#4551), and a disabled CommandButton
half-transparent (#4586). Where does VB6 put a caption in the button, where does it clip it, and what does a disabled
button look like?

**The probe:**
- **The software:** VB6 SP6 (6.00.9782), in the Win10 oracle guest at 100% (96 DPI), built with `/make`. The program
  ran in the guest's console session, and the screen was captured through the VM's console 8 seconds after start.
- **The look:** the EXE has no common-controls manifest, so Windows draws its buttons in the classic style, not the
  themed one.
- **The form:** a yellow form (`BackColor = &H0000FFFF&`) holding default-font CommandButtons, all `Style` 0:
  - `aZ_g`, 1200 twips wide, at each Height from 300 down to 180 in steps of 15;
  - three more `aZ_g` buttons in Tahoma 8.25, at Heights 330, 315 and 300;
  - `Enabled` and `Disabled`, 1500 × 495, in the default font and in MS Sans Serif 12 bold;
  - `Disabled` in Arial 14.25.
- **The colours:** the program logged `GetSysColor`:

  | Index | Name | Value |
  |---|---|---|
  | 15 | COLOR_BTNFACE | F0F0F0 |
  | 16 | COLOR_BTNSHADOW | A0A0A0 |
  | 17 | COLOR_GRAYTEXT | 6D6D6D |
  | 20 | COLOR_BTNHIGHLIGHT | FFFFFF |
  | 21 | COLOR_3DDKSHADOW | 696969 |
  | 22 | COLOR_3DLIGHT | E3E3E3 |

  The console capture is 16-bit, so a captured colour reads as the nearest RGB565 value, such as F7F3F7 for F0F0F0.
  Each row below names the system colour whose quantised value it matched.
- **The builds:** native code and p-code drew the form identically, pixel for pixel.
- **The fonts' metrics:** read through GDI on Windows 11, not in the guest.
  - MS Sans Serif 8.25 is 11 px: `tmHeight` 13. `aZ_g` measures 25 px wide, and its ink runs from row 2 to row 12 of
    its line.
  - Tahoma 8.25: `tmHeight` 13. `aZ_g`'s ink runs from row 3 to row 12.
  - MS Sans Serif 12 bold is 16 px: `tmHeight` 20. `Enabled` measures 66 px wide, and its ink runs from row 4 to row
    15, and from column 2 to column 64.

### Where the caption sits

Rows and columns count from the button's outer top-left pixel. The ink rows are the caption's black pixels.

| Height (twips, px) | Ink rows | Rows drawn |
|---|---|---|
| 300, 20 | 5–15 | all |
| 285, 19 | 4–14 | all |
| 270, 18 | 4–14 | all |
| 255, 17 | 3–13 | all |
| 240, 16 | 3–13 | all |
| 225, 15 | 2–12 | all |
| 210, 14 | 2–11 | the bottom row is cut: the underscore and the foot of the `g` |
| 195, 13 | 2–10 | the bottom two rows are cut |
| 180, 12 | 2–9 | the `Z`'s top bar and the bottom three rows are cut |

- **Across:** every `aZ_g` button, 80 px wide, has ink in columns 28–51. Bold `Enabled`, 100 px wide, has ink in
  columns 18–80.
- **Tahoma:** ink rows 7–16 at 22 px, 6–15 at 21 px, and 6–15 at 20 px.
- **Bold `Enabled`,** 33 px high: ink rows 10–21.

**The rule.** The caption's line starts at `trunc((W − 1 − w) / 2)` across and `trunc((H − 1 − h) / 2)` down. *W* and
*H* are the button's size in pixels, *w* is the caption's extent and *h* is the font's `tmHeight`. `trunc` rounds
towards zero, as C's integer division does.
- **The default font:** the line starts at `trunc((H − 14) / 2)`, so the ink starts two rows lower. That gives every
  row in the table. At 180 the line starts at −1, which puts the `Z`'s top bar at row 1, inside the border.
- **Rows that rule out the near alternatives:**
  - At 285, centring in the whole height, `(H − h) / 2`, would give 3 where VB6 gave 2.
  - At 195, `floor` would give −1 where VB6 gave 0.
  - At 225, centring between rows 1 and *H* − 2 would give 1 where VB6 gave 0.
  - Bold `Enabled` starts its line 16 columns in, where `(W − w) / 2` gives 17.
- **Tahoma** has the same `tmHeight`, so it agrees with the rule without testing anything new. Its ink starts a row
  lower, as its metrics say.

**The clip.** The caption is cut at the inside of the button's 2-pixel edge, top and bottom alike. In every row, ink
appears only from row 2 to row *H* − 3. The edge itself is never painted over.

### A disabled button

| What | Enabled | Disabled |
|---|---|---|
| The face | COLOR_BTNFACE | COLOR_BTNFACE. It's opaque: no yellow from the form shows through. |
| The edge | as below | the same pixels as an enabled button that isn't the default |
| The caption | black | COLOR_BTNSHADOW, with a COLOR_BTNHIGHLIGHT copy one pixel right and one pixel down, drawn under it |

- **The highlight copy:** every white pixel inside the face is a caption pixel moved by (+1, +1): 99 of 99 in the
  default font, and 174 of 174 in 12 bold. Where the copy falls on the caption itself, the caption's grey wins.
- **The position:** a disabled caption sits where an enabled one would. `Disabled` in the default font, at 100 × 33,
  starts its ink at row 11, which is the rule's 9 plus the font's 2.
- **Arial** draws the same way: grey with a white copy.
- **The edge,** in both states:
  - the top and left lines are COLOR_BTNHIGHLIGHT outside, then COLOR_3DLIGHT;
  - the bottom and right lines are COLOR_BTNSHADOW inside, then COLOR_3DDKSHADOW outside.

  So the disabled caption takes the same grey as the edge's inner shadow.
- **The caption is grey but isn't COLOR_GRAYTEXT:** that is 6D6D6D, and no disabled caption pixel matched it.

**Not measured:**
- the VB6 IDE's designer, which these rows came from an EXE rather than;
- a themed (manifested) program;
- a caption that wraps;
- a pressed button;
- `Style` 1 (graphical);
- the default button's own caption position, inside its black frame;
- scaling other than 100%.
