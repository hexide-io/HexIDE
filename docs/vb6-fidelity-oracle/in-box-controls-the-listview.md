## In-box controls: the ListView (2026-10-26, #1085)

**The probes.** VB6 6.00.8176 SP6, with MSCOMCTL.OCX 6.01.9782, the library and `Object=` line of [In-box controls: the
ProgressBar](in-box-controls-the-progressbar.md).
- **The object-model program:** a Sub Main showing a 6000×4500-twip form holding a 5000×3000-twip ListView with VB6's
  double-clicked property lines, a PictureBox, and the three-picture ImageList saved in [In-box controls: the
  ImageList](in-box-controls-the-imagelist.md). It drives the ListView late-bound under `On Error Resume Next`, logging
  each value with its `TypeName`, or the error.
- **The events program:** the same form with a TextBox, KeyPreview on, and a report-view ListView with MultiSelect and
  Checkboxes, three columns and five items with small icons. A driver on the console clicked with real mouse input and
  typed with real keystrokes, and marked each action in the log. Each event handler logged itself, with the selection.
- **The drawing program:** the same form, setting up states in turn and asking the driver for a screenshot of each. It
  logged each item's Left, Top, Width and Height in pixels. The guest runs at 96 dpi, and a VB6 EXE has no manifest, so
  the control draws without visual styles.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. The two logged the same lines
  throughout.
- **Session 0 can't be used for this control.** Run there, adding a column header raised automation error
  -2147417848, "The object invoked has disconnected from its clients", and returned a stale object, though the column
  was added. On the console the same program ran clean. Every row below is from the console.

### The object model

- **Its members,** from the type library:
  - View, Arrange, LabelEdit, LabelWrap, HideSelection, HideColumnHeaders, MultiSelect, FullRowSelect, GridLines,
    Checkboxes;
  - Sorted, SortKey, SortOrder, AllowColumnReorder, FlatScrollBar, HotTracking, HoverSelection, TextBackground;
  - Picture, PictureAlignment, BorderStyle, Appearance, BackColor, ForeColor, Font, Enabled, MousePointer, MouseIcon,
    OLEDragMode, OLEDropMode, hWnd;
  - ListItems, ColumnHeaders, Icons, SmallIcons, ColumnHeaderIcons, SelectedItem, DropHighlight;
  - the methods FindItem, GetFirstVisible, HitTest, StartLabelEdit, Refresh and OLEDrag.
- **Its events:** ItemClick, ItemCheck, ColumnClick, BeforeLabelEdit, AfterLabelEdit, Click, DblClick, the key and
  mouse events, and the OLE drag events.
- **ListItems** has Count, `Add([Index], [Key], [Text], [Icon], [SmallIcon])`, Item, Remove and Clear.
- **A ListItem** has Text, Key, Tag, Index, Icon, SmallIcon, Selected, Checked, Ghosted, Bold, ForeColor, ToolTipText,
  Left, Top, Width, Height, `SubItems(Index)`, ListSubItems, EnsureVisible and CreateDragImage.
- **ListSubItems** has `Add([Index], [Key], [Text], [ReportIcon], [ToolTipText])` and the rest.
- **A ListSubItem** has Text, Key, Tag, Index, ReportIcon, ForeColor, Bold and ToolTipText.
- **ColumnHeaders** has `Add([Index], [Key], [Text], [Width], [Alignment], [Icon])` and the rest.
- **A ColumnHeader** has Text, Key, Tag, Index, Width, Alignment, Position, SubItemIndex, Icon and Left.
- **`TypeName`:** the collections are `IListItems` and `IColumnHeaders`, and a member is `IListItem`, `IListSubItem` or
  `IColumnHeader`.
- **Each object's default member is its Text.** `v = ListItems(1)` reads the Text, as `v = ColumnHeaders(1)` and
  `v = ListSubItems(1)` do.
- **The counts and indexes are Longs:** `ListItems.Count`, `ColumnHeaders.Count`, and a ListItem's, ListSubItem's or
  ColumnHeader's Index. A ColumnHeader's Position and SubItemIndex are Integers, and so is SortKey.

### Defaults

