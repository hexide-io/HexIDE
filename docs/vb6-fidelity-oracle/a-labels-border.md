## A Label's border (2026-10-17, #1077)

**The probe.** A pixel-mode form with a white BackColor, holding Labels 20 pixels square (300 twips): Fixed Single with
Appearance 3D, Fixed Single Flat, 3D with a red BackColor, no border, and two 3D Labels edge to edge. The others are
grey, &H00C0C0C0. A Timer, once the form is shown, logs `Point` for every pixel from two outside each Label to two past
it. It ran on the oracle guest's console session, Windows 10, built native and p-code, and both logged the same.

**3D Fixed Single is a sunken edge two pixels deep**, Win32's EDGE_SUNKEN. Reading a Label from its first pixel to its
last, through the middle:

| Ring | Top and left | Bottom and right |
|---|---|---|
| outer | `A0A0A0`, button shadow | `FFFFFF`, button highlight |
| inner | `696969`, 3D dark shadow | `E3E3E3`, 3D light |

So the first row is 19 pixels of shadow and one of highlight, the second is shadow, then dark shadow to the
18th pixel, then light and highlight, and the BackColor fills the 16 x 16 inside. The edge is the same with a red
BackColor, so it doesn't come from the BackColor. Two Labels side by side read highlight then shadow where they meet,
which is what makes a field of them a grid.

**Flat Fixed Single is one pixel of window frame,** `646464` on Windows 10, all the way round, with the BackColor inside
it. The obvious guess was black, which is what HexIDE drew, or tried to.

**No border is the BackColor to the edge.**

**In HexIDE:** fixed (#1077). The Label's ClassicBorderDecorator had no brushes, since its defaults come from the Classic
theme, which isn't loaded, so neither border drew at all. It now draws each ring in the VB6 system colour above, as
HexIDE resolves that colour. HexIDE's Classic palette keeps its own values for four of them: shadow `808080`, dark shadow
`404040`, and both lights `FFFFFF`, against Windows 10's `A0A0A0`, `696969`, `E3E3E3` and `FFFFFF`. So the structure is
VB6's and the shades are the palette's. Window frame had no colour at all, so `vbWindowFrame` resolved to nothing; it is
now Windows 10's `646464`. That is the Windows 10 measurement inside an otherwise Win98-era palette, whose window frame
would be black, which is why black was the obvious guess.

