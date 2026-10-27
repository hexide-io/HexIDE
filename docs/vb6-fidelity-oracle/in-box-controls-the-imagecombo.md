## In-box controls: the ImageCombo (2026-10-27, #1092)

**The probes:**
- **The software:** VB6 SP6 (6.00.9782) with MSCOMCTL.OCX 6.01.9782, on Windows 10 at 96 dpi, in the en-GB user locale.
  - The form line is `Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"`.
  - The block is `Begin MSComctlLib.ImageCombo`, `_Version` 393216.
- **The form:**
  - Text1, then ImageCombo1 at 1500, 120, 2400 × 330, a hand-written block with only its position, extents and
    `_Version`;
  - an ImageList;
  - an AutoRedraw PictureBox, on which the program drew three 16 × 16 pictures, red, green and blue. It added them to
    the ImageList keyed by colour.
- **The programs:** each ran in a Sub Main and logged every value with its type.
  - Every error was read straight after its statement.
  - Each scenario ran on a fresh instance of the form, loaded but not shown.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.

### The object model

From MSCOMCTL.OCX's type library:

| Interface | Members |
|---|---|
| `IImageCombo` | Text, SelectedItem, ComboItems, ImageList, Indentation, Locked, SelStart, SelLength and SelText; BackColor, ForeColor, Enabled, Font, MouseIcon, MousePointer, OLEDragMode, OLEDropMode and hWnd; GetFirstVisible, Refresh and OLEDrag. Hidden: Style, UsePathSep and AboutBox |
| `IComboItems` | Count, Item (with a hidden `_CollectionDefault`), `Add([Index], [Key], [Text], [Image], [SelImage], [Indentation])`, Remove, Clear |
| `IComboItem` | Index, Key, Text, Tag, Image, SelImage, Indentation and Selected, with a hidden `_ObjectDefault` |

**Its events are** Change, Dropdown, Click, the key events and the OLE events. There are no mouse events, and no
GotFocus beyond the extender's.

### Defaults, from a block with no properties

| Read | Logged |
|---|---|
| Text, SelText | empty |
| SelectedItem, ImageList | Nothing |
| ComboItems | an `IComboItems`, Count 0 |
| Indentation | 0 |
| Locked | False |
| SelStart, SelLength | 0, Longs |
| BackColor, ForeColor | **`&H8000000F` and `&H80000012`**, the button face and button text |
| Enabled | True |
| MousePointer, OLEDragMode, OLEDropMode | 0 |
| Font | MS Sans Serif |
| **`v = ImageCombo1`** | **438: the ImageCombo has no default member** |
| Style, UsePathSep, the hidden members | **438**, reading or writing |
| GetFirstVisible, with no items | Nothing, and no error |

**The colours differ from a new control's.** VB6's save of an ImageCombo double-clicked from the toolbox, in
`corpus/inbox-controls/pending/defaults`, writes `BackColor = -2147483643` and `ForeColor = -2147483640`. Those are the
window and window-text colours. A block without the lines reads the button colours.

### ComboItems

| Call | Result |
|---|---|
| `Add(, "a", "Alpha")` | item 1, returned as an `IComboItem` |
| `Add(, , "Bravo")` | no key |
| `Add(1, "c", "Charlie")` | inserted first, the others moving down |
| `Add(, "a", "dup")` | **35602**, "Key is not unique in collection" |
| `Add(9, …)`, past the end; `Add(0, …)` | **35600**, "Index out of bounds" |
| `Add(, "1", …)`, a numeric key | **35603**, "Invalid key" |
| `Add(, "", …)`, an empty key | taken, as no key |
| `Add(, "i", "img", 1)`, with no ImageList | **35613**, "ImageList must be initialized before it can be used" |
| `Add()`, with no arguments | taken: an empty item |
| `ComboItems(99)`, `ComboItems(0)`; `ComboItems("nope")` | 35600; **35601**, "Element not found" |
| `v = ComboItems(1)` | the item's Text: **Text is an item's default member** |
| `ComboItems.Count = 3` | 383, "Property is read-only" |