| Read | Logged |
|---|---|
| View, Arrange, LabelEdit, SortOrder, TextBackground, PictureAlignment | `0` each, Longs: icons, no arrangement, automatic, ascending |
| SortKey | `0`, an Integer |
| LabelWrap, HideSelection, Enabled | `True` |
| HideColumnHeaders, MultiSelect, FullRowSelect, GridLines, Checkboxes, Sorted | `False` |
| AllowColumnReorder, FlatScrollBar, HotTracking, HoverSelection | `False` |
| BorderStyle, Appearance | `1`, `1` |
| BackColor, ForeColor | `&H80000005`, `&H80000008` |
| OLEDragMode, OLEDropMode, MousePointer | `0` |
| Font | MS Sans Serif 8.25 |
| Icons, SmallIcons, ColumnHeaderIcons, Picture | `Nothing` |
| SelectedItem, DropHighlight, GetFirstVisible | `Nothing` |
| ListItems.Count, ColumnHeaders.Count | `0` |
| TabStop | `True` |
| Index | 343 |

**What each property takes:**
- View is 0 to 3, Arrange 0 to 2, LabelEdit 0 and 1, SortOrder 0 and 1, TextBackground 0 and 1, and PictureAlignment
  0 to 5. One past the last of each is **380**, and the property is left as it was.
- **SortKey must name a column that exists:** with no columns only 0 is taken, and with three, 3 is 380. So are -1 and
  32767.

### ColumnHeaders

- **`ColumnHeaders.Add()` gives:**
  - Index 1, an empty Key, Text and Tag;
  - **Width 1440**, Alignment 0, Position 1, SubItemIndex 0, Left 0;
  - Icon 0, an Integer, where an item's unset Icon is Empty.
- **Widths and positions:**
  - Columns added as Name 2000, Size with no width, and Kind 1200 read back Widths 2000.126, 1440 and 1200.189. Their
    Lefts were 0, 2000.126 and 3440.126.
  - Each later column's SubItemIndex is one more than the last: 0, 1, 2.

| Call | Logged |
|---|---|
| `Add(, "name")`, a key already used | **35602** |
| `Add(, "5")` | **35603** |
| `Add(99)` | **35600** |
| `Add(, , , , 3)`, an Alignment past 2 | **380** |
| `Add(, , , , , 1)`, an Icon with no ColumnHeaderIcons | **35613**, "ImageList must be initialized before it can be used" |
| the first column's `Alignment = 1` | **35604**, "The first column in a ListView control must be left aligned" |
| `Width = -5` | 380. `Width = 0` is taken |
| `Index =`, `Left =`, `ColumnHeaders.Count =` | 383 |
| `SubItemIndex =` | **382**, "Property cannot be set at runtime" |
| `Position = 1` for the third column | taken. The Positions became 2, 3 and 1, while Index, SubItemIndex and the Lefts read back unchanged |
| `Position = 0`, `= 4` with three columns | 380 |

- **A column added at Index 1 becomes the items' own text column.** `Add(1, "front")` came in at Index 1, SubItemIndex
  0 and Position 1, and Name moved to Index 2, SubItemIndex 1 and Position 2. Removing Front gave every column its
  old SubItemIndex back.
- **A removed column can still be read:** its Text was "Front". A removed ListItem raises 35605 instead.

### ListItems

**A new item,** `Add(, "a", "Alpha")` in report view:

| Property | Value |
|---|---|
| Index | 1, a Long |
| Key, Text | as given |
| Tag, ToolTipText | empty |
| Icon, SmallIcon | `Empty` |
| Selected | **True for the first item added**, and False for the rest |
| Checked | **True**, see below |
| Ghosted, Bold | False |
| ForeColor | `&H80000008` |
| Left, Top, Width, Height | 30.04725, 255.1181, 524.9764 and 209.7638 |
| ListSubItems.Count | 0, with `SubItems(1)` reading `""` while a second column exists |

| Call | Logged |
|---|---|
| `Add(, "a")`, a key already used | **35602** |
| `Add(, "5")` | **35603** |
| `Add(, "")` | no error |
| `Add(1, "first")` | inserted at 1 |
| `Add(99)`, `Add(0)` | **35600** |
| `Add(, , , 1)`, `Add(, , , , 1)`, an Icon or SmallIcon with no ImageList | **35613** |
| `Add(, , 42)` | Text `42`, a String |
| `ListItems("missing")`, `("5")` | 35601 |
| `ListItems("")` | 35603 |
| `ListItems(0)`, `(99)` | 35600 |
| `ListItems(1.5)`, `(2.5)` | both item 2 |

**An item's properties:**

| Assignment | Logged |
|---|---|
| `Index =`, `Width =` | **383** |
| `Left = 5` | no error, and no effect: Left still read 30.04725 |
| `Key =` another's key | 35602. `Key = ""` is taken |
| `Text = 42` | `42`, a String |
| `Checked`, `Ghosted`, `Bold = True`, `ForeColor = vbRed` | taken, each |
| `Set Tag = ListView1` | **35700** |
| `EnsureVisible` | returns `True`, a Boolean |
| `CreateDragImage` | returns a `Picture` |

