## In-box controls: the Toolbar (2026-10-26, #1088)

**The probes.** VB6 SP6 (6.00.9782), with MSCOMCTL.OCX 6.01.9782, the library and `Object=` line of [In-box controls: the
ProgressBar](in-box-controls-the-progressbar.md). Every build was made with `/make` as native code and as p-code, and
the two logged the same lines. The one exception is the junk Left and Top an invisible button reads back, given below.
- **Run time:** a Sub Main showing a 6000×4500-twip form holding VB6's double-clicked Toolbar, a PictureBox and an
  ImageList. The ImageList is VB6's save of three pictures, red, blue and ico, from [In-box controls: the
  ImageList](in-box-controls-the-imagelist.md). It was driven late-bound under `On Error Resume Next`, as for the
  StatusBar, in session 0.
- **Re-run on the console (2026-11-18, #4768):** the size rows below were rebuilt into one probe and run on the
  guest's console, in the IDE (`/runexit`), p-code and native, and again in session 0, which reproduced them. The
  console's values sit beside session 0's. They follow the rules the later sections measured on the console, Segoe
  UI 9 captions and a 15 px dropdown arrow, and the bar's height depends on its Align, below. HexIDE's live sizes are
  #4802.
- **The files:** a hand-written form holding two configured Toolbars was opened in VB6's IDE, made dirty and saved. The
  second Toolbar's ShowTips was then set to False in the Properties window, by double-clicking its value, and saved
  again. VB6's save is `corpus/inbox-controls/saved/toolbar`.
- **The saved form run again:** the hand-written form and VB6's save were each built and run, reading every property
  back. Their logs were the same.
- **Events:** VB6's save, with event handlers added, run on the guest's console. The agent clicked each button with real
  mouse clicks and logged every event.

### The control

- **Its members,** from the type library:
  - Appearance, AllowCustomize, BorderStyle, ButtonHeight, ButtonWidth, Enabled, ShowTips, Style, TextAlignment,
    Wrappable;
  - ImageList, DisabledImageList, HotImageList, Buttons, Controls;
  - HelpContextID, HelpFile, MousePointer, MouseIcon, OLEDropMode, hWnd;
  - the methods Refresh, Customize, SaveToolbar, RestoreToolbar and OLEDrag.
- **No Font:** reading `Toolbar1.Font` is 438.
- **Its events:**
  - ButtonClick(Button), ButtonMenuClick(ButtonMenu) and ButtonDropDown(Button);
  - Change, Click, DblClick, MouseDown, MouseMove, MouseUp;
  - the OLE drag events.
- **Buttons** has Count, `Add([Index], [Key], [Caption], [Style], [Image])`, Item, Remove and Clear.
- **A Button** has Caption, Description, Enabled, Height, Image, Index, Key, Left, MixedState, Style, Tag,
  ToolTipText, Top, Value, Visible, Width and ButtonMenus. **Its default member is Caption**: `v = Buttons(1)` reads it,
  and `Buttons(1) = "x"` sets it.
- **ButtonMenus** has Count, `Add([Index], [Key], [Text])`, Item, Remove and Clear.
- **A ButtonMenu** has Enabled, Index, Key, Parent, Tag, Text and Visible. Its default member is Text.
- **`TypeName`:**
  - the control is `Toolbar`;
  - its collections and members are `IButtons`, `IButton`, `IButtonMenus` and `IButtonMenu`;
  - Controls is `IControls`.

### Defaults

| Read | Logged |
|---|---|
| Appearance, BorderStyle, Style, TextAlignment | `1`, `0`, `0`, `0`, Longs |
| AllowCustomize, Enabled, ShowTips, Wrappable | `True` each |
| ButtonWidth, ButtonHeight | 345.2604 and 569.7643, Singles: the `.frm`'s 609 and 1005 HIMETRIC |
| HelpContextID, MousePointer, OLEDropMode | `0`, Longs |
| HelpFile | empty |
| Align | `1`, an Integer: top |
| Height, Width | 660 and 6000 |
| ImageList, DisabledImageList, HotImageList | `Nothing` |
| Buttons.Count, Controls.Count | `0`, an Integer, and `0` |
| TabStop, Index | 438, 343 |

**A new button**, from `Add(, "a", "Alpha")`:

| Property | Value |
|---|---|
| Index | 1, an Integer |
| Caption, Key | as given |
| Tag, ToolTipText, Description | empty |
| Enabled, Visible | True |
| MixedState | False |
| Style, Value | 0, Longs |
| Image | 0, an Integer |
| Left, Top | 0 and 30.04725 |
| Width, Height | 510.2362 and 540.2835 in session 0; **569.7638 and 569.7638**, 38 × 38 px, on the console |
| ButtonMenus.Count | 0 |

The bar's ButtonWidth and ButtonHeight became the button's, 510.2362 and 540.2835. Its Height went from 660 to 630.
On the console both became 569.7638, and Height stayed 660. This bar is `Align = 1`, at the top, and **the bar's height
depends on Align.** Three bars side by side on the console (2026-11-18, #4802), the IDE, p-code and native agreeing,
one "Alpha" button each:

| Bar | Style 0: Height, the button's Top | Style 1 |
|---|---|---|
| `Align = 0`, none | 630 and 30.04725: the 38 px buttons + 4 px | 570 and 0: + 0 |
| `Align = 1`, top, or `2`, bottom | 660 and 30.04725: + 6 px | 600 and 0: + 2 |

The `Align = 0` rows are what #2373 measured on its unaligned bars. In session 0, with 36 px buttons, every Style 0 bar
was 630, 6 px over its buttons whatever its Align, and Style 1 was 540 unaligned and 570 aligned.

### Buttons

The same family of errors as the StatusBar's Panels:
- **Add:**
  - a duplicate key is **35602**;
  - a key that reads as a number, `"5"`, is **35603**;
  - Index 0 or past Count + 1 is **35600**;
  - a Style past 5 is **380**;
  - an Image with no ImageList set is **35613**, "ImageList must be initialized before it can be used".
- Add takes an empty key, inserts at an Index, and takes no arguments at all, giving Style 0 and an empty Caption.
- **Lookup:**
  - `Buttons("missing")` and `("5")` are 35601;
  - `("")` is 35603;
  - `(0)` and `(99)` are 35600;
  - `(1.5)` and `(2.5)` are both 2.
- **Remove and Clear:** the same errors. Clear leaves no buttons, and Height stays 630 (660 on the console).
- **Read-only:** `Buttons.Count =` and `Set Buttons =` are 383.

**Each Style's width,** for buttons whose captions fit the bar's ButtonWidth of 510.2362:

| Style | Width |
|---|---|
| 0 default, 1 check, 2 button group | 510.2362; 569.7638 on the console |
| 3 separator | 120.189, 8 px, on the console too |
| 4 placeholder | 0, until its Width is set |
| 5 dropdown | 705.2599: the button and a 13 px arrow. On the console 794.8347, 53 px: 38 and a 15 px arrow |

### A button's properties

| Assignment | Logged |
|---|---|
| Style 0 to 5, Value 0 and 1 | no error. Style 6, and Value 2 or 3, are **380**, and the property is left as it was |
| `Index =`, `Left =`, `Top =`, `Height =` | **383** |
| `Width =` | 383, except on a placeholder, where it is set and the buttons after it move |
| `Key =` another's key | 35602 |
| `Caption = 42` | `42`, a String |
| `MixedState = True`, `Enabled = False` | no error |
| `Visible = False` | the button takes no space. The buttons after it move up into its place, and its own Left and Top read as junk, different between the native and p-code builds (3816850 and 3562380, -4047840 and 3816850), with Width and Height 0 |
| `Set Tag = Toolbar1` | **35700** |

**Check and button group:**
- A check button's Value can be set to 1.
- Setting a group button's Value to 1 clears the one before it in its group.
- Setting the pressed one to 0 leaves none pressed.

### ImageList

| Call | Logged |
|---|---|
| `Set ImageList = Picture1` | **35610**, "Invalid object", and ImageList stays Nothing |
| `ImageList = "ImageList1"`, a Let of a name | **13** |
| `Set ImageList = ImageList1` | no error |
| `Image = 1`, `Image = "blue"` | read back as given: `1` an Integer, `blue` a String |
| `Image = 99`, `Image = "missing"` | 35600 and 35601, and Image is left as it was |
| `Image = 0` | no error |

**While a Toolbar is bound to it, an ImageList can grow but not shrink:**
- `ListImages.Add` works.
- `ListImages.Remove`, of any image, and `ListImages.Clear` are **35617**, "ImageList cannot be modified while another
  control is bound to it".
- MaskColor can be set.
- ImageWidth is 35611, because the list holds images.

**Unbinding:**
- `Set Toolbar1.ImageList = Nothing` works, and **every button's Image becomes 0**.
- The ImageList can then lose images.
- `Image = 1` is 35613 while no ImageList is set.
- Binding the ImageList again leaves Image at 0.

### ButtonMenus

- `ButtonMenus.Add(, "m1", "Menu one")` on a dropdown returns Index 1, with Parent the button. It starts Enabled and
  Visible, with an empty Tag.
- A duplicate key is 35602, `"7"` is 35603, and Index 99 is 35600.
- **A button that isn't a dropdown takes ButtonMenus too,** with no error.
- A menu's Key can be set to `""`, and Remove works by index.

### The bar

| Assignment | Logged |
|---|---|
| Style 0 and 1, TextAlignment 0 and 1 | no error. 2 is **380** for each |
| `TextAlignment = 1`, captions to the right | ButtonHeight became 329.9528, 22 px. **On a bar that has never had a button it stays 569.7643 until the first `Add`**, which makes it 329.9528 (#4768, session 0 and the console alike). "Alpha" is then 870.2363 wide on the console, 58 px, and 810.1418 in session 0, 54, and the bar 420 high, 28 px, in both |
| `ButtonWidth = 1000`, `= 10`, `= 0`, `= -5`, `ButtonHeight = 800` | **no error, and no effect.** They read back the size the buttons' contents need, 390.0473 and 540.2835 by then. On the console, with one "Alpha" button, 569.7638 and 569.7638 (#4768) |
| `Height = 2000` | no error. It reads 630: the bar sizes itself. 660 on the console (#4768) |
| `Style = 1` at run time, with one "Alpha" button (#4768) | no error. Height 570 in session 0 and 600, 40 px, on the console: 2 px more than the buttons, for this top-aligned bar, with the buttons' Top 0. With `Align = 0` the bar is the buttons' height exactly, as #2373 records (the Align table above) |
| `Wrappable = False`, `BorderStyle = 1` | no error. Height stays 630 |

### Events

Real clicks on the console, in the order each fired:

| Action | Events |
|---|---|
| Click on a button | MouseDown (button 1), MouseUp, Click, **ButtonClick** |
| Double-click on it | MouseDown, MouseUp, Click, ButtonClick, **ButtonClick**: no DblClick, and the second press raises only ButtonClick |
| Click on a pressed check button, then again | each: MouseDown, MouseUp, Click, ButtonClick, with Value already the new one, 0 and then 1 |
| Click on a group button that isn't pressed | MouseDown, MouseUp, Click, ButtonClick with Value 1 |
| Click on the group button that is pressed | MouseDown, MouseUp, Click: **no ButtonClick** |
| Click on a separator, a placeholder, a disabled button or the bar past the buttons | MouseDown, MouseUp, Click: no ButtonClick |
| Right-click on a button | MouseDown (button 2), MouseUp, Click: no ButtonClick |
| Click on a dropdown's face | MouseDown, MouseUp, Click, ButtonClick |
| Click on a dropdown's arrow | MouseDown, **ButtonDropDown**, and its menu opens. No MouseUp, Click or ButtonClick |
| Click on a menu item, or Down and Enter | **ButtonMenuClick** alone, with the ButtonMenu's Parent its button |
| A click away from the open menu | nothing |

- **ButtonClick comes after Click,** where the StatusBar's PanelClick comes before MouseUp.
- **A menu item that isn't Visible isn't shown.** The menu listed only "First".
- MouseDown's x and y are twips from the bar's top-left.
- Change didn't fire: it needs the Customize dialog, which wasn't opened.

### The files

VB6's save of the hand-written form, with the order it put each line in:

```
   Begin MSComctlLib.Toolbar Toolbar1 
      Align           =   1  'Align Top
      Height          =   660
      ...
      _ExtentX        =   10583
      _ExtentY        =   1164
      ButtonWidth     =   1005
      ButtonHeight    =   1005
      Appearance      =   1
      ImageList       =   "ImageList1"
      _Version        =   393216
      BeginProperty Buttons {66833FE8-8583-11D1-B16A-00C0F0283628} 
         NumButtons      =   9
         BeginProperty Button1 {66833FEA-8583-11D1-B16A-00C0F0283628} 
            Caption         =   "New"
            Key             =   "new"
            Description     =   "Makes one"
            Object.ToolTipText     =   "New file"
            Object.Tag             =   "t1"
            ImageIndex      =   1
         EndProperty
         BeginProperty Button2 {66833FEA-8583-11D1-B16A-00C0F0283628} 
            Key             =   "bold"
            ImageIndex      =   2
            Style           =   1
            Value           =   1
         EndProperty
         ...
         BeginProperty Button6 {66833FEA-8583-11D1-B16A-00C0F0283628} 
            Key             =   "ph"
            Style           =   4
            Object.Width           =   1500
         EndProperty
         BeginProperty Button7 {66833FEA-8583-11D1-B16A-00C0F0283628} 
            Caption         =   "Menu"
            Key             =   "drop"
            ImageIndex      =   3
            Style           =   5
            BeginProperty ButtonMenus {66833FEC-8583-11D1-B16A-00C0F0283628} 
               NumButtonMenus  =   2
               BeginProperty ButtonMenu1 {66833FEE-8583-11D1-B16A-00C0F0283628} 
                  Key             =   "m1"
                  Text            =   "First"
               EndProperty
               BeginProperty ButtonMenu2 {66833FEE-8583-11D1-B16A-00C0F0283628} 
                  Enabled         =   0   'False
                  Object.Visible         =   0   'False
                  Key             =   "m2"
                  Object.Tag             =   "mt"
                  Text            =   "Second"
               EndProperty
            EndProperty
         EndProperty
         BeginProperty Button8 {66833FEA-8583-11D1-B16A-00C0F0283628} 
            Enabled         =   0   'False
            Caption         =   "Off"
            MixedState      =   -1  'True
         EndProperty
         BeginProperty Button9 {66833FEA-8583-11D1-B16A-00C0F0283628} 
            Object.Visible         =   0   'False
            Caption         =   "Hidden"
         EndProperty
      EndProperty
   End
   Begin MSComctlLib.Toolbar Toolbar2 
      ...
      ButtonWidth     =   609
      ButtonHeight    =   1005
      ToolTips        =   0   'False
      AllowCustomize  =   0   'False
      Wrappable       =   0   'False
      Appearance      =   1
      HelpContextID   =   7
      Style           =   1
      TextAlignment   =   1
      _Version        =   393216
      BorderStyle     =   1
   End
```

- **ShowTips is written as `ToolTips`.**
  - The hand-written `ShowTips = 0 'False` was ignored without a message: the form ran with ShowTips True, and VB6's
    save dropped the line.
  - Set to False in the Properties window, it was saved as `ToolTips = 0 'False`, after ButtonHeight.
  - A control's property bag ignores a name the control doesn't read, so a wrong name costs the value and gives no
    error.
- **BorderStyle is written after `_Version`**, the one property that is.
- **The ImageList is bound by name,** `ImageList = "ImageList1"`, as VB6's own Web Browser form template has it.
- **ButtonWidth and ButtonHeight are HIMETRIC.** The designer rewrote ButtonWidth 609 as 1005 to fit the first
  Toolbar's images and captions. The second Toolbar, with captions to the right and no buttons, was saved 600 high
  rather than 660, at Top 3900.
- **A placeholder's `Object.Width` is twips.** 1500 ran as Width 1500. A StatusBar panel's `Object.Width` is
  HIMETRIC.
- **A button's Image is written as `ImageIndex`,** its position in the ImageList. At run time Image reads that Integer.
  Whether a design-time Image given by key is written as a key wasn't tried.
- **A button's lines, in the order VB6 wrote them:** Enabled, `Object.Visible`, Caption, Key, Description,
  `Object.ToolTipText`, `Object.Tag`, ImageIndex, Style, Value, `Object.Width`.
  - MixedState came after Caption. No button held every property, so MixedState's place against Key and the rest isn't
    pinned.
  - A button's ButtonMenus group comes after its own lines.
- **A ButtonMenu's lines:** Enabled, `Object.Visible`, Key, `Object.Tag`, Text.
- **The groups are named by their coclasses' GUIDs,** read from the type library: Buttons `{66833FE8-…}`, Button
  `{66833FEA-…}`, ButtonMenus `{66833FEC-…}` and ButtonMenu `{66833FEE-…}`, each `-8583-11D1-B16A-00C0F0283628`.
- **Run, the save gave every property back as written,** ShowTips aside. The Toolbar1 buttons' Images were 1, 2 and 3,
  as Integers. An invisible ButtonMenu read Visible False.
- **Double-clicked from the toolbox,** a Toolbar is written with Align 1, Height 660, Top 0, Width 6000, extents 10583
  by 1164, ButtonWidth 609, ButtonHeight 1005 and Appearance 1. Appearance is written even at its default.

### Caption widths (2026-10-27)

**The probe:**
- **The form:** twenty Toolbars from hand-written blocks with `Align = 0 'None`, 8000 twips wide, in a shown form.
- **The cases:** buttons were added from code, and ButtonWidth, ButtonHeight and each button's size read back in
  pixels.
- **The comparison:** each caption was also measured with the form's TextWidth in three candidate fonts. The Toolbar has
  no Font property.
- **The setting:** Windows 10 at 96 dpi, without visual styles. Native and p-code logged the same.

**The rule, for captions below the images:** a button is **the caption's width in Segoe UI 9 plus 7 pixels, and at
least 23**.

| Caption | Text width: MS Sans Serif 8 / Tahoma 8 / **Segoe UI 9** | Button width |
|---|---|---|
| `""`, `A`, `i`, `W`, `OK`, `Ab` | up to 16 in Segoe UI | **23**, the minimum |
| `File` | 16 / 16 / **18** | **25** |
| `Alpha` | 27 / 27 / **31** | **38** |
| `Save As` | 40 / 39 / **40** | **47** |
| `Alphabet` | 42 / 43 / **48** | **55** |
| `iiiiiiii` | 16 / 16 / **24** | **31** |
| `MMMMMMMM` | 72 / 64 / **88** | **95** |
| `A much longer caption here` | 130 / 131 / **148** | **155** |

**Segoe UI 9 fits every row exactly, and neither 8-point font fits any two.** MMMMMMMM would need 23 pixels of padding
in MS Sans Serif and 31 in Tahoma, where `Alpha` needs 11.

**This reconciles the 510.2362 above.** The first probes in this entry ran in session 0, not on a logged-on desktop.
There `Alpha` made a button 34 pixels wide, which is its 27 pixels in an 8-point font plus the same 7. So the caption
font is the system's: Segoe UI 9 on the Windows 10 desktop, an 8-point font in session 0. **That is inferred from the
fit,** not measured again in session 0. A reader on another
configuration should measure the caption in the system's font, not in a fixed one.

**With TextAlignment 1, captions to the right:** a button is **the caption's width in Segoe UI 9 plus 27 pixels, and at
least 43**. That held the same with an image bound and shown and with none.

| Caption | Segoe UI 9 | Button width |
|---|---|---|
| `""`, `i`, `A`, `OK` | up to 16 | **43**, the minimum |
| `Alpha` | 31 | **58** |
| `Save As` | 40 | **67** |
| `Alphabet` | 48 | **75** |
| `MMMMMMMM` | 88 | **115** |
| `A much longer caption here` | 148 | **175** |

**Heights:**
- **A bar with any caption** has buttons 38 high, or 22 with TextAlignment 1.
- **A bar with no caption at all** has buttons 22 high, image or no image.
- An image alone gives 23 × 22.
- The bar is the buttons' height plus 4 for Style 0, and their height for Style 1, flat.

**Every button on a bar has the widest button's width.** Mixed captions `A`, `Alphabet`, `""` and `OK` gave four
buttons of 55 each, at Left 0, 55, 110 and 165, or four of 75 with TextAlignment 1. With images, `A`, `Alpha` and
`MMMMMMMM` gave three of 95.

**The width follows the captions as they change:**
- `A` gave 23. `Caption = "Alphabet"` gave 55, and `Caption = "A"` again gave 23.
- Adding an `Alphabet` button widened both buttons to 55. Removing it narrowed the first back to 23.
- **After `Buttons.Clear`, ButtonWidth and ButtonHeight keep their last values,** 23 and 38, and so does the bar's
  height. A button with no caption added then brought the height down to 22.

**Captions with special characters:**
- **`&File` drew as "File" with the F underlined, but was measured with the ampersand,** at 35 rather than `File`'s 25.
  That fits the rule: `&File` is 28 pixels in Segoe UI 9.
- **A tab and a line break drew as nothing,** "Tabx" and "Twolines", on one line.
- **Neither fits the rule through the form's TextWidth.** TextWidth expands a tab to a tab stop and measures a broken
  caption by its widest line, so `Tab` vbTab `x` read 54 and `Two` vbCrLf `lines` 24, where the buttons were 40 and 62.
- **Measured against the same captions with the characters left out:**

  | Caption | Segoe UI 9 | Button |
  |---|---|---|
  | `Tabx` | 24 | 32 |
  | `Tab` vbTab `x` | | **40**: 8 more |
  | `Twolines` | 45 | 53 |
  | `Two` vbCrLf `lines` | | **62**: 9 more |
  | `Two` vbLf `lines` | | **61**: 8 more |
  | `Two` vbCr `lines` | | **54**: 1 more |

  **So the bar measures a caption on one line, with each control character taking a width of its own:** about 8
  pixels for a tab or a line feed, and 1 for a carriage return. That is measured, not explained.
- **`Tabx` and `Twolines` came out one pixel above the rule,** 32 and 53 where 31 and 52 were expected. The eight
  captions in the first table fit it exactly. Rounding in the TextWidth comparison may account for it. An
  implementation that measures with the system font itself, rather than through TextWidth, should land within a pixel.

**Styles, on a flat bar, with the caption `Alpha`:**
- default and check: 38;
- **dropdown: 53**, the button and a 15-pixel arrow;
- separator: 8.

**A bar narrower than its buttons:**
- **Wrappable True, the default:** six 55-pixel buttons on a 133-pixel bar went into **three rows of two**. Their Tops
  were 2, 40 and 78, and the bar's Height grew to 118, three rows of 38 plus 4.
- **Wrappable False:** the same six went back into one row, at Left 0 to 275, past the bar's right edge. They kept
  Visible True. The bar's Height went back to 42.
- With Wrappable False from the start, Width 2000 or 1000 left the six in one row. Wrappable True with Width 8000 gave
  one row again.

### Drawing a bound ImageList (2026-10-27, #2334)

**The probe:**
- **The form:** eight Toolbars from hand-written blocks with `Align = 0 'None`, stacked down a shown form, and six
  ImageLists filled at run time.
- **The images:** each is a solid colour with a black one-pixel border and a square of the mask colour, `&HC0C0C0`,
  in its top-left corner, a quarter of its width. The corner shows where an image sits and whether it was masked.

  | List | Images |
  |---|---|
  | IL16 | red, yellow, cyan, 16 × 16 |
  | IL32 | the same at 32 × 32 |
  | ILHot | green, dark green: **no third image** |
  | ILDis | blue, navy: **no third image** |
  | ILEmpty | none |
  | ILNoMask | as IL16, filled after `UseMaskColor = False` |

- **The buttons:** each bar got four, from Add: "One" with image 1, no caption with image 2, "Three" with image 3, and
  "Four" with no image.
- **The measurements:** sizes from code, and screenshots at 96 dpi, without visual styles. A driver on the guest's
  console moved the mouse. Native and p-code logged and drew the same.

**Button size follows the image size and TextAlignment:**

| Bar | ButtonWidth × ButtonHeight | In pixels | Bar Height |
|---|---|---|---|
| no buttons, no list | 360.0005 × 329.9533 | 24 × 22 | 600, as written |
| IL16, captions below | **540.2835 × 569.7638** | **36 × 38** | 630 |
| **no list, the same captions** | **540.2835 × 569.7638** | **36 × 38** | 630 |
| IL16, **TextAlignment 1**, captions right | **840.189 × 329.9528** | **56 × 22** | 390 |
| **IL32**, captions below | **585.0709 × 810.1418** | **39 × 54** | 870 |
| IL16, Style 1, flat | 540.2835 × 569.7638 | 36 × 38 | **570**, the buttons at Top 0 rather than 30 |

- Every button on a bar has the bar's size, with or without its own caption or image.
- **A bar with captions and no list is already sized for a 16 × 16 image.** Binding IL16 to it afterwards changed nothing,
  and setting its buttons' images then drew them in the space it had.

**Where the image sits,** measured from the button's top-left corner, the border included:

| Bar | Button | Image | Caption |
|---|---|---|---|
| IL16, captions below | 36 × 38 | 16 × 16 at **10, 3**: centred across, 3 down | below it, its text 23 to 31 down |
| IL16, TextAlignment 1 | 56 × 22 | 16 × 16 at **5, 3** | to its right. "One"'s text ran from 34 to 51 across, ending 4 pixels from the right edge |
| IL32, captions below | 39 × 54 | 32 × 32 at **3, 3** | below it, its text 39 to 47 down |

- **A button with no image keeps the image's space.** "Four" drew its caption where the others did, below or to the
  right of an empty square.
- The button's raised edge is two pixels: white and 227 at the top and left, 160 and 105 at the bottom and right.

**UseMaskColor:**
- **With UseMaskColor True,** the corner drew in the button face, 240: **masked**.
- **ILNoMask, filled with UseMaskColor False,** drew the corner in 192: **not masked**.
- **`IL16.UseMaskColor = False` after binding** unmasked every bar bound to IL16 at its next paint. TA was refreshed, but
  the corner showed 192 on the other bars too. IL32's bar stayed masked.
- **`ILNoMask.UseMaskColor = True` after binding** wasn't read. The resized bar above it covered that bar by then.

**HotImageList and DisabledImageList:**

| Button | Standard bar, Style 0 | Flat bar, Style 1 |
|---|---|---|
| 1, enabled, image 1 | red, **unchanged under the mouse** | red; **under the mouse, green: ILHot's image 1**, with a raised edge |
| 2, disabled, image 2 | **navy: ILDis's image 2** | navy, unchanged under the mouse |
| 3, disabled, image 3, which ILDis lacks | **no image**, and the caption embossed grey | the same |
| 4, enabled, image 3, which ILHot lacks | cyan, unchanged under the mouse | cyan; **under the mouse, no image at all**, with a raised edge |

So **only a flat bar draws hot images.** **A disabled button draws DisabledImageList's image, and a missing index in
either list draws nothing,** not the normal image.

**Binding and rebinding:**
- **`Set ImageList = ILEmpty`, an empty list, raises 35613,** "ImageList must be initialized before it can be used",
  and the bar stays unbound. A button's `Image = 1` then raises 35613 too.
- **Replacing a bound list,** `Set ImageList = IL32` on a bar bound to IL16 with images set, raised nothing.
  - **Every button's Image went to 0,** as unbinding does.
  - The buttons grew to 39 × 54 at once, before any image was set again.
  - Setting the images again drew IL32's.

### The Properties window

Thirty-two rows:
- (About), (Custom), (Name), Align, AllowCustomize, Appearance, BorderStyle, ButtonHeight, ButtonWidth, DragIcon,
  DragMode;
- Enabled, Height, HelpContextID, HelpFile, Index, Left, MouseIcon, MousePointer, Negotiate, OLEDropMode, ShowTips;
- Style, TabIndex, Tag, TextAlignment, ToolTipText, Top, Visible, WhatsThisHelpID, Width and Wrappable.

ImageList, DisabledImageList, HotImageList and the buttons aren't rows. They are set in the property pages, which
weren't opened.

### Caption widths (#2373)

Measured in #1088, issuecomment-6445245253 (corrected; d4593637e): vb6.exe SP6,
MSCOMCTL.OCX 6.01.9782, native and p-code matching, Windows 10 console at 96 dpi without visual styles.
The host message font was Segoe UI 9. Caption widths below are single-line text extents; `&` counts.
The rule is extent +7, minimum 23 px below, or +27, minimum 43 px right.

| Caption | Segoe UI 9 extent (px) | Button below (px) |
|---|---:|---:|
| File | 18 | 25 |
| Alpha | 31 | 38 |
| Save As | 40 | 47 |
| Alphabet | 48 | 55 |
| iiiiiiii | 24 | 31 |
| MMMMMMMM | 88 | 95 |
| A much longer caption here | 148 | 155 |
| &File | 28 | 35 |
| Tabx | 24 | 32 |
| Twolines | 45 | 53 |

The last two exceed +7 by one pixel, unexplained. Do not special-case them to conceal that.
Tabs and LF add 8 px each; CR adds 1 px. None draws: Tab-tab-x measured 40 px,
Two-CRLF-lines 62, Two-LF-lines 61, Two-CR-lines 54. TextWidth's tab stops and
multiline measurement do not describe these captions. `&File` displays File with the F underlined.
The earlier Alpha dimensions came from session 0; the system-font explanation for that difference is inferred.

All buttons share the widest caption width, recomputed after edits/add/remove. Clear retains the last
ButtonWidth, ButtonHeight and bar height. Any caption gives 38 px button height below, otherwise 22 px;
right alignment gives 22 px. Style 0 adds 4 px to the bar height and starts buttons at 2 px;
flat Style 1 starts at 0 and adds nothing. Flat Alpha dropdowns measured 53 px (38 +15), separators 8 px.
Six 55 px buttons on a 133 px bar wrapped into three rows of two, at Tops 2/40/78 and bar height 118.
Wrappable False kept one row extending past the right edge, all Visible True, bar height 42.

### Windows caption metric follow-up (#2334, supervisor measurement, 2026-10-28)

GDI was measured on Windows with WinForms TextRenderer, Segoe UI 9 pt at 96 dpi,
without padding. Whole-string and summed-character extents were:

| Caption | Whole string (px) | Sum of characters (px) |
|---|---:|---:|
| OK | 16 | 16 |
| iiiiiiii | 24 | 24 |
| Save As | 40 | 40 |
| MMMMMMMM | 88 | 88 |
| Alphabet | 48 | 48 |
| A much longer caption here | 148 | 148 |
| Twolines | 46 | 47 |

Whole-string GDI extents match the measured button widths. This supersedes the
per-glyph rounding model: Skia's rounded advances give 147 for the long caption,
and character sums also miss Twolines. The earlier TextWidth figure of 45 for
Twolines remains recorded above; the whole-string extent of 46 accounts for its
53 px button without a caption-specific correction. Windows uses GetTextExtentPoint32W
with a font at its 96 dpi pixel height; other hosts retain per-glyph rounding.

The console widths above used Windows 10 19044's Segoe UI (`segoeui.ttf`, 955,804 bytes,
SHA-256 `ba32a222b23d727267cf1aba4e5296fe84ce99b9d910915103fc085d7931bc88`).
The supervisor repeated GetTextExtentPoint32W with Segoe UI, height -12 and default quality
on that VM and on Server 2025 build 26100 (2026-10-28):

| Caption | Oracle VM (px) | Server 2025 (px) |
|---|---:|---:|
| Tabx | 25 | 25 |
| Twolines | 46 | 47 |
| OK | 16 | 16 |
| MMMMMMMM | 88 | 88 |
| A much longer caption here | 148 | 148 |

Server 2025's font was 959,752 bytes, SHA-256 prefix `8134DBCD09E7B123…`.
The installed font build changes the metrics: Twolines therefore gives a 54 px button
there, versus the oracle's 53 px. Exact console expectations apply only to the oracle
font digest; Windows sizing follows the installed font's GDI extent on other builds.

### In HexIDE

The runtime-only portions of #1088 model a top-level MSComctlLib.Toolbar's scalar defaults and measured
assignments, including no-effect size writes, and its Buttons/ButtonMenus collections and item scalar
properties. #2373 measures captions on one line in the host font, using the later console measurement's
padding/minima, shared widths, caption-driven height, Clear retention and wrapping/overflow rules.
Tabs/LF/CR add their measured widths and do not draw; ampersands contribute to width and mark mnemonics.
The ten oracle extents are pinned, including the two one-pixel discrepancies, without caption-specific fixes.
Non-default-style item Top/Height remain refused. Hidden Left/Top
refuse rather than reproduce build-dependent junk; hidden Width/Height remain zero. Unmeasured conversions
refuse. The designer still carries every Toolbar block verbatim. Nested and control-array bars retain the load
refusal. Designed top-level bars now load their Buttons and ButtonMenus property bags, including the measured Object.* aliases, numeric ImageIndex values, ToolTips and
twips placeholder widths. Saved HIMETRIC button dimensions are not applied as runtime size assignments;
runtime geometry still uses only the measured content cases. Runtime drawing and the measured pointer/dropdown
menu events are implemented;
#2334 adds the runtime ImageList binding slice: populated-list binding, index/key Image validation (including
Buttons.Add), unbinding/reset and the bound-list Remove/Clear lock. HotImageList and DisabledImageList read
the measured Nothing defaults; populated-list assignments now support the measured drawing probe. Empty-list binding raises 35613; replacing a bound list resets every Image
to Integer 0 and recomputes geometry immediately, releasing the old list's shrink lock. The [#4147 measurements](https://github.com/hexide-io/HexIDE/issues/4147) add numeric CInt/Empty selectors, case-sensitive keys and the measured selector errors. Boolean False selectors remain refused: #4147 measured True only. Saved top-level bars now resolve their
named populated ImageList after controls are created, including when the list follows the bar in the file,
retaining saved Integer selectors and preserving both files byte for byte. Missing, ambiguous and wrong-type
saved references refuse rather than guess an unmeasured result. The later #2334 drawing measurement supplies
16 px captioned buttons at 38 px high below or 22 px right, and 32 px captioned buttons at 54 px high below
with a 39 px minimum width; caption widths retain #2373's rule. Other populated image geometries refuse
pending measurements. Image painting now follows #2334’s recorded offsets and paint-time UseMaskColor; populated alternate lists supply disabled images and flat-only hot images by position, and missing alternate indexes draw nothing. [#4147](https://github.com/hexide-io/HexIDE/issues/4147) adds alternate-list clearing, shorter populated lists and the measured assignment errors (13 for Strings, 380 for empty lists, 35610 for PictureBox). HexIDE deliberately preserves the previous list after refusing PictureBox, rather than reproducing VB6’s clear-on-error side effect: under the Fidelity Principle a refused assignment should leave its target intact. Other alternate geometries and hot/disabled rendering without an alternate list remain unmeasured. No new VB6 measurements were made. Part C verifies the existing verbatim preservation path against the saved
Toolbar form and ImageList companion byte for byte, both unchanged and after a form-caption edit, and through a
second save. This preserves `ToolTips`, BorderStyle after `_Version`, HIMETRIC button dimensions, twips placeholder
widths and the nested ButtonMenus without modelling or rewriting them. The runtime loading slice does not yet
enable editing/rewriting Toolbar property bags in the designer. No new VB6 measurements were made for these portions.

### A button's Image from other types, keys by case, and the Hot and Disabled lists (2026-11-16, #4147)

vb6.exe SP6, `/make`, native and p-code agreeing in every row. **The setup:** a form with an MSCOMCTL `Toolbar1` and four ImageLists, and a 16-pixel PictureBox to paint squares into them at run time.
- `ImageList1` holds 3 images, keys `Img1` to `Img3`, and is bound as `Toolbar1.ImageList`. One button was added with Image 1.
- `ImageList2` holds 3 images, `ImageList3` holds 1, and `ImageList4` is empty.

Each row set `Buttons(1).Image` to 1 first, then to the value under `On Error Resume Next`, and read it back.

#### A button's Image

| Set to | Error | Image afterwards |
|---|---|---|
| `2#`, `CSng(2)`, `CByte(2)`, `CCur(2)`, `CDec(2)` | | Integer 2 |
| `1.6#` | | Integer 2 |
| `2.5#` | | Integer **2**: half to even, as CInt |
| `3E0` | | Integer 3 |
| `0` | | Integer 0 |
| `Empty` | | Integer 0 |
| `True` (-1), `-1` | 35600 | Integer 1, unchanged |
| `4`, past the 3 images | 35600 | Integer 1 |
| `Null` | 35603 | Integer 1 |
| `""` | 35603 | Integer 1 |
| `"Img2"` | | String `Img2` |
| `"IMG2"`, `"img2"` | **35601** | Integer 1: **keys are case-sensitive** |
| `"Nope"` | 35601 | Integer 1 |
| `"2"` | 35601 | Integer 1: a String is always a key, never an index |

`Buttons.Add`'s Image argument follows the same rules: `2#` gives Integer 2, `"IMG3"` is 35601, `-1` is 35600 and `""` is 35603.

#### HotImageList and DisabledImageList

Each step follows the one before it, with Hot and Disabled behaving alike:

| Step | Error | The list afterwards |
|---|---|---|
| `Set … = ImageList2` (Hot) or `ImageList3` (Disabled, 1 image against the bound list's 3) | | that list |
| `… = "x"` | 13 | unchanged |
| `Set … = ImageList4`, empty | **380** | unchanged |
| `Set … = Picture1`, a PictureBox | **35610** | **Nothing**: the failed assignment clears it |
| `Set … = Nothing` | | Nothing |

- **A list with fewer images than the bound ImageList is accepted** (`ImageList3`, 1 against 3).
- **An empty list raises 380 here.** That differs from the bound ImageList, where #2334 measured 35613.
- **A PictureBox raises 35610 and clears the list.** That's an error that also changes state.
- **The button's Image is untouched** by any of these.
- **What resisted a rule:** none. The one surprise is the PictureBox row, which both raises and clears.
- **Not measured:** how a Hot or Disabled list with fewer images draws a button whose image it lacks.

### List bindings by value type, alternate-list geometry, a button's Image by type, and saved references (2026-11-21, #4407, SP6)

**The question.** #4407 collected the image cases #4147 left refused:
- a saved ImageList reference that's missing, ambiguous or of the wrong type;
- binding value types not yet measured;
- alternate lists whose geometry isn't the measured 16 px.

**The probe.** It used the corpus's saved Toolbar form (`corpus/inbox-controls/saved/toolbar/`), whose `Toolbar1` is saved bound to `ImageList1`, a 16 × 16 list of three images. Added to it were an empty `ImageList2` and a `TextBox`. At run time, `ImageList2` was given 32 × 32 and two images from `Form1.Icon`.

Before each binding row, the bar was reset: `ImageList1` bound, both alternate lists `Nothing`, and button 1's `Image` set to 1. Each binding went through `Dim t As Object` to reach its run-time answer. The early-bound forms ran in projects of their own, since some don't compile. Built with `/make` on vb6.exe SP6 with MSCOMCTL.OCX, as native code and as p-code, every project logged the same lines both ways. A second project started each Let and Variant row from an unbound bar, set the bound list again, bound each alternate list before refusing it, and tried halves for Image.

**Binding the lists, late-bound:**

| Assigned | `ImageList` | `HotImageList`, `DisabledImageList` |
|---|---|---|
| `Set t.X = Text1`, `= Toolbar2`, `= New Collection` | **35610**, and ImageList becomes **Nothing**; button 1's Image stays 1 | **35610**; the list stays Nothing |
| `t.X = 5`, `= Empty`, `= Null` | **13**; the list is unchanged | **13** |
| `t.X = ImageList1`, a Let of the list | no error: from unbound, it binds as a Set does | no error: bound |
| `Set t.X = v`, `v` a Variant holding ImageList1 | from unbound, bound | bound |
| `Set t.X = ImageList1`, the list already bound | no error; button 1's Image **stays 1** | not run |
| `Set t.X = ImageList2`, the 32 px list | bound; button 1's Image becomes 0 | **380**; the list stays Nothing |
| `Set t.X = Nothing` | unbound; button 1's Image becomes 0 | unbound |

**Mixed geometry, the other way round:** with ImageList2 (32 px) bound as the ImageList, `Set HotImageList = ImageList1` (16 px) raised **380** too. So an alternate list has to match the bound list's size, either way round.

**Refusing an alternate list that's already bound:** with HotImageList or DisabledImageList bound to ImageList1, `Set` to `Text1` raised 35610 and **cleared it to Nothing**, as ImageList is cleared. `= 5` (13) and `Set` to the 32 px list (380) left it bound to ImageList1.

**Early-bound:**
- `Toolbar1.ImageList = 5` and `Toolbar1.HotImageList = 5` are **compile errors**, "Type mismatch".
- `Toolbar1.ImageList = Empty` and `= Null` compile, and raise **424**, not the 13 they raise late-bound.
- `Toolbar1.ImageList = ImageList1`, a Let, binds from unbound.

**A button's `Image`, by the type assigned** (ImageList1 bound, 3 images):

| Assigned | Err | Image reads |
|---|---|---|
| `False` | 0 | 0 |
| `True` | **35600** | 1, unchanged |
| `#1/2/1900#`, a Date of 3 | 0 | 3 |
| `2@`, `2!`, `CByte(2)`, `CDec(2)` | 0 | 2 |
| `0.5`, `1.5`, `2.5`, `2.6`, `3.4` | 0 | 0, 2, 2, 3, 3: halves go to even |
| `CSng(2.5)`, `CCur(2.5)` | 0 | 2 |
| `"2"` | **35601**: a String is a key, even when it looks like a number | 1 |
| `Empty` | 0 | 0 |

Image always read back as an Integer. Early-bound, `Image = False` gave the same as late-bound.

**A saved reference that doesn't resolve loads without an error, and reading it raises 430.** Each case edited only the bar's `ImageList = "ImageList1"` line:

| Saved | Loading | `Toolbar1.ImageList` | Button 1's Image |
|---|---|---|---|
| `"ImageList1"`, as saved | no error | ImageList1 | 1 |
| `"imagelist1"` | no error | ImageList1: the name's case doesn't matter | 1 |
| `"NoSuchList"` | no error | **430** "Class does not support Automation or does not support expected interface" | 1 |
| `"Text1"` | no error | **430** | 1 |
| `"Toolbar2"` | no error | **430** | 1 |
| `"ImageList1"`, with the list saved as a control array element (`Index = 0`) | no error | **430** | 1 |

The saved button images keep their indexes even when the list doesn't resolve.

**What it shows:**
- **A non-list object is 35610 for all three lists, and clears the list it was assigned to.** That's the PictureBox row above, now seen for every non-list object and for the alternate lists. A number, Empty or Null is 13 late-bound and leaves the list as it was.
- **A Let of a list binds,** late or early.
- **Setting the bound list again keeps every button's Image.** Binding a different list resets them to 0 (above).
- **A list of another size is refused as an alternate,** 380, and leaves the alternate as it was.
- **A button's Image takes any number,** rounded as `CInt` rounds, halves to even. True is -1 and raises 35600.

**Not measured:** drawing, which needs the console. The disabled and hot rendering with no alternate list, or with mixed geometry, is still open.

### A Buttons bag in another order (2026-11-21, #21, design-time, IDE, SP6)

**The question.** #21's findings list asks, as Q15, whether the order of the lines inside an OCX's `BeginProperty` bag
matters to VB6. If it reads a bag by name, keeping a file's own order is courtesy. If it reads by position, it's
required.

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, started elevated and driven on its console. The
input was VB98's own `Template\Forms\Web Browser.frm` and its `.frx`. Its `tbToolBar` holds a `Buttons` bag with
`NumButtons = 6`, then `Button1` to `Button6`, each giving `Key`, `Object.ToolTipText` and `ImageIndex` in that order.
Two one-form projects were opened:
- **A,** the template as shipped;
- **B,** the same file with the bag rearranged three ways at once:
  - each button's three lines in reverse order;
  - `NumButtons` moved from first in the bag to last;
  - the `Button2` block placed before `Button1`, each keeping its own name.

Each project was opened, its designer shown, and VB6 closed. Each time VB6 asked to save the project and the form, as
it does for this template on Windows 10
([An OCX-bearing form, saved by HexIDE and opened by vb6.exe](serialization--file-format-fidelity.md#an-ocx-bearing-form-saved-by-hexide-and-opened-by-vb6exe-2026-11-21-160-sp6)),
and was answered Yes.

**What happened:**
- **Both loaded with no message and no log.** The designer drew the same six buttons in the same order, Back first.
- **The two saves were byte-identical:** `Web Browser.frm`, 8710 bytes each, and the `.frx` too.
- **So VB6 wrote B's bag in its own order,** `NumButtons` first, then `Button1` to `Button6`, each `Key`,
  `Object.ToolTipText`, `ImageIndex`. The file's order was gone.

So the Toolbar reads its `Buttons` bag by name, and a button by its `ButtonN` name, not by where its block sits. A writer
that changes the order inside this bag doesn't change what VB6 loads. The next VB6 save puts the order back anyway.

### Not measured

- **Another control's bag,** such as a ListView's `ColumnHeaders` or a StatusBar's `Panels`. This is one control.
- **A bag whose `NumButtons` disagrees with its blocks,** or one with a block missing.
