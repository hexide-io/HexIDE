## In-box controls: the TreeView (2026-10-27, #1086)

**The probes.** VB6 SP6 (6.00.9782), with MSCOMCTL.OCX 6.01.9782, the library and `Object=` line of [In-box controls: the
ProgressBar](in-box-controls-the-progressbar.md).
- **The program:** a Sub Main showing a 6000×4500-twip form holding:
  - a 5000×3000-twip TreeView with VB6's double-clicked property lines;
  - a PictureBox;
  - the three-picture ImageList saved in [In-box controls: the ImageList](in-box-controls-the-imagelist.md).

  It drives the TreeView late-bound under `On Error Resume Next`, logging each value with its `TypeName`, or the error.
- **The events program:** the same form, with a TextBox and KeyPreview on, and a tree with LineStyle 1 of three roots:
  - Root 1, over Alpha (itself over Alpha one) and Bravo;
  - Root 2, over Charlie;
  - Root 3.

  A driver on the console clicked with real mouse input and typed with real keystrokes, and marked each action in the
  log. It found where to click from a HitTest scan. Each event handler logged itself, with the selection and which
  nodes were expanded.
- **The drawing program:** the same form, setting up states in turn and asking the driver for a screenshot of each. The
  guest runs at 96 dpi, and a VB6 EXE has no manifest, so the control draws without visual styles.
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
- **EnsureVisible expands every collapsed ancestor.** In this probe it returned False every time, for a node already
  visible too, but every node here was in view. **It returns True when it has to scroll:** see "Scrolling" below.
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

### Events

**The layout the driver found,** with the default Indentation of 38 pixels:
- Rows are 16 pixels high, the first at the top.
- A root's label starts 38 pixels in, so with LineStyle 1 a root's button sits in the first 38 pixels.
- A child's label is one Indentation further in.
- With Checkboxes on, the box takes the place where the label began, and the label moves right.

**The mouse:**

| Action | Events, in order |
|---|---|
| click an unselected node's label | MouseDown, **NodeClick**, MouseUp, Click |
| click a node's plus or minus button | MouseDown, **Expand** or **Collapse**, MouseUp, Click. **No NodeClick, and the selection doesn't move** |
| double-click a node's label | MouseDown, NodeClick, MouseUp, Click, **Expand**, DblClick, MouseUp. A double-click toggles the node |
| click the selected node's label again, with LabelEdit automatic | MouseDown, NodeClick, MouseUp, Click, and a moment later BeforeLabelEdit. **NodeClick is raised again for the node already selected** |
| the same, with LabelEdit manual | MouseDown, NodeClick, MouseUp, Click, and no edit |
| right-click a node's label | MouseDown with Button 2, **NodeClick**, MouseUp with Button 2, **Click**. **It selects the node** |
| click the empty area below the nodes | MouseDown, MouseUp, Click. The selection is unchanged |
| click a node's checkbox | MouseDown, **NodeCheck** with the new Checked, MouseUp, Click. **No NodeClick, and the selection doesn't move** |

- **MouseDown and MouseUp give X and Y in twips** from the control's top-left.
- **The TreeView selects its first node when it gets the focus with nothing selected.** The form showed with Root 1
  selected though nothing had selected it, where nodes added after the control had the focus left SelectedItem
  `Nothing`.

**The keyboard,** as the TreeView's KeyDown and KeyUp logged it:

| Key | Events |
|---|---|
| Home | KeyDown, NodeClick on the first node, KeyUp |
| Down | KeyDown, NodeClick on the next visible node, KeyUp |
| Right, on a collapsed node | KeyDown, **Expand**, KeyUp |
| Right, on an expanded node | KeyDown, NodeClick on its first child, KeyUp |
| Left, on an expanded node | KeyDown, **Collapse**, KeyUp |
| Left, on a collapsed child | KeyDown, NodeClick on its parent, KeyUp |
| numpad `*` | KeyDown, Expand, KeyUp |
| numpad `-` | KeyDown, Collapse, KeyUp |
| **Enter** | **Expand or Collapse, toggling the selected node, then** KeyDown and KeyUp. The Expand or Collapse comes **before** the KeyDown |
| Space, with Checkboxes on | KeyDown, NodeCheck on the selected node, KeyUp |