**An item's properties:**
- Text and Key can be set. A Key already in use raises 35602.
- **Tag keeps its type.** `Tag = 5` reads back 5 as an Integer, where a control's Tag is a String.
- Indentation takes 3, and -1 or 32767 raise 380.
- Index raises 383.
- **`Selected = True` selects the item and sets the combo's Text** to the item's.

**Remove and Clear:**
- `Remove 1` renumbers the rest, and **an item held in a variable sees its new Index**: 2 became 1.
- `Remove "c"` removes by key. `Remove 9` raises 35600.
- **After Clear, reading a held item's Text raises an error.** The line that read it failed whole, so the number wasn't
  logged.

### SelectedItem and Text

| Step | Result |
|---|---|
| `Set SelectedItem = ComboItems(2)` | selected, and **Text becomes the item's**, "Bravo" |
| `SelectedItem = ComboItems(3)`, without Set | **taken**: its put takes a Variant |
| `SelectedItem = 1` | **13**, "Type mismatch" |
| `Set SelectedItem = Nothing` | Nothing, and **Text stays "Charlie"** |
| `Text = "Bravo"`, an item's text | Text "Bravo", but **SelectedItem stays Nothing**: setting Text never selects |
| `Text = "zzz"` | taken |
| the selected item's `Text = "Alef"` | the combo's Text follows, to "Alef" |
| removing the selected item | SelectedItem Nothing, and Text stays "Alef" |

### The ImageList, Image, SelImage and Indentation

- **An Image before ImageList is set** raises 35613.
- **`Set ImageList = ImageList1`** binds it. **`ImageList = ImageList1` without Set** works too.
- **`Set ImageList = Text1`** raises **35610**, "Invalid object", and leaves ImageList **Nothing**: the earlier binding is
  lost.
- **Image takes** an index, as `1` or `3`, or a key, as `"green"`, and the item keeps what it was given, Integer or
  String.
  - **0 is taken, as no image.**
  - 4, -1 and 7, past the three images, raise 35600; a missing key raises 35601.
- `Add(, , "both", 1, 2, 4)` gave SelImage 2 and Indentation 4.
- **The ImageCombo's own Indentation is the default for new items.** After `Indentation = 2`, an Add without one gave the
  item 2.
- `Set ImageList = Nothing` is taken. The items keep their Image values.

### Locked, SelStart, SelLength and SelText

With Text "Hello world", 11 characters:

| Set | Read back |
|---|---|
| SelStart 0, SelLength 5 | SelText "Hello" |
| **SelStart 6** | **SelStart 5**; with SelLength 5, SelText " worl" |
| SelStart 11, 20 or -1 | **10**, and no error |
| SelStart 3, SelLength 100 | SelLength 8, SelText "lo world" |
| SelStart 0, SelLength 5, then `SelText = "Howdy"` | Text "Howdy world" |

**SelStart 6 reading 5, and 11 reading 10, resisted explanation.** SelStart 0 and 3 read back as set. The end of the
text was reached at 10, not 11.

**Locked True doesn't stop code.** `Text = "locked text"` and `SelText = "x"` both changed the text.

### The rest

- MousePointer takes 0, 1 and 2, and -1 raises 380.
- OLEDragMode and OLEDropMode take 0 and 1, and 2 raises 380.
- **Enabled and Locked take any number:** 1, 2 and -1 all read True.
- `BackColor = vbRed` is taken. `hWnd = 0` raises 383.
- **No event comes from code.** None was raised by `Text =`, Add, `Set SelectedItem` or Clear.

### In HexIDE

HexIDE models MSCOMCTL.OCX's ImageList, ProgressBar and StatusBar. `RuntimeLibrary` maps an ImageCombo to this issue
(#1092) and refuses it at load.
