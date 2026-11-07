## In-box controls: the Animation (2026-10-27, #1096)

**The probes:**
- **The software:** VB6 SP6 (6.00.9782) with MSCOMCT2.OCX 6.01.9782, on Windows 10 at 96 dpi, in the en-GB user locale.
  - The form line is `Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"`.
  - The block is `Begin MSComCtl2.Animation`, `_Version` 393216.
- **The form:** Animation1, a hand-written block 2400 × 1200 (160 × 80 pixels) with only its position, extents and
  `_Version`.
- **The programs:** each ran in a Sub Main and logged every value with its type.
  - Every error was read straight after its statement.
  - Each scenario ran on a fresh instance of the form, shown.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.
- **The clips:**
  - the eighteen AVIs VB6 installs in `Common\Graphics\Videos`, whose headers were read first;
  - six written for the probe, 32 × 32 with 4 frames, for the formats the samples lack;
  - a text file named `.avi`.

### The object model

From MSCOMCT2.OCX's type library:
- **`IAnimation` has:**
  - AutoPlay, BackStyle, Center, Enabled, BackColor, OLEDropMode and hWnd;
  - the methods `Open(bstrFilename)`, `Play([varRepeatCount], [varStartFrame], [varEndFrame])`, Stop, Close and
    OLEDrag;
  - AboutBox, hidden.
- **Its events are** Click, DblClick, MouseDown, MouseMove and MouseUp, with x and y in **pixels**
  (`OLE_XPOS_PIXELS`), and the OLE events. **There is no event for a clip starting or ending.**
- **BackStyleConstants:** cc2BackstyleTransparent 0, cc2BackstyleOpaque 1.

### Defaults, from a block with no properties

| Read | Logged |
|---|---|
| AutoPlay | False |
| BackStyle | **0, transparent** |
| Center | **False** |
| Enabled | True |
| BackColor | `&H8000000F` |
| OLEDropMode | 0 |
| TabStop, CausesValidation | True, True |
| ToolTipText, Tag | "" |
| HelpContextID | 0 |
| **`v = Animation1`** | **438: the Animation has no default member** |

### Which clips it opens

| Clip | Frames, size | Format | Open |
|---|---|---|---|
| BLUR8, BLUR16, BLUR24 | 8, 128 × 128 | uncompressed, 8, 16 and 24 bits | **opens** |
| COUNT8, COUNT16, COUNT24 | 10, 128 × 128 | uncompressed, 8, 16 and 24 bits | **opens** |
| DOWNLOAD | 8, 16 × 20 | uncompressed, 8 bits | **opens** |
| COUNT8R, DRILLRLE, FILECOPY, FILEMOVE, FINDCOMP, FINDFILE, SEARCH | 8 to 34 | **RLE8**, stream handler `RLE ` | **opens** |
| FILEDEL, FILEDELR, FILENUKE | 23 or 24 | **RLE8**, stream handler `mrle` | **opens** |
| CINEAPK | 16, 128 × 128 | **Cinepak** (`cvid`) | **35752**, "Unable to open AVI file" |
| written: 4 bits | 4, 32 × 32 | uncompressed, 4 bits | **opens** |
| written: 8 bits | 4, 32 × 32 | uncompressed, 8 bits | **opens** |
| written: 32 bits | 4, 32 × 32 | uncompressed, 32 bits | **opens** |
| written: RLE4 | 4, 32 × 32 | **RLE4** | **35752** |
| written: **with a sound stream** | 4, 32 × 32 | uncompressed 8 bits, and 8-bit PCM audio | **opens**, and plays with no error |
| written: **no `idx1` index** | 4, 32 × 32 | uncompressed 8 bits | **−2147417848**, "Automation error / The object invoked has disconnected from its clients" |
| a text file named `.avi` | | | 35752 |
| a missing file; a folder | | | **53**, "File not found" |
| `""` | | | **52**, "Bad file name or number" |

So **it plays uncompressed clips at any depth from 4 to 32 bits, and RLE8. It refuses Cinepak and RLE4.** It opens a
clip with a sound stream, and isn't measured making any sound.

**A clip with no index can crash the program.**
- On a fresh control, the Open raises −2147417848, a later Play raises 35755, and the control carries on: Stop, Close and
  another Open all work.
- **After another clip has been opened, played and closed,** the failed Open and the Play went the same way, but **the
  next `DoEvents` ended the program.**
- Windows logged an **access violation, `0xC0000005`, in MSCOMCT2.OCX at offset `0xA5F8`**, in both builds.
- The first run stopped the same way, after nineteen other clips. A run written to repeat it did too.

### Open, Play, Stop and Close

