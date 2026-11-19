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

### What an AutoSize Label measures its caption by, and which edge stays (2026-11-19, #922, SP6)

**The question.** The rows above give an AutoSize Label's size but not the rule behind it.
- Is the width the caption's text width?
- What do an empty caption, a tab, a mnemonic, a border and a bold or italic font add?
- To the pixel, where does each Alignment put the Label?

**The probes.**
- **The software:** VB6 SP6 6.00.9782 and MSVBVM60 6.00.9848, at 15 twips a pixel.
- **The first two forms:** a twips form with seven AutoSize Labels, each `WordWrap` False and `Alignment` Left:
  - MS Sans Serif 8.25: plain, bold, with a 3-D Fixed Single border, and with a flat one;
  - MS Sans Serif 18, Arial 9.75 and Tahoma 8.25.

  For each caption, the program set the Label's Caption and gave the form the Label's font with `Set Form1.Font =
  lbl.Font`. It then logged the Label's Width and Height beside `Form1.TextWidth` and `TextHeight` of the same text.
- **The third form:** four AutoSize Labels in MS Sans Serif 8.25, Center and Right Justify, each with and without a 3-D
  border.
  - Before each caption, the program set the caption to `zz`, moved the Label to Left 600 and a Width of 3000, 3015
    or 2990, and then set the caption.
  - Two more rows used Left 607.
- **The fourth form:** one Label for each Alignment. They started from a Left of 610 or 615, a Top 10 twips off the
  pixel, and a Width of 600, 615 or 3000.
- **The fifth form:** four WordWrap Labels, and three plain ones in MS Sans Serif 8.25, Arial 9.75 and Tahoma 8.25 for
  line breaks and tabs.
- **The builds:** `/make` as native code and as p-code, in session 0. Each logged the same 202 rows.

**Without a border or an overhang, the Label is the text's size.** Width is `TextWidth` of the caption as drawn, and
Height is `TextHeight`:

| Caption, MS Sans Serif 8.25 | Width | `TextWidth` | Height |
|---|---|---|---|
| `x`, `i`, `l`, `W` | 75, 30, 30, 165 | the same | 195 |
| `Hi`, `Hello` | 150, 360 | the same | 195 |
| `Hello, world, a longer caption` | 2055 | 2055 | 195 |
| `a b`, `" lead"`, `"trail "`, `"two spaces  x"` | 225, 345, 285, 975 | the same | 195 |
| `MMMMMMMMMM`, `iiiiiiiiii` | 1350, 300 | the same | 195 |
| `"two" & vbCrLf & "lines"` | 315 | 315 | 390 |
| `"a" & vbLf & "b"`, `"a" & vbCr & "b"` | 90 | 90 | 390 |

Other fonts matched their own `TextWidth` and `TextHeight` the same way:
- Arial 9.75: `Hello` 435, the long caption 2505, Height 240;
- MS Sans Serif 18: `Hello` 855, Height 435;
- Tahoma 8.25: `Hello` 345, the long caption 2100;
- Arial bold: `Hello` 495, the long caption 2835;
- Arial italic: `Hello` 435, `Hf` 195;
- Tahoma bold: `Hello` 420.

Spaces count, leading and trailing alike. CR, LF and CRLF each end a line.

**An empty caption is one space wide.**

| Font | Empty caption's Width | `TextWidth(" ")` |
|---|---|---|
| MS Sans Serif 8.25 | 45 | 45 |
| Tahoma 8.25 | 45 | 45 |
| Arial 9.75 | 60 | 60 |
| MS Sans Serif 18 | 90 | 90 |

The height stays one line: 195, 195, 240 and 435.

**A tab isn't expanded.** `TextWidth` expands a tab to a tab stop. The Label measures the tab as one more character of
the font, which in MS Sans Serif is as wide as a space and in the TrueType fonts is not:

| Font | `"a" & vbTab & "b"`: Width / `TextWidth` | `"a b"` | So the tab is |
|---|---|---|---|
| MS Sans Serif 8.25 | 225 / 690 | 225 | 45, a space's width |
| Arial 9.75 | 360 / 825 | 270 | 150, where a space is 60 |
| Tahoma 8.25 | 345 / 690 | 225 | 165, where a space is 45 |

`vbTab` alone is 45 wide in MS Sans Serif (`TextWidth` 600) and 150 in Arial (`TextWidth` 720). In MS Sans Serif,
`"tab" & vbTab & "x"` is 345 wide, where `TextWidth` gives 675. That the tab's width is the font's own glyph for the
character is reasoned from these rows, not read from the font.

**Line breaks** each start a line, empty or not, and the widest line sets the width (MS Sans Serif 8.25):

| Caption | Width | Height | `TextWidth`, `TextHeight` |
|---|---|---|---|
| `"a" & vbCrLf` | 90 | 390 | 90, 390 |
| `vbCrLf & "a"` | 90 | 390 | 90, 390 |
| `"a" & vbCrLf & vbCrLf & "b"` | 90 | 585 | 90, 585 |
| `vbCrLf` alone | **15** | 390 | 0, 390 |

`vbCrLf` alone is one pixel wide, where an empty caption is a space wide. Nothing measured here joins the two, and I
have no rule for it.

**A mnemonic follows `UseMnemonic`.**
- **On:** `&File` is 240 wide, the width of `File`. `a&&b` is 270, which is `a&&b`'s 360 less one `&` of 90.
- **Off:** `&File` is 330 wide, its `TextWidth` with the `&` included.

**A border adds to the width and the height alike:**
- **3-D, Fixed Single:** 4 pixels to each, which is 2 on each side. `Hello` is 420 by 255, the long caption 2115 by 255,
  and an empty caption 105 by 255.
- **Flat, Fixed Single:** 2 pixels to each, 1 on each side. `Hello` is 390 by 225.

**MS Sans Serif's bold and italic add an overhang that `TextWidth` leaves out:**

| Caption | Bold: Width / `TextWidth` | Italic: Width / `TextWidth` |
|---|---|---|
| `Hello` | 450 / 435 | 450 / 360 |
| `Hello, world, a longer caption` | 2520 / 2505 | |
| `i` | 60 / 45 | |
| `" "`, and an empty caption | 75 / 60, and 75 | |

- **How much:** bold adds 15 twips, one pixel, and italic adds 90, six.
- **Why, reasoned rather than measured:** MS Sans Serif is a raster font. Windows makes its bold and italic by
  emboldening and slanting it, and reports the extra width as the font's overhang.
- **TrueType adds nothing:** Arial in bold and in italic, and Tahoma in bold, matched `TextWidth` exactly.

**Which edge stays, to the pixel.** The rows above say which edge stays for each Alignment. In these rows, *c* is the
new content's width in pixels: the text, then any overhang, then any border.
- **Positions round to the nearest pixel.**
  - A Label moved to Left 610 and Top 1510 (40⅔ and 100⅔ pixels) and then given a caption reads Left 615 and Top
    1515, with Left Justify.
  - The same Left on Right Justify and Center moved its edge by the same pixel.
- **Left Justify:** the left edge stays, and Width is *c*.
- **Right Justify:** the right edge stays, and Width is *c*. From Left 600 and Width 600, the long caption reads
  Left −855 and Width 2055.
- **Center:** take *L* and *W* as the old Left and Width in whole pixels.
  - The new Width is `2 × ceil(c / 2) + (W mod 2)`.
  - The new Left is `L + floor(W / 2) − ceil(c / 2)`.
  - So the width is *c* rounded up to even, plus one when the old width was odd. The Label grows or shrinks by the
    same amount on each side.