**Checked is True for an item with no unchecked box:**
- Three items added while Checkboxes was False all read Checked True.
- After `Checked = False` on the second, they read True, False, True.
- Turned on, Checkboxes drew **no box at all on the first and third items** and an empty box on the second. Their
  Checked values were unchanged.
- An item added while Checkboxes was True read False and drew an empty box.
- Turning Checkboxes off and on again changed no item's Checked.

### SubItems and ListSubItems

- **`SubItems(n)` takes n from 1 to one less than the number of columns.** `SubItems(0)`, and an n past the columns,
  raise **380** on reading and on assigning. With no columns, `SubItems(1) =` is 380.
- **Assigning `SubItems(2)` of an item with no sub-items creates two:** ListSubItems.Count became 2, and `SubItems(1)`
  read `""`.
- `SubItems(1) = 42` reads `42`, a String.
- **A new ListSubItem:** Index 1, an empty Key and Tag, ReportIcon `Empty`, Bold False, and an empty ToolTipText.
- **ListSubItems can outnumber the columns.** `ListSubItems.Add(, "k3", "Three")` on an item holding two made a third
  with two columns after the first. `SubItems(3)` still raised 380.
- `ListSubItems.Remove 1` moved the rest down: SubItems(1) and (2) became the old second and third.
- A duplicate key is 35602, `Add(99)` is 35600, and a ReportIcon with no SmallIcons is 35613.

### Icons

| Call | Logged |
|---|---|
| `Set Icons = ImageList1`, and so for SmallIcons and ColumnHeaderIcons | no error |
| `Icons = ImageList1`, a Let | no error: bound |
| `Set Icons = Picture1` | **35610**, "Invalid object" |
| `Icon = 1`, `Icon = "blue"` | read back as given, `1` an Integer and `blue` a String |
| `Icon = 99` | 35600, and Icon is left as it was |
| `SmallIcon = "red"`, a sub-item's `ReportIcon = 2`, a column's `Icon = "ico"` | taken |
| `ImageList1.ListImages.Remove 3` while bound | **35617** |
| `Set Icons = Nothing` | every item's **Icon becomes 0**. SmallIcon is kept, `red` |

### Selection

| Step | Selected items, and SelectedItem |
|---|---|
| five items added | 1, and 1 |
| `ListItems(3).Selected = True` | 3, and 3: **one is unselected when another is selected** |
| `ListItems(4).Selected = True` | 4, and 4 |
| `ListItems(4).Selected = False` | none, **and SelectedItem still 4** |
| `Set SelectedItem = ListItems(2)` | 2, and 2 |
| `Set SelectedItem = Nothing` | **still 2**, and `Nothing` |
| `MultiSelect = True`, then 1 and 5 Selected | 1, 2 and 5, and 5 |
| `MultiSelect = False` | **still 1, 2 and 5**, and 5 |
| `ListItems.Remove 2` | 1 and 4, which were 1 and 5, and 4 |

### Removing items

- **A held ListItem's Index follows the shift.** Item k4, held in a variable, read Index 3 once item 2 was removed.
- **A removed item raises 35605,** "This item's control has been deleted", on any read.
- `Remove 99` is 35600, and `Remove "missing"` is 35601.
- `ListItems.Count =` is 383. `Set ListItems = Nothing` is **438**.
- `Clear` leaves the columns.

### Sorting

**The comparison,** with `Sorted = True`, ascending, on 26 items added in this order: `b B a10 a2 10 9 "" Zeta alpha _x
-y " space" a A ab AB ~t 1e3 co-op coop it's its eé ez Ole OLE`.

```
"" " space" _x ~t 10 1e3 9 a A a10 a2 ab AB alpha b B coop co-op eé ez its it's Ole OLE -y Zeta
```

- **Case is ignored.** Items that differ only by case kept the order they were added in, in both directions.
- **A hyphen or an apostrophe counts for nothing, except to break a tie.**
  - `-y` sorted among the y words.
  - `coop` came before `co-op` and `its` before `it's`, and descending reversed both pairs. So they aren't equal: the
    punctuation is the tiebreak.
- **Digits are compared as text:** `10`, `1e3`, `9`.
- **Spaces and symbols come before digits,** and the empty text first.
- **An accented letter sorts by its base letter:** `eé` came before `ez`.
- **SortOrder 1 is descending,** and SortKey n sorts by `SubItems(n)` as text, `"1"`, `"10"`, `"11"`, … `"2"`.

