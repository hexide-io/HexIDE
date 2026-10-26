## In-box controls: the ImageList (2026-10-26, #1083)

**Why the ImageList.** The ListView, TreeView, Toolbar, TabStrip and ImageCombo all draw their icons from one. It lives
in MSCOMCTL.OCX with the ProgressBar, so its library record, its `Object=` line and the run of lines VB6 writes are
those of [In-box controls: the ProgressBar](in-box-controls-the-progressbar.md). This entry covers what is its own.

**The probes.** VB6 6.00.8176 SP6, with MSCOMCTL.OCX 6.01.9782 registered in the oracle VM, its design-time licence
included.
- **Run time:** a Sub Main loads a form holding two ImageLists and an AutoRedraw PictureBox. It drives the ImageList
  late-bound under `On Error Resume Next`, logging each value with its `TypeName`, or the error number and its text.
  Pictures came from `LoadPicture` of four files made for the probe: 16×16 red and blue bitmaps, the red one with a
  4×4 magenta corner, a 32×32 green bitmap and a 16×16 icon. They are kept in `corpus/inbox-controls/pending/imagelist/pictures`.
  It was built with `/make` as native code and as p-code in session 0. Both builds logged the same lines.
- **The designer:** the IDE on the console through the agent. Each MSCOMCTL tool was double-clicked onto an empty
  6000×4500 form, and the form saved.
- **The property pages:** the ImageList's (Custom) row opened its Property Pages. Pictures were inserted through
  its Insert Picture button and the file dialog, with a Key and a Tag typed for each, then OK, then Ctrl+S. VB6's saves
  are in `corpus/inbox-controls/pending/imagelist`. Each saved form was then built and run, and its images listed.

### The control

- **No events.** Its event interface, `ImageListEvents`, declares none.
- **Its members,** from the type library:
  - `ImageWidth`, `ImageHeight` (Integer);
  - `MaskColor`, `BackColor` (OLE_COLOR);
  - `UseMaskColor` (Boolean);
  - `ListImages`;
  - `hImageList` (a handle);
  - the method `Overlay(Key1, Key2)`.

  `ListImages` has `Count`, `Item(Index)`, `Add([Index], [Key], [Picture])`, `Remove(Index)` and `Clear`. A
  `ListImage` has `Index`, `Key`, `Tag`, `Picture`, `Draw(hDC, [x], [y], [Style])` and `ExtractIcon`.
- **`TypeName`:**
  - `ImageList1` is `ImageList`, and `TypeOf ImageList1 Is MSComctlLib.ImageList` is True.
  - `ImageList1.ListImages` is **`IImages`**, and what `Add` returns is **`IImage`**: the interfaces' names, not
    ListImages and ListImage.
- **The extender** is an invisible-at-run-time control's, as for a UserControl with InvisibleAtRuntime ([A
  UserControl's own properties at run time](a-usercontrols-own-properties-at-run-time.md)):
  - Name, Tag, Parent and Object;
  - Index, which is 343;
  - Left and Top, which are 393;
  - Visible, Width, Height, Container, Enabled, TabIndex and hWnd, each 438.

### Defaults

| Read | Logged |
|---|---|
| ImageWidth, ImageHeight | `0`, `0`, Integers |
| MaskColor | `&HC0C0C0`, a Long |
| BackColor | `&H80000005`, a Long: the window colour |
| UseMaskColor | `True` |
| hImageList | `0`, while the list is empty |
| ListImages.Count | `0`, an Integer |

### ListImages

Each row follows the ones above it on the same list.

| Call | Logged |
|---|---|
| `Add(, "r", red)` | returns Index 1, Key `r`. **ImageWidth and ImageHeight become 16**, the first picture's size. hImageList is nonzero |
| `Add(, "r", blue)`, a key already used | **35602**, "Key is not unique in collection" |
| `Add(, "", blue)` | no error; Key empty |
| `Add(, "5", blue)`, `Add(, 7, blue)` | **35603**, "Invalid key": a key that reads as a number is refused |
| `Add(, "7x", blue)` | no error |
| `Add(1, "first", blue)` | no error; it goes in at 1 and the rest move up |
| `Add(99, …)`, `Add(0, …)` | **35600**, "Index out of bounds" |
| `Add(Count + 1, …)` | no error; at the end |
| `Add(, "big", green32)`, a 32×32 picture in a 16×16 list | no error |
| `Add(, "ico", icon)` | no error |
| `Add(, "nopic")`, no picture | **35607**, "Required argument is missing" |
| `Add(, , Nothing)` | **481**, "Invalid picture" |
| `Add(, , "abc")` | **13** |
| `Add(, , blue)`, no key | no error; Key empty |

