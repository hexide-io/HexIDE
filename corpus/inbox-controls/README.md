# In-box control corpus

The only files in the repository holding one of VB6's in-box ActiveX controls that HexIDE models: the ProgressBar, from
MSCOMCTL.OCX (`MSComctlLib`, 6.0) and COMCTL32.OCX (`ComctlLib`, 5.0). They exist because nothing else in the tree is
one, and because what VB6 writes for such a control isn't what it writes for an intrinsic: the extender's properties by
name, then `_ExtentX`, `_ExtentY` and `_Version`, then the control's own properties in the control's own order
(hexide-io/HexIDE#1082).

## Two halves

| Folder | What it is | Walked by the round-trip lane |
|---|---|---|
| `saved/` | Forms **VB6's IDE wrote**, each by its own save of the form in `input/` of the same name | **Yes**: every byte is VB6's, and HexIDE must give each back unchanged |
| `input/` | The hand-written forms and projects VB6 was given to open | **No**: these are not VB6's bytes |
| `pending/` | **VB6's saves of in-box controls HexIDE doesn't model yet**, kept until the issue that models each moves its files into `saved/` | **No**, until then: HexIDE refuses each such form at load, so it can't round-trip yet |
| `typelib/` | MSCOMCTL.OCX's and COMCTL32.OCX's type libraries, dumped by `scripts/dump-typelib-types.ps1` and `scripts/dump-typelib-members.ps1` on the oracle VM | No: `InBoxMemberTableTests` checks the libraries and the ProgressBar's member table against them |
| `look/` | Two forms of ProgressBars, hand-written with a project each to open them by, and VB6's drawing of each: the client area of the running form, captured from the console of the oracle VM (Windows 10, 100% scaling) and cropped | No: `ProgressBarDrawingTests` draws each bar and compares it with VB6's, pixel by pixel |

An input is not a round-trip fixture because VB6 rewrote it. It added a `TabIndex` to each control, wrote the default
extents for a control read without any, dropped a `Value` line, which it doesn't save, and laid out the aligned
controls. What it wrote back is the evidence for each of those, and `ProgressBarSerializationTests` pins them.

## Provenance, stated exactly

**`saved/`: written by VB6's IDE.** VB6 6.0 SP6 (VB6.EXE 6.00.9782) on the oracle VM, 2026-10-25, driven on the console
session. Each project in `input/` was opened, its form's designer shown, one property changed through the Properties
window so the form was modified, and the form saved with Ctrl+S:
- the form's Caption, to "Saved by VB6" or "Saved by VB6 *n*", in `six/`, `five/`, `sizes-six/` and `sizes-five/`;
- the ProgressBar's Tag, to `x`, in `window/`.

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

## `pending/`

Written by VB6's IDE (SP6) on the oracle VM, 2026-10-26, on the console session, for the measurements in the oracle's "In-box
controls: the ImageList" (hexide-io/HexIDE#1083) and "In-box controls: the StatusBar" (#1087) and "In-box controls: the Toolbar" (#1088) and "In-box controls: the ListView" (#1085). The `.vbp` files beside them are hand-written, as in `input/`.

| Folder | What it is |
|---|---|
| `imagelist/three/` | An ImageList with 16 x 16 chosen and three pictures inserted through its property pages: `red` with Tag `t1`, `blue` with none, and an icon `ico` with Tag `t3`. The `.frx` holds each as StdPicture's CLSID, `lt\0\0`, a size and the file |
| `imagelist/custom/` | The same with a Custom size of 24 by 20 and UseMaskColor unticked, holding a 32 x 32 bitmap and a 16 x 16 one |
| `imagelist/empty-key/` | Four pictures, 16 x 16: no Key and no Tag; Key `k2` and no Tag; Key `k3` and Tag `t3`; no Key and Tag `t4`. An empty Key writes `Key = ""`, and an empty Tag no line |
| `imagelist/pictures/` | The four picture files inserted, made for the probe. Each is byte for byte what the `.frx` records hold |
| `statusbar/` | Two StatusBars: one with four configured panels (a Spring with Key, Tag and ToolTipText, a Date panel, a disabled Caps panel, an invisible Contents panel), and one in Simple style with ShowTips off. The panels' `Object.Width` and `MinWidth` are HIMETRIC (#1087) |
| `statusbar-picture/` | A StatusBar whose first panel holds a bitmap and second an icon, written by hand and saved again by VB6, which kept the `.frx` byte for byte and put each `Picture` line first in its panel (#1087) |
| `toolbar/` | Two Toolbars, written by hand and saved by VB6: nine buttons of every Style bound to an ImageList, with a dropdown's ButtonMenus, and a bar with its own properties off their defaults, ShowTips set False in the Properties window and saved as `ToolTips` (#1088). The `.frx` is the ImageList's |
| `listview/` | Three ListViews written by hand and saved by VB6. The first has every property off its default, its lines scrambled, and two columns with Key, Tag, Width and ImageIndex: VB6 put the lines in its own order, wrote SortOrder 1 and TextBackground 1 as `-1  'True`, and OLEDragMode and OLEDropMode twice. The second was written with only its size and `_Version`, and the third flat with a border (#1085). The `.frx` is the ImageList's |
| `listview-columns/` | The same form, after a column's Icon Index was set and a column inserted in the first ListView's Column Headers property page: VB6 wrote the icon as `ImageIndex` and the new column's 1440 twips as `Object.Width = 2540`, which is HIMETRIC, and moved the later columns' SubItemIndex up (#1085) |
| `defaults/` | All nine MSCOMCTL.OCX controls double-clicked from the toolbox onto an empty 6000 x 4500 form, as VB6 saved them. The ImageCombo's name, `s`, is the rig's: a keystroke meant for Ctrl+S landed in its (Name) row |
