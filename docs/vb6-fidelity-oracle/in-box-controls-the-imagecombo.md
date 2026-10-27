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

### Return types, Tag and the case of a key (2026-10-27)

Read into a Variant and logged with TypeName and VarType, early-bound through `ImageCombo1` and a `ComboItem` variable
and late-bound through `As Object`. The two bindings gave the same types. Native and p-code logged the same lines.

| Read | Type |
|---|---|
| `ComboItems.Count`, empty and with items | **Long**, VarType 3 |
| `ComboItem.Index` | **Long** |
| `ComboItem.Indentation`, and the ImageCombo's own `Indentation` | **Integer**, VarType 2 |
| `ComboItem.Image` and `SelImage`, never set | Integer 0 |
| `ComboItem.Text` and `Key` | String |
| `ComboItem.Selected` | Boolean |

**A new item's Tag is an empty String,** VarType 8, not Empty. `IsEmpty(Tag)` is False and `Tag = ""` is True. That held
for an item from `Add(, "abc", "Alpha")` and for one from `Add()`.

**Tag is a Variant that keeps what it is given, except Empty:**

| Assignment | Tag reads |
|---|---|
| `Tag = Empty` | **an empty String**, VarType 8 |
| `Tag = Null` | Null, VarType 1, with no error |
| `Tag = 5`, `= 2.5`, `= True`, `= "x"` | Integer 5, Double 2.5, Boolean True, String `x` |
| `Set Tag = Text1` | the TextBox object |
| `Tag = Text1`, without Set | the String `Text1`, the TextBox's default property |
| `Set Tag = Nothing` | Nothing. `Tag Is Nothing` is True |

**A key lookup is case-sensitive:**
- With an item keyed `abc`, `ComboItems("abc")` found it, and **`ComboItems("ABC")` and `("Abc")` raised 35601**,
  "Element not found".
- **`Add(, "ABC", "Upper")` was then taken**, not refused as a duplicate. `ComboItems("ABC")` found the new item.
- After `ComboItems(1).Key = "XYZ"`, `ComboItems("xyz")` raised 35601, and so did `Remove "xYz"`.

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
- **Events from code are posted, so they arrive only once messages are pumped.** Group 1's probe logged no event from
  `Text =`, Add, `Set SelectedItem` or Clear, but it never yielded while its handlers were logging. A later probe ran
  `DoEvents` after each step, on the shown form and on a second instance loaded and never shown, which logged alike:

  | From code | Event |
  |---|---|
  | `Text = "typed"`, and the same again, unchanged | **Change**, each time |
  | `Set SelectedItem = item 1`, and the same item again | **Change**, each time |
  | `Selected = True`; the selected item's `Text =`; `Text =` an item's text; `SelText =` | Change |
  | Add, Remove, Clear, `Set SelectedItem = Nothing`, Enabled False and True | nothing |

  **Code never raises Click.** The Change arrives after the statement that caused it has returned, and after any
  later statements before the next yield.

### Events, with real input

**The probe:**
- **The items:**

  | Item | Image | SelImage | Indentation |
  |---|---|---|---|
  | Alpha | red | none | 0 |
  | Bravo | green | none | 1 |
  | Charlie | blue | red | 2 |
  | Delta | none | none | 0 |

- **The input.** The form was shown with Text1 focused and the combo empty. A driver on the guest's console clicked and
  typed for real, and KeyPreview took the F keys to change the setup between steps.
- **The log.** Every event was logged with Text, SelectedItem, SelStart and SelLength.
- **The builds.** Native and p-code logged the same lines.

| Action | Events, in order |
|---|---|
| A click 20 pixels in, on the space left for an image | **Dropdown, then GotFocus.** The list opened |
| Type `B`, then `r` | KeyDown, KeyPress, **Change**, KeyUp for each. **No Click** |
| Alt+Down, with the list open | the key events only. The list closed, with no event for closing it |
| Down arrow, nothing selected | KeyDown, **Click** with Alpha selected and its text highlighted, KeyUp. **No Change** |
| Enter | KeyDown, KeyPress 13, KeyUp. **SelectedItem became Nothing**, and Text stayed "Alpha" |
| A click on the dropdown button | Dropdown |
| A click on Charlie in the open list | **Click** only |
| F4 | KeyDown, Dropdown, KeyUp |
| Esc | KeyDown, KeyPress 27, KeyUp. The selection stayed |
| Up arrow, then Down arrow, the list closed | Click with Bravo, then Click with Charlie |
| Type `x` over the highlighted "Charlie" | KeyDown, KeyPress, **Change** with Text "x" and **SelectedItem Nothing**, KeyUp |
| Locked True, then type `q` | the key events only. **The text didn't change** |
| Up arrow, Locked, "x" with nothing selected | Click with **Charlie**. **Locked doesn't stop the arrow keys** |
| Alt+Down, Locked | KeyDown, Dropdown, KeyUp. **Locked doesn't stop the list** |
| `Enabled = False`, with the focus | **Validate during the statement**, then LostFocus after the handler returned |
| `Enabled = True`, then `Set SelectedItem` to the item already selected | Change, after the handler returned |