Typing a letter, `q`, `a` or `r`, raised KeyDown and KeyUp and nothing else.

**SingleSel True:**
- **Clicking a node's label expands it,** with Expand raised before NodeClick.
- **It also collapses the node selected before,** with that node's Collapse raised first. Clicking Root 1 with Root 2
  selected raised Collapse Root 2, Expand Root 1, NodeClick Root 1.
- Clicking Alpha under Root 1 expanded Alpha and left Root 1 open.
- **Collapsing Root 1 with its button moved the selection from Alpha to Root 1 and raised no NodeClick.**

**Editing a label** behaves as the ListView's does:
- StartLabelEdit raises BeforeLabelEdit before it returns.
- Enter after typing raises AfterLabelEdit with the new text in NewString, a String, and **the node's Text still the old
  one inside the event**.
- **Escape raises no AfterLabelEdit, and nor does Enter with the text unchanged.**
- Enter with the text deleted raised AfterLabelEdit with NewString `""`, and the node was left with no text.
- `Cancel = True` in BeforeLabelEdit opens no edit box, and the keys typed then go to the TreeView. `Cancel = True` in
  AfterLabelEdit keeps the old text.

**What code raises:**
- **Expanded = True raises Expand, and Expanded = False raises Collapse.** The ListView's code raised nothing.
- **EnsureVisible raises Expand for each ancestor it opens.**
- Expanded = False on the parent of the selected node moved the selection to the parent, raising Collapse and **no
  NodeClick**.
- **These raised nothing:**
  - setting Selected, Set SelectedItem, Checked or Text;
  - setting Sorted;
  - Add, Remove or Clear.

### Mouse coordinates: from the client area (2026-10-27)

**The probe:** a TreeView and a ListView with their default edges, and one of each with Appearance 0 and BorderStyle 0,
flat and borderless, each 2400 × 1500 twips. A driver on the guest's console clicked each at 21,21 and at 40,30
pixels from its outer top-left corner, from GetWindowRect. Native and p-code logged the same events, apart from where
the pointer rested when the program started.

| Control | Edge, each side, from GetClientRect | Click 21,21 px in: X, Y | Click 40,30 px in: X, Y |
|---|---|---|---|
| TreeView, Appearance 1, BorderStyle 0: the defaults | 2 px | **285, 285** | 570, 420 |
| TreeView, Appearance 0, BorderStyle 0 | none | **315, 315** | 600, 450 |
| ListView, Appearance 1, BorderStyle 1: its defaults | **3 px** | **270, 270** | 555, 405 |
| ListView, Appearance 0, BorderStyle 0 | none | 315, 315 | 600, 450 |

- **X and Y are measured from the client area, inside the edge,** in twips at 15 a pixel. A click 21 pixels in from
  the outer corner of a default TreeView is 19 pixels into its client, 285 twips. On a flat one it is 315.
- **MouseDown, MouseMove and MouseUp all report the same point** for a click.
- The ListView's default edge is 3 pixels, where the TreeView's is 2: the ListView's defaults are BorderStyle 1 with
  Appearance 1, the 3D edge and a line inside it.

### Label editing, in full (2026-10-27, #2360)

**The probe:**
- **The form:** TreeView1 (Style 7, nodes `Node 1` to `Node 5`), a Report-view ListView1 beside it, and Text1, in that
  tab order.
- **The input:** real clicks, double-clicks and keys from a driver on the guest's console. Each case set the tree up
  afresh with Node 2 selected and the focus on the tree.
- **What was logged:** every event, with its time. A Timer logged the edit box from `TVM_GETEDITCONTROL` whenever it
  changed: its rectangle in the tree's client coordinates, its text, the label's rectangle from `TVM_GETITEMRECT`, the
  selected node's Text, and the active control.
