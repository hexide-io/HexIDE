# hexlogo — the HexIDE mark as terminal art

`hexide-logo-24.ans` is the HexIDE logo rendered as coloured terminal art: 12 rows of 24 cells, for the
`--help` banner. `hexlogo.py` is the generator that produced it. Regenerate; never hand-edit the `.ans`.

## Regenerate

```sh
cd tools/hexlogo
python3 hexlogo.py --mode half --width 24 --ss 6 --pad --crop -o hexide-logo-24.ans
```

Python 3 only, no packages. `python3` rather than `python`: Ubuntu has shipped no unversioned `python`
since 20.04, so the bare name fails on CI's runner, on the SDK container, in WSL and on macOS. The output
is deterministic, so a regeneration that changes nothing produces a clean `git status`.

`HelpMarkArtTests` checks the file on every test run, and the contract is all five of these: **12 rows,
24 visible cells each, SGR-only escapes, every row ending in a reset, 24-bit colour on every row, and
UTF-8 with LF endings and no BOM.** A regeneration that breaks any of them fails the build rather than the
help screen. Note the last two especially, because they are the ones a plausible regeneration trips:
`--depth 256` satisfies everything except 24-bit colour, and omitting `--crop` leaves a fully transparent
top and bottom row, which carry no colour sequence at all.

- `--width 24` is the box: 24 cells wide, and in `half` mode 24 pixels tall, which is 12 rows. It is the
  largest mark that keeps `--help` on one 24-row screen with the text beside it. The 60-column rendering
  that was considered first is faithful and pushes the option list off screen.
- `--crop` fits the hexagon's bounding box to that grid instead of the SVG's whole 200×200 canvas. The
  canvas has margins; without this the mark is 10 rows of hexagon in a 12-row box with a blank row at top
  and bottom. Cropped, it is 12 rows tall and 20 cells wide, centred in the 24-cell box.
- `--pad` keeps every row at exactly 24 visible cells. Without it the trailing transparent cells are
  trimmed, which is fine for printing on its own and wrong for laying text beside it.
- `--ss 6` is the supersampling factor: each cell averages a 6×6 grid of samples. It does not change the
  silhouette (measured: 4, 5, 6 and 8 all give the same shape at this width), only the blended colour of
  edge cells, so keep it at 6 because that is what produced the checked-in bytes. Changing it produces a
  file that differs without looking different, which is the wrong kind of diff.

### How `--help` places it

`ConsoleLayout` (in `HexIDE.Core`) puts the mark beside the text when the console is at least 24 + 2 +
the widest text row wide, and above it otherwise. The widest row is an option line, and the summaries in
`ServerOptions.Options` are kept short enough that beside fits in 120 columns, the default width of both
Windows consoles and Windows Terminal. Stacked costs about two rows more than the text alone, so a narrow
window comes out slightly taller than it did before the mark existed. That is deliberate, and the reason
is worth stating precisely rather than approximately: the mark moves above the text below 120 columns, but
the option lines themselves do not wrap until about 94. Between those two the stacked layout costs a
couple of rows and buys nothing — which is the price of never slicing the mark into bands, and cheap at
that.

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
mark instead. The `.ans` written to a file is 7.5 KB of escape codes, not a picture.

## Other modes, for the record

| `--mode` | Cell | Use |
|---|---|---|
| `half` | 1×2 pixels, `▀`/`▄` | The one shipped. Best fidelity per row. |
| `quad` | 2×2 pixels, quadrant blocks | Rejected: a cell holding orange, white and transparent can only carry two of them, and the dropped one shows as a dark notch on the circle edges. |
| `space` | 1 pixel, ASCII space with background colour | Pure ASCII when a font has no block glyphs. Half the vertical resolution. |

`--depth 256` maps to the xterm palette by nearest colour. It bands visibly on the gradient because a
six-level cube cannot carry a smooth orange. It is also **not** a usable fallback for the shipped file:
`HelpMarkArtTests` requires 24-bit colour on every row, so a `--depth 256` regeneration passes every
other part of the contract and fails the build. It is here for the record, not as an option.

## Where the shape comes from

The geometry in `hexlogo.py` is transcribed from `IDE/HexIDE/Icons/AppIcon/hexide-logo.svg`: the rounded
pointy-top hexagon, its radial gradient and rim shade, and the six white bonds ending in circles. There
is no SVG parser; if the logo changes, update the constants at the top of the script and regenerate.

To view any output in a terminal, from PowerShell 7:

```powershell
[Console]::OutputEncoding = [Text.Encoding]::UTF8; Get-Content -Raw hexide-logo-24.ans
```

or `cat hexide-logo-24.ans` from any *nix shell.
