## In-box controls: the TreeView (2026-10-27, #1086)

**The probes.** VB6 6.00.8176 SP6, with MSCOMCTL.OCX 6.01.9782, the library and `Object=` line of [In-box controls: the
ProgressBar](in-box-controls-the-progressbar.md).
- **The program:** a Sub Main showing a 6000×4500-twip form holding:
  - a 5000×3000-twip TreeView with VB6's double-clicked property lines;
  - a PictureBox;
  - the three-picture ImageList saved in [In-box controls: the ImageList](in-box-controls-the-imagelist.md).

  It drives the TreeView late-bound under `On Error Resume Next`, logging each value with its `TypeName`, or the error.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console, as the ListView had to be. The
  two logged the same lines throughout, apart from timings.

### The object model

- **Its members,** from the type library:
  - Style, LineStyle, Indentation, LabelEdit, PathSeparator, HideSelection, Sorted, Checkboxes, FullRowSelect, HotTracking,
    Scroll and SingleSel;
  - BorderStyle, Appearance, Enabled, Font, MousePointer, MouseIcon, OLEDragMode, OLEDropMode and hWnd;
  - Nodes, ImageList, SelectedItem and DropHighlight;
  - the methods HitTest, GetVisibleCount, StartLabelEdit, Refresh and OLEDrag.
- **It has no BackColor or ForeColor.** Reading `BackColor` late-bound is **438**.
- **Its events:** NodeClick, NodeCheck, Expand, Collapse, BeforeLabelEdit, AfterLabelEdit, Click, DblClick, the key and
  mouse events, and the OLE drag events.
- **Nodes** has Count, `Add([Relative], [Relationship], [Key], [Text], [Image], [SelectedImage])`, Item, Remove and
  Clear.
- **A Node** has:
  - Text, Key, Tag and Index;
  - Image, SelectedImage and ExpandedImage;
  - Expanded, Selected, Visible, Checked, Bold, ForeColor, BackColor and Sorted;
  - Children and FullPath;
  - Parent, Child, Root, FirstSibling, LastSibling, Next and Previous;
  - the methods EnsureVisible and CreateDragImage.
- **The Relationship values** are tvwFirst 0, tvwLast 1, tvwNext 2, tvwPrevious 3 and tvwChild 4.
- **`TypeName`:** the collection is `INodes`, and a node is `INode`.
- **A node's default member is its Text.**
- **`Nodes.Count`, a node's Index and its Children are Integers,** where the ListView's counts and indexes are Longs.

### Defaults

