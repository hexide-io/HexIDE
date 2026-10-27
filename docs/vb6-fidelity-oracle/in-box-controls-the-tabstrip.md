## In-box controls: the TabStrip (2026-10-27, #1089)

**The probes.**
- **The software:** VB6 6.00.8176 SP6, with MSCOMCTL.OCX, on Windows 10 at 96 dpi.
  - The form line is `Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"`.
  - The block is `Begin MSComctlLib.TabStrip`, `_Version` 393216.
- **The form:** written by hand. It held TabStrip1 at Left 120, Top 120, 5000 × 3000, with only its position, extents
  and `_Version`, and VB6's saved three-image ImageList from `corpus/inbox-controls/pending/imagelist/three/`.
- **The programs:** each ran in a Sub Main and logged each value with its `TypeName`.
  - Every error was read straight after its statement, before any helper ran.
  - Questions that earlier statements could disturb each ran on a fresh instance of the form.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.

### The object model

- **Its members,** from the type library:
  - **The strip:** Tabs, SelectedItem, Style, MultiRow, TabWidthStyle, TabFixedWidth, TabFixedHeight, TabMinWidth,
    Placement, TabStyle, HotTracking, MultiSelect, Separators, ShowTips, ImageList, ClientLeft, ClientTop, ClientWidth,
    ClientHeight, Font, Enabled, hWnd, MousePointer, MouseIcon, OLEDropMode.
  - **Its methods:** DeselectAll, Refresh, OLEDrag.
  - **Tabs:** Count, Item (the default), Add(Index, Key, Caption, Image), Remove, Clear, and For Each.
  - **A Tab:** Caption (the default), Key, Tag, Index, ToolTipText, Image, Selected, HighLighted, and Left, Top,
    Width and Height.
- **Its events:** BeforeClick(Cancel), Click, KeyDown, KeyPress, KeyUp, MouseDown, MouseMove, MouseUp, and the OLE drag
  events.
- **The constants:**
  - Style: tabTabs 0, tabButtons 1, tabFlatButtons 2.
  - TabWidthStyle: tabJustified 0, tabNonJustified 1, tabFixed 2.
  - Placement: tabPlacementTop 0, tabPlacementBottom 1, tabPlacementLeft 2, tabPlacementRight 3.
  - TabStyle: tabTabStandard 0, tabTabOpposite 1.
- **`TypeName`** is `TabStrip`.
- **The TabStrip has no default member.** `v = TabStrip1` is **438**.
- **A Tab's default is Caption.** `v = Tabs(1)` read the caption, and `Tabs(1) = "Renamed"` set it.

### Defaults

| Read | Logged |
|---|---|
| Tabs | **one tab**, Caption and Key empty, selected. That is from a block with no tabs in it |
| Style, TabWidthStyle, Placement, TabStyle, MousePointer, OLEDropMode | 0, Longs |
| MultiRow, HotTracking, MultiSelect, Separators | False |
| ShowTips, Enabled, TabStop | True |
| TabFixedWidth, TabFixedHeight | 0, Integers |
| TabMinWidth | **566.9291**, a Single: 1000 HIMETRIC |
| ImageList | Nothing |
| Font | MS Sans Serif 8.25 |
| ClientLeft, ClientTop | **180.2835, 450.1418** |
| ClientWidth, ClientHeight | 4875.024, 2610.142 |

**ClientLeft and ClientTop are in the form's coordinates, not the TabStrip's.** They include its Left and Top of 120:
- ClientLeft is 4 pixels in from the TabStrip's left edge.
- ClientTop is 22 pixels down: 2 pixels, the 18-pixel row of tabs, and 2 more.

A Tab's Left and Top are in the form's coordinates too: the first tab sat at 150.2362, 150.2362, 2 pixels inside the
TabStrip.

### Tabs