**When the list sorts:**
- **Setting Sorted to True sorts at once.**
- **An item added while Sorted is True goes at the end.** The list was sorted again at the next DoEvents: "bravo",
  added to alpha, charlie, delta, moved to second.
- **A changed Text never re-sorts,** not even at a Refresh.
- **A changed SortOrder sorted at once in one run, and only after a Refresh in another.** The second run had just added
  26 items and set Sorted to True, with nothing in between. The first had a DoEvents first. A changed SortKey with no
  Refresh sorted nothing until Sorted was set to True again. Measured, but not narrowed to a rule.
- `Sorted = False` leaves the order as it is.

### FindItem

Six items: Apple, apricot, Banana, app, Cherry, apple. Each had a SubItem `s` plus its text and a Tag `t` plus its
text.

| Call | Found |
|---|---|
| `FindItem("apple")`, `("APPLE")` | 1, Apple: **a whole match, ignoring case** |
| `FindItem("ap")`, `("Ban")`, `("")` | `Nothing` |
| `FindItem("ap", 0, , 1)`, partial | 1, Apple |
| `FindItem("ban", 0, , 1)` | 3, Banana: **a partial match is a prefix, ignoring case** |
| `FindItem("", 0, , 1)` | 1 |
| `FindItem("zzz", 0, , 1)` | `Nothing`, and no error |
| `FindItem("ap", 0, 3, 1)` | 4, app: the search starts at Index |
| `FindItem("app", 0, 5, 1)` | 6, apple |
| `FindItem("Cherry", 0, 6)` | `Nothing`: **no wrap past the end** |
| `FindItem("sBanana", 1)`, a sub-item | 3 |
| `FindItem("sban", 1, , 1)`, a sub-item, partial | `Nothing` |
| `FindItem("tCherry", 2)`, a Tag | 5 |
| a Where of 3, an Index of 99, an fPartial of 2 | **380** each |

### HitTest and GetFirstVisible

- **HitTest takes twips in the ListView.** Item 3's Left and Top plus 15 found item 3.
- `HitTest(-1, -1)` and `(100000, 100000)` returned `Nothing`.
- `GetFirstVisible` returned item 1, and `Nothing` with no items.

### Events

**The mouse,** on an item's label, with FullRowSelect False and LabelEdit automatic:

| Action | Events, in order |
|---|---|
| click an unselected item | MouseDown, **ItemClick**, MouseUp, Click |
| click the selected item again | MouseDown, ItemClick, MouseUp, Click, and a moment later BeforeLabelEdit |
| double-click an item | MouseDown, ItemClick, MouseUp, Click, DblClick, MouseUp: one ItemClick, and no second MouseDown |
| Ctrl+click item 4, with 3 selected | MouseDown with Shift 2, ItemClick 4, MouseUp, Click. Both stay selected |
| Shift+click item 5, after Ctrl+clicking 4 with 3 selected | MouseDown with Shift 1, **ItemClick 4, ItemClick 5, ItemClick 5**, MouseUp, Click. The selection became 4 and 5, the range from the last item clicked, and 3 was dropped |
| right-click item 2 | MouseDown with Button 2, ItemClick 2, MouseUp with Button 2, **Click**. It selects item 2 |
| click the empty area below the items, with 4 and 5 selected | MouseDown, MouseUp, Click, and no ItemClick. Item 5, which had the focus, stayed selected, and 4 did not |
| click 7 pixels right of the label, inside the first column | MouseDown, MouseUp, Click. Nothing was selected. Native run only |
| click an item's checkbox | MouseDown, **ItemCheck** with the new Checked, MouseUp, Click. No ItemClick, and the selection is unchanged |
| click a column header | **ColumnClick and nothing else**: no MouseDown, MouseUp or Click |
| double-click a column header | one ColumnClick |

- **MouseDown and MouseUp give X and Y in twips** from the control's top-left. A click 50 pixels across and 42 down gave
  750 and 630.
- **ItemClick is raised for an item already selected,** on every click on it.
- **SelectedItem can be Nothing inside ItemClick.** It read Nothing in the Ctrl+click's ItemClick and in the
  Shift+click's last one, and named the clicked item by Click.

**The keyboard,** as the ListView's KeyDown and KeyUp logged it:

| Key | Events |
|---|---|
| Space | KeyDown, ItemCheck on the focused item, ItemClick on it, KeyUp |
| Down | KeyDown, ItemClick on the next item, KeyUp |
| Ctrl+Down | KeyDown, KeyUp. **The focus moved and the selection didn't** |
| Home | KeyDown, ItemClick 1, KeyUp |

