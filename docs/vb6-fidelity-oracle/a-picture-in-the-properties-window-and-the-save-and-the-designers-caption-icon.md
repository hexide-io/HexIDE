## A picture in the Properties window and the save, and the designer's caption icon (2026-11-21, #2715, #2716, #2872, design-time, IDE, SP6)

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console through Hyper-V's synthetic
keyboard and mouse, with the screen read from the host's framebuffer.
- **The form:** four PictureBoxes and an Image.
- **The pictures:** each loaded through its row's `...` button in the Properties window, in the Load Picture dialog,
  from files made on the guest with .NET's System.Drawing:
  - a 32 × 32 bitmap, 4150 bytes;
  - an icon of the same image, 766 bytes;
  - the same image as a GIF (898 bytes) and a JPEG (756);
  - an enhanced metafile with one ellipse, 636 bytes.
- **The Load Picture dialog** is the old-style common dialog. It refuses a path written with forward slashes:
  `C:/probe/pic/a.bmp` gave "The file name is not valid."

**What the row shows (#2715):**

| Loaded | Row |
|---|---|
| nothing | `(None)` |
| the bitmap | `(Bitmap)` |
| the icon, in a PictureBox and as the form's Icon | `(Icon)` |
| the GIF | `(Bitmap)` |
| the JPEG | `(Bitmap)` |
| the metafile, in the Image | `(Metafile)` |

The Image resized itself to the metafile's size when it was loaded.

**Clearing a picture (#2872):**
- **Delete with the row only selected in the list does nothing.** The row stayed `(Bitmap)`, and the form's Icon stayed
  `(Icon)`.
- **Clicking the row's value cell first, then pressing Delete, clears it.** The row read `(None)`, the picture went from
  Picture1, and the save wrote no `Picture` line for Picture1.

**What the save writes.** `Form1.frm` gave the form `Icon = "Form1.frx":0000`, and each loaded control a `Picture`
line with its offset. Each record in `Form1.frx` was:
- a 4-byte record length;
- `6C 74 00 00`;
- a 4-byte payload length;
- then **the file's own bytes**, as loaded.

| Picture | Record length | Payload | Payload begins |
|---|---|---|---|
| the icon | `0x306` | `0x2FE` (766) | `00 00 01 00`, the icon header |
| the GIF | `0x38A` | `0x382` (898) | `GIF89a` |
| the JPEG | `0x2FC` | `0x2F4` (756) | `FF D8 FF E0` |
| the metafile | `0x284` | `0x27C` (636) | `01 00 00 00`, the EMF header |

So a GIF and a JPEG are saved as GIF and JPEG, though the row calls both `(Bitmap)`. The bitmap's record wasn't read,
because it was cleared before the save.

**The designer's caption (#2716):**

| Icon | BorderStyle and ControlBox | The caption's left |
|---|---|---|
| no Icon line in the file | Sizable | VB's own form icon |
| the loaded icon | Sizable | the loaded icon |
| the loaded icon | Fixed Single | the loaded icon |
| the loaded icon | Fixed Dialog | the loaded icon |
| the loaded icon | Fixed ToolWindow, Sizable ToolWindow | no icon, in a smaller caption |
| the loaded icon | None | no caption at all |
| the loaded icon | Sizable, ControlBox False | no icon |
| `(None)`, cleared as above | Sizable | a different icon: a small window picture |
| `(None)` | Fixed Dialog | the same small window picture |

The small window picture looks like Windows' default application icon. That was not checked against the system's
own.

### Not measured

- **A cursor file, a WMF, or a picture pasted from the clipboard.**
- **The bitmap's record,** and an icon file holding several images.

What a running form's window shows for each Icon is in [A form's window icon](a-forms-window-icon-no-icon-line.md).

### A bitmap's record, and a copied picture saved twice (2026-11-21, #21, design-time, IDE, SP6)

**The questions.** The section above didn't read a bitmap's record. #21's findings list asks two more questions:
- Q10: whether `6C 74 00 00` is a fixed signature or varies with the picture's kind;
- Q14: whether a PictureBox copied and pasted with its picture leaves the picture in the `.frx` once or twice.

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console. A one-form Standard EXE held
`Picture1`, a PictureBox. Each picture was made on the guest and loaded through the Picture row's `...` button, and
each save was Ctrl+S:
- a 16-by-16 red bitmap, `red.bmp`, 1078 bytes;
- then a 16-by-16 blue JPEG, GIF, icon and EMF.

**The bitmap's record** has the same shape as the other kinds:

| Picture | Record length | Payload | Payload begins |
|---|---|---|---|
| the bitmap | `0x43E` | `0x436` (1078) | `42 4D`, `BM` |

**Q10, the signature.** With the copy below holding a picture of each kind in turn, every record began the same way: a
length, `6C 74 00 00`, the payload length, then the file's bytes.

| Picture | Record begins |
|---|---|
| JPEG, 633 bytes | `81 02 00 00 6C 74 00 00 79 02 00 00 FF D8 FF E0` |
| GIF, 832 bytes | `48 03 00 00 6C 74 00 00 40 03 00 00 47 49 46 38` |
| icon, 446 bytes | `C6 01 00 00 6C 74 00 00 BE 01 00 00 00 00 01 00` |
| EMF, 568 bytes | `40 02 00 00 6C 74 00 00 38 02 00 00 01 00 00 00` |

- **The signature is fixed.** The record length is the payload's plus 8 in every row, the payload length is the file's
  own size, and there is no class id.
- **The EMF's payload was compared with its file:** byte for byte the same.
- **An SSTab's `TabPicture` record does carry StdPicture's class id**
  ([An SSTab, drawn fresh](in-box-controls-the-sstab.md)), so that shape belongs to a control's property bag, not to a
  form's own `Picture` lines.

**Q14, a copied picture.**
- **The copy:** Picture1, holding the bitmap, was copied and pasted on the form, with No to the control-array question.
- **The save:** `Form1.frx` went from 1090 bytes to 2180, twice the size.
- **The `.frm`** gave the pasted `Picture2` `"Form1.frx":0000` and `Picture1` `"Form1.frx":0442`.
- **The two records were identical,** byte for byte.

So VB6 keeps one copy of the picture per control and shares nothing between them. The pasted control's block comes
first in the `.frm`, so its record does too, and the original's record moved to after it.

### Not measured

- **A WMF or a cursor file,** and an icon file holding several images.
- **Two controls given the same picture file separately,** rather than by copying.
