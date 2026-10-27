## In-box controls: the StatusBar (2026-10-26, #1087)

**The probes.** VB6 6.00.8176 SP6, with MSCOMCTL.OCX 6.01.9782, the library and `Object=` line of [In-box controls: the
ProgressBar](in-box-controls-the-progressbar.md). Every build was made with `/make` as native code and as p-code, and
the two logged the same lines throughout.
- **Run time:** a Sub Main showing a 6000×4500-twip form holding one StatusBar, at its defaults, aligned to the bottom. It
  is driven late-bound under `On Error Resume Next`, logging values with their `TypeName`, or the error.
  - It ran in session 0 and again on the guest's console, with the same lines except four AutoSize widths, given below
    from the console.
  - A second program, on the console, held eight panels, one of each Style. The agent clicked them with real mouse
    clicks and logged every event, and the screen was captured.
- **The files:** a hand-written form holding two configured StatusBars was opened in VB6's IDE, made dirty and saved.
  The toolbox double-click is the ImageList entry's ([In-box controls: the ImageList](in-box-controls-the-imagelist.md)).

### The control

- **Its members,** from the type library:
  - SimpleText, Style (0 sbrNormal, 1 sbrSimple), Panels, ShowTips, Enabled, Font;
  - MousePointer, MouseIcon, OLEDropMode, hWnd;
  - the methods Refresh and OLEDrag.
- **Its events:**
  - **PanelClick(Panel)** and **PanelDblClick(Panel)**;
  - MouseDown, MouseMove, MouseUp, Click and DblClick;
  - the OLE drag events.
- **Panels** has Count, `Add([Index], [Key], [Text], [Style], [Picture])`, Item, Remove and Clear.
- **A Panel** has Alignment, AutoSize, Bevel, Enabled, Index, Key, Left, MinWidth, Picture, Style, Tag, Text,
  ToolTipText, Visible and Width. **Its default member is Text**: `v = Panels(1)` reads it, and `Panels(1) = "x"` sets
  it.
- **`TypeName`:** `StatusBar`, its Panels **`IPanels`**, and a panel **`IPanel`**.

### Defaults

| Read | Logged |
|---|---|
| Style, SimpleText | `0`, empty, a Long and a String |
| ShowTips, Enabled | `True`, `True` |
| MousePointer, OLEDropMode | `0`, `0`, Longs |
| Font | MS Sans Serif 8.25 |
| Align | `2`, an Integer: bottom |
| Left, Top, Width, Height | 0, 4125, 6000 and 375, Singles |
| TabIndex | `0`, an Integer |
| TabStop | 438: it has none |
| Index | 343 |
| Tag, ToolTipText | empty Strings |
| Panels.Count | 1: a new StatusBar holds one panel |

**The one panel, and any panel `Add` makes with no arguments:**

| Property | Value |
|---|---|
| Index | 1 |
| Key, Text, ToolTipText, Tag | empty |
| Width, MinWidth | 1440 and 1440 twips |
| Left | 14.74016 |
| AutoSize, Alignment, Style | 0, Longs |
| Bevel | 1, inset |
| Enabled, Visible | True |
| Picture | Nothing |

Lengths come back with fractions, such as 2000.126 for 2000. Panel lengths go through HIMETRIC and back, so each is the
nearest whole HIMETRIC in twips.

**What each reads back as,** by `TypeName`, native and p-code alike:
- **Integer:** `Panels.Count`, also after `Clear` when it is 0, and a panel's Index.
- **Single:** Width, MinWidth and Left.
- **Long:** AutoSize, Alignment, Bevel and Style, and the bar's Style.
- **Boolean:** Enabled, Visible and the bar's ShowTips.
- **String:** Key, Text, an unset Tag and SimpleText.

### Panels

The same family of errors as the ImageList's ListImages:
- **Add:**
  - a duplicate key is **35602**;
  - a key that reads as a number, `"5"`, is **35603**;
  - Index 0 or past Count + 1 is **35600**;
  - a Style past 7 is **380**.
