## A rounded Shape's corners (2026-11-21, #916, SP6)

**The question.** How round are the corners of Shape 4 (Rounded Rectangle) and Shape 5 (Rounded Square)? One size
can't tell a fraction of the shorter side from a fraction of each side, so several were drawn.

**First probe: a picture.** Eight shapes, each FillStyle 0 (Solid), black, BorderWidth 1, on a white form. They were
Shape 4 and Shape 5 at 100 × 60, 200 × 60, 60 × 200 and 300 × 300 pixels. The form was run on the guest's console and
opened in VB6's designer (VB6.EXE 6.00.9782, SP6, 96 dpi), and the host's framebuffer was captured.
- **Every shape whose shorter side was 60 had the same corner,** inset 5 pixels at the top row and over 5 rows.
- **300 × 300 was inset 33.**
- **Shape 5 in a 100 × 60 rectangle drew a 60 × 60 square, centred.**
- **The designer drew the same corners,** by eye. Its black grid dots touch the shapes, so pixels weren't counted there.

**Second probe: matched against GDI.** A program added 24 Shapes at run time with `Controls.Add`, on a white form
with ScaleMode 3 (pixels). Two seconds after the form showed, it measured each one:
- It read the shape's top-left corner from the screen with `GetPixel`.
- It drew `RoundRect(0, 0, w, h, e, e)` into a memory DC with the stock black pen and brush, for every `e` from 0 to
  the shorter side + 2.
- It listed the values of `e` whose corner matched the screen pixel for pixel, down to half the shorter side.

| Shape | Size, pixels | `e` matching |
|---|---|---|
| 4 | 10 × 10, 15 × 15 | 2, 3, 4 |
| 4 | 20 × 20, 21 × 21 | 5, 6 |
| 4 | 30 × 30 | 7, 8 |
| 4 | 40 × 40 | 9, 10 |
| 4 | 45 × 45 | 11 |
| 4 | 50 × 50 | 12 |
| 4 | 60 × 60, 61 × 61 | 15 |
| 4 | 75 × 75 | 18 |
| 4 | 80 × 80 | 20 |
| 4 | 90 × 90 | 22 |
| 4 | 100 × 100 | 25 |
| 4 | 120 × 120 | 30, 31 |
| 4 | 150 × 150 | 37 |
| 4 | 200 × 200 | 50 |
| 4 | 300 × 300 | 75 |
| 4 | 100 × 60, 60 × 100 | 15 |
| 4 | 301 × 120 | 30, 31 |
| 5 | 100 × 60: drawn 60 × 60 at x + 20 | 15 |
| 5 | 60 × 100: drawn 60 × 60 at y + 20 | 15 |
| 5 | 150 × 150 | 37 |

**The rule: VB6 draws a rounded Shape as `RoundRect` with an ellipse `min(Width, Height) \ 4` pixels across and the
same down.** That is, a quarter of the shorter side, truncated.
- **Every row agrees with it.** 50, 75, 90 and 150 rule out rounding half up, which would give 13, 19, 23 and 38, and
  150 rules out rounding half to even (38).
- **The corner is circular whatever the rectangle's shape.** 100 × 60, 60 × 100 and 301 × 120 match their shorter
  side.
- **A Rounded Square is a square of the shorter side, centred in the rectangle,** with the same corner.

**What was overturned.** A least-squares circle fitted to the first probe's picture gave radii of 8.25 and 38.5
pixels for 60 and 300 (hexide-frm's own first fit). That was close, and wrong: the match against GDI gives 7.5 and
37.5. A circle fitted to pixels can't settle a rule, and only matching against the function VB6 calls could.

### Not measured

- **BorderWidth above 1,** other DrawStyle and DrawMode values, and FillStyle Transparent.
- **Sizes that aren't whole pixels,** in twips or another ScaleMode.
- **A DPI other than 96.**
- **p-code.** It was built as native code only, and the drawing is the runtime's either way.