- **The builds:** native and p-code logged the same lines, times and window handles aside. The ListView behaved the
  same way throughout, as its own entry records.

**What starts an edit:**

| Action | Result |
|---|---|
| **Click the selected node's label**, LabelEdit 0 | MouseDown, NodeClick, MouseUp, then **BeforeLabelEdit 500 to 520 ms after MouseUp**. GetDoubleClickTime was 500 |
| Click an unselected label, then click it again | the first selects it, with no edit. The second edits it |
| **Double-click the selected label** | MouseDown, NodeClick, MouseUp, DblClick, MouseUp, and **no edit** |
| **Click the selected label, LabelEdit 1, manual** | MouseDown, NodeClick, MouseUp, and **no BeforeLabelEdit and no edit**, waited for 1.8 s. A key typed then raised the tree's KeyDown |
| LabelEdit 1: click an unselected label, then click it again | the first selects it. The second raises NodeClick again, and no edit |
| LabelEdit 1: double-click the selected label | as with LabelEdit 0: DblClick, and no edit |
| **F2** | **nothing but KeyDown 113**, with LabelEdit 0 or 1 |
| StartLabelEdit, LabelEdit 0 or 1 | BeforeLabelEdit before StartLabelEdit returns, then the edit box |
| StartLabelEdit with no node selected | nothing, and no error |
| `Cancel = True` in BeforeLabelEdit | no edit box. A key typed then raised the tree's KeyDown |

**In BeforeLabelEdit:**
- Cancel is an Integer, 0.
- **The edit box already exists, holding the label's text,** at 0,0 and 0 × 0. It is placed after the event returns.

**The edit box:**

| When | Edit box, in the tree's client coordinates | The label |
|---|---|---|
| opened on `Node 2` | **-2,15, 53 × 17**, with the text all selected | 0,16, 39 × 16 |
| after typing `abc` | **-2,13, 36 × 21** | unchanged |
| after typing 30 characters | -2,13, **177 × 21**, past the label's width | unchanged |

- **The box starts 2 pixels left of the label and 1 above it,** wider than the text, and is drawn with a single dark
  line.
- **After the first key it is 4 pixels taller and 2 pixels higher,** and from then on it follows the text's width.
  That is measured, not explained.

**What Text reads during the edit: the old text.** The selected node's Text read `Node 2` while the box held `xyz`,
inside AfterLabelEdit too. It changed only after AfterLabelEdit returned.

**What ends an edit:**

| Action | AfterLabelEdit | Then |
|---|---|---|
| Enter, the text changed | yes, NewString the box's text, a String | the node takes NewString |
| Esc | **no** | the old text |
| **Click another node's label** | **yes, before that click's MouseDown** | the edit is kept, and the click goes on to select the node it hit |
| **Click Text1** | **yes**, before Text1's GotFocus | the edit is kept |
| **Tab** | yes | the edit is kept, and **the focus moves to the next control in tab order**, ListView1 |
| Click Text1, the text unchanged | **no** | the focus moves |
| `NewString = "Changed"` in AfterLabelEdit | | **the node takes `Changed`**, not what was typed |
| `Cancel = True` in AfterLabelEdit | | the old text |

- **Leaving the edit keeps what was typed,** by any route but Esc.
- **NewString is the text the node will take.** A handler can change it.

### Scrolling (2026-10-27, #2359)

**The probe:**
- **The tree:** TreeView1, 2400 × 1200 twips (160 × 80 pixels), Style 7, Appearance 1, no ImageList, in a shown form.
- **The nodes:** roots `Node 01` to `Node 30`, keyed `k01` to `k30`, unless a case says otherwise.
- **The input:** a driver on the guest's console pressed real keys with SendKeys and keybd_event, and turned the wheel
  with mouse_event over the tree's centre.
- **What was logged:** after each action, a Timer logged:
  - the selected node;
  - the nodes in the top and bottom rows, by HitTest;
  - `GetScrollPos` on each bar, and whether the window had WS_VSCROLL and WS_HSCROLL;
  - the client size and GetVisibleCount.