| Center Label, from Left 600 | Old Width (pixels) | *c* | Left, Width |
|---|---|---|---|
| `x` | 3000 (200) | 5 | 2055, **90** |
| `x` | 3015 (201) | 5 | 2055, **105** |
| `Hi` | 3000 (200) | 10 | 2025, 150 |
| `Hi` | 3015 (201) | 10 | 2025, **165** |
| `i` | 3000 (200) | 2 | 2085, 30 |
| An empty caption | 3000 (200) | 3 | 2070, **60** |
| An empty caption, 3-D border | 3000 (200) | 7 | 2040, **120** |
| The long caption | 600 (40) | 137 | **−135**, 2070 |
| The long caption | 615 (41) | 137 | −135, **2085** |

- **Every Center and Right Justify row fits these rules,** bordered or not, growing or shrinking, from Widths of 3000,
  3015 and 2990.
- **Assigning the caption it already has resizes nothing.** A Label moved back to Width 3000 and given its own caption
  `x` again kept Width 3000, for each Alignment.
- **What the rows don't separate:** rounding the right edge, against rounding Left and Width apart. Every row gave the
  same pixel either way.
- **When the size and place come out the same in pixels, nothing is re-read.**
  - With WordWrap, a Left Justify Label at Width 1000 (66⅔ pixels) given `Hi` kept Width 1000. The size it worked out
    was the 67 by 13 pixels it already had.
  - Given the four-line sentence, its height changed, and it read Width 1005.
  - Whenever the rectangle changes, all four values are read back from whole pixels, Top included.

**With WordWrap.** The earlier rows say the width stays, widens only for a word that doesn't fit, and never narrows.
Four WordWrap Labels, Left, Center and Right Justify and a Center one with a 3-D border, were moved to Left 600, Height
195 and a Width of 1000 or 990 (67 or 66 pixels) before each sequence. Center and Right Justify follow the rules above, with *c* in pixels taken as the larger of the Label's old width
and its widest word plus any border:

| Center, WordWrap, from Left 600 | Old Width (pixels) | *c* | Left, Width, Height |
|---|---|---|---|
| The sentence, four lines | 1000 (67) | 67 | **585, 1035**, 780 |
| then `Antidisestablishmentarianism` | 1035 (69) | 133 | 90, 2025, 195 |
| then `Hi` | 2025 (135) | 135 | **75, 2055**, 195 |
| The sentence, four lines | 990 (66) | 66 | 600, 990, 780 |
| then the long word | 990 (66) | 133 | 90, 2010, 195 |
| then `Hi` | 2010 (134) | 134 | 90, 2010, 195 |

| Right Justify, WordWrap, from Left 600 | Old Width (pixels) | *c* | Left, Width |
|---|---|---|---|
| The sentence | 1000 (67) | 67 | 600, 1005 |
| then the long word | 1005 (67) | 133 | −390, 1995 |
| then `Hi` | 1995 (133) | 133 | −390, 1995 |

- **A Center Label of odd width grows by two pixels on every caption change,** one on each side, because the width it
  works out from is already its own odd width. That is what the formula gives, and what vb6.exe did.
- **A 3-D border** adds its 4 pixels to *c*: Center from 1000 widened to 2085, then 2115.
- **All 32 WordWrap rows fit,** Left Justify's and the bordered Label's included.

**The rule these rows fit:**
- **Width:** `TextWidth` of the caption as drawn, plus the font's overhang, plus the border.
  - An empty caption measures as one space. A tab isn't expanded, and measures as the font's own character.
  - With `UseMnemonic` on, each lone `&` is dropped and `&&` measures as one `&`.
  - With WordWrap, the width is the larger of the old width and the widest word.
  - Center then rounds it, as above.
- **Height:** `TextHeight`, one line for each line break, plus the border.
- **Placing it:** round to the pixel, then keep the edge or the centre that Alignment names. If that leaves the
  rectangle as it was, nothing changes.

**Not measured:**
- other raster fonts, other sizes of MS Sans Serif in bold or italic, and bold italic together;
- a display other than 15 twips a pixel;
- which words go on which line with WordWrap. Only the line counts were read, not where each line broke;
- a caption of only line breaks other than `vbCrLf`;
- an AutoSize Label at design time.