| Call | Logged |
|---|---|
| `Tabs.Add()` | a tab with an empty Caption and Key, at the end |
| `Tabs.Add(1, "k0", "Zero")` | inserted first. The rest renumbered |
| `Tabs.Add(Count + 1)` | at the end |
| `Tabs.Add(Count + 2)`, `Tabs.Add(0)` | **35600** "Index out of bounds" |
| `Tabs.Add(, "k2")` with `k2` taken | **35602** "Key is not unique in collection" |
| `Tabs.Add(, "K2")` with `k2` taken | added: **keys are case-sensitive** |
| `Tabs.Add(, "7")` | **35603** "Invalid key": a key can't be a number |
| `Tabs.Add(, "")` | added. An empty key is no key |
| `Tabs.Add(, , , 1)` with no ImageList | **35613** "ImageList must be initialized before it can be used" |
| `Tabs("k2")` | the tab |
| `Tabs("K2")` | **35601** "Element not found": the lookup is case-sensitive too |
| `Tabs("3")` | **35601**: a string is always a key |
| `Tabs(0)`, `Tabs(99)` | **35600** |
| `Tabs(1.5)`, `Tabs(2.5)`, `Tabs(3.5)` | tabs 2, 2 and 4: **half rounds to even** |
| `Tabs(1.4)`, `Tabs(1.6)`, `Tabs(2.6)` | tabs 1, 2 and 3 |
| `Tabs.Count = 3` | **383** |
| `Tabs.Remove 99`, `Tabs.Remove "nokey"` | 35600, 35601 |
| `Tabs.Clear` | no tabs. SelectedItem is Nothing, and reading `SelectedItem.Caption` is **91** |

### A Tab

| Assignment | Logged |
|---|---|
| `Index = 3` | **383** "Property is read-only" |
| `Left`, `Top`, `Width` or `Height = 100` | **383** "Set not supported (read-only property)" |
| `Key` = another tab's key | **35602** |
| `Key = "renamed"` | taken, and `Tabs("renamed")` found it |
| `Caption = "&Mnemonic"` | kept as given. It reads back `&Mnemonic` |
| `Set Tag = Text1`, `Set Tag = Me`, `Set Tag = New Collection` | taken |
| `Set Tag` = the TabStrip itself, its Tabs, or one of its tabs | **35700** "Circular object referencing is not allowed" |
| `Tag = 42` | kept as an Integer |
| `Image = 1` with no ImageList | **35613** |
| with ImageList1 set: `Image = 2`, `Image = "ico"` | taken, **kept as given**: an Integer 2, a String `ico` |
| `Image = 9` | **35600**, the image unchanged |
| `Image = "nokey"` | **35601** |
| `Image = 0` | no image |
| `HighLighted = True` | taken |

- **`ImageList = "ImageList1"`, a name rather than the control, is 13.** `Set ImageList = ImageList1` works.
- **An image makes every tab taller:** 18 pixels became 19, and ClientTop moved down with it.

### The strip's properties

| Assignment | Logged |
|---|---|
| Style, TabWidthStyle, TabStyle, OLEDropMode: -1, 3, 4, 99 | **380**, the value kept |
| Placement 3 | taken. Placement -1, 4, 99 are **380** |
| MousePointer 3, 4, 99 | taken. -1 is **380** |
| TabFixedWidth, TabFixedHeight: -1 | **380** |
| TabFixedWidth, TabFixedHeight: 40000 | **6** "Overflow": they are Integers |
| TabMinWidth: -1 | **380** |
| TabMinWidth: 40000 | taken: 40000.25 |
| ClientLeft, ClientTop, ClientWidth, ClientHeight | **383** |

**Placement 2 sets MultiRow to True, and it stays True when Placement goes back to 0.** A fresh form with Placement set
to 2 and back read MultiRow True. Left and right tabs are always in rows.

**The layout,** in twips in the form's coordinates, with the TabStrip at 120, 120, 5000 × 3000:

| Setting | Logged |
|---|---|
| 3 tabs, `Tab number n` | each 1170 wide and 270 high, from 150, 150, edge to edge. Client 180, 450, 4875 × 2610 |
| an empty caption, `&Mnemonic` | 570 wide, 915 wide. 570 is TabMinWidth, to the pixel |
| Placement 1, bottom | tabs at top 2820. Client 180, 180 |
| Placement 2, left | client 750, 180, 4305 × 2880 |
| Placement 3, right | client 180, 180, 4305 × 2880 |
| Style 1, buttons | first button at 120, 120, 315 high, the next 3 pixels on. Client top 495 |
| Style 2, flat buttons | the next button 10 pixels on. Separators True moved nothing |
| TabWidthStyle 1, one row | the same widths as 0 |
| TabWidthStyle 2, TabFixedWidth 1500 | each 1500 wide |
| 12 tabs, MultiRow False | **one row, running past the TabStrip's right edge:** tab 12 at left 13200 |
| 12 tabs, MultiRow True | **four rows, the selected tab's row at the bottom,** next to the client area. Client top 1260, height 1800. The rows are justified: 4, 4, 2 and 2 tabs |

