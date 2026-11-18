## LoadPicture: which files load, and the errors for those that don't (2026-11-18, #4597, SP6)

**The question.** `LoadPicture` of a bitmap or an icon is measured elsewhere ("An icon picture's size, and the image it
draws"). What does it do with a PNG, a JPEG and a GIF, with a file that isn't a picture, and with a path that names no
file? And can a program trap each failure?

**The probe.** One 20 by 10 pixel red image, saved by .NET's `System.Drawing` as a `.bmp`, `.png`, `.jpg` and `.gif`,
beside a text file named `t.png` and an empty file named `e.png`. A `Sub Main` passed each full path to a `Try` routine
that ran `Set p = LoadPicture(path)` under `On Error Resume Next`, then wrote `Err.Number`, `Err.Description` and
`TypeName(p)`, and, when `p` wasn't Nothing, its `Type`, `Width` and `Height`. A last statement called `LoadPicture` of
the PNG with no `Try`, under `Main`'s own `On Error Resume Next`. Built with `VB6.EXE /make` as native code and as
p-code, on the Win10 guest. **Native and p-code agreed on every row.** `<folder>` stands for the probe's own folder.

| File | `Err.Number` | `Err.Description` | `TypeName(p)` | `Type`, `Width`, `Height` |
|---|---|---|---|---|
| `x.bmp` | 0 | | Picture | 1, 529, 265 |
| `x.jpg` | 0 | | Picture | 1, 529, 265 |
| `x.gif` | 0 | | Picture | 1, 529, 265 |
| `x.png` | **481** | Invalid picture | Nothing | |
| `t.png`, a text file | 481 | Invalid picture | Nothing | |
| `e.png`, an empty file | 481 | Invalid picture | Nothing | |
| `nope.png`, which doesn't exist | 53 | File not found: '`<folder>`\nope.png' | Nothing | |
| `nodir\x.png`, in a folder that doesn't exist | 76 | Path not found: '`<folder>`\nodir\x.png' | Nothing | |
| `<folder>` itself, a directory | 75 | Path/File access error: '`<folder>`' | Nothing | |

The last statement, `LoadPicture` of the PNG under `Main`'s `On Error Resume Next`, left `Err.Number` 481.

**What it shows:**
- **A JPEG and a GIF load as a bitmap,** `Type` 1, with the same size in HiMetric as the BMP of the same pixels: 529 by
  265 for 20 by 10, at 96 dpi.
- **A PNG does not load.** VB6's `LoadPicture` refuses it as it refuses a file that isn't a picture at all, with 481
  "Invalid picture". The extension decides nothing, measured in the second probe below.
- **Every failure is an ordinary run-time error,** trappable with `On Error`. The file errors name the path in their
  description, in single quotes: here the full path the call passed, since no relative path was tried.

**The second probe: content or extension?** The rows above can't tell the two apart. The PNG and the two files that
weren't pictures were all named `.png`, so they would be refused alike whether `LoadPicture` read the content or went
by the name, and every image that loaded carried its own format's extension. The first version of this page said "the
extension decides nothing" from those rows alone. That was a reading, not a measurement, and this probe was added to
test it. The same files were copied under other formats' extensions, and one with none, and each was passed to the same
`Try` routine. Native and p-code agreed on every row.

| File | Its content | `Err.Number` | `Err.Description` | `TypeName(p)` | `Type`, `Width`, `Height` |
|---|---|---|---|---|---|
| `jpg-as.png` | the JPEG | 0 | | Picture | 1, 529, 265 |
| `bmp-as.dat` | the BMP | 0 | | Picture | 1, 529, 265 |
| `gif-as.jpg` | the GIF | 0 | | Picture | 1, 529, 265 |
| `jpg-noext`, no extension | the JPEG | 0 | | Picture | 1, 529, 265 |
| `png-as.bmp` | the PNG | **481** | Invalid picture | Nothing | |
| `text-as.bmp` | the text file | **481** | Invalid picture | Nothing | |

So the content decides. An image loads under any name, or none, and a PNG or a text file is refused even when it's
named `.bmp`.

**Unmeasured:** a relative path, a path through a mapped drive or UNC share, a GIF with several frames, a progressive
JPEG, a CMYK JPEG, and the description when the path is longer than `Err.Description` holds.

### In HexIDE

At `86dcc6c50` (read from the code, not run: a probe in `HexIDE.Runtime.Tests` stopped on the BMP itself, where
decoding needs the Avalonia platform that project lacks):
- a file whose content is neither a bitmap nor an icon is refused, "This Panel.Picture file format is not implemented
  (#1087)" (`PanelPicture.FromFile`), so a JPEG, a GIF, a PNG and a text file all stop the program rather than load or
  raise 481;
- a file that can't be read is refused, "LoadPicture file errors are unmeasured (#1087)" (`PanelPicture.Load`), where
  VB6 raises 53, 75 or 76.
