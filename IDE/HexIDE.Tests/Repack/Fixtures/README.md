# macOS icon fixtures

Apple's own encoding of HexIDE's 16- and 32-pixel icon images, for testing the icon writer's ARGB elements (#4493).

| File | What it is |
|---|---|
| `stock-16.png`, `stock-32.png` | The 16- and 32-pixel PNGs stored in stock's `IDE/HexIDE/Icons/AppIcon/hexide.icns` (its `icp4` and `icp5`), unchanged. |
| `apple-iconutil-ic04-ic05.icns` | The `ic04` and `ic05` elements of the icon Apple's `iconutil -c icns` wrote from those images and the other eight sizes, with every other element removed. |
| `apple-iconutil-alpha-ramp-ic04.icns` | The `ic04` element `iconutil -c icns` wrote for a 16-pixel image whose every pixel is (200, 100, 50), with alpha `17 * x` along each row, with every other element removed. |

Made on macOS 15.5 (24F74), M4, on 2026-11-17. `iconutil` wrote these elements in this order: `ic12 ic07 ic13 ic08 ic04 ic14 ic09 ic05 ic10 ic11 info`. Its 16- and 32-pixel elements are `ic04` and `ic05`, and it wrote no `icp4` or `icp5`.

Each of `ic04` and `ic05` holds `ARGB`, then the alpha, red, green and blue channels in that order, `size × size` bytes each, run-length packed. A control byte below `0x80` copies the next `c + 1` bytes. One of `0x80` or above repeats the next byte `c − 0x80 + 3` times. Decoded that way, both match the PNGs above: alpha exactly, and colour within 1 on every visible pixel.

**Colour is stored straight, not premultiplied by alpha.** The stock images can't show that, because almost every pixel in them is opaque or clear. The alpha ramp can; it was made the same day on the same Mac, by then on macOS 27.2.1 (26B131). Its alpha decodes exactly, and its colour stays (200, 100, 50), out by at most 5 at alpha 17, 3 at 34 and 1 above that, which is within `255 / alpha` everywhere. Premultiplied, it would be (13, 7, 3) at alpha 17.

**Don't judge an icon by `iconutil -c iconset`.** Exporting brightens partly transparent pixels, Apple's own encoding included. The ramp exports as (255, 255, 255) at alpha 17 and (250, 125, 63) at alpha 204, where the element holds (195, 105, 45) and (200, 100, 50). Decode the elements instead.