### The selection, and what raises events

| Statement | Events, and what is selected after |
|---|---|
| `Set SelectedItem = Tabs(3)` | BeforeClick, with SelectedItem still the old tab, then Click. Tab 3 |
| the same again | **BeforeClick and Click again** |
| `Tabs(2).Selected = True` | BeforeClick, Click. Tab 2 |
| `Tabs(2).Selected = False`, tab 2 selected | **BeforeClick, Click, and tab 2 still selected** |
| `Set SelectedItem = Tabs(4)`, BeforeClick setting Cancel | **Click is still raised.** Tab 2 still selected |
| `Set SelectedItem = Nothing` | no events. No tab selected |
| then `Set SelectedItem = Tabs(3)` | **Click only**: BeforeClick needs a tab already selected |
| `SelectedItem = Tabs(1)`, without Set | the same as with Set |
| `SelectedItem = 2` | **13** |

- **Loading and showing the form raised no events.**
- **Add, Remove, Clear and changing a Caption or Key raise no events, but they can move the selection:**
  - **Every Remove leaves the first tab selected:** removing the selected tab, one before it, one just after it,
    or the last.
  - An Add after the selected tab leaves the selection where it is.
  - **An Add at the selected tab's index gives the new tab the selection.**
  - An Add to an empty TabStrip selects the new tab. Clear leaves none.
- **MultiSelect:**
  - Setting it True is taken with any Style.
  - With Style 1 and MultiSelect True, `Tabs(3).Selected = True` and then `Tabs(5).Selected = True` left only tab 5
    selected. Code selects one tab at a time.
  - **DeselectAll with Style 1 left no tab selected,** SelectedItem Nothing. With Style 0 it did nothing.

### Events, with real input

**The probe:** four tabs, `&Alpha`, `&Beta`, `Gamma` with an image, and `&Delta`, each with a ToolTipText, run on the
guest's console. A driver clicked and typed for real and marked each action in the log. Native and p-code logged the
same lines, mouse coordinates apart. Mouse X and Y are twips from the TabStrip's top-left corner.

| Action | Events, in order |
|---|---|
| Click a tab that isn't selected | MouseDown, with SelectedItem still the old tab; BeforeClick; **MouseUp, with the new tab already selected**; Click |
| Click the selected tab | MouseDown, MouseUp, Click. **No BeforeClick** |
| Click in the client area, below the tabs | **nothing**, on the TabStrip or on the form |
| Double-click a tab | the first click's events, then **MouseUp and Click again, with no MouseDown**. The TabStrip has no DblClick event |
| Right-click a tab | MouseDown and MouseUp with Button 2, then **Click**. The selection is unchanged |
| Left or Right arrow | KeyDown, BeforeClick, Click, KeyUp. Left on the first tab raises only KeyDown and KeyUp |
| Home, End | the first tab, the last, with BeforeClick and Click |
| Down arrow, Ctrl+Tab, Ctrl+PgDn | KeyDown and KeyUp only: no tab changes |
| Space, Escape | KeyDown, KeyPress, KeyUp |
| **Alt and a caption's `&` letter** | **selects that tab:** BeforeClick, Click, then KeyUp, with no KeyDown. From Text1, Alt+B also moved the focus: GotFocus came first |
| Tab, Shift+Tab | the focus moves in and out. The TabStrip takes the focus, and draws a focus rectangle on the selected tab |
| Click a tab, BeforeClick setting Cancel | MouseDown, BeforeClick, MouseUp, **Click**. The selection is unchanged |
| Right arrow, BeforeClick setting Cancel | KeyDown, BeforeClick, KeyUp. **No Click** |
| Style 1 and MultiSelect: **Ctrl+click** a second tab | MouseDown, MouseUp, Click, **no BeforeClick**. Both tabs Selected; SelectedItem stays the first |
| then a plain click on a third tab | MouseDown, MouseUp, Click, no BeforeClick. Only that tab selected |
| Hover a tab with a ToolTipText | its tip appears. ShowTips is True by default |