| Call | Result |
|---|---|
| **Play, before Open; and after a failed Open, or after Close** | **35755**, "Must open AVI file first" |
| Stop or Close, before Open; Close twice | no error |
| Play with no arguments | no error |
| **Play's repeat count:** 1, 0, −1, 32767, 32768, 65535, 65536 or 1.5 | no error |
| **Play's start frame:** 17 or more on a 17-frame clip | no error |
| **Play's start frame −1** | **380**, "Invalid property value" |
| **Play's end frame:** −1, 16, 17, 65535 or 65536 | no error |
| a start after the end, `Play 1, 5, 2` | no error |
| `Play "x"`; `Play Null` | **13**, "Type mismatch" |
| `Play 1, , 5`; `Play , 3` | no error |

**Play checks only that a clip is open and that the start frame isn't negative.** What a start past the last frame, or
a start after the end, plays is for the screenshots in group 2.

### The properties

- **Center and BackStyle are design-time only.** Writing either raises **382**, "Set not supported at runtime", and they
  keep their values.
- **AutoPlay and Enabled take any number.** 1, 2 and −1 all read True.
- **OLEDropMode** takes 0 and 1, and 2 or −1 raise 380.
- **`hWnd = 0` raises 450,** "Wrong number of arguments or invalid property assignment". The FlatScrollBar and the
  ImageCombo raise 383 there.
- **With AutoPlay True:**
  - Open is enough to start a clip. A second Open replaces the first, with no error.
  - Setting AutoPlay False while it plays, Play, and Close while it plays all raise nothing.

### The control's size

The Animation was 2400 × 1200 each time, with Center False, the only value it can have at run time without a line in
the file.
- **Its Width and Height didn't change when a clip opened, or while it played,** before or after `DoEvents`. That held
  for FILECOPY at 260 × 40, DOWNLOAD at 16 × 20 and COUNT8 at 128 × 128.
- **After Close they read 0 × 0,** and stayed so. A failed Open, as of Cinepak, left 2400 × 1200 after Close.
- **Move works on an open clip.** 600 × 600 read back as set.
- With AutoPlay on, a second Open read 0 × 0.

What the control draws at these sizes, and what the 0 × 0 looks like on screen, is for group 2.

### Drawing, and the window behind Width and Height

**The gallery.** A light green form held seven Animations, each a 2400 × 1200 block, 160 × 80 pixels:

| Control | Center | BackStyle | Clip |
|---|---|---|---|
| A1 | False | 0, transparent | FILECOPY, 260 × 40 |
| A2 | **True** | 0 | FILECOPY |
| A3 | False | **1, opaque** | FILECOPY |
| A4 | True | 1 | FILECOPY |
| A5 | False | 0 | COUNT8, 128 × 128, ten frames showing the digits 1 to 10 |
| A6 | True | 0 | COUNT8 |
| A7 | False | 0 | DOWNLOAD, 16 × 20 |

Center and BackStyle were set by lines in the file, the only way they can be. Each clip was opened and shown at one frame
with `Play 1, n, n`. Each window's real rectangle came from GetWindowRect, and the screenshots were taken at 96 dpi. Native
and p-code logged and drew the same.

**With Center False, the window becomes the clip's size, and Width and Height don't notice:**

| Control | Window before Open | Window after Open | Width × Height after Open |
|---|---|---|---|
| A1, A3 | 160 × 80 | **260 × 40** | 2400 × 1200 |
| A5 | 160 × 80 | **128 × 128** | 2400 × 1200 |
| A7 | 160 × 80 | **16 × 20** | 2400 × 1200 |
| A2, A4, A6, Center True | 160 × 80 | 160 × 80 | 2400 × 1200 |

- **The window keeps its top-left corner** and grows or shrinks from there, **over its neighbours if need be.**
  - A1 at 260 wide covered the left of A2.
  - A5 at 128 high covered A7.
  - A click 10 pixels inside A2's box landed on A1, which raised MouseDown at 2790, 150.
  - **A control earlier in the file is on top.**
- **Close shrinks the window to 0 × 0,** and Width and Height then read 0 × 0 too. A7 appeared once A5 had gone.
- **With Center True the window keeps its size.** The clip is centred and clipped to it: A6 showed the middle 80 rows of
  COUNT8's 128, and A2 the middle 160 columns of FILECOPY's 260.

**BackStyle:**
- **Transparent, 0, draws the clip's background in the control's BackColor**, the button face, not the form's green.
- **Opaque, 1, draws the clip's own background**, magenta in FILECOPY.
- **The control's own area outside the clip** is BackColor either way, as the bands above and below A4's clip show.

**Which frame is left showing,** on A5 with COUNT8, whose frames 0 to 9 show the digits 1 to 10:

