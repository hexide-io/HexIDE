## An icon picture's size, and the image it draws (2026-11-14, #2626)

**The question.** [LoadResPicture's icon rows](loadresstring-loadresdata-and-loadrespicture.md) read a Picture 847
HiMetric square from a group holding a 16×16 image alone. Which image does a group with two sizes draw? Does
`LoadPicture` of an `.ico` file size it the same way? And what `.Type`, `.Width` and `.Height` does `LoadPicture` give?

**The probe.** VB6 SP6, `/make`, native code and p-code, which logged the same lines, at 96 DPI. The images were all
32-bit, opaque, and one colour each: every 16×16 image red, every 32×32 image green.
- The `.res` from the rows linked above: group 401 holds the red 16×16 and the green 32×32, group 402 the red 16×16 alone.
- Two `.ico` files: one with the red 16×16 alone, one with both.
- A 2×2, 24-bit `.bmp`.

A picture was read through `Dim p As StdPicture`. To see which image it holds, it was painted with `PaintPicture p, 0, 0`
at its own size into a hidden, AutoRedraw PictureBox with a white background, scaled in pixels. `Point` then read three
pixels.

| Picture | `.Type` | `.Width`, `.Height` | `Point` at (5, 5), (20, 20), (40, 40) |
|---|---|---|---|
| `LoadPicture` of the `.bmp` | 1, Integer | 53, 53, Long | |
| `LoadPicture` of the 16×16 `.ico` | 3, Integer | **423**, 423, Long | 255 (red), 16777215 (white), white |
| `LoadPicture` of the two-size `.ico` | 3, Integer | 847, 847, Long | 65280 (green), green, white |
| `LoadResPicture(401, vbResIcon)`, both sizes | 3 | 847, 847 | green, green, white |
| `LoadResPicture(402, vbResIcon)`, the 16×16 alone | 3 | 847, 847 | **254**, 254, white |

**Readings:**
- **A two-size icon draws its 32×32 image,** from a resource group and from a file alike.
- **A resource icon is always 32 pixels square.** The 16×16 group is stretched to fill 32: (20, 20) is inside the
  picture, and the red reads 254, not 255, from the scaling.
- **`LoadPicture` keeps a file's icon at its own size.** The 16×16 `.ico` is 423 HiMetric and draws 16 pixels, leaving
  (20, 20) white.
- **`LoadPicture`'s `.Type`, `.Width` and `.Height` are an Integer and two Longs,** as `LoadResPicture`'s are, with
  HiMetric at 2540 per 96 pixels: 2 pixels are 53.

**Not measured:** a group or file with a 48×48 or larger image and no 32×32; an icon with fewer colours; a DPI other
than 96.