- Add accepts an empty key, a Picture, and no arguments at all.
- **Lookup:**
  - `Panels("missing")` and `("5")` are **35601**;
  - `("")` is 35603;
  - `(0)` and `(99)` are 35600;
  - `(1.5)` and `(2.5)` are both 2: half to even.
- **Remove** by key or index shifts the rest down, with the same errors. **Clear** leaves no panels at all.
- `Panels.Count = 1` and `Set Panels = Nothing` are 383.

### A panel's properties

| Assignment | Logged |
|---|---|
| Alignment 0 to 2, Bevel 0 to 2, Style 0 to 7, AutoSize 0 to 2 | no error. One past the last of each is **380**, and the property is left as it was |
| `Width = 0`, `= -5`, `MinWidth = 0`, `= -5` | 380: both must be positive |
| `Left = 100`, `Index = 1` | **383**, read-only |
| `Key =` another's key | 35602 |
| `Text = 42` | `42`, a String |
| Text of 300 characters | kept whole |
| `Set Tag = StatusBar1` | **35700**, "Circular object referencing is not allowed" |
| `Visible = False` | the panel takes no space. The next panel moves to its Left |
| `Enabled = False`, `Set Picture = …`, `= Nothing`, `ToolTipText = "tip"` | no error |

- **Setting MinWidth sets Width to the same value**, downwards as well as up. MinWidth 2500 made Width 2500, and later
  MinWidth 500 made a Width of 1000 into 500.
- **Setting Width ignores MinWidth on a panel that isn't AutoSize:**
  - Width 1000 with MinWidth 2500 raised nothing.
  - It read back 1000 after DoEvents and Refresh.
  - The next panel was laid out after it.
- **Setting AutoSize recomputes Width,** and setting it back to 0 keeps what it computed.
- **Style doesn't change Text.** A panel of any Style reads back the Text it was given.

### The bar

- **`Style = 1`, simple, shows SimpleText across the whole bar.** The panels are kept, and their Left and Width still
  read back. `Style = 2` is 380.
- **Align 1 puts it at the top, Top 0.** Align 0 leaves it where it is.
- **Height can be set:** Height 600 kept through Align 2 put it at Top 3900.

### AutoSize: how the width is shared

Three panels of 1440 twips (96 px) on a bar 6000 twips (400 px) wide, read on the console:
- **Placing:** the first panel's Left is 1 px, and there is 1 px between panels. The last 19 px at the right are kept
  for the size grip.
- **Panel 2 Spring (AutoSize 1):** it took the rest, 2789.858 twips (186 px). Panel 3 then ran to 381 px.
- **Panels 2 and 3 Spring:** 2115.213 each, 141 px.
- **Panel 3's MinWidth raised to 3000:** the bar couldn't fit both springs above their MinWidths. Each fell to its
  MinWidth, 1440 and 3000, and panel 3 ran on into the size grip's 19 px.
- **Panel 1 Contents (AutoSize 2):** it stayed at its MinWidth for the text `panel 1`. Given a much longer text, it
  grew to 2250.142 to fit it.
- **The two springs, with MinWidths 1440 and 100:**
  - They came to 2775.118 and 1434.898. Each is its MinWidth plus the same extra, 1335.118.
  - With the longer Contents text they were 2369.764 and 1029.543, again an equal extra over each MinWidth.
  - So **springs share what is left equally, above their own MinWidths.**
- **Two fixed panels of 5000 on the 6000 bar:** they kept their widths, and the third ran on past the edge, at Left
  10035. Nothing is squeezed.
- **The form made 1500 twips wider,** after the two fixed panels: the spring, held at its MinWidth of 1440 until then,
  grew to 2069.858, the room the wider bar left, and nothing else moved.
- **Session 0 differed:** wherever there was one spring, or springs with unequal MinWidths, it gave each spring 1 px
  (about 15 twips) more than the console did. The console is the one to follow.

### Events

Real clicks on the console, centred on each panel, in the order each fired:

