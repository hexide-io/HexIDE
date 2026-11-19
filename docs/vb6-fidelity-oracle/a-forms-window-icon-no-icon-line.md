## A form's window icon: no Icon line, (None), and an icon of its own (2026-10-24, #1594, SP6)

**The question.** #1575's probe read only forms with no Icon line: VB6's default icon as the window's big icon, and no
small one. The usual account says a Fixed Dialog therefore shows no icon on its caption. HexIDE gave every running
window Avalonia's icon, big and small, whatever the file said. What does VB6 do with each kind of Icon, and what does
Windows draw?

**The probe.** VB6 SP6 (VB6.EXE 6.00.9782). One project of six forms was built by `/make`, native and p-code, and run
on the guest's console. There were two BorderStyles, 3 (Fixed Dialog) and 2 (Sizable), and three Icons for each:
- no Icon line;
- `Icon` = (None);
- a 32 × 32 icon made for the probe.

(None) is what VB98's own `Splash Screen`, `Options Dialog` and `ODBC Log In` templates save: `Icon = "….frx":0000`,
where the record is an empty picture, `08 00 00 00`, then `lt\0\0`, then a size of 0. The real icon was written in the
same framing, with its own size and the `.ico`'s bytes.

Each window was read with `WM_GETICON` for `ICON_SMALL`, `ICON_BIG` and `ICON_SMALL2`, and with the class's icons.
Its caption was captured. **Native and p-code agree on every row.**

| The form's file | `ICON_BIG` | `ICON_SMALL` | `ICON_SMALL2` | Fixed Dialog caption | Sizable caption |
|---|---|---|---|---|---|
| no Icon line | VB6's default | none | made by Windows | VB6's form icon | VB6's form icon |
| `Icon` = (None) | none | none | none | **no icon** | Windows' generic window icon |
| an icon of its own | the icon | the icon, its own handle | the small icon | the icon | the icon |

The class icons were none on every form.

**What it shows:**
- **The usual account is wrong.** A Fixed Dialog with VB6's default icon draws it on its caption on Windows 10. With
  no small icon, Windows makes one from the big icon.
- **A bare dialog caption comes from Icon = (None).** That is why VB98's dialog templates save it.
- **An icon of the form's own is both its big and small icon.** Only the default is big only.

