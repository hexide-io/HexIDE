# hexlogo — the HexIDE mark as terminal art

`hexide-logo-24.ans` is the HexIDE logo rendered as coloured terminal art: 12 rows of 24 cells, for the
`--help` banner. `hexlogo.py` is the generator that produced it. Regenerate; never hand-edit the `.ans`.

## Regenerate

```sh
cd tools/hexlogo
python hexlogo.py --mode half --width 24 --ss 6 --pad -o hexide-logo-24.ans
```

Python 3 only, no packages. The output is deterministic, so a regeneration that changes nothing produces
a clean `git status`.

- `--ss 6` is the supersampling factor: each cell averages a 6×6 grid of samples. It does not change the
  silhouette (measured: 4, 5, 6 and 8 all give the same shape at this width), only the blended colour of
  edge cells, so keep it at 6 because that is what produced the checked-in bytes. Changing it produces a
  file that differs without looking different, which is the wrong kind of diff.
- `--pad` keeps every row at exactly 24 visible cells. Without it the trailing transparent cells are
  trimmed, which is fine for printing on its own and wrong for laying text beside it.
- `--width 24` is the largest mark that keeps `--help` on one 24-row screen when the text sits beside it.
  The 60-column rendering that was considered first is faithful and pushes the option list off screen.

## What the file is

- UTF-8, no BOM, LF line endings (pinned in `.gitattributes`), 12 lines.
- Each cell is `▀` (U+2580) carrying two pixels: 24-bit SGR foreground for the top one, background for
  the bottom one. Where one half is transparent the cell is `▀` or `▄` with foreground only; where both
  are, it is a plain space. So the mark sits on the terminal's own background and is correct on dark and
  light themes. The cost is a stepped outline: with the background unknown, an edge cannot blend.
- SGR only, no cursor movement, and every line ends in `ESC[0m`. It can be written as plain text, and a
  line is safe to concatenate with ordinary text to its right.

### What the console needs

- A font with the half blocks and UTF-8 output. PowerShell 7 and every *nix shell have both. Legacy
  `cmd.exe` needs `chcp 65001`.
- Truecolour SGR. Windows Terminal, conhost since Windows 10 1703, and every mainstream *nix terminal
  accept it. On Windows the process must also enable `ENABLE_VIRTUAL_TERMINAL_PROCESSING` on its stdout
  handle. PowerShell does this for itself; a process writing its own output does not get it for free.

When any of that is absent, or stdout is redirected, or `NO_COLOR` is set, print the monochrome ASCII
mark instead. The `.ans` written to a file is 6 KB of escape codes, not a picture.

## Other modes, for the record

| `--mode` | Cell | Use |
|---|---|---|
| `half` | 1×2 pixels, `▀`/`▄` | The one shipped. Best fidelity per row. |
| `quad` | 2×2 pixels, quadrant blocks | Rejected: a cell holding orange, white and transparent can only carry two of them, and the dropped one shows as a dark notch on the circle edges. |
| `space` | 1 pixel, ASCII space with background colour | Pure ASCII when a font has no block glyphs. Half the vertical resolution. |

`--depth 256` maps to the xterm palette by nearest colour. It bands visibly on the gradient because a
six-level cube cannot carry a smooth orange, so it is a fallback of last resort rather than a degraded
default.

## Where the shape comes from

The geometry in `hexlogo.py` is transcribed from `IDE/HexIDE/Icons/AppIcon/hexide-logo.svg`: the rounded
pointy-top hexagon, its radial gradient and rim shade, and the six white bonds ending in circles. There
is no SVG parser; if the logo changes, update the constants at the top of the script and regenerate.

To view any output in a terminal, from PowerShell 7:

```powershell
[Console]::OutputEncoding = [Text.Encoding]::UTF8; Get-Content -Raw hexide-logo-24.ans
```

or `cat hexide-logo-24.ans` from any *nix shell.