- **The window:** `TreeView1.hWnd` is the tree itself, of class `TreeView20WndClass`, with no SysTreeView32 child.
- **The builds:** native and p-code logged the same lines.

**The sizes:**
- **Every row is 16 pixels high,** from `TVM_GETITEMHEIGHT`.
- **The client area is 156 × 76** inside the 160 × 80 tree.
- **GetVisibleCount is 4: the rows that fit whole.** 76 / 16 is 4.75, and the fifth row is drawn cut off at the bottom.
- **Each bar is 17 pixels,** SM_CXVSCROLL and SM_CYHSCROLL at 96 dpi:
  - the vertical bar took the client width from 156 to 139;
  - the horizontal bar took the height from 76 to 59, and GetVisibleCount from 4 to 3.

**When the bars appear,** adding nodes one at a time with Scroll True:

| Nodes | Vertical bar | Horizontal bar | GetVisibleCount |
|---|---|---|---|
| 1 to 4 | no | no | 4 |
| **5**, the fifth only partly in view | **yes** | no | 4 |
| 7, then a node of 30 W's added | yes | **yes**, the node being wider than the client | 3 |
| the long node removed | yes | no | 4 |
| down to 4 nodes again | **no** | no | 4 |
| 4 nodes and the long one | yes: the horizontal bar left room for only 3 | yes | 3 |
| a long node with 3 nodes in all | no | yes | 3 |

- **The vertical bar appears as soon as the rows exceed GetVisibleCount,** a row partly in view counting as out of it.
- **The bars come and go as nodes are added, removed, expanded and collapsed.** Expanding a root with 8 children,
  from code or the keyboard, added the vertical bar, and collapsing it took the bar away.

**The keyboard, with Scroll True:**

| From | Key | Selected | Top row |
|---|---|---|---|
| k01 selected, at the top | Down, three times | k02, k03, k04 | k01: no scroll |
| k04, the last whole row | **Down** | k05 | **k02: one row, so k05 is the last whole row** |
| | Down, twice more | k06, k07 | k03, k04 |
| k07, the last whole row | **PageDown** | **k10: 3 on**, GetVisibleCount less 1 | **k07: scrolled 3**, k10 the last whole row |
| | PageDown, twice more | k13, k16 | k10, k13 |
| | **End** | k30 | **k27**: the end of the list in the last whole row |
| k30, the last node | Down | nothing | nothing |
| k30 | **PageUp** | **k27**, the top row | k27: no scroll |
| k27, the top row | PageUp | k24 | k24: scrolled 3 |
| k24, the top row | Up | k23 | k23: one row |
| | Home | k01 | k01 |
| k01, the first node | Up | nothing | nothing |
| **k01, at the top** | **PageDown** | **k04, the last whole row** | k01: **no scroll** |

- **Down and Up scroll one row** when the selection leaves the whole rows.
- **PageDown moves to the last whole row** if the selection isn't there already. If it is, PageDown moves on by
  GetVisibleCount less 1 and scrolls so the selection is the last whole row again. **PageUp mirrors it** with the top
  row.
- **The scroll position is the index of the top row.** The most it reached with 30 nodes was 26, 30 less GetVisibleCount.
- **Expanding with the keyboard doesn't scroll:**
  - Right on a collapsed root, numpad + and numpad * expanded it, with the root still the top row;
  - numpad - and Left collapsed it;
  - **Right on a node with no children did nothing.**
- After numpad * opened Root 1's 8 children, End selected Root 2 and scrolled to position 6, Root 2 the last whole
  row.

**Selecting from code scrolls too:**
- `Set SelectedItem = k25`, with k10 at the top, scrolled so k25 was the last whole row, k22 at the top.
- `k02.Selected = True` then scrolled up so k02 was the top row.

**EnsureVisible**, called in this order, each call starting where the one before left the tree:

| Call | Returned | Top row after |
|---|---|---|
| k20 | **True** | **k17: k20 the last whole row** |
| k05 | True | **k05: k05 the top row** |
| k30 | True | k27 |
| k01 | True | k01 |
| k12 | True | k09 |
| k13 | True | k10: one row |
| **k10, already the top row** | **False** | k10: no scroll |
| c8, under a collapsed Root 1 | | Root 1 expanded, and position 5: c8 the last whole row |

- **EnsureVisible scrolls as little as it can.** A node below the view goes to the last whole row, and one above it to
  the top row.
- **It returns True when it scrolled,** and False when the node was already in view.

**The wheel:**
- **One notch scrolls 3 rows,** the system's setting. Two notches down gave top rows k04 and k07, one up gave k04
  again, and three more down gave k13.
- **The selection doesn't move.** k01 stayed selected throughout, scrolled out of view.
- **The wheel stops at the ends:** 20 notches down gave position 26, and 20 up gave 0.
- **With only the horizontal bar, the wheel scrolls sideways:** one notch down moved the horizontal position from 0 to
  15.

**Horizontally:**
- **Selecting the long node, with Down, didn't scroll sideways.** The horizontal position stayed 0, its text cut off at
  the right.
- Right and Left on that node did nothing.

**Scroll False:**
- **No bar is ever shown,** even with 30 nodes or the long one. The client stayed 156 × 76, and GetVisibleCount 4.
- **The keyboard still moves the selection, out of sight.** Down six times selected k07, PageDown k10 and End k30, with
  k01 still the top row and the scroll position 0. Home came back to k01.
- **EnsureVisible returns False for every node and scrolls nothing.** Setting SelectedItem or Selected scrolls nothing.
- **The wheel does nothing.**

### GetVisibleCount, the edge, and Form_Load (2026-10-27)

**The probe:**
- **The trees:** eight TreeViews from hand-written blocks, 2400 twips wide and 1200 or 3000 high. Each height came with
  the four pairs of Appearance and BorderStyle.
- **The reads:** GetVisibleCount, and the window and client sizes from GetWindowRect and GetClientRect. They were read in
  Form_Load, before the form was shown, then after Show, then once 30 nodes were added.
- **The builds:** native and p-code logged the same lines.

| Appearance, BorderStyle | Edge, each side | 160 × 80 tree: client, GetVisibleCount | 160 × 200 tree: client, GetVisibleCount |
|---|---|---|---|
| **1, 0: the defaults** | **2 px**, the sunken 3D edge | 156 × 76, **4** | 156 × 196, **12** |
| 1, 1 | **3 px**: the 3D edge and a line inside it | 154 × 74, 4 | 154 × 194, 12 |
| **0, 0: flat, no border** | **none** | 160 × 80, **5** | 160 × 200, 12 |
| 0, 1: flat, single line | 1 px | 158 × 78, 4 | 158 × 198, 12 |

- **GetVisibleCount is the client height divided by the 16-pixel row, rounded down.** 76 / 16 is 4.75, so 4. 80 / 16
  is 5. 200 / 16 is 12.5, so 12.
- **Form_Load reads the same as the shown form.** Every value, sizes included, was the same before Show as after it.
- **It doesn't depend on the nodes.** With no nodes and with 30 it was the same. The vertical bar took 17 pixels off
  the client width and nothing off the height.
- **It's a Long** in every case.

### Drawing

**The Styles,** with LineStyle 0, Root 1 and Alpha expanded, Bravo selected, and an Image on every node:

| Style | Drawn |
|---|---|
| 0 | text only, indented, with no lines, buttons or pictures |
| 1 | pictures and text |
| 2 | buttons and text. **With LineStyle 0 a root has no button,** and Alpha did |
| 3 | buttons, pictures and text |
| 4 | lines and text. With LineStyle 0 there are no lines at the root level |
| 5 | lines, pictures and text |
| 6 | lines, buttons and text |
| 7 | lines, buttons, pictures and text |

- **LineStyle 1** gives the roots lines and buttons too.
- **Indentation 300** set the step between levels to 20 pixels. It read back as 299.9055.