| Read | Logged |
|---|---|
| Style | 7, a Long |
| LineStyle, LabelEdit | 0 |
| Indentation | **566.9291**, a Single: 1000 HIMETRIC, in twips |
| PathSeparator | `\` |
| HideSelection, Scroll, Enabled, TabStop | True |
| Sorted, Checkboxes, FullRowSelect, HotTracking, SingleSel | False |
| BorderStyle, Appearance | **0**, 1 |
| MousePointer, OLEDragMode, OLEDropMode | 0 |
| Font | MS Sans Serif 8.25 |
| ImageList, SelectedItem, DropHighlight | `Nothing` |
| Nodes.Count | 0, an Integer |
| GetVisibleCount | 12, a Long. It counts the rows the control has room for, not the nodes |
| Index | 343 |

**What each property takes:**
- **Style** takes 0 to 7, and **LineStyle** and **LabelEdit** 0 and 1. One past the last, and -1, are **380**.
- **Indentation** takes 0 and up, and -1 is 380. It is kept as whole HIMETRIC: 100 read back as 99.77953, 1000 as
  1000.063, and 100000 as 100000.1.
- **PathSeparator** takes any string: `""`, `/`, and `ab` were each kept.

### Nodes.Add

**A node added with `Add()`:**

| Property | Value |
|---|---|
| Index | 1, an Integer |
| Key, Text, Tag | empty |
| Image, SelectedImage, ExpandedImage | `Empty` |
| Expanded, Selected, Checked, Bold, Sorted | False. **The first node added is not selected**, where the ListView's first item is |
| Visible | True |
| Children | 0, an Integer |
| FullPath | empty |
| Parent, Child, Next, Previous | `Nothing` |
| Root, FirstSibling, LastSibling | the node itself |

**Where a node goes.** Under root r1, children were added in this order:
- c1 and c2 with tvwChild;
- then, each relative to c1: tvwFirst, tvwLast, tvwNext, tvwPrevious, and a Relative with no Relationship.

r1's children came out as:

```
first  prev  c1  norel  next  c2  last
```

- **A Relative with no Relationship is tvwNext:** the node went straight after c1, ahead of the one added earlier with
  tvwNext.
- **A Relationship with no Relative** made a root at the end.
- **The Relative can be given by Index, by Key, or as a Node.**
- **tvwFirst relative to a root** made the new node the first root.
- **Index counts nodes in the order they were added,** not in tree order.

| Call | Logged |
|---|---|
| a Key already used | **35602** |
| Key `"5"` | **35603** |
| Key `""` | no error |
| a Relative Key that doesn't exist | **35601** |
| a Relative Index of 0 or 999 | **35600** |
| Relationship 5 or -1 | **380** |
| Relationship `"x"` | **13** |
| an Image with no ImageList | **35613** |
| `Add(, , , 42)` | Text `42`, a String |

**Lookup:**
- `Nodes(1)` and `Nodes("r1")` find the node.
- `Nodes("missing")` and `Nodes("5")` are 35601, `Nodes(0)` and `Nodes(999)` 35600, and `Nodes("")` 35603.
- `Nodes(1.5)` and `Nodes(2.5)` were both node 2.

### Navigation

- **Root is the tree's first root node, not the node's own top-level ancestor.**
  - With roots r1 and r2, Root was r1 for r2, for a child of r2, and for a grandchild of r1.
  - Once a root r0 was added in front, Root was r0 for every node.
- **The roots are siblings of each other:**
  - r1's Next was r2 and its Previous the root in front of it.
  - Its FirstSibling and LastSibling were the first and last roots.
- **FullPath** joins the Texts from the node's top-level ancestor down with PathSeparator, `Root 1\Child 1`. With
  PathSeparator `/` it was `Root 1/Child 1`, and with `""` it was `Root 1Child 1`.
- **Setting Parent moves a node:**
  - **`Set Parent =`** moved node a, with its children, under r2. **It became r2's first child**, and its Index was
    unchanged.
  - **A Parent that would be the node itself or one of its descendants is 35614,** "This would introduce a cycle".
  - `Set Parent = Nothing` is **35610**, and `Parent =` without Set is **438**.
- **Next, Previous and the other links can't be set:** `Set Next =` is 383.

### A node's properties

| Assignment | Logged |
|---|---|
| `Index =`, `Children =`, `FullPath =`, `Visible =` | **383** |
| `Key =` another node's Key | 35602. `Key = ""` is taken |
| `Text = 42` | `42`, a String |
| `Set Tag = TreeView1` | **35700** |
| `Bold = True`, `ForeColor = vbRed`, `BackColor = vbYellow` | taken |
| `Nodes.Count =`, `Set Nodes = Nothing` | 383 |

**A colour read late-bound comes back as an unsigned Long.**
- `v = n.ForeColor`, with `n As Object`, gave **VarType 19**.
- `TypeName` of it, or of `n.ForeColor` itself, is **458**, "Variable uses an Automation type not supported in Visual
  Basic".
- `Hex` gave `80000008`, and `CStr(n.BackColor)` gave `2147483653` for `&H80000005`.
- Declared `As MSComctlLib.Node`, the same read is a Long, VarType 3.

The ListView's colour properties read blank through the same late-bound helper, which fits the same cause.

### Expanding

- **A new node is collapsed.** The nodes under a collapsed node read Visible False.
- **Expanded = True on a node with no children** is taken. It read True, before a DoEvents and after.
- **EnsureVisible expands every collapsed ancestor and returns False.** It returned False for a node already visible
  too, in every call made.
- **Collapsing a node leaves its children's Expanded as it was.** A grandchild read Visible False until the node was
  expanded again.
- **A node added under an expanded node** is Visible at once.

### Selection

| Step | Selected nodes, and SelectedItem |
|---|---|
| nodes added | none, and `Nothing` |
| `Selected = True` on b, then on a1 | a1 alone, and a1 |
| `Selected = False` on a1 | none, **and SelectedItem still a1** |
| `Set SelectedItem = a11` | a11, and a11 |
| `Set SelectedItem = Nothing` | **none, and `Nothing`**. The ListView left its item selected |
| a1 selected, then its parent a collapsed | **a**, and a |
| a2 selected, then the root r1 above it collapsed | **r1**, and r1 |
| the selected a2 removed | **its parent a**, and a |
| the selected b's parent r2 removed | r1, the other root, and r1 |

### Removing nodes

- **A node's children go with it.** Removing k1, which had two children, took the count from 6 to 3.
- **Indexes are renumbered from 1, in the order the nodes were added.** k3a, held in a variable, went from Index 6 to 3.
- **A removed node raises 35605** on any read.
- `Remove 99` is 35600, and `Remove "missing"` is 35601.
- **A removed node's Key can be used again.** The new node goes at the end of the Indexes.

### Sorting

**The comparison is the ListView's.** Roots added as `b B a10 a2 10 9 "" Zeta _x -y " space" co-op coop eé ez Ole OLE`
sorted to:

```
"" " space" _x 10 9 a10 a2 b B coop co-op eé ez Ole OLE -y Zeta
```

**What sorts:**
- **The control's Sorted sorts the roots only.** A root's children kept their order until **that node's Sorted** was
  set True.
- **Setting the control's Sorted to True sorts at once,** and again when it is already True.
- **A changed Text is not re-sorted.** Sorted False leaves the order as it is.

**Adding to a sorted list:**
- **Under a node whose Sorted is True, a new child goes straight to its sorted place, whatever the Relationship.**
  - Added with tvwFirst relative to mike, yankee went after mike.
  - Added with tvwPrevious relative to mike, xray went after it too.
- **A root added with no Relative while the control is Sorted went to the end,** and moved to its sorted place at the
  next DoEvents.
- **A root added with tvwFirst stayed first** through DoEvents, until Sorted was set True again.
- A node's own Sorted stayed False when its parent, or the control, was Sorted.

### Checked

- **With Checkboxes False, a new node's Checked is False,** and `Checked = True` is taken.
- **Turning Checkboxes on clears every node's Checked.** It cleared again when turned on a second time.
- Turning Checkboxes off keeps each node's Checked.
- A node added while Checkboxes is True reads False.

### Images

| Call | Logged |
|---|---|
| `Set ImageList = ImageList1`, and `ImageList = ImageList1` without Set | bound, each |
| `Set ImageList = Picture1` | **35610** |
| `Image = 1`, `Image = "blue"` | read back as given, an Integer and a String |
| `Image = 99` | 35600, and Image is left as it was |
| `Image = "missing"` | 35601, and Image is left as it was |
| `SelectedImage = 2`, `ExpandedImage = 3` | taken |
| `Add(, , "i2", , 2, "red")` | Image 2, SelectedImage `red` |
| **`ImageList1.ListImages.Remove 3`, and `Clear`, while bound** | **no error**, where the ListView and Toolbar raise 35617. A node's Image read 3 still |
| `Image = 1` with the bound ImageList emptied | **35613** |
| `Set ImageList = Nothing` | every node's **Image, SelectedImage and ExpandedImage become 0** |

### HitTest

- **HitTest takes twips in the TreeView.** (150, 100) found the first node.
- **`HitTest(-1, -1)` returned the first node.**
- A point below the last node, or to the right of a node's text, returned `Nothing`.

### Past 32767 nodes

- **Adding never failed.** 65537 nodes were added with no error, the first 32767 in about 5 seconds.
- **Count and Index wrap as Integers:**
  - With 32768 nodes, Count read **-32768**, and so did the newest node's Index.
  - With 32770, Count read -32766. At 65536, Count read **0**, and one more node made it 1.
- **Lookup past 32767:**
  - `Nodes(32768&)` found the 32768th node.
  - `Nodes(-32768)` is 35600.
  - `Nodes("k32768")` found it by Key, with Index -32768.
- **Clearing is slow.** `Nodes.Clear` on 33000 nodes was still running after a minute of CPU in one run.

### In HexIDE

A form holding a TreeView is refused at load, as any in-box control HexIDE doesn't model is (#1086).
