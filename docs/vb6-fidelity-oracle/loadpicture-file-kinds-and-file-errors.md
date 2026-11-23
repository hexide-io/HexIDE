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

### An empty Picture, and what SavePicture writes (2026-11-21, #992, SP6)

**The question.** #992 owed the rows above, and three more:
- `TypeName` of a PictureBox's `Picture` with no picture, and of `LoadPicture("")`;
- the type of a Picture's `Width`;
- what `SavePicture` writes for a picture loaded from a GIF or a JPEG.

**The probe.**
- **The images:** the program made them itself with a 32-bit PowerShell child, using .NET's `System.Drawing`, as the probe above did. They were one 20 by 10 pixel image, red with a blue corner, saved as:
  - a 32-bit `.bmp` (`x.bmp`);
  - a 24-bit `.bmp` (`x24.bmp`);
  - a `.jpg`;
  - a `.gif`;
  - an 8-bit `.bmp` (`x8.bmp`), made by saving the GIF again as a bitmap.
- **Each row** called `SavePicture`, then read the file's first bytes and its header, compared it with the source byte for byte, and loaded it back.
- **The builds:** `/make` on vb6.exe SP6, as native code and as p-code, run in session 0. The two logged the same lines.

**An empty picture:**

| Expression | `TypeName` | `Type` | `Width`, `Height` | `Handle` |
|---|---|---|---|---|
| `Picture1.Picture`, none set | Picture | 0, Integer | 0, 0, Long | 0, Long |
| `Form1.Picture`, none set | Picture | 0 | 0, 0 | 0 |
| `LoadPicture("")` and `LoadPicture()` | Picture, no error | 0 | 0, 0 | 0 |
| `Form1.Icon`, the default | Picture | 3, an icon | 847, 847 | not 0 |

A loaded picture's `Width` and `Height` are Longs too: `x.bmp` read 529 and 265.

**What `SavePicture` writes:**

| Saved | The file written | Against the source |
|---|---|---|
| `LoadPicture("x.bmp")`, 32-bit | a **24-bit** BMP, 654 bytes | different: the source is 854 bytes |
| `LoadPicture("x24.bmp")` | a 24-bit BMP, 654 bytes | same length; the first difference is at byte 34, the header's `biSizeImage` |
| `LoadPicture("x8.bmp")` | an 8-bit BMP, 1278 bytes, `biClrUsed` 0 where the source had 256 | same length; first difference at byte 34 |
| `LoadPicture("x.jpg")` | a 24-bit BMP, 654 bytes | — |
| `LoadPicture("x.gif")` | an **8-bit** BMP, 1278 bytes, with a 256-colour table | — |
| the GIF, saved to a name ending `.gif` | the same 8-bit BMP | — |
| the JPEG, saved to a name ending `.jpg` | the same 24-bit BMP | — |
| the JPEG or the GIF through `Picture1.Picture` | the same as from the object | — |
| `Picture1.Picture` with none set, or `LoadPicture("")` | **380**, "Invalid property value", and no file | — |
| `Form1.Icon` | an icon file, 766 bytes, starting 0, 0, 1, 0 | loads back as Type 3, 847 by 847 |
| `Picture1.Image`, after drawing with AutoRedraw | a 24-bit BMP of the box's inside, 116 by 76 pixels | — |
| a picture, to a folder that doesn't exist | **76**, "Path not found: '`<folder>`\nodir\o_nodir.bmp'" | — |

Every BMP written loaded back as Type 1, 529 by 265.

**What it shows:**
- **`SavePicture` writes a bitmap fresh from the picture, never the file it came from.** A JPEG or a 24- or 32-bit bitmap comes out 24-bit. A GIF or an 8-bit bitmap comes out 8-bit, with a 256-colour table. Whether the table's entries match the source's wasn't compared.
- **The obvious guess was that a picture loaded from a BMP is written back as that file.** It isn't: even the 24-bit source came back with a different header, and the 32-bit one lost a byte a pixel.
- **The name doesn't choose the format.** A file named `.gif` or `.jpg` gets a BMP.
- **An empty picture can't be saved,** 380. An icon is saved as an icon.

### SavePicture's bytes, and the colour table it writes (2026-11-23, #992, SP6)

**The question.** The section above gives each file's kind and length. A writer needs every header field, and the
section left open whether an 8-bit file's colour table is the source's.

