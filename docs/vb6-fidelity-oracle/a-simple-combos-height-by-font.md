## A Simple Combo's Height, by font, at load and from code (2026-11-21, #1655, `/make`, SP6)

**The question.** #1603 measured a Simple Combo's Height snapping to its edit box plus whole list rows, in VB6's
default font only: MS Sans Serif 8, rows 13 pixels. Does the same rule hold in other fonts, and what are the edit box
and the row there? What does assigning `Height` from code do? And is a Dropdown Combo's or Dropdown List's Height
fixed at the font's height, as was said?

**The probe:**
- **The software:** VB6 SP6, in the Win10 oracle guest at 96 dpi, where one pixel is 15 twips.
- **The form:** 42 ComboBoxes, one per font and saved Height.
  - **Simple Combos (`Style = 1`):** seven fonts, each at saved Heights 400, 900, 1500 and 2000.
  - **The two dropdown styles:** a Dropdown Combo (`Style = 0`) and a Dropdown List (`Style = 2`) in each font, each
    saved at Height 1500.
- **The fonts:** MS Sans Serif 8.25 (no Font block), MS Sans Serif 9.75, Arial 8.25, Arial 12, Tahoma 8.25,
  Courier New 9.75 and Times New Roman 14.25.
- **What it wrote:** each control's Height from `Form_Load`. Then, on the MS Sans Serif 8.25 and Arial 12 Simple Combos,
  `Height = n` for 1000, 1100, 1200, 1500, 2000, 300 and 0, with each read back.
- **The builds:** native and p-code wrote the same 44 lines.

**A Simple Combo at load,** each Height read in twips:

| Font | Saved 400 | Saved 900 | Saved 1500 | Saved 2000 | Edit box E, row R (pixels) |
|---|---|---|---|---|---|
| MS Sans Serif 8.25 | 315 | 765 | 1350 | 1935 | 21, 13 |
| MS Sans Serif 9.75 | 360 | 900 | 1380 | 1860 | 24, 16 |
| Arial 8.25 | 330 | 810 | 1440 | 1860 | 22, 14 |
| Arial 12 | 390 | 720 | 1260 | 1800 | 26, 18 |
| Tahoma 8.25 | 315 | 765 | 1350 | 1935 | 21, 13 |
| Courier New 9.75 | 360 | 900 | 1380 | 1860 | 24, 16 |
| Times New Roman 14.25 | 435 | 810 | 1440 | 1755 | 29, 21 |

**Every row fits one rule.**
- In pixels, the Height is `E + 4 + R × n`, where `n = floor((H − E − 4) / R)` and `H` is the saved Height in pixels.
- When `n` comes out 0 or less, the Height is `E` alone: the edit box, with no list.
- So E and R are the only things that depend on the font. The last column gives them, solved from the four Heights.
- **Two worked rows:**
  - Arial 12 saved at 1500 is 100 pixels, so `n = floor((100 − 30) / 18) = 3`, and the Height is `26 + 4 + 54 = 84`
    pixels, or 1260 twips.
  - Times New Roman saved at 400 is 26.7 pixels, which leaves no room for a row, so the Height is the edit box's 29
    pixels, 435 twips.
- **In all seven fonts, `E = R + 8`.** That's a pattern in these rows, not a rule I'd defend for other fonts.

**A Simple Combo's Height from code** follows the same rule. With no error on any row:

| Font | Assigned 1000 | 1100 | 1200 | 1500 | 2000 | 300 | 0 |
|---|---|---|---|---|---|---|---|
| MS Sans Serif 8.25 | 960 | 960 | 1155 | 1350 | 1935 | 1935, unchanged | 1935, unchanged |
| Arial 12 | 990 | 990 | 990 | 1260 | 1800 | 1800, unchanged | 1800, unchanged |

**The dropdown styles.** A Dropdown Combo and a Dropdown List saved at 1500 read Height 1500 in all seven fonts.

**What was overturned.** #1655 recorded that VB6 "is said to fix" the dropdown styles' Height "at the font's height",
since every earlier probe read 315. Height reads back whatever the file saved. The 315s were what the file held. What
the control draws at a saved 1500 wasn't looked at.

**What resisted explanation.** A saved Height too small for a row gives the edit box alone (400 reads 315). But
assigning 300 or 0 from code leaves the Height where it was, with no error. Where the threshold lies between them
wasn't measured.

**Not measured:**
- fonts other than these seven, bold or italic faces, and other sizes of these fonts;
- a Height assigned between the edit box and one row, such as 400 from code;
- what the designer draws, and what a save writes, beyond #1603's MS Sans Serif rows;
- other dpi settings.

**Where main differs.** Main `3f3ad5742` ran the same `.frm` through a Debug `HexIDE.Standalone` on Windows.
- **Simple Combos:** main keeps every saved Height. It reads 400, 900, 1500 and 2000, except where VB6's rule happens
  to give the saved value back, such as MS Sans Serif 9.75 at 900.
- **Assigning `Height` to a Simple Combo:** main raises error 383 on every row and keeps 1500.
- **The dropdown styles:** main matches, reading 1500.