| Lookup | Logged |
|---|---|
| `ListImages(1)`, `(2)` | Index 1 and 2 |
| `ListImages("r")`, `ListImages.Item("r")`, `ListImages!r` | Index 2 |
| `ListImages("5")`, `("missing")`, `("1")`, `(Empty)` | **35601**, "Element not found". A String is always a key |
| `ListImages("")`, `(Null)` | **35603**, "Invalid key" |
| `ListImages(0)`, `(-1)`, `(99)`, `(True)` | **35600**, "Index out of bounds". True is -1 |
| `ListImages(1.5)`, `(2.5)` | Index 2: each rounds half to even |
| `v = ListImages(1)`, with no Set | **438**: a ListImage has no default member |

| Remove and Clear | Logged |
|---|---|
| `Remove "first"`, then `Remove 1` | no error; the rest move down |
| `Remove 99`, `Remove 0` | 35600 |
| `Remove "missing"` | 35601 |
| `Clear` | Count 0, and **ImageWidth and ImageHeight go back to 0**, hImageList to 0 |

`For Each li In ImageList1.ListImages` walks them in Index order.

### A ListImage

| Call | Logged |
|---|---|
| `Picture` | a `Picture`, Type 1 for a bitmap and 3 for an icon, 423×423 HIMETRIC for 16 px. The 32×32 one stays 847×847 |
| `Key = "first"`, another's key | 35602 |
| `Key = "r2"`, then `Key = ""` | no error, each |
| `Index = 1` | **383**, "Property is read-only" |
| `Tag = 42` | no error; `42`, an Integer |
| `Set Tag = ImageList1`, its own ImageList | **35700**, "Circular object referencing is not allowed" |
| `Set Tag = Picture1` | no error; a PictureBox |
| `Set Picture = …`, `= Nothing` | 383: a ListImage's picture can't be replaced |
| `ExtractIcon`, of a bitmap | a Picture, Type 3, 423×423 |
| `Overlay("r", "first")` | a Picture, Type 1, 423×423. `Overlay(1, 99)` is 35600 |

### The ImageList's properties, with images in it

| Assignment | Logged |
|---|---|
| `ImageWidth = 32` | **35611**, "Property is read-only if image list contains images" |
| `ImageHeight = 16`, its own value | no error |
| `MaskColor = &HFF00FF`, `UseMaskColor = False`, `BackColor = vbRed` | no error, each |
| `ListImages.Count = 3`, `Set ListImages = …`, `hImageList = 0` | 383 |
| `ImageWidth = 32`, `= 0` once empty | no error |
| `ImageWidth = -1` | 380 |
| `ImageWidth = 40000` | 6, Overflow: an Integer |

### Drawing

`ListImage.Draw hDC, x, y, Style` drew into an AutoRedraw PictureBox, whose pixels were read back with `Point`:
- **x and y are twips,** whatever the target's ScaleMode. `Draw hDC, 150, 150` put the image's corner at pixel 10, 10
  on a twips PictureBox, and on a pixels one too. Both default to 0.
- **The image is drawn at the list's size.** A 16×16 picture in a 32×32 list filled pixels 0 to 31. A 32×32 one in a
  16×16 list filled 0 to 15.
- **Style 0, normal, draws the masked pixels in the ImageList's BackColor.** It's the window colour by default, and
  blue once BackColor was blue.
- **Style 1, transparent, leaves them showing what was there.**
- **Styles 2 and 3, selected and focus, cover the image in a checkerboard of the highlight colour,** `&HD77800` here.
  Every pixel whose x + y is even is the highlight, and the others are the image.
- **The mask follows MaskColor and UseMaskColor as they are when the image is drawn,** for every image, whenever it was
  added:
  - With MaskColor magenta and UseMaskColor True, the magenta corner was masked.
  - Setting MaskColor to grey, with no new Add, unmasked it.
  - An image added while UseMaskColor was False was masked once it was True again.

### Double-clicked onto a form

```
   Begin MSComctlLib.ImageList ImageList1 
      Left            =   2715
      Top             =   1965
      _ExtentX        =   1005
      _ExtentY        =   1005
      BackColor       =   -2147483643
      MaskColor       =   12632256
      _Version        =   393216
   End
```

- **It is 38×38 pixels**, which the toolbar showed as `570 x 570`, with extents of 1005.
- **It is centred by its own size, in pixels, and not put on the grid.** The 6000×4500 form is 400×300 pixels, and
  (400 − 38) / 2 is 181, which is 2715 twips. (300 − 38) / 2 is 131, which is 1965.