**The probe.**
- **Setup:** VB6.EXE 6.00.9782 (SP6), `/make` as native code in a clone of the oracle guest, with the program run there in session 0.
- **The images,** made in the guest with .NET's `System.Drawing`, as above: the 20 by 10 red image with a blue 3 by 3 corner, as a 24-bit `.bmp`, a 32-bit `.bmp`, a `.jpg`, a `.gif` and an 8-bit `.bmp`, and a 32 by 32 green icon, `x.ico`, of 766 bytes.
- **A grey ramp** was added: an 8-bit `.bmp` written byte by byte, whose colour table entry *i* is (*i*, *i*, *i*). Every pixel is index 100, and `biClrUsed` is 256.
- **The program** passed each to `LoadPicture`, then `SavePicture` to a name ending `.out`. Each file's bytes were read back in the guest.

**The headers.** Every BMP written had these fields, whatever the source:

| Field | 24-bit output | 8-bit output |
|---|---|---|
| `bfType`, `bfSize` | `BM`, the file's length (654) | `BM`, the file's length (1278) |
| `bfReserved1`, `bfReserved2` | 0, 0 | 0, 0 |
| `bfOffBits` | 54 | 1078 |
| `biSize`, `biWidth`, `biHeight` | 40, 20, **10**: positive, so the rows are bottom-up | 40, 20, 10 |
| `biPlanes`, `biBitCount`, `biCompression` | 1, 24, 0 | 1, 8, 0 |
| `biSizeImage` | **600**: the pixel bytes, rows padded to 4 | **200** |
| `biXPelsPerMeter`, `biYPelsPerMeter` | **0, 0**: the sources had 3780 | 0, 0 |
| `biClrUsed`, `biClrImportant` | 0, 0 | **0, 0**: the sources had 256 |

**The pixels:**
- **From the 24-bit and 32-bit BMPs:** the top row (the file's last) began `ff 00 00` three times then `00 00 ff`, which is blue then red, in BGR order, as the source's pixels are. The 32-bit source's fourth byte is dropped.
- **From the JPEG:** the decoder's colours, such as `d6 01 15`, not the exact red.

**The 8-bit colour table:**

| Source | The table written | Pixels written |
|---|---|---|
| The grey ramp | **the source's own:** entry *i* is (*i*, *i*, *i*), with the fourth byte 0 | index 100, as the source's |
| The GIF | the GIF's global colour table, in its order: `000000`, `000033`, `000066` and so on | red as index 210 |
| The 8-bit BMP saved from that GIF | the same header, the same first entries and 251 entries that aren't black, as from the GIF | index 210, the same |

The third row's source table wasn't byte for byte the output's, and the bytes that differ weren't located. The grey ramp is the row that tells "the source's table" from "a fixed palette".

**The icon.** `x.ico` was written back as 766 bytes, the same as the source except byte 9. That is the directory
entry's `bReserved`, which the source had as `0x80` and the output as 0. It loaded as Type 3, 847 by 847.

**Readings:**
- **An 8-bit picture keeps its own colour table and indexes.** `biClrUsed` becomes 0, the table has 256 entries, and each entry's fourth byte is 0.
- **A bitmap's header is VB6's own,** with the resolution fields 0 and `biSizeImage` filled in.
- **An icon is written as the icon it is,** with the directory entry's reserved byte cleared.

**Still not measured:**
- an 8-bit source whose table has fewer than 256 entries;
- 1-, 4- and 16-bit and RLE bitmaps, and top-down ones;
- a GIF with a local colour table or transparency;
- an icon with more than one image, or one not 32 by 32;
- write errors other than a missing folder.

### In HexIDE

At `86dcc6c50` (read from the code, not run: a probe in `HexIDE.Runtime.Tests` stopped on the BMP itself, where
decoding needs the Avalonia platform that project lacks):
- a file whose content is neither a bitmap nor an icon is refused, "This Panel.Picture file format is not implemented
  (#1087)" (`PanelPicture.FromFile`), so a JPEG, a GIF, a PNG and a text file all stop the program rather than load or
  raise 481;
- a file that can't be read is refused, "LoadPicture file errors are unmeasured (#1087)" (`PanelPicture.Load`), where
  VB6 raises 53, 75 or 76.