| Action | Events |
|---|---|
| Click on panel 1 | MouseDown (button 1), **PanelClick 1**, MouseUp, Click |
| Double-click on panel 2 | MouseDown, PanelClick 2, MouseUp, Click, DblClick, **PanelDblClick 2**, MouseUp |
| Right-click on panel 3 | MouseDown (button 2), **PanelClick 3**, MouseUp, Click |
| Click on the size grip | nothing |
| Style 1, then a click where panel 1 was | MouseDown, MouseUp, Click: **no PanelClick** |
| Style 1, then a double-click | MouseDown, MouseUp, Click, DblClick, MouseUp: **no PanelDblClick** |

- **PanelClick comes between MouseDown and MouseUp**, for either button.
- **A right click raises PanelClick and Click as a left one does.**
- **MouseDown's x and y are twips from the bar's own top-left:** 300, 180 at the centre of a panel whose Left is 14.74
  and Width 566.93.

### Drawing

Read from the console's screen, eight panels with Text `T0` to `T7`:
- **Style 0** draws its Text.
- **Styles 1 to 4 (Caps, Num, Ins and Scrl) draw the panel's own Text**:
  - black while the key's toggle is on and greyed while it is off;
  - with empty Text they draw nothing, in either state, with no CAPS or NUM of their own.

  The probe turned Caps, Num, Insert and Scroll Lock on and back off to see it. Each panel followed its own key.
- **Style 7, Kana,** stayed greyed: this keyboard has no Kana key.
- **Style 5, Time,** drew `20:58` and **Style 6, Date,** drew `26/10/2026`, the en-GB short time and date, whatever the
  Text.
- **Simple style** drew SimpleText from the bar's left edge, over the whole width, with the size grip still at the right.

### The files

VB6's save of the hand-written form:

```
   Begin MSComctlLib.StatusBar StatusBar1 
      Align           =   2  'Align Bottom
      Height          =   375
      Left            =   0
      TabIndex        =   0
      Top             =   4125
      Width           =   6000
      _ExtentX        =   10583
      _ExtentY        =   661
      _Version        =   393216
      BeginProperty Panels {8E3867A5-8586-11D1-B16A-00C0F0283628} 
         NumPanels       =   4
         BeginProperty Panel1 {8E3867AB-8586-11D1-B16A-00C0F0283628} 
            AutoSize        =   1
            Object.Width           =   4916
            MinWidth        =   1000
            Text            =   "Ready"
            TextSave        =   "Ready"
            Key             =   "status"
            Object.Tag             =   "t1"
            Object.ToolTipText     =   "tip1"
         EndProperty
         BeginProperty Panel2 {8E3867AB-8586-11D1-B16A-00C0F0283628} 
            Style           =   6
            Alignment       =   1
            Bevel           =   2
            TextSave        =   "26/10/2026"
         EndProperty
         BeginProperty Panel3 {8E3867AB-8586-11D1-B16A-00C0F0283628} 
            Style           =   1
            Enabled         =   0   'False
         EndProperty
         BeginProperty Panel4 {8E3867AB-8586-11D1-B16A-00C0F0283628} 
            AutoSize        =   2
            Object.Visible         =   0   'False
            Text            =   "x"
         EndProperty
      EndProperty
   End
```

- **A panel's `Object.Width` and `MinWidth` are HIMETRIC, not twips.** A form whose panels said `Object.Width = 1000`
  and `MinWidth = 1000` ran with each panel 566.9291 twips wide, which is 1000 HIMETRIC. A default panel, with no
  Width line, is 1440 twips, 2540 HIMETRIC.
- **A Spring panel's `Object.Width` is rewritten from the designer's layout.** The probe wrote 3000, and VB6 saved
  4916.
- **The panel names VB6 uses** are those above. The hand-written `Visible` came back as `Object.Visible`, and each
  `Object.` name is padded as though it had no prefix, as the ImageList's `Object.Tag` is.
  - Within each panel the order is as the save shows.
  - No panel held every property, so where Style falls against AutoSize, or Enabled against Text, isn't pinned.
