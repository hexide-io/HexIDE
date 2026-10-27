# In-box control corpus

The saved fixtures cover ProgressBars from MSCOMCTL.OCX (`MSComctlLib`, 6.0) and COMCTL32.OCX (`ComctlLib`, 5.0),
and StatusBars, ImageLists and Toolbars from MSCOMCTL.OCX. An in-box control's native persistence differs from an intrinsic's: the extender's
properties sort by name, followed by the control's own run, whose ordering and nested property bags are measured
rather than inferred (hexide-io/HexIDE#1082, #1083, #1087, #1088).

## Two halves

| Folder | What it is | Walked by the round-trip lane |
|---|---|---|
| `saved/` | Forms **VB6's IDE wrote**, including its normalization of hand-written inputs | **Yes**: every byte is VB6's, and HexIDE must give each back unchanged |
| `input/` | The hand-written forms and projects VB6 was given to open | **No**: these are not VB6's bytes |
| `pending/` | **VB6's saves of in-box controls HexIDE doesn't model yet**, kept until the issue that models each moves its files into `saved/` | **No**, until then: HexIDE refuses each such form at load, so it can't round-trip yet |
| `typelib/` | MSCOMCTL.OCX's and COMCTL32.OCX's type libraries, dumped by `scripts/dump-typelib-types.ps1` and `scripts/dump-typelib-members.ps1` on the oracle VM | No: `InBoxMemberTableTests` checks the libraries and the ProgressBar's member table against them |
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

**`input/`: authored here.** Each control's property lines are named as its type library names the property, so a line
VB6 didn't recognise comes back missing and a value it changed comes back changed. Their `.vbp` files are hand-written
too: VB6's save of a form doesn't rewrite the project, so none is VB6's.

## Folders

| Folder | What it is for |
|---|---|
| `six/` | Nine 6.0 ProgressBars, each with a property group off its default, and no extents: the order VB6 writes them in, the default extents (2646 x 1323), `Value` not saved, an aligned bar laid out |
| `five/` | The same eight a 5.0 ProgressBar has, the vertical one aside: `_Version` 327682 |
| `sizes-six/`, `sizes-five/` | Every own property off its default in one control, a control whose extents agree with its size, and four aligned controls VB6 sized itself, which set the extent formula (132, 156, 177 and 180 px) |
| `window/` | One control with every own property off its default, set from VB6's Properties window. Its `MouseIcon` didn't load, so where VB6 writes one is not measured |
| `statusbar/` | Two StatusBars: one with four configured panels (a Spring with Key, Tag and ToolTipText, a Date panel, a disabled Caps panel, an invisible Contents panel), and one in Simple style with ShowTips off. The panels' `Object.Width` and `MinWidth` are HIMETRIC (#1087) |
| `statusbar-picture/` | A StatusBar whose first panel holds a bitmap and second an icon, written by hand and saved again by VB6, which kept the `.frx` byte for byte and put each `Picture` line first in its panel (#1087) |
| `toolbar/` | Two configured Toolbars and an ImageList companion, preserved verbatim (#1088); provenance above |

## ImageList saves

`saved/imagelist/` holds VB6's `three/`, `custom/` and `empty-key/` saves for #1083.
The round-trip lane walks their forms and companions. The original probe pictures remain in `pending/imagelist/pictures/`.

## `pending/`

Written by VB6's IDE (SP6) on the oracle VM, 2026-10-26, on the console session, for the measurements in the oracle's "In-box
controls: the ImageList" (hexide-io/HexIDE#1083) and "In-box controls: the StatusBar" (#1087) and "In-box controls: the Toolbar" (#1088) and "In-box controls: the ListView" (#1085) and "In-box controls: the TreeView" (#1086) and "In-box controls: the CommonDialog" (#1084) and "In-box controls: the SSTab" (#1090) and "In-box controls: the MSFlexGrid" (#1099) and "In-box controls: the RichTextBox" (#1100) and "In-box controls: the TabStrip" (#1089), and on 2026-10-27 for "In-box controls: the Slider" (#1091) and "In-box controls: the UpDown" (#1093) and "In-box controls: the DTPicker" (#1094). The `.vbp` files beside them are hand-written, as in `input/`.

| Folder | What it is |
|---|---|
| `saved/imagelist/three/` | An ImageList with 16 x 16 chosen and three pictures inserted through its property pages: `red` with Tag `t1`, `blue` with none, and an icon `ico` with Tag `t3`. The `.frx` holds each as StdPicture's CLSID, `lt\0\0`, a size and the file |
| `saved/imagelist/custom/` | The same with a Custom size of 24 by 20 and UseMaskColor unticked, holding a 32 x 32 bitmap and a 16 x 16 one |
| `saved/imagelist/empty-key/` | Four pictures, 16 x 16: no Key and no Tag; Key `k2` and no Tag; Key `k3` and Tag `t3`; no Key and Tag `t4`. An empty Key writes `Key = ""`, and an empty Tag no line |
| `imagelist/pictures/` | The four picture files inserted, made for the probe. Each is byte for byte what the `.frx` records hold |
| `listview/` | Three ListViews written by hand and saved by VB6. The first has every property off its default, its lines scrambled, and two columns with Key, Tag, Width and ImageIndex: VB6 put the lines in its own order, wrote SortOrder 1 and TextBackground 1 as `-1  'True`, and OLEDragMode and OLEDropMode twice. The second was written with only its size and `_Version`, and the third flat with a border (#1085). The `.frx` is the ImageList's |
| `listview-columns/` | A form like `listview/`, whose first ListView has HideColumnHeaders, AllowReorder and ColHdrIcons set and three plain columns, saved after a column's Icon Index was set and a column inserted in that ListView's Column Headers property page: VB6 wrote the icon as `ImageIndex` and the new column's 1440 twips as `Object.Width = 2540`, which is HIMETRIC, and moved the later columns' SubItemIndex up (#1085) |
| `treeview/` | Three TreeViews written by hand and saved by VB6: one with every property off its default, its lines scrambled and Indentation written as 529, which is HIMETRIC; one with only its size and `_Version`, which VB6 saved with `Appearance` alone, its Style being 0; and one with its defaults written out, which VB6 cut to `Style` and `Appearance` (#1086). The `.frx` is the ImageList's |
| `commondialog/` | Four CommonDialogs saved by VB6. Three were written by hand: one with every property off its default and its lines reversed, one with only its position and `_Version`, and one with its defaults written out, which VB6 cut back to the minimal block. The fourth, `CommonDialog4`, was double-clicked from the toolbox, and VB6 wrote it ahead of the other three (#1084). There is no `.frx` |
| `sstab/` | An SSTab drawn in VB6's IDE: three tabs, with a CommandButton drawn on tab 0, a TextBox on tab 1 and a CheckBox on tab 2, tab 1 left showing. The hidden tabs' controls are 75000 twips left, and each tab has a `TabPicture` record in the `.frx` (#1090) |
| `sstab-resaved/` | Three SSTabs written by hand and saved by VB6, with a fourth double-clicked from the toolbox: one with every property off its default and its lines reversed, a minimal block, and one whose `ControlEnabled` flags and lists did not match its controls, which VB6 rewrote. See the oracle entry before taking it as a model (#1090) |
| `msflexgrid/` | Three MSFlexGrids saved by VB6: one written by hand with every property off its default and its lines reversed, a minimal block, and one double-clicked from the toolbox. There is no `.frx`: cell text is not saved, only FormatString (#1099) |
| `richtextbox/` | Three RichTextBoxes saved by VB6: one written by hand with every property off its default, its lines reversed and its TextRTF inline, then Locked set in the Properties window (saved as `ReadOnly`); a minimal block; and one double-clicked from the toolbox. Every TextRTF is in the `.frx` (#1100) |
| `tabstrip/` | Three TabStrips saved by VB6: one written by hand with every property off its default, its lines reversed and three tabs with tags, tips and images by index and by key; a minimal block; and one double-clicked from the toolbox. Every one gets a Tabs block. The `.frx` is the ImageList's (#1089) |
| `defaults/` | All nine MSCOMCTL.OCX controls double-clicked from the toolbox onto an empty 6000 x 4500 form, as VB6 saved them. The ImageCombo's name, `s`, is the rig's: a keystroke meant for Ctrl+S landed in its (Name) row |
| `slider/` | Five Sliders saved by VB6: one written by hand with every property off its default and its lines reversed, its `Text` line dropped as VB6 doesn't save it; a minimal block; an upright one; one with only `Value`; and one double-clicked from the toolbox. There is no `.frx` (#1091) |
| `updown/` | Four UpDowns from MSCOMCT2.OCX saved by VB6: one written by hand with every property but the buddy's off its default and its lines reversed; a minimal block; one bound to Text2 through the Properties window, which wrote `BuddyControl`, `BuddyDispid`, the four `Orig` lines and `BuddyProperty = 0` and narrowed Text2; and one double-clicked from the toolbox. There is no `.frx` (#1093) |
| `dtpicker/` | Seven DTPickers from MSCOMCT2.OCX, written by hand with their lines reversed and saved by VB6 untouched: a minimal block; one with only `MaxDate`; one with `MinDate`, `MaxDate`, `CurrentDate`, `CustomFormat` and Format 3; one with the five calendar colours, `CheckBox`, `UpDown`, `Enabled`, `MousePointer` and `OLEDropMode`; one with `CurrentDate` and an earlier save's `Format`; one with Format 0; and one with a `Font` block. Every block gains a `Format` line, whose high 16 bits differ between IDE sessions, and a `CurrentDate`; a block without one is saved as 36494. There is no `.frx` (#1094) |
| `dtpicker-added/` | Three DTPickers saved by VB6: two minimal blocks, the second of which had its Properties window shown and so saved the moment it was read as its `CurrentDate`, and one double-clicked from the toolbox, 1500 x 750 and written first. There is no `.frx` (#1094) |