**Selection and node styles:**
- **Selection:** a selected node's label is highlighted, with a dotted focus rectangle while the control has the focus.
  **FullRowSelect highlights the whole row,** across to the right edge, with lines (Style 7) and without (Style 3).
- **Checkboxes:** every node drew a box, with only the node set Checked afterwards ticked.
- **A node's styles:**
  - **Bold** makes the label bold.
  - **ForeColor** colours the label.
  - **BackColor** fills the label's background only.
- **Losing the focus:** with HideSelection True, the selection is not drawn at all. With HideSelection False, the
  selected label is drawn on button-face grey.
- **Enabled False** left every label as it was, apart from the selected node's, which was drawn grey.
- **Borders:**
  - BorderStyle 1 with Appearance 1 adds a line inside the sunken edge.
  - Appearance 0 with BorderStyle 1 draws a single flat line.
  - Appearance 0 with BorderStyle 0 draws no border at all.

### The saved files

**The programs.**
- **The save:** a form of three TreeViews, written by hand, opened in VB6's IDE, and saved there once the form had been
  dirtied with a comment.
  - The first had every property off its default, under its type-library name, in scrambled order.
  - The second had only its size and `_Version`.
  - The third had its defaults written out.
- **The run-time reads:** EXEs read every property back, natively and as p-code, from the form as written and from
  VB6's save of it. The two reads agreed.
- **The corpus:** VB6's save is in `corpus/inbox-controls/pending/treeview/`.

**Double-clicked onto a form,** VB6 writes this block (corpus `defaults/`):

```
   Begin MSComctlLib.TreeView TreeView1 
      Height          =   750
      Left            =   2250
      TabIndex        =   4
      Top             =   1875
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393217
      Style           =   7
      Appearance      =   1
   End
```

**The order VB6 writes the lines in,** whatever order they were read in: the extender's lines; `_ExtentX` and
`_ExtentY`; then

| Lines |
|---|
| **`_Version`, first** |
| HideSelection, Indentation, LabelEdit, LineStyle, PathSeparator, Sorted, Style |
| Checkboxes, FullRowSelect, HotTracking, Scroll, SingleSel |
| ImageList, as a string: `ImageList = "ImageList1"` |
| BorderStyle, Appearance, MousePointer, Enabled |
| the Font block |
| OLEDragMode, OLEDropMode |

- **Every property is saved under its type-library name.** Lines naming each one were kept.
- **OLEDragMode and OLEDropMode are written once,** after the Font block. The ListView writes them twice.
- **`_Version` comes before every other property,** where the ListView writes it in the middle.

**Indentation is HIMETRIC in the file:**
- A hand-written `Indentation = 529` read back as 299.9055 twips, and VB6 saved it as 529.
- A hand-written 1000, the default, was dropped.

**Which lines are left out:**
- **Appearance was written in every save,** at 1 too.
- **Style 7 is written.** Everything else is written only when off its default. In the third TreeView, LineStyle 0,
  LabelEdit 0, Indentation 1000, PathSeparator `\`, HideSelection True, Scroll True and BorderStyle 0 were all dropped.

**A block missing a line** loads as follows, read at run time from the second TreeView:

| Property | Loaded as |
|---|---|
| **Style** | **0**, text only, **though a new control starts at 7** |
| Indentation | 566.9291 twips, 1000 HIMETRIC |
| PathSeparator | `\` |
| HideSelection, Scroll, Enabled | True |
| BorderStyle | 0, as a new control has |
| Appearance | 1 |
| everything else | its default, as listed under Defaults |

Saved again, that block came back with `Appearance = 1` and nothing else.

**In the IDE:**
- **At design time, the control draws four nodes, each called `Sample Node`:** a root over two children, then a second
  root.
- They are drawn in the control's Style, LineStyle, Checkboxes and Font, with the bound ImageList's first picture.
- No nodes are saved. The property pages have no page for them.

### In HexIDE

A form holding a TreeView is refused at load, as any in-box control HexIDE doesn't model is (#1086).