- **TextSave is the panel's displayed text when saved.** The Date panel gained `TextSave = "26/10/2026"`.
  - The Ready panel kept the `TextSave` the probe had written.
  - The Caps panel and the invisible panel got none.
  - Whether VB6 writes one for a plain text panel the probe gave no TextSave wasn't measured.
- **A panel at every default is an empty `BeginProperty Panel1 … EndProperty`.**
- **A panel's Picture is written first in its block, as an `.frx` reference.** The record is the ImageList's: a
  4-byte length, StdPicture's CLSID, `lt\0\0`, a 4-byte size, then the picture file as it is.
  - A form was written by hand with a bitmap on one panel and an icon on another, each line after Text. VB6 loaded it
    with no log and saved it again.
  - The `.frx` it wrote was byte for byte the hand-written one.
  - The `Picture` lines moved to the top of their panels:

  ```
         BeginProperty Panel1 {8E3867AB-8586-11D1-B16A-00C0F0283628} 
            Picture         =   "Form1.frx":0000
            Text            =   "red"
            TextSave        =   "red"
         EndProperty
         BeginProperty Panel2 {8E3867AB-8586-11D1-B16A-00C0F0283628} 
            Picture         =   "Form1.frx":0352
            Text            =   "icon"
            TextSave        =   "icon"
         EndProperty
  ```

  Built and run, the saved form gave the first panel a Picture of Type 1 and the second one of Type 3, each 423×423
  HIMETRIC. VB6's save is `corpus/inbox-controls/pending/statusbar-picture`.
- **The bar's own properties come after the extents and before `_Version`.** A second bar was kept as written:

  ```
        _ExtentX        =   10583
        _ExtentY        =   661
        Style           =   1
        SimpleText      =   "simple"
        ShowTips        =   0   'False
        _Version        =   393216
  ```
- **Double-clicked from the toolbox** onto a 6000×4500 form, a StatusBar is written as `Align = 2 'Align Bottom`,
  Height 375, Left 0, Width 6000, with one empty panel. It was at Top 2055, mid-form, in the designer. That Top is
  pixel-centred and isn't yet laid out to the bottom.

### The Properties window

Twenty-five rows:
- (About), (Custom), (Name), Align, DragIcon, DragMode, Enabled, Font, Height, Index, Left;
- MouseIcon, MousePointer, Negotiate, OLEDropMode, ShowTips, SimpleText, Style;
- TabIndex, Tag, ToolTipText, Top, Visible, WhatsThisHelpID and Width.

There's no TabStop row, as there's no TabStop member.

### In HexIDE

#1087 part A implements MSComctlLib.StatusBar registration and its Panels/IPanel object model. The default member is
Text; fixed Width/MinWidth and the measured collection/property errors are covered by headless interpreter tests.
The first part B slice implements the newly measured Integer Count/Index and pins the other measured member types.
Spring widths share extra whole pixels above their minimums, keep the 19 px grip, and fall back to minimums on overflow;
fixed overflow and later panel positions are covered headlessly.
Saved panel blocks now load their measured values, with HIMETRIC lengths converted to twips. Panel picture records survive load/save byte for byte and now supply runtime objects. Panels accept another picture, a LoadPicture BMP/ICO file, or Nothing, and draw the picture in normal style. Picture submembers and TypeName refuse until their return types are measured; the recorded numeric values alone do not pin their types.
The rest of part B draws panels, key indicators, time/date, SimpleText and the grip, with the measured mouse/panel event order. Exact text inset/clipping remains unmeasured rendering. Nonempty Contents sizing still refuses rather than guessing a program-visible width; the measured StatusBar forms and picture companions round-trip byte for byte. Non-Windows key indicators follow keys seen by the form, without an initial host lock-state query.
The `statusbar` and `statusbar-picture` saves are now in `corpus/inbox-controls/saved/`. Their raw panel blocks retain TextSave snapshots, field order and picture citations; runtime panel edits do not change the designer model.