**In HexIDE (#1594):** `FormIcon` reads the form's Icon as the window is made, from the picture's own size, since a
blob runs to the next cited offset:
- an icon of its own is the window's icon, big and small;
- (None) leaves the window without one. On Windows, the window procedure keeps no icon and reports none, since
  Avalonia would otherwise give it the executable's;
- no Icon line keeps HexIDE's mark, big only. VB6's own artwork can't ship, and the made program's runner gives its
  `form.ico`.

Checked live with F5 on the probe's six forms: each window's `WM_GETICON` answers, and its caption drawn by
`PrintWindow`, matched the table above. The default's icon is HexIDE's mark.

**Not measured:**
- An icon with several sizes, and which of them VB6 picks for the small icon.
- An Icon changed at run time, `Me.Icon = LoadPicture(…)`. HexIDE reads the Icon once, as the window is made. Measured since: "An Icon set at run time", below.


### An Icon set at run time (2026-11-18, #4603, SP6)

**The probe.** A running form set its own `Icon`, and the Icon of New instances of a second form, logging each result
and error. p-code and native code logged the same lines. The first file was `ARW01UP.ICO`, VB6's own sample from
`Common\Graphics\Icons\Arrows`: 1078 bytes, and by its directory a 32-by-32 and a 16-by-16 image at 4 bits a pixel.

| Statement | Result |
|---|---|
| reading `Me.Icon` on a new form | TypeName Picture, Type 3, a Handle that isn't 0 |
| `Set p = LoadPicture(ARW01UP.ICO)` | ok: p.Type 3, Width 847, Height 847 (HiMetric, 32 pixels) |
| `Set Me.Icon = p` | ok. `Me.Icon Is p` is True, with the same Handle |
| `Me.Icon = LoadPicture(ARW01UP.ICO)`, without Set | ok |
| `Set Me.Icon = LoadPicture(x.bmp)`, a 32-by-32 bitmap | **error 380**, Invalid property value, and the Icon keeps its Type 3 |
| `Set Me.Icon = LoadPicture()` | ok: Type 0, Handle 0 |
| `Set k1.Icon = Nothing`, straight after an icon | ok. `k1.Icon` then reads TypeName Picture, Type 0, Handle 0. `k1.Icon Is Nothing` is False |
| `k1.Icon = Nothing`, without Set | ok, no error |
| `Set f2.Icon = …` on a `New Form2` not yet loaded | ok, and it loads the form |

**On screen** (console screenshots, on a sizable form):
- **After `Set Me.Icon` to an icon,** the form's title bar and its taskbar button both show it.
- **After `Set k1.Icon = Nothing`,** k1's title bar shows Windows' generic window icon, as a sizable form saved with
  Icon (None) does (above).
- **After `Set k2.Icon = LoadPicture()`, k2's title bar still shows the arrow it had,** though `k2.Icon` reads back
  Type 0 and Handle 0, as Nothing's does. The two clear the property alike, and the window differently.
- **On a Fixed Dialog,** from a later run of New instances of a Fixed Dialog form beside a sizable one:
  - after `Set d1.Icon = Nothing`, following an icon, its title bar shows **no icon at all**, as a Fixed Dialog saved
    with Icon (None) does (above);
  - after `Nothing` and then `LoadPicture()`, it still shows none;
  - an untouched instance of the same form shows VB6's default icon.
- **On the sizable form,** `Nothing` and then `LoadPicture()` shows Windows' generic icon, the same as `Nothing` alone.
  So an empty picture leaves the window as it was, icon or none.

**Which image `LoadPicture` takes from an icon file**, from hand-built files, each image a red square with a white centre:

| The file's images, in order | Result |
|---|---|
| one 32-by-32 at 4, 8, 24 or 32 bits a pixel, each with its AND mask | ok, Type 3, Width 847 |
| one 16-by-16 at 4 bits | ok, Width 423 |
| one 48-by-48 at 4 bits | ok, Width 1270 |
| 32 then 16, and 16 then 32, at 4 bits | ok, Width 847: the 32 |
| a 256-by-256 stored as PNG, then a 32 at 4 bits | ok, Width 847: the 32 |
| a 32 at 4 bits, then a 256 stored as PNG | ok, Width 847 |
| one 32-by-32 stored as PNG | **481**, Invalid picture |
| a 32-by-32 at 32 bits **with no AND mask** after its pixels, first before a 256 PNG, or among a 48 and a 16 alike | **481** |
| a 256 PNG, then 48, 32 and 16 at 32 bits with no AND masks | **481** |
| one 32-by-32 at 32 bits with its mask, its header's image size and resolution set (4096, and 3780 a metre) or left 0 | ok, either way |
| one 32-by-32 at 32 bits without its mask, its header's image size and resolution set or left 0 | **481**, either way |

- **The image it takes is the 32-by-32 one,** wherever it sits in the file. A file of one image loads it at its own size.
  The masks were left out by Pillow, which writes 32-bit icon images that way, and fills in the header's image size
  and resolution. The last two rows cross the two: **the missing mask is what VB6 refuses**, and the header fields
  make no difference.
- **The 481 is LoadPicture's own,** raised before any assignment, and `LoadPicture(nosuch.ico)` is error 53, File not
  found, naming the path.
- **Every error here was trappable** under `On Error Resume Next`.

**Not measured** here: an icon file with several images and none of them 32 by 32. The section on an icon file with no 32-by-32 image, below, measures it.

### Several 32-by-32 images in one icon file (2026-11-18, #4623, SP6)

**The question.** The rows above show `LoadPicture` taking an icon file's 32-by-32 image wherever it sits. Many real
icon files hold more than one 32-by-32 image, at different bit depths. Which one does it take?

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged the same lines.
- **The files:** hand-built icons in which every image is one solid colour, with its AND mask:
  - red at 4 bits a pixel;
  - green at 8 or 24;
  - blue at 32.
- **The read:** an unshown form's PictureBox, with AutoRedraw on and ScaleMode in pixels, draws the picture with
  `PaintPicture p, 0, 0`. `Point(10, 10)` then reads a pixel back, as `&HBBGGRR`.

| The file's images, in order | Width | `Point(10, 10)` |
|---|---|---|
| 32 at 4 bits (red), then 32 at 32 (blue) | 847 | `FF`: red |
| 32 at 32 (blue), then 32 at 4 (red) | 847 | `FF0000`: blue |
| 32 at 8 (green), then 32 at 32 (blue) | 847 | `FF00`: green |
| 32 at 4 (red), then 32 at 8 (green) | 847 | `FF`: red |
| 32 at 8 (green), then 32 at 4 (red) | 847 | `FF00`: green |
| 32 at 4 (red), 32 at 24 (green), 32 at 32 (blue) | 847 | `FF`: red |
| 32 at 32 (blue), 32 at 24 (green), 32 at 4 (red) | 847 | `FF0000`: blue |
| one 32 at 4 (red); at 32 (blue); at 8 (green); at 24 (green) | 847 each | red; blue; green; green |
| 16 at 4 (red), then 32 at 4 (blue) | 847 | `FF0000`: blue |

**Reading:** **the image it takes is the first 32-by-32 one in the file,** whatever the bit depths of the others.
It isn't the deepest one, and it isn't the one nearest the display's depth. The single-image rows check that each
colour is drawn as built. The last row repeats the section above's "the 32, wherever it sits".

**Not measured:** the display's colour depth, which wasn't recorded. The probe ran as a service-session process on
the guest, so a display set to fewer colours wasn't tried.

### An icon file with no 32-by-32 image, and images without their mask (2026-11-18, #4748, SP6)

**The question.** Both sections above leave two shapes of icon file unmeasured: several images with none of them 32 by
32, and an image without its AND mask at a depth other than 32 bits.

**The probe.** The same as the section above: VB6 SP6, `/make`, native code and p-code, which logged the same lines.
The files were hand-built, each image one solid colour:
- red at 4 bits a pixel;
- green at 8 or 24;
- blue at 32.

`PaintPicture p, 0, 0` drew each picture at its own size in a pixel-scaled PictureBox. `Point` then read four pixels
back, as `&HBBGGRR`, at (5, 5), (20, 20), (40, 40) and (60, 60). The box's background is `F0F0F0`.

| The file's images, in order | Width and Height | Pixels at 5, 20, 40 and 60 |
|---|---|---|
| 16 at 4 (red), then 48 at 4 (blue) | 423, 423 | red, then background |
| 48 at 4 (blue), then 16 at 4 (red) | 1270, 1270 | blue, blue, blue, background |
| 16 at 32 (red), then 48 at 32 (blue) | 423, 423 | red, then background |
| 16 at 8 (green), 24 at 8 (red), 48 at 8 (blue) | 423, 423 | green, then background |
| 48 at 4 (blue), then 64 at 4 (red) | 1270, 1270 | blue, blue, blue, background |
| 16 at 4 (red), then 24 at 4 (blue) | 423, 423 | red, then background |
| one 16 at 4 (red); one 48 at 4 (blue) | 423; 1270 | as built, at its own size |
| one 32 at 4, 8, 24 or 32 bits, each **without its AND mask** | **481** | |
| a 256 stored as PNG (red), then 48 at 4 (blue) | **481** | |
| 48 at 4 without its mask (blue), then 16 at 4 (red) | **481** | |
| 48 at 32 without its mask (blue), then 16 at 4 (red) | **481** | |
| 16 at 4 (red), then 48 at 32 without its mask (blue) | 423, 423 | red, then background |

**Reading:**
- **With no 32-by-32 image, it takes the first image, whatever its size,** and draws it at that size. It isn't the
  largest, and it isn't the one nearest 32: 16 then 24 takes the 16, and 48 then 64 takes the 48.
- **A missing mask is 481 at every depth.** The section above measured that at 32 bits only.
- **Only the image it takes is checked.** A first image it can't use is 481, whether a PNG or maskless, even though a
  later one would load. A maskless image after the one it takes changes nothing.