- **ImageWidth, ImageHeight and UseMaskColor are not written at their defaults.** BackColor and MaskColor are written
  even at theirs.
- **The other MSCOMCTL controls,** double-clicked onto the same form, were centred the same way, by sizes of their own
  in pixels and off the grid. A TabStrip, ListView, TreeView or ProgressBar is 100×50 pixels, at Left 2250 and Top 1875.
  The aligned Toolbar went to Top 0, but the Align Bottom StatusBar stayed at Top 2055, mid-form, in the designer. VB6's
  save of all nine is `corpus/inbox-controls/pending/defaults`. In it the ImageCombo is named `s`, because one of the
  rig's keystrokes landed in its (Name) row, so don't read that name as VB6's.

### The Properties window and the property pages

- **The Properties window lists 12 rows.** Read on the console: (About), (Custom), (Name), BackColor, ImageHeight,
  ImageWidth, Index, Left and MaskColor. The other three weren't captured. By the type library and the extender they
  are Tag, Top and UseMaskColor.
- **The (Custom) row opens Property Pages with three tabs:**
  - **General:** options 16 x 16, 32 x 32, 48 x 48 and Custom, with Height and Width boxes, and a UseMaskColor box.
    A new list showed Custom, with 0 and 0.
  - **Images:** a Current Image group holding Index, Key and Tag, the Images list, Insert Picture..., Remove Picture,
    and an Image Count.
  - **Color:** not opened.
- **The file dialog** Insert Picture opens is captioned "Select picture".
- **A 32×32 picture went into a 24×20 list with no message.**

### The `.frm` and the `.frx`

Three pictures inserted with 16 x 16 chosen: the red bitmap with Key `red` and Tag `t1`, the blue one with Key `blue`
and no Tag, and the icon with Key `ico` and Tag `t3`:

```
   Begin MSComctlLib.ImageList ImageList1 
      Left            =   2715
      Top             =   1965
      _ExtentX        =   1005
      _ExtentY        =   1005
      BackColor       =   -2147483643
      ImageWidth      =   16
      ImageHeight     =   16
      MaskColor       =   12632256
      _Version        =   393216
      BeginProperty Images {2C247F25-8591-11D1-B16A-00C0F0283628} 
         NumListImages   =   3
         BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628} 
            Picture         =   "Form1.frx":0000
            Key             =   "red"
            Object.Tag             =   "t1"
         EndProperty
         BeginProperty ListImage2 {2C247F27-8591-11D1-B16A-00C0F0283628} 
            Picture         =   "Form1.frx":0352
            Key             =   "blue"
         EndProperty
         BeginProperty ListImage3 {2C247F27-8591-11D1-B16A-00C0F0283628} 
            Picture         =   "Form1.frx":06A4
            Key             =   "ico"
            Object.Tag             =   "t3"
         EndProperty
      EndProperty
   End
```

- **The run is not the ProgressBar's.** After the extents come the control's own properties, then `_Version`, then the
  `Images` group. The ProgressBar writes `_Version` straight after the extents.
- **The own properties come in this order:** BackColor, ImageWidth, ImageHeight, MaskColor, UseMaskColor.
  - A Custom size of 24 by 20 wrote `ImageWidth = 24` and `ImageHeight = 20`.
  - UseMaskColor unticked wrote `UseMaskColor = 0 'False` after MaskColor.
- **`Object.Tag`** is the Tag, and its name is padded as though it were `Tag`, so `=` sits seven columns further right
  than in the lines around it. An empty Tag writes no line. Whether an empty Key writes one wasn't saved.
- **The `.frx` holds each picture as an object record:**
  - a 4-byte length of what follows;
  - StdPicture's CLSID, `{0BE35204-8F91-11CE-9DE3-00AA004BB851}`, 16 bytes;
  - `lt\0\0`;
  - a 4-byte size;
  - **the picture file itself, byte for byte as inserted**: each BMP and the ICO were identical to the file chosen.

  So a record is 24 bytes longer than its file, and the records follow one another with nothing between. A form's
  `Picture` record has no CLSID: it's the length, `lt\0\0`, the size and the data.
- **VB6's save is what a run loads.** Both saved forms, built and run, listed every image with its Key, its Tag (as a
  String, empty where none was set) and its picture's type and size. They also listed the list's ImageWidth, ImageHeight
  and UseMaskColor.

### In HexIDE

A form holding an ImageList is refused at load, as any in-box control HexIDE doesn't model is (#1083).