| Call | Shown |
|---|---|
| `Play 1, 3, 3` | frame 3, "4" |
| `Play 1, 9, 9` | frame 9, "10" |
| **`Play 1, 20, 20`**, past the last frame | **frame 9, the last** |
| **`Play 1, 5, 2`**, start after end | **frame 5, the start, and it stays** |
| `Play 1, 0, -1` | ran to the last frame and stayed on it |
| `Play 2, 7, 9` | ended on frame 9 |
| `Play -1` | kept changing: "7", then "2" a third of a second later |
| Stop, during that loop | **the frame it had reached**, "5", which stayed |

### Events, with real input

Clicks were real, from a driver on the guest's console, with Text1 focused.
- **A click raised MouseDown, then the focus moved** (LostFocus on Text1, GotFocus on the Animation), then MouseUp, then
  Click. **The Animation takes the focus.**
- **x and y are in the container's scale, twips here, not pixels,** despite the type library's `OLE_XPOS_PIXELS`. A
  click 20 pixels in gave 300, 300.
- **A click lands only in the window as it now is.** On A1, resized to 260 × 40, a click 20 pixels in from the bottom
  right gave 3600, 300.
- **A double-click raised MouseDown, MouseUp, Click, then DblClick,** with no second MouseDown or MouseUp.

### The saved files

**The passes.** Two hand-written forms were opened in VB6's IDE in turn, each holding five Animations 2400 × 1200.
- **The first form** held:
  - a minimal block;
  - AutoPlay and Center True;
  - BackStyle 1 and BackColor 255;
  - Enabled False and OLEDropMode 1;
  - a minimal block whose Properties window was read. It has **24 rows**.
- **The second form** held:
  - a minimal block;
  - **every candidate line in one block**, reversed, with FullWidth 100 and FullHeight 50;
  - FullWidth and FullHeight alone;
  - `Enabled = -1 'True` alone;
  - a minimal block.
- **Each pass also added an Animation from the toolbox.**

Each form was dirtied with a comment and saved, and VB6 wrote no load log. The second save is
`corpus/inbox-controls/pending/animation/`. That form and its save were then read at run time, native and p-code, which
logged the same values.

**VB6's blocks, as it wrote them:**

```
   Begin MSComCtl2.Animation Animation6 
      Height          =   570
      Left            =   2730
      TabIndex        =   5
      Top             =   1815
      Width           =   1140
      _ExtentX        =   2011
      _ExtentY        =   1005
      _Version        =   393216
      Enabled         =   -1  'True
      FullWidth       =   76
      FullHeight      =   38
   End
   Begin MSComCtl2.Animation Animation2 
      …the same lines as far as _Version…
      AutoPlay        =   -1  'True
      Center          =   -1  'True
      BackStyle       =   1
      Enabled         =   0   'False
      OleDropMode     =   1
      BackColor       =   255
      FullWidth       =   100
      FullHeight      =   50
   End
```

**The order:** AutoPlay, Center, BackStyle, Enabled, **OleDropMode**, BackColor, FullWidth, FullHeight. That mixes the
control's own lines with the stock ones. The DTPicker and the MonthView write all their stock lines first.

**What the lines hold:**
- **`OleDropMode` is VB6's spelling here,** where every other control and the Properties window write `OLEDropMode`. A
  hand-written `OLEDropMode` line was read, since the names are case-insensitive.
- **A minimal block gains nothing.**
- **FullWidth and FullHeight are the control's size in pixels.** The toolbox Animation was 1140 × 570 twips and wrote
  76 and 38. They are written only when the block had them: hand-written 100 and 50 were kept verbatim, and minimal
  blocks never got them. **They change nothing at load.** A 2400 × 1200 block with FullWidth 100 read 2400 × 1200.
  There is no row for them in the Properties window.
- **Enabled True is written for the toolbox Animation but dropped from a block that said it.** The explicit
  `Enabled = -1 'True` came back without the line. This resisted explanation.
- **Center and BackStyle are kept and read back,** although code can't set either at run time.
- **Every line read back as written.**
- **The Properties window's 24 rows:**
  - (About), (Custom) and (Name);
  - AutoPlay False, BackColor `&H8000000F`, BackStyle `0 - cc2BackstyleTransparent`, CausesValidation True and Center
    False;
  - DragIcon, DragMode, Enabled, Height, HelpContextID, Index, Left and OLEDropMode `0 - cc2OLEDropNone`;
  - TabIndex, TabStop, Tag, ToolTipText, Top, Visible, WhatsThisHelpID and Width.

**Double-clicked from the toolbox,** an Animation is **1140 × 570 twips, 76 × 38 pixels.**
- It is centred on its own size: 2730, 1815 on a 6600 × 4200 form.
- It is written first, with the three lines above.

### In HexIDE

HexIDE doesn't know MSCOMCT2.OCX. `ControlLibraries` lists only MSCOMCTL.OCX and COMCTL32.OCX, so nothing maps an
Animation to this issue (#1096).
