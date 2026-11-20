## An Image's size when its Picture is set at run time (2026-11-20, #5010, SP6)

**An Image with `Stretch` False takes its picture's size whenever a picture is set, at run time as at design time. Turning `Stretch` back to False takes it again.** HexIDE kept the designed size, so a program that centres an Image from its new `Width` and `Height` centred the wrong box (#5010).

VB6.EXE 6.00.9782 (SP6) and MSVBVM60 6.00.9848. Each probe was built with `/make` as native code and as p-code, and run in session 0. **The two agreed on every row.** The form was never shown, so these are sizes, not drawing: nothing here says how an Image draws its picture.

**The probes.**
- A form designed in twips, with each Image at 330 × 330 twips.
- `Form_Load` writes `Width` and `Height` after each statement, under `On Error Resume Next`, with `Err.Number` beside them. Every row's was 0.
- The pictures are BMPs of 14 × 10 and 40 × 30 pixels, and a classic 32 × 32 icon, 24 bpp with its mask (not a PNG-compressed one).
- `Screen.TwipsPerPixelX` and `Y` read 15.

| Image, then statements, in order | `Width` × `Height` after |
|---|---|
| `Stretch` False; `Set Image1.Picture = LoadPicture(14 × 10 BMP)` | **210 × 150**: the picture's size, 14 × 10 px at 15 twips a pixel |
| `Image1.Picture.Width` × `Height` | 370 × 265, in HIMETRIC |
| `Stretch` True; `Set Image2.Picture = LoadPicture(14 × 10 BMP)` | 330 × 330: unchanged |
| `Image1`, picture loaded; `Width = 600: Height = 600` | 600 × 600 |
| then `Image1.Stretch = True` | 600 × 600; no error, `Stretch` reads True |
| then `Image1.Stretch = False` | **210 × 150**: back to the picture's size |
| then `Set Image1.Picture = LoadPicture()`, empty | 210 × 150: an empty picture leaves the size as it was |
| `Image1` back to 330 × 330; `Image1.Picture = LoadPicture(14 × 10 BMP)`, with no `Set` | 210 × 150: the same as with `Set` |
| `BorderStyle` 1 (Fixed Single); `Set Image3.Picture = LoadPicture(14 × 10 BMP)` | **270 × 210**: the picture plus 2 px on each side |
| `Stretch` False; a 40 × 30 BMP | 600 × 450: it grows past its designed size |
| `Stretch` False; the 32 × 32 icon | 480 × 480. `Picture.Type` 3; `Picture.Width` × `Height` 847 × 847 HIMETRIC |
| `Visible = False`, then a 14 × 10 BMP | 210 × 150: a hidden Image takes the size too |
| then `Set Image1.Picture = Nothing` | 210 × 150: the size stays |

**RDC agrees** on every row it measured, a 14 × 10 PNG under an RDCore target included (#4610, issuecomment-7026728482).

**Not measured:**
- a picture set from the designer's Properties window;
- `BorderStyle` changed at run time;
- how any of these draws.