**Click means the selection changed, and Change means the text did.** A choice from the list, or by the arrow keys,
changed Text and raised only Click. Typing raised only Change.

**The arrow keys from no selection didn't follow one rule.** With "Br" typed and nothing selected, Down chose Alpha,
the first item. With "x" typed over Charlie, which left nothing selected, Up chose Charlie, not Bravo.

### Drawing

These are screenshots at 96 dpi, without visual styles.
- **The edit part leaves room for an image** at its left, even with nothing selected.
- **The edit part draws the selected item's SelImage, not its Image.** With Charlie selected it drew red, with the focus
  or without it.
  - **With nothing selected it draws no image,** even with Text matching an item, as after Enter.
  - **The edit part ignores Indentation.**
  - **With the focus and the text highlighted,** the image is drawn blended with the highlight colour. Every other
    pixel is the highlight colour, in a checkerboard.
- **The open list draws each item's Image,** indented by **10 pixels per level**. Alpha's image starts at 108, Bravo's
  at 118 and Charlie's at 128.
  - **The highlighted row draws its SelImage,** blended in the same checkerboard, with a dotted focus rectangle round
    its text.
  - **Delta, which has no image, leaves the image's space blank,** and its text lines up with Alpha's.
- **Disabled,** the text is grey and the image isn't.

### The saved files

**The pass.** A form was written by hand, holding an ImageList and seven ImageCombos 2400 × 330, and opened in VB6's IDE.
- **ImageCombo1:** minimal.
- **ImageCombo2 to 6:** each holds candidate lines in reverse of VB6's order:
  - `Text = "hello"`;
  - `ImageList = "ImageList1"`, `Indentation = 2` and `Locked = True`;
  - Enabled False, MousePointer 11, OLEDragMode 1 and OLEDropMode 1;
  - BackColor 255 and ForeColor 65280;
  - an Arial 9.75 bold `Font` block.
- **ImageCombo7:** minimal, with its Properties window read page by page.
- **An eighth,** double-clicked from the toolbox.

The form was dirtied with a comment and saved, and VB6 wrote no load log. VB6's save is
`corpus/inbox-controls/pending/imagecombo/`. The hand-written form and the save were then read at run time, native and
p-code, which logged the same values.

**VB6's blocks, as it wrote them:**

```
   Begin MSComctlLib.ImageCombo ImageCombo8 
      Height          =   330
      Left            =   2850
      TabIndex        =   7
      Top             =   2325
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   582
      _Version        =   393216
      ForeColor       =   -2147483640
      BackColor       =   -2147483643
      Text            =   "ImageCombo8"
   End
   Begin MSComctlLib.ImageCombo ImageCombo3 
      …the same lines as far as _Version…
      Indentation     =   2
      Locked          =   -1  'True
      ImageList       =   "ImageList1"
   End
```

**The order:**
1. ForeColor, BackColor;
2. MousePointer, Enabled, OLEDragMode, OLEDropMode;
3. Indentation, Locked;
4. the `Font` block;
5. Text;
6. **ImageList, last.**

**What the lines hold:**
- **A minimal block gains nothing.** Unlike the DTPicker's and the MonthView's, VB6 wrote ImageCombo1 and ImageCombo7
  back with only their position, extents and `_Version`.
- **ImageList is written as the ImageList's name,** a string, and binds by that name at load.
- **The ImageCombo takes its height from its font, in the IDE and at run time alike.** The Arial block, written 330
  high, was read and saved as **375**, with `_ExtentY` 661.
- **Every line read back as written.** A block without the colour lines reads the button colours, as in group 1.
- **The Properties window has 34 rows:**
  - (About), (Custom) and (Name);
  - BackColor `&H8000000F` and ForeColor `&H80000012`;
  - CausesValidation, then **DataBindings, DataField, DataFormat, DataMember and DataSource**;
  - DragIcon, DragMode, Enabled, Font, Height, HelpContextID, Indentation, Index, Left, Locked, MouseIcon,
    MousePointer, OLEDragMode and OLEDropMode;
  - TabIndex, TabStop, Tag, Text, ToolTipText, Top, Visible, WhatsThisHelpID and Width.

  **There is no ImageList row.** It is set on the (Custom) property pages.

**Double-clicked from the toolbox,** an ImageCombo is written first in the file:
- **It is 1500 × 330 at 2850, 2325, on a 7200 × 5400 form.** That is centred as if it were 750 high, `(5400 − 750) / 2`,
  and the control then shrank to its font's height. The save of nine controls in `pending/defaults` agrees: 1875 on a
  4500-high form.
- **It gets three lines a hand-written block doesn't need:** ForeColor `-2147483640` and BackColor `-2147483643`, the
  window-text and window colours, and **`Text` set to its own name**.

So **a new ImageCombo and one read from a block without those lines differ.** The new one is white with "ImageCombo8"
in it, and the block reads button-face grey and empty.

### In HexIDE

HexIDE models MSCOMCTL.OCX's ImageList, ProgressBar and StatusBar. `RuntimeLibrary` maps an ImageCombo to this issue
(#1092) and refuses it at load.