**Changing Style at run time selects the first tab.**
- From 0 to 1, Click was raised with no BeforeClick, and the focus left the TabStrip and came back (LostFocus,
  Text1's GotFocus, GotFocus).
- Setting Style or TabStyle on a fresh form moved the selection from the second tab to the first.
- hWnd was unchanged each time.

**HotTracking is read-only at run time: 383.** TabStyle can be set at run time.

### Drawing

Screenshots on Windows 10 at 96 dpi, one fresh form per state, native code. The TabStrip draws as the classic,
unthemed tab control.
- **As loaded,** with its one empty tab: a narrow tab with no caption.
- **The selected tab is drawn raised** and joined to the client area. **A caption's `&` letter is underlined.**
- **Images** sit left of the captions.
- **Placement 1** puts the tabs along the bottom.
  - **Placement 2:** a column down the left, the captions turned to read upwards.
  - **Placement 3:** a column down the right, the captions reading downwards.
- **Style 1** draws buttons, the selected one pressed. **Style 2** draws flat buttons, and Separators draws a line
  between them. With MultiSelect, Ctrl+clicked buttons are all drawn pressed.
- **MultiRow with 12 tabs:** four rows, the selected tab's row at the bottom, each row stretched to the full width.
  With TabStyle 1 the selected row was still the bottom one.
- **MultiRow False with 12 tabs:** one row, cut off at the right edge, with a pair of scroll arrows.
- **TabWidthStyle 2 with TabFixedWidth 1200 and TabFixedHeight 500:** large tabs, the captions centred.
- **Enabled False draws the TabStrip exactly as enabled.** The captions aren't greyed.
- **HighLighted True on a tab drew no differently,** in a disabled TabStrip.

### The designer: a control drawn on a TabStrip is not its child

Whether a TabStrip takes the controls drawn on it, answered on VB6's IDE rather than from memory:
- **The steps:**
  1. A form holding TabStrip1 at 120, 120 was opened in the IDE.
  2. A CommandButton was drawn with the toolbox on TabStrip1's client area.
  3. TabStrip1 was dragged by its row of tabs, 60 pixels right and 60 down, and the form was saved.
- **The button stayed where it was drawn:**
  - TabStrip1 moved to 1020, 1020, while Command1 stayed at 900, 1350.
  - After the drag, Command1 sat over the TabStrip's top-left corner.
- **The `.frm` holds the button at the form's level,** in its own `Begin VB.CommandButton` beside the TabStrip's block,
  not inside it.
- **The drawn button was above the TabStrip,** being drawn later. That is z-order, not containment: programs show and hide
  their own frames over a TabStrip for this reason.
- **A TabStrip double-clicked from the toolbox** was 1500 × 750 at 4050, 3225, centred, with one tab whose caption is
  empty.

### The saved files

**The programs:**
- **The save:** a form written by hand, opened in VB6's IDE, dirtied with a comment and saved. VB6's save is in
  `corpus/inbox-controls/saved/tabstrip/`.
  - TabStrip1 had every property off its default, its lines in reverse order, and three tabs.
  - TabStrip2 had only its position, extents and `_Version`.
  - A third TabStrip was double-clicked from the toolbox.
- **The tab names:**
  - Caption, Key, `Object.ToolTipText` and `ImageVarType` are from VB6's own `Template\Forms\Options Dialog.frm`.
  - The image lines are what VB6 wrote after a tab's image was set to 2, then to `blue`, on the TabStrip's Property
    Pages in the IDE.
- **The read:** the hand-written form and VB6's save were read back at run time, natively and as p-code. All four runs
  logged the same values.

**VB6's block, as it wrote it:**

```
   Begin MSComctlLib.TabStrip TabStrip1 
      Height          =   3000
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   4400
      _ExtentX        =   7752
      _ExtentY        =   5292
      TabWidthStyle   =   2
      MultiRow        =   -1  'True
      Style           =   1
      ShowTips        =   0   'False
      TabFixedWidth   =   1200
      TabFixedHeight  =   400
      HotTracking     =   -1  'True
      MultiSelect     =   -1  'True
      Placement       =   1
      Separators      =   -1  'True
      TabMinWidth     =   600
      TabStyle        =   1
      ImageList       =   "ImageList1"
      _Version        =   393216
      BeginProperty Tabs {1EFB6598-857C-11D1-B16A-00C0F0283628} 
         NumTabs         =   3
         BeginProperty Tab1 {1EFB659A-857C-11D1-B16A-00C0F0283628} 
            Caption         =   "&One"
            Key             =   "k1"
            Object.Tag             =   "tag one"
            Object.ToolTipText     =   "tip one"
            ImageVarType    =   2
            ImageIndex      =   1
         EndProperty
         BeginProperty Tab2 {1EFB659A-857C-11D1-B16A-00C0F0283628} 
            Caption         =   "Two"
            Key             =   "k2"
            ImageVarType    =   8
            ImageKey        =   "blue"
         EndProperty
         BeginProperty Tab3 {1EFB659A-857C-11D1-B16A-00C0F0283628} 
            Caption         =   "Three"
            Object.Tag             =   "t3"
            ImageVarType    =   2
         EndProperty
      EndProperty
      MousePointer    =   2
      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} 
         ...
      EndProperty
      OLEDropMode     =   1
   End
```

- **The order is VB6's, whatever order the lines were read in.**
  - `_Version` comes after the properties, not straight after the extents as the MSFlexGrid's does.
  - Then the Tabs block, MousePointer, the Font block and OLEDropMode.
- **Each property is written only when off its default.**
- **Every TabStrip gets a Tabs block,** with `NumTabs` and one `BeginProperty TabN` per tab.
  - The minimal block and the double-clicked one were each given one tab holding only `ImageVarType = 2`.
  - A tab's lines are Caption, Key, `Object.Tag`, `Object.ToolTipText`, ImageVarType, and then the image. Each is written
    only when set, except **ImageVarType, which is always written**: 2 with no image.
- **A tab's image is two lines:**
  - an index is `ImageVarType = 2` and `ImageIndex = n`;
  - a key is `ImageVarType = 8` and `ImageKey = "key"`.

  Read back, Image kept the type: Integer 1, String `blue`.
- **The ImageList is named:** `ImageList = "ImageList1"`. Written by hand after the Tabs block, it still linked, and both
  tabs' images loaded.
- **TabFixedWidth, TabFixedHeight and TabMinWidth are HIMETRIC in the file.** 1200, 400 and 600 read back as 680, 226 and
  340.1575 twips. TabFixedHeight is an Integer, so 226.77 became 226.
- **The selection isn't saved.** A hand-written `Selected = -1` on the third tab was ignored, and VB6's save left it out.
  The first tab is selected at load.
- **There is no `.frx` for the TabStrip.** The corpus's `.frx` holds the ImageList's images.

**What a wrong line does.**
- **A wrong name is ignored silently:** `Image = 1` loaded no image.
- **`ImageVarType = 8` with no `ImageKey` beside it breaks the whole form.**
  - Written with `Image = "blue"`, the IDE said "Type mismatch" and opened no designer. Ctrl+S then asked where to save.
  - `vb6.exe /make` failed with "Type mismatch".
  - Dropping those two lines, and nothing else, made both load.

**The designer re-sizes to whole pixels when it moves a TabStrip.** After TabStrip1 was dragged in the IDE, its Width of
5000 was saved as 4995: 333 pixels. Re-saving without a drag kept 4400.

### In HexIDE

The MSCOMCTL TabStrip's collection and code-selection model is bound to the interpreter (#1089). A form with no
persisted own TabStrip properties runs, and code can add tabs and select one, including BeforeClick cancellation
followed by Click. Integer/Boolean strip properties and Tab Caption, Key, Selected, ToolTipText, HighLighted and scalar
Tag are reachable. Window input now selects with Left/Right/Home/End and Alt plus a caption mnemonic; Arrow-key BeforeClick cancellation suppresses Click; cancelled mnemonic and Home/End selection refuse pending measurement. Mnemonics take focus from another control. Unmeasured conversions refuse. Mouse input now dispatches MouseDown before selection and MouseUp/Click afterwards, with cancellation still followed by Click. Selected-tab and right clicks skip BeforeClick; Style 1 MultiSelect supports plain and Ctrl clicks. A double-click suppresses its second MouseDown, and client-area clicks raise nothing. The single top row now paints raised selected tabs, pressed buttons, flat-button separators and mnemonic underlines; disabled painting is identical. TabStyle 1 drawing refuses because the supplied measurement does not describe its visual distinction. Other placements, multiple rows, width styles, fixed sizes, overflow arrows, images, tooltips, HighLighted painting, Font and geometry remain for later
slices. The measured save now round-trips byte for byte, with mandatory Tabs/ImageVarType records and the measured
property order. ImageVarType 8 without ImageKey refuses the whole form. Saved scalar properties and plain tabs load,
with tab sizes converted from HIMETRIC and selection starting on tab 1. Runtime loading of saved ImageList/Font and
tab images remains refused. Unsupported tab fields and missing extents keep the designer read-only. COMCTL32 is still refused.
