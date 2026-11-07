## In-box classes: a bare name binds to the first library (2026-10-27, #2265)

**The obvious guess is that a control library's class needs its library's name in a `Dim`. It doesn't:** with MSCOMCTL.OCX
in the project, `Dim li As ListImage` compiles and runs. With two libraries that both declare `ListView`, the bare name
takes the one whose `Object=` line comes first in the `.vbp`.

**The probe.**
- **The software:** VB6 6.00.8176 SP6, with MSCOMCTL.OCX 6.01.9782 (MSComctlLib, Common Controls 6.0) and COMCTL32.OCX
  6.00.8105 (ComctlLib, Common Controls 5.0), on Windows 10.
- **The projects:** five Sub Main projects, each made native and p-code.
  - One referenced MSCOMCTL.OCX alone, with VB6's saved ImageList and VB6's own double-clicked ListView, TreeView and
    ProgressBar.
  - Four referenced both libraries, with a ListView and a ProgressBar of each: one per order of the two `Object=` lines in
    the `.vbp` and in the `.frm`.
- **The read:** every Set read Err straight after it. Native code and p-code logged the same lines in all five.

### Bare names, with MSCOMCTL.OCX referenced

| Declared bare, then | Err | `TypeName` | `TypeOf … Is` the bare name |
|---|---|---|---|
| `Dim il As ImageList`, `Set il = ImageList1` | 0 | ImageList | True, and True for `MSComctlLib.ImageList` |
| `Dim lis As ListImages`, `Set lis = il.ListImages` | 0 | **IImages** | True |
| `Dim li As ListImage`, `Set li = il.ListImages(1)` | 0 | **IImage** | True |
| `Dim lv As ListView`, `Set lv = ListView1` | 0 | ListView | True |
| `Dim ch As ColumnHeader`, `Set ch = lv.ColumnHeaders.Add(...)` | 0 | **IColumnHeader** | True |
| `Dim it As ListItem`, `Set it = lv.ListItems.Add(...)` | 0 | **IListItem** | True |
| `Dim tv As TreeView`, `Set tv = TreeView1` | 0 | TreeView | True |
| `Dim ns As Nodes`, `Set ns = tv.Nodes` | 0 | **INodes** | True |
| `Dim n As Node`, `Set n = tv.Nodes.Add(...)` | 0 | **INode** | True |
| `Dim pb As ProgressBar`, `Set pb = ProgressBar1` | 0 | ProgressBar | True |
| `Dim q As MSComctlLib.Node`, `Set q = n` | 0 | | `q Is n` True |
| `Set pb = ListView1`; `Set n = it`, a ListItem | **13** | | |

- **A variable dimmed but not set** reads `TypeName` Nothing.
- **`TypeName` of a control is its class, but of a control's objects it is the interface,** an `I` in front of the
  class: IImage, not ListImage; INode, not Node. `TypeOf n Is Node` is still True.
- **`TypeOf ListView1 Is ProgressBar` is False.**

### Two libraries that declare the same classes

ComctlLib and MSComctlLib both declare ListView, ListItem, ProgressBar, TreeView and the rest. The form held ListView1 and
ProgressBar1 from MSComctlLib, ListView2 and ProgressBar2 from ComctlLib.

| `Object=` first in the `.vbp` | first in the `.frm` | A bare `ListView`, `ListItem` and `ProgressBar` are |
|---|---|---|
| MSCOMCTL.OCX | MSCOMCTL.OCX | **MSComctlLib's** |
| MSCOMCTL.OCX | COMCTL32.OCX | **MSComctlLib's** |
| COMCTL32.OCX | COMCTL32.OCX | **ComctlLib's** |
| COMCTL32.OCX | MSCOMCTL.OCX | **ComctlLib's** |

- **The `.vbp`'s order decides. The `.frm`'s doesn't.** Swapping the two lines in the `.vbp` swapped the binding.
- **The other library's control doesn't fit.** With MSCOMCTL.OCX first, `Dim b As ListView: Set b = ListView2` was **13**
  "Type mismatch". So was adding a ListItem to ListView2 into a bare `ListItem`, and ProgressBar2 into a bare
  `ProgressBar`. With COMCTL32.OCX first, the same was true the other way round.
- **`TypeOf` follows the same binding.** `TypeOf ListView2 Is ListView` was False with MSCOMCTL.OCX first and True with
  COMCTL32.OCX first. `TypeOf … Is MSComctlLib.ListView` and `Is ComctlLib.ListView` each named their own library's
  control, in every order.
- **Nothing failed to compile:** VB6 gave no "Ambiguous name" for a bare name both libraries declare. `TypeName` was
  ListView for both ListViews.

### A `Reference=` library against an `Object=` one

Four more projects, native and p-code alike:
- **Two set the form's controls from MSCOMCTL.OCX.** The `.vbp` had its `Object=` line and a `Reference=` to COMCTL32.OCX's
  type library, with no control from it.
- **Two did the reverse.**
- Each pair had the two lines in both orders.

| First of the two lines in the `.vbp` | Second | A bare `ListView`, `ListItem` and `ProgressBar` are |
|---|---|---|
| `Object=` MSCOMCTL.OCX | `Reference=` COMCTL32.OCX | **MSComctlLib's**: the controls fit |
| `Reference=` COMCTL32.OCX | `Object=` MSCOMCTL.OCX | **ComctlLib's**: the form's own ListView is **13** in a bare `ListView` |
| `Object=` COMCTL32.OCX | `Reference=` MSCOMCTL.OCX | **ComctlLib's**: the controls fit |
| `Reference=` MSCOMCTL.OCX | `Object=` COMCTL32.OCX | **MSComctlLib's**: 13 again |

- **A `Reference=` line ranks exactly as an `Object=` line does: by its place in the `.vbp`.** The two kinds form one list,
  and the first library declaring the name wins.
- **The form's own controls get no priority.** With the reference first, `TypeOf ListView1 Is ListView` was False for
  the form's own ListView, and assigning it to a bare `ListView` was 13 "Type mismatch".
- **Where VB6 writes the two kinds depends on the References dialog.** A save with no edit keeps every line where it was
  read. Once Project → References has been confirmed, even with nothing changed, the next save writes every `Reference=`
  line before every `Object=` line ("A reference's path, and the line order a save writes", #2274). So a referenced type
  library outranks a component for a bare name only where the file lists it first, as a save after a References OK
  always does.
  - **Corrected 2026-10-28:** this bullet said VB6 always writes the references first, and that a referenced library
    therefore outranks every component in any project VB6 has saved. The probe it cited had confirmed the References
    dialog before saving. The follow-up found that dialog is the trigger: VB6's unedited saves of its own templates kept a
    `Reference=` after a `Designer=`, and an `Object=` after the members.
  - **Corrected 2026-10-27:** this entry first cited the 16 `.vbp` files in `corpus/inbox-controls/pending/` that have
    both kinds. Those files are hand-written, as the corpus README says, so they show nothing about VB6. The #2274
    measurement replaced them.

**Not measured:** whether the References dialog shows the libraries in the `.vbp`'s order.

### In HexIDE

The first #2265 slice implements bare ProgressBar declarations, library-sensitive Set (13 on the other library) and
`TypeOf`, using the project's referenced control-library order. The other measured classes and their interface
`TypeName` results await their control/object implementations on this base. Ranking a `Reference=` type library
against an `Object=` library remains unchanged and unmeasured.
