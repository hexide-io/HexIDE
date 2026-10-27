## A font's size, and the default font (2026-10-22, #910)

**The probe.** A form with three controls:
- a TextBox whose Font block says Arial `Size = 9.75`;
- a Label with no Font block;
- a Label whose block says Tahoma 12, Weight 700, Italic.

`Form_Load` logs each one's `FontName`, `FontSize`, `FontBold`, `FontItalic` and `Font.Size`, and the form's own. Built
native and p-code, and every row agreed.

| Control | Logged |
|---|---|
| the TextBox | Arial, 9.75, `Font.Size` 9.75 |
| the Label with no Font block | MS Sans Serif, **8.25**, not bold, not italic |
| the form, which has no Font block either | MS Sans Serif, **8.25** |
| the Tahoma Label | Tahoma, 12, bold, italic |

So a size is in points, as the .frm writes it, and VB6's default font is MS Sans Serif at 8.25 points: 11 pixels at 96
dpi, the size that point value rounds to. The rest of the same probe, the sizes a set `FontSize` reads back as, is
#1343's.

**In HexIDE (#910):** a font's size is held in points and drawn at 96 pixels to the inch, so 12 points draw at 16 pixels.
It used to be handed to Avalonia as pixels, which drew every VB6 font a quarter too small. The default is held as 8.25,
which draws at the same 11 pixels the old default of 11 did. The designer's Font dialog keeps a fractional size, offers it
in its list, and draws its sample in points. Not measured: whether a control with no Font block takes its form's font
rather than VB6's default. The two are the same here, and HexIDE gives it the default.