**SelectedItem is the item with the focus, selected or not.** After Ctrl+Down, item 4 was the only item selected, and
SelectedItem was item 5. This is what the selection rows above showed: `Selected = False` left SelectedItem in place,
and `Set SelectedItem = Nothing` left the item selected. Removing the selected item in code left nothing selected, and
SelectedItem was the item that moved up into its Index.

**Editing a label:**
- **StartLabelEdit raises BeforeLabelEdit before it returns,** with LabelEdit automatic or manual. With no
  SelectedItem it raised nothing, and no error.
- With LabelEdit manual, clicking the selected item again started no edit.
- While the edit box is open, typed keys go to it and not to the ListView's KeyDown. Enter's KeyUp reaches the ListView
  once the edit has ended.
- **Enter after typing raises AfterLabelEdit** with Cancel 0 and the typed text in NewString, a String. **Inside the
  event, the item's Text is still the old one.**
- **Escape raises no AfterLabelEdit, and nor does Enter with the text unchanged.**
- Enter with the text deleted raised AfterLabelEdit with NewString `""`, and the item was left with no text.
- **`Cancel = True` in BeforeLabelEdit opens no edit box,** and the keys typed then went to the ListView.
  `Cancel = True` in AfterLabelEdit kept the old text.

**Code raises none of these, except through StartLabelEdit.** No ItemClick, ItemCheck or Click was raised by:
- setting Selected or SelectedItem;
- setting Checked to True or False, or setting Text;
- `ListItems.Add` or `Remove`, or setting Sorted;
- a column's Width, or EnsureVisible.

### Drawing

**Report view:**
- **Row height and placement:**
  - With 16-pixel SmallIcons, rows are 17 pixels high. The first is at Top 17, under a header 17 pixels high, and at
    Left 2.
  - With no SmallIcons, rows were 14 pixels high.
  - With Checkboxes, Left is 18.
  - With HideColumnHeaders, the first row is at Top 0.
- **An item's Left and Width cover its icon and its label,** clipped to the first column. With FullRowSelect, Width is
  the whole row: 253 pixels, the three columns' widths together.
- **Text and alignment:**
  - Text too long for the first column is cut short with `...`.
  - Alignment 1 right-aligns a column's header and its cells, and 2 centres them.
  - A column's Icon is drawn at the left of its header, and the header text keeps its alignment.
- **Selection:**
  - **A selected item is highlighted on its label only,** and its icon is drawn blended with the highlight colour. The
    item with the focus has a dotted focus rectangle.
  - FullRowSelect highlights the whole row.
  - GridLines draws lines between the rows and the columns, carried on through the empty area below the last item.
- **An item's styles:**
  - Ghosted dithers the item's icon.
  - Bold makes the label bold.
  - **An item's ForeColor colours its label only.** A sub-item's ForeColor and Bold apply to its own cell.
  - A ReportIcon is drawn at the left edge of its cell, in a right-aligned or centred column too.
- **Losing the focus:** with HideSelection True, the selection is not drawn at all. With HideSelection False, the
  selected label is drawn on button-face grey.
- **Enabled False** does not grey the text, and the selected label stays highlighted. Each item's row, across the
  columns, is painted button-face grey, and the area below stays white.
- **Colours and borders:**
  - The control's ForeColor colours every label and cell that has no ForeColor of its own.
  - Appearance 0 with BorderStyle 0 draws the control flat, with no border. The header stays raised.

**Icon view, View 0,** with the 16-pixel ImageList as Icons:
- **The grid:** items sit on a grid 59 pixels across and 59 down, five to a row in the 333-pixel control, starting at
  Left 21 and Top 2.
- **An item's size:** each was 32 to 40 pixels wide and 37 high.
- **A long label:** the five-word caption was wrapped to two lines and cut short with `...`, making it 53 by 50.
  - With LabelWrap False it was one line, 133 pixels wide, drawn over its neighbour's label.
- **Arrange:**
  - **Arrange 1** laid the items out in columns down the left, and **Arrange 2** in rows across the top.
  - Both took the items in the order of where they already stood, not by Index. Arrange 1 read the old layout down each
    column, and Arrange 2 read it along each row.

**Small-icon view, View 1:**
- Items were 157 pixels apart: the widest item's 149 pixels, plus 8.
- They stayed five to a row, as in icon view, so the row ran past the right edge and a horizontal scroll bar appeared.

**List view, View 2:** one column of 17-pixel rows, each 157 pixels wide.

### In HexIDE

A form holding a ListView is refused at load, as any in-box control HexIDE doesn't model is (#1085).
