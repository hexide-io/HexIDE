# In-box control corpus

The saved fixtures cover ProgressBars from MSCOMCTL.OCX (`MSComctlLib`, 6.0) and COMCTL32.OCX (`ComctlLib`, 5.0),
StatusBars, ImageLists, Toolbars, TabStrips and TreeViews from MSCOMCTL.OCX, UpDowns from MSCOMCT2.OCX, CommonDialogs from COMDLG32.OCX (`MSComDlg`), and RichTextBoxes from RICHTX32.OCX (`RichTextLib`). An in-box control's native persistence differs from an intrinsic's: the extender's
properties sort by name, followed by the control's own run, whose ordering and nested property bags are measured
rather than inferred (hexide-io/HexIDE#1082, #1083, #1084, #1086, #1087, #1088, #1100).

## Two halves

| Folder | What it is | Walked by the round-trip lane |
|---|---|---|
| `saved/` | Forms **VB6's IDE wrote**, including its normalization of hand-written inputs | **Yes**: every byte is VB6's, and HexIDE must give each back unchanged |
| `input/` | The hand-written forms and projects VB6 was given to open | **No**: these are not VB6's bytes |
| `pending/` | **VB6's saves of in-box controls HexIDE doesn't model yet**, kept until the issue that models each moves its files into `saved/` | **No**, until then: HexIDE refuses each such form at load, so it can't round-trip yet |
| `typelib/` | MSCOMCTL.OCX's, COMCTL32.OCX's and MSMASK32.OCX's type libraries, dumped by `scripts/dump-typelib-types.ps1` and `scripts/dump-typelib-members.ps1` on the oracle VM. MSMASK32's constants are dumped by `scripts/dump-typelib-constants.ps1`, with its help strings removed: names, containers and values only | No: `InBoxMemberTableTests` checks the libraries and the ProgressBar's member table against them |
| `look/` | Two forms of ProgressBars, hand-written with a project each to open them by, and VB6's drawing of each: the client area of the running form, captured from the console of the oracle VM (Windows 10, 100% scaling) and cropped | No: `ProgressBarDrawingTests` draws each bar and compares it with VB6's, pixel by pixel |

An input is not a round-trip fixture because VB6 rewrote it. It added a `TabIndex` to each control, wrote the default
extents for a control read without any, dropped a `Value` line, which it doesn't save, and laid out the aligned
controls. What it wrote back is the evidence for each of those, and `ProgressBarSerializationTests` pins them.

## Provenance, stated exactly

**`saved/`: written by VB6's IDE.** The ProgressBar saves were written by VB6 6.0 SP6 (VB6.EXE 6.00.9782) on the oracle
VM, 2026-10-25, driven on the console session. Each project in `input/` was opened, its form's designer shown, one property changed through the Properties
window so the form was modified, and the form saved with Ctrl+S:
- the form's Caption, to "Saved by VB6" or "Saved by VB6 *n*", in `six/`, `five/`, `sizes-six/` and `sizes-five/`;
- the ProgressBar's Tag, to `x`, in `window/`.

The StatusBar saves were written on 2026-10-26 for the [StatusBar console measurements](../../docs/vb6-fidelity-oracle/in-box-controls-the-statusbar.md)
(#1087). Their companion picture records are VB6's bytes; the `.vbp` files are hand-written.

**`saved/toolbar/`: written by VB6's IDE on 2026-10-26.** Two hand-written Toolbars saved by VB6, with nine buttons
of every Style, an ImageList and nested ButtonMenus; ShowTips was set False in the Properties window and saved as
`ToolTips` (#1088, issuecomment-6428845889). The `.frx` is the ImageList's; the `.vbp` is hand-written. Moved from
`pending/` after `ToolbarSerializationTests` verified both files byte for byte, including after a form-caption edit.
The Toolbar's blocks are carried verbatim, and the ImageList's is modelled (#1083); ImageList binding is not implemented yet.

**`saved/treeview/`: written by VB6's IDE on 2026-10-26**, on the same oracle VM console, from a hand-written form (#1086).
Its companion belongs to the bound ImageList; its project is hand-written. Moved from `pending/` once
`TreeViewSerializationTests` verified both files byte for byte.

**`input/`: authored here.** Each control's property lines are named as its type library names the property, so a line
VB6 didn't recognise comes back missing and a value it changed comes back changed. Their `.vbp` files are hand-written
too: VB6's save of a form doesn't rewrite the project, so none is VB6's.

## Saved folders

| Folder | What it is for |
|---|---|
| `saved/six/` | Nine 6.0 ProgressBars, each with a property group off its default, and no extents: the order VB6 writes them in, the default extents (2646 x 1323), `Value` not saved, an aligned bar laid out |
| `saved/five/` | The same eight a 5.0 ProgressBar has, the vertical one aside: `_Version` 327682 |
| `saved/sizes-six/`, `saved/sizes-five/` | Every own property off its default in one control, a control whose extents agree with its size, and four aligned controls VB6 sized itself, which set the extent formula (132, 156, 177 and 180 px) |
| `saved/window/` | One control with every own property off its default, set from VB6's Properties window. Its `MouseIcon` didn't load, so where VB6 writes one is not measured |
| `saved/statusbar/` | Two StatusBars: one with four configured panels (a Spring with Key, Tag and ToolTipText, a Date panel, a disabled Caps panel, an invisible Contents panel), and one in Simple style with ShowTips off. The panels' `Object.Width` and `MinWidth` are HIMETRIC (#1087) |
| `saved/statusbar-picture/` | A StatusBar whose first panel holds a bitmap and second an icon, written by hand and saved again by VB6, which kept the `.frx` byte for byte and put each `Picture` line first in its panel (#1087) |
| `saved/toolbar/` | Two configured Toolbars and an ImageList companion, preserved verbatim (#1088); provenance above |
| `saved/treeview/` | Three TreeViews written by hand and saved by VB6: one with every property off its default, its lines scrambled and Indentation written as 529, which is HIMETRIC; one with only its size and `_Version`, which VB6 saved with `Appearance` alone, its Style being 0; and one with its defaults written out, which VB6 cut to `Style` and `Appearance` (#1086). The `.frx` is the ImageList's |
| `saved/commondialog/` | Four CommonDialogs saved by VB6. Three were written by hand: one with every property off its default and its lines reversed, one with only its position and `_Version`, and one with its defaults written out, which VB6 cut back to the minimal block. The fourth, `CommonDialog4`, was double-clicked from the toolbox, and VB6 wrote it ahead of the other three (#1084). There is no `.frx` |
| `saved/tabstrip/` | Three TabStrips saved by VB6: one written by hand with every property off its default, its lines reversed and three tabs with tags, tips and images by index and by key; a minimal block; and one double-clicked from the toolbox. Every one gets a Tabs block. The `.frx` is the ImageList's (#1089) |
| `saved/richtextbox/` | Three RichTextBoxes saved by VB6: one written by hand with every property off its default, its lines reversed and its TextRTF inline, then Locked set in the Properties window (saved as `ReadOnly`); a minimal block; and one double-clicked from the toolbox. Every TextRTF is in the `.frx` (#1100) |
| `saved/updown/` | Four UpDowns saved by VB6: off-default properties, a minimal block, a Text2 buddy binding and the toolbox default; provenance below (#1093). There is no `.frx` |
| `saved/maskedbox/` | Four MaskEdBoxes from MSMASK32.OCX saved by VB6 (#1098), after a comment typed into the form's code made it dirty: a minimal block with no extents and no `PromptChar`, given 3000 x 1125 and `PromptChar "_"`; one written by hand with every own property off its default and its lines reversed, for VB6's order, which writes `OLEDragMode` and `OLEDropMode` twice; one with no `Mask` and a `Text`; and one with a `Mask` and no `Text`. VB6 dropped every `Text` line: it isn't saved, and the Properties window has no row for it. There is no `.frx`. The hand-written form is `input/maskedbox/` |

The MaskedEdBox save is documented in [the MaskedEdBox oracle](../../docs/vb6-fidelity-oracle/the-maskededbox-mask-text-and-keystrokes.md).
It moved from pending after `MaskedEdBoxSerializationTests` verified the save and input normalization.
Saved own properties remain carried; their runtime binding and own-property editors are unfinished.

The CommonDialog save was written on 2026-10-27 with COMDLG32.OCX 6.01.9782; its project is hand-written.

The RichTextBox save was produced on 2026-10-27 (oracle PR #2250), including the second pass setting Locked
in the Properties window. Its form and companion are both required byte-for-byte round-trip evidence;
its accompanying `probe.vbp` is hand-written.

## ImageList saves

`saved/imagelist/` holds VB6's `three/`, `custom/` and `empty-key/` saves for #1083.
The round-trip lane walks their forms and companions. The original probe pictures live in `input/imagelist/pictures/`.

| Folder | What it is |
|---|---|
| `saved/imagelist/three/` | An ImageList with 16 x 16 chosen and three pictures inserted through its property pages: `red` with Tag `t1`, `blue` with none, and an icon `ico` with Tag `t3`. The `.frx` holds each as StdPicture's CLSID, `lt\0\0`, a size and the file |
| `saved/imagelist/custom/` | The same with a Custom size of 24 by 20 and UseMaskColor unticked, holding a 32 x 32 bitmap and a 16 x 16 one |
| `saved/imagelist/empty-key/` | Four pictures, 16 x 16: no Key and no Tag; Key `k2` and no Tag; Key `k3` and Tag `t3`; no Key and Tag `t4`. An empty Key writes `Key = ""`, and an empty Tag no line |
| `input/imagelist/pictures/` | The four picture files inserted, made for the probe. Each is byte for byte what the `.frx` records hold |

## UpDown saves

`saved/updown/` holds the four MSCOMCT2.OCX UpDowns VB6 SP6 saved on 2026-10-27 (#1093).
One has its own properties off their defaults, one is minimal, one is bound to Text2 through the
Properties window, and one was double-clicked from the toolbox. The bound block records BuddyControl,
BuddyDispid, OrigLeft/Top/Right/Bottom and BuddyProperty = 0; Text2 is saved narrowed. There is no `.frx`.
The round-trip lane walks the save; loaded UpDown blocks remain read-only in the designer and are
preserved verbatim when the form is edited. The `.vbp` is hand-written, as in `input/`.

## Pending saves

Written by VB6's IDE (SP6) on the oracle VM, 2026-10-26, on the console session, for the measurements in the oracle's "In-box
controls: the ImageList" (hexide-io/HexIDE#1083) and "In-box controls: the StatusBar" (#1087) and "In-box controls: the Toolbar" (#1088) and "In-box controls: the ListView" (#1085) and "In-box controls: the TreeView" (#1086) and "In-box controls: the CommonDialog" (#1084) and "In-box controls: the SSTab" (#1090) and "In-box controls: the MSFlexGrid" (#1099) and "In-box controls: the RichTextBox" (#1100) and "In-box controls: the TabStrip" (#1089), and on 2026-10-27 for "In-box controls: the Slider" (#1091) and "In-box controls: the UpDown" (#1093) and "In-box controls: the DTPicker" (#1094) "In-box controls: the MonthView" (#1095), "In-box controls: the ImageCombo" (#1092), "In-box controls: the FlatScrollBar" (#1097) and "In-box controls: the Animation" (#1096). The `.vbp` files beside them are hand-written, as in `input/`.

| Folder | What it is |
|---|---|
| `pending/listview/` | Three ListViews written by hand and saved by VB6. The first has every property off its default, its lines scrambled, and two columns with Key, Tag, Width and ImageIndex: VB6 put the lines in its own order, wrote SortOrder 1 and TextBackground 1 as `-1  'True`, and OLEDragMode and OLEDropMode twice. The second was written with only its size and `_Version`, and the third flat with a border (#1085). The `.frx` is the ImageList's |
| `pending/listview-columns/` | A form like `pending/listview/`, whose first ListView has HideColumnHeaders, AllowReorder and ColHdrIcons set and three plain columns, saved after a column's Icon Index was set and a column inserted in that ListView's Column Headers property page: VB6 wrote the icon as `ImageIndex` and the new column's 1440 twips as `Object.Width = 2540`, which is HIMETRIC, and moved the later columns' SubItemIndex up (#1085) |
| `pending/sstab/` | An SSTab drawn in VB6's IDE: three tabs, with a CommandButton drawn on tab 0, a TextBox on tab 1 and a CheckBox on tab 2, tab 1 left showing. The hidden tabs' controls are 75000 twips left, and each tab has a `TabPicture` record in the `.frx` (#1090) |
| `pending/sstab-resaved/` | Three SSTabs written by hand and saved by VB6, with a fourth double-clicked from the toolbox: one with every property off its default and its lines reversed, a minimal block, and one whose `ControlEnabled` flags and lists did not match its controls, which VB6 rewrote. See the oracle entry before taking it as a model (#1090) |
| `pending/msflexgrid/` | Three MSFlexGrids saved by VB6: one written by hand with every property off its default and its lines reversed, a minimal block, and one double-clicked from the toolbox. There is no `.frx`: cell text is not saved, only FormatString (#1099) |
| `pending/defaults/` | All nine MSCOMCTL.OCX controls double-clicked from the toolbox onto an empty 6000 x 4500 form, as VB6 saved them. The ImageCombo's name, `s`, is the rig's: a keystroke meant for Ctrl+S landed in its (Name) row |
| `pending/slider/` | Five Sliders saved by VB6: one written by hand with every property off its default and its lines reversed, its `Text` line dropped as VB6 doesn't save it; a minimal block; an upright one; one with only `Value`; and one double-clicked from the toolbox. There is no `.frx` (#1091) |
| `pending/dtpicker/` | Seven DTPickers from MSCOMCT2.OCX, written by hand with their lines reversed and saved by VB6 untouched: a minimal block; one with only `MaxDate`; one with `MinDate`, `MaxDate`, `CurrentDate`, `CustomFormat` and Format 3; one with the five calendar colours, `CheckBox`, `UpDown`, `Enabled`, `MousePointer` and `OLEDropMode`; one with `CurrentDate` and an earlier save's `Format`; one with Format 0; and one with a `Font` block. Every block gains a `Format` line, whose high 16 bits differ between IDE sessions, and a `CurrentDate`; a block without one is saved as 36494. There is no `.frx` (#1094) |
| `pending/dtpicker-added/` | Three DTPickers saved by VB6: two minimal blocks, the second of which had its Properties window shown and so saved the moment it was read as its `CurrentDate`, and one double-clicked from the toolbox, 1500 x 750 and written first. There is no `.frx` (#1094) |
| `pending/monthview/` | Nine MonthViews from MSCOMCT2.OCX saved by VB6: a minimal block; six written by hand with their lines reversed, for the dates and selection, the appearance and switches, two months by two, the six colours and a `Font`; a minimal block whose Properties window was shown; and one double-clicked from the toolbox, written first. Every block gains `Appearance`, a `StartOfWeek` whose high 16 bits differ between IDE sessions, and a `CurrentDate`; the toolbox one has StartOfWeek 2, the locale's Monday. The ones that size themselves were rewritten at their new size. There is no `.frx` (#1095) |
| `pending/imagecombo/` | Eight ImageCombos from MSCOMCTL.OCX and an ImageList, saved by VB6: a minimal block; five written by hand with their lines reversed, for `Text`, the `ImageList` binding with `Indentation` and `Locked`, the switches, the two colours and a `Font`; a minimal block whose Properties window was shown; and one double-clicked from the toolbox, written first. A minimal block gains nothing. The toolbox one gets the window colours and a `Text` of its name. The `Font` block was rewritten at its new height. There is no `.frx` (#1092) |
| `pending/flatscrollbar/` | Seven FlatScrollBars from MSCOMCT2.OCX saved by VB6: a minimal block; four written by hand with their lines reversed, for the range and change sizes, `Orientation` 0, and the appearance and switches; a minimal block whose Properties window was shown; and one double-clicked from the toolbox, written first. `Arrows`, `Orientation` and `Appearance` are written with a high word, which a second IDE session wrote identically. The vertical block has no `Arrows` line. There is no `.frx` (#1097) |
| `pending/animation/` | Six Animations from MSCOMCT2.OCX saved by VB6: a minimal block; one written by hand with every candidate line reversed, for VB6's order; one with only `FullWidth` and `FullHeight`; one with an explicit `Enabled = True`, which VB6 dropped; a minimal block whose Properties window was shown; and one double-clicked from the toolbox, written first, with `Enabled`, `FullWidth` and `FullHeight`. VB6 writes `OleDropMode`, not `OLEDropMode`. There is no `.frx` (#1096) |
