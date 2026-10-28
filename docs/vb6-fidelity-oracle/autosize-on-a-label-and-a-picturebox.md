## AutoSize on a Label and a PictureBox (2026-10-27, #922)

**The question.** HexIDE accepts AutoSize on a Label or PictureBox and does nothing with it. What does vb6.exe do?
- When does an AutoSize Label resize, and which edge moves for each Alignment?
- How does WordWrap change that?
- What does a PictureBox take its size from?
- Does an assignment to Width win?

**The probe.** A twips form at 15 twips a pixel holding:
- seven Labels in MS Sans Serif 8.25, each with a Caption in its .frm, and Width and Height of their own;
- a PictureBox with no border, which the program fills with red and saves as a 40 x 30 pixel bitmap;
- two AutoSize PictureBoxes, one with the default Fixed Single border and one with none, which load that bitmap.

The program logs Left, Top, Width and Height after each step. Built native and p-code, and run both in the guest's
console session and in the probe runner's session, and all four logs agreed line for line.

**A Label:**

| Step | Left, Top, Width, Height |
|---|---|
| Every Label as loaded, AutoSize or not: lA, AutoSize True, Caption "Hello", saved as 3000 x 1000 at Top 1400 | 600, 1400, **3000, 1000**: not resized at load |
| lA's Caption set to "Hello, world, a longer caption" | 600, **1395**, 2055, 195 |
| the same, with Alignment Right, from Left 600, Width 3000 | **1545**, 2505, 2055, 195: the right edge stays at 3600 |
| the same, with Alignment Center | **1065**, 3600, 2070, 195: the centre stays at 2100 |
| lA's Caption "Hi" | 600, 1395, 150, 195 |
| lA's Caption `"two" & vbCrLf & "lines"` | 600, 1395, 315, 390 |
| lA's Caption "Hello", then FontSize 18 | 600, 1395, 855, 435 |
| lA's Width set to 5000 | 600, 1395, **5000**, 435: the assignment wins |
| lE, Caption "" in the .frm, set to "x", then back to "" | 600, 5805, 75, 195; then **45**, 195 |
| lNo, AutoSize False, Caption "Hello", 3000 x 1000; AutoSize set True | 600, 300, 360, 195 |
| then AutoSize False, and a longer Caption | 600, 300, 360, 195: no resize |

**A Label with WordWrap True.** lW is 1000 x 300 at Top 4700:

| Step | Left, Top, Width, Height |
|---|---|
| Caption "The quick brown fox jumps over the lazy dog" | 600, 4695, **1005**, 780: the width kept, to the pixel, and four lines |
| Caption "Antidisestablishmentarianism" | 600, 4695, **1995**, 195: wider, to fit one word |
| Caption "Hi" | 600, 4695, **1995**, 195: it does not narrow again |
| Width set to 3000 | 600, 4695, 3000, 195 |

**A PictureBox:**

| Step | Left, Top, Width, Height |
|---|---|
| AutoSize, Fixed Single border, given the 40 x 30 pixel picture | 5000, 1500, **660, 510**: the picture and 2 pixels of border each side |
| AutoSize, no border, the same picture | 5000, 3500, **600, 450** |
| that picture cleared with `LoadPicture()` | 600, 450: no change |
| AutoSize set True with no picture, after Width 3000 | 3000, 510: no change |
| given the picture, then Width set to 3000 | 3000, 510: the assignment wins |

**What it says.**
- **AutoSize acts when the content changes, not when the form loads.** A Label resizes when its Caption or font
  changes, or when AutoSize becomes True. A PictureBox resizes when it is given a picture. The size the .frm saved is
  what loads, and an assignment to Width or Height holds until the next change.
- **The size is whole pixels,** and the edge that stays put is snapped to a pixel too: Top 1400 became 1395.
- **Alignment decides which edge stays:** the left for Left, the right for Right, and the centre for Center.
- **Without WordWrap,** the Label fits its text both ways. With it, the width stays as it is, widening only for a word
  that doesn't fit, and the height fits the lines.
- **An empty Caption is 45 wide,** three pixels, and one line high.
- **A PictureBox fits its picture plus its border.** Clearing the picture leaves the size as it was.
