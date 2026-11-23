## In-box controls: the FlatScrollBar (2026-10-27, #1097)

**The probes:**
- **The software:** VB6 SP6 (6.00.9782) with MSCOMCT2.OCX 6.01.9782, on Windows 10 at 96 dpi, in the en-GB user locale.
  - The form line is `Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"`.
  - The block is `Begin MSComCtl2.FlatScrollBar`, `_Version` 393216.
- **The form:**
  - FlatScrollBar1, a hand-written block 2400 × 255 with only its position, extents and `_Version`;
  - **HScroll1, VB6's own HScrollBar, the same size**, which went through every step too, for comparison.
- **The programs:** each ran in a Sub Main and logged every value with its type.
  - Every error was read straight after its statement, and each step was followed by `DoEvents`.
  - Each scenario ran on a fresh instance of the form, shown.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.

### The object model

From MSCOMCT2.OCX's type library:
- **`IFlatSB` has** Value, Min, Max, SmallChange and LargeChange, all Integers. It also has:
  - Orientation, Appearance and Arrows;
  - Enabled, MouseIcon, MousePointer and hWnd;
  - Refresh;
  - hidden: `_Value` and AboutBox.
- **Its only events are Change and Scroll.** There are no key, mouse or focus events of its own.

| Enum | Constants |
|---|---|
| OrientationConstants | cc2OrientationVertical 0, cc2OrientationHorizontal 1 |
| FlatScrollBarAppearanceConstants | fsb3D 0, fsbFlat 1, fsbTrack3D 2 |
| ArrowsConstants | cc2Both 0, cc2LeftUp 1, cc2RightDown 2 |

### Defaults, from a block with no properties

| Read | FlatScrollBar | HScrollBar |
|---|---|---|
| Value, Min, Max | 0, 0, 32767 | the same |
| SmallChange, LargeChange | 1, 1 | the same |
| `v = the control` | Value: **Value is the default member** | the same |
| Orientation | **1, horizontal** | 438 |
| Appearance | **1, fsbFlat** | 438 |
| Arrows | 0, cc2Both | 438 |
| MousePointer | 0, **a Long** | 0, an Integer |
| `_Value` | 0 | 438 |
| **TabStop, CausesValidation** | **438** | True, True |
| **ToolTipText** | "" | **438** |

### Where it matches the HScrollBar

Each of these logged the same on both:
- **The range:**
  - Max and Min take any Integer, -32768 to 32767. 32768 and -32769 raise 6, "Overflow".
  - **Min above Max is taken.** With Min 200 and Max 100, Value takes 150 and 199, and 250 raises 380.
- **Value:**
  - outside the range raises 380, "Invalid property value";
  - `"12"` is taken as 12;
  - **12.6 becomes 13**;
  - `"abc"` raises 13;
  - **the same value again raises no Change.**
- **SmallChange and LargeChange** take 1 to 32767. 0 and -1 raise 380, and 32768 raises 6.
- **Change from code is raised during the assignment,** before the statement returns.
- **MousePointer** takes 0 to 3, and -1 raises 380.
- **Enabled** takes 0 and 2.
- **`hWnd = 0`** raises 383, "Set not supported (read-only property)".
- **The size:** Move takes 2400 × 600, 255 × 2400 and 60 × 60, and **0 × 0 gives 30 × 30**.

### Where it doesn't

- **The FlatScrollBar moves Value silently when the range no longer holds it. The HScrollBar raises Change:**

  | Step | FlatScrollBar | HScrollBar |
  |---|---|---|
  | Value 0, then Min −32768 with Max −1 | Value −1, no event | Value −1, **Change** |
  | Value 60, then Max 50 | Value 50, no event | Value 50, **Change** |
  | Value 50, Max 100, then Min 200 | Value 100, no event | Value 100, **Change** |

  With the range reversed, Value went to the nearer end: 50 became 100, not 200.
- **Orientation swaps Width and Height.** On a 2400 × 255 horizontal bar, `Orientation = 0` gave 255 × 2400, and
  `Orientation = 1` gave 2400 × 255 again. 2, 3 and -1 raise 380.
- **Appearance and Arrows take 0 to 2.** 3 and -1 raise 380.
- **There is no TabStop or CausesValidation.** There is a ToolTipText, which the HScrollBar lacks.

### Events, with real input

**The probe:**
- **The setup.** FlatScrollBar1 and HScroll1 each ran 0 to 100, with SmallChange 1, LargeChange 10 and Value 50. Text1
  had the focus.
- **The input.** A driver on the guest's console clicked and dragged each bar for real. F8 set both Values back to 50
  between steps.
- **The builds.** Native and p-code logged the same lines.

| Action | FlatScrollBar | HScrollBar |
|---|---|---|
| Click the right arrow, then the left | Change 51, Change 50 | the same, **and the bar takes the focus**: LostFocus on Text1, GotFocus |
| Click the track right of the thumb, then left | Change 60; Change 40 | the same |
| Hold the right arrow down for a second | **eleven Changes**, 51 to 61 | the same |
| Drag the thumb 40 pixels right in four steps | **Scroll 50 first**, then Scroll 59, 68, 77 and 86, then Change 86 at the release | Scroll 58, 67, 76 and 85, then Change 85. **No Scroll before the thumb moves** |

**The FlatScrollBar never takes the focus.**
- Clicking it left the focus on Text1.
- **Early-bound, `FlatScrollBar1.SetFocus` is a compile error,** "Method or data member not found". Late-bound it raises
  438.
- **Tab passes it by.** From HScroll1 it went to Text2, then Text1.
- So the keyboard never reaches it. The arrow keys and Page Down moved HScroll1 when it had the focus: KeyDown, then
  Change 86, then Change 96.

**The same 40 pixels moved the two thumbs to different values,** 86 and 85.

### Drawing

**The gallery.** A form held nine FlatScrollBars 1800 × 255 (120 × 17 pixels), one for each Appearance and Arrows. It also
held one turned vertical, one disabled and two HScrollBars. Each bar ran 0 to 100 with Value 30, and the properties were
set at load. Screenshots were taken at 96 dpi, without visual styles, native and p-code alike.

- **The track is a checkerboard** of white and the button face, where the HScrollBar's is plain white.
- **An arrow button is 17 × 16 pixels** with the glyph in black:
  - **fsb3D, 0:** a two-pixel raised edge. Light grey 227 and white are at the top and left, grey 160 and 105 at the
    bottom and right, as on the HScrollBar.
  - **fsbFlat, 1:** **a single grey 160 outline,** with no edge.
  - **fsbTrack3D, 2:** **flat at rest, as fsbFlat.** Under the mouse, the arrow took **a one-pixel raised edge**, white
    at the top and left and grey 160 at the bottom and right. An fsbFlat arrow under the mouse stayed flat.
- **The thumb** is drawn the same way as the arrows: raised for fsb3D, outlined for the other two.
  - **While it is dragged,** the FlatScrollBar draws it as **a solid grey box** at the new position. The HScrollBar
    draws its usual raised thumb there.
- **Arrows doesn't hide an arrow. It disables one:**
  - **cc2LeftUp, 1** draws the right arrow's glyph grey;
  - **cc2RightDown, 2** draws the left arrow's glyph grey.
- **Disabled,** the bar draws both glyphs grey and **no thumb.**
- **Vertical,** the bar has the up and down arrows at the ends, drawn flat for fsbFlat.

### The saved files

**The pass.** A form was written by hand with six FlatScrollBars 2400 × 255 and opened in VB6's IDE.
- **FlatScrollBar1:** minimal.
- **FlatScrollBar2 to 5:** each holds candidate lines in reverse of VB6's order:
  - Min 10, Max 200, Value 30, SmallChange 2 and LargeChange 20;
  - `Orientation = 0`;
  - Appearance 0, Arrows 1, Enabled False and MousePointer 11;
  - Appearance 2 and Arrows 2.
- **FlatScrollBar6:** minimal, with its Properties window read page by page. It has **26 rows**.
- **A seventh,** double-clicked from the toolbox.

The form was dirtied with a comment and saved, and VB6 wrote no load log. VB6's save is
`corpus/inbox-controls/saved/flatscrollbar/`. **A second IDE session, opening the same hand-written form, saved the same
bytes.** The hand-written form and the save were then read at run time, native and p-code, which logged the same values.

**VB6's blocks, as it wrote them:**

```
   Begin MSComCtl2.FlatScrollBar FlatScrollBar2 
      Height          =   255
      Left            =   120
      TabIndex        =   1
      Top             =   620
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   450
      _Version        =   393216
      Arrows          =   65536
      LargeChange     =   20
      Min             =   10
      Max             =   200
      Orientation     =   1638401
      SmallChange     =   2
      Value           =   30
   End
   Begin MSComCtl2.FlatScrollBar FlatScrollBar4 
      …the same lines as far as _Version…
      MousePointer    =   11
      Enabled         =   0   'False
      Appearance      =   65536
      Arrows          =   65537
      Orientation     =   1638401
   End
```

**The order:** MousePointer, Enabled, then Appearance, Arrows, LargeChange, **Min, Max**, Orientation, SmallChange and
Value. That is alphabetical but for Min coming before Max.

**What the lines hold:**
- **Arrows, Orientation and Appearance are written with a high word,** and read back by their low word:

  | Property | Value | Written |
  |---|---|---|
  | Arrows | 0, 1, 2 | 65536, 65537, 65538: **`&H10000` above** |
  | Orientation | 1, horizontal | 1638401: **`&H190000` above** |
  | Orientation | 0, vertical | 1638400 |
  | Appearance | 0, fsb3D | 65536: `&H10000` above |
  | Appearance | 2, fsbTrack3D | **131074: `&H20000` above** |

  **The high words resisted explanation.** Appearance's high word was 1 for 0 and 2 for 2. The DTPicker's Format and
  the MonthView's StartOfWeek had high words that changed between IDE sessions; these didn't.
- **Every block gets Arrows and Orientation, even at their defaults. Appearance is written only off its default,**
  fsbFlat.
- **The vertical block got no Arrows line.** FlatScrollBar3, written with only `Orientation = 0`, was saved with
  Orientation 1638400 and nothing else. Every horizontal block got `Arrows = 65536`. This resisted explanation too.
- **A hand-written `Orientation = 0`, with no high word, was read as vertical.** The block kept its 2400 × 255 size, in
  the IDE and at run time. Width and Height swap only when Orientation is set from code, as in group 1.
- **Every line read back as written.**
- **The Properties window's 26 rows:**
  - (About), (Custom) and (Name);
  - Appearance `1 - fsbFlat` and Arrows `0 - cc2Both`;
  - DragIcon, DragMode, Enabled, Height, Index, LargeChange, Left, Max 32767 and Min 0;
  - MouseIcon, MousePointer `0 - cc2Default` and Orientation `1 - cc2OrientationHorizontal`;
  - SmallChange, TabIndex, Tag, ToolTipText, Top, Value, Visible, WhatsThisHelpID and Width.

  There is no TabStop, CausesValidation or HelpContextID row.

**Double-clicked from the toolbox,** a FlatScrollBar is horizontal, 1500 × 255, and written first.
- It sits at 2850, 2325 on a 7200 × 5400 form. That is **centred as if it were 750 high**, as the ImageCombo is.
- It gets the same lines as a minimal block.

### In HexIDE

HexIDE doesn't know MSCOMCT2.OCX. `ControlLibraries` lists only MSCOMCTL.OCX and COMCTL32.OCX, so nothing maps a
FlatScrollBar to this issue (#1097). Its own HScrollBar and VScrollBar are where the measured matches above would
be shared, and the clamp without Change is the one behaviour that must not be.

## The FlatScrollBar's conversions, MousePointer, `[_Value]` and Refresh (2026-11-23, #1097, SP6)

**A value stored into a FlatScrollBar property converts as VB converts it to the property's type, Integer or Boolean, then meets the property's own check.** `MISSING_LANGUAGE.md` listed these as unmeasured, and HexIDE refuses them.

**The probe.**
- **The form:** an `MSComCtl2.FlatScrollBar` (MSCOMCT2.OCX 2.0), and `Sub Main` showing it.
- **Each store:** before it, the bar was reset to Min 0, Max 100, Value 50, SmallChange 1, LargeChange 5, Enabled True and MousePointer 0.
  - The value was put in a `Variant`, then stored **early-bound**, as `Form1.FlatScrollBar1.Value = v`, and **late-bound**, through an `Object` variable.
  - `Err.Number` was read at once, then the property was read back through `CallByName` with its `TypeName`.
- **The build:** `/make` as native code and as p-code on VB6.EXE 6.00.9782 (SP6) with MSVBVM60 6.00.9848, on the Win10-C clone's console. **The two builds wrote identical logs.**
- **Early and late binding agreed on every row but Null:** 94 "Invalid use of Null" early-bound, 13 "Type mismatch" late-bound, the property unchanged either way.

**The Integer properties.** Each cell is what the property read afterwards, as an Integer, or the error raised, with the property unchanged:

| Stored | Value | Min | Max | SmallChange | LargeChange |
|---|---|---|---|---|---|
| `Null` | 94 / 13 | 94 / 13 | 94 / 13 | 94 / 13 | 94 / 13 |
| `Empty` | 0 | 0 | 0 | **380** | **380** |
| `True` | **380** | -1 | -1 | **380** | **380** |
| `False` | 0 | 0 | 0 | **380** | **380** |
| `"7"` | 7 | 7 | 7 | 7 | 7 |
| `"abc"` | 13 | 13 | 13 | 13 | 13 |
| `CByte(7)` | 7 | 7 | 7 | 7 | 7 |
| `CCur(7.5)` | 8 | 8 | 8 | 8 | 8 |
| `CDate(2)` | 2 | 2 | 2 | 2 | 2 |
| `CDec(7.5)` | 8 | 8 | 8 | 8 | 8 |
| `7.5` (Double) | 8 | 8 | 8 | 8 | 8 |
| `CSng(2.5)` | 2 | 2 | 2 | 2 | 2 |
| `40000` (Long) | **6** Overflow | **6** | **6** | **6** | **6** |

**What the table shows:**
- **The conversion is VB's `CInt`:** Empty is 0, True is -1, a numeric string parses, other text raises 13, halves go to the even neighbour, and anything beyond an Integer raises 6.
- **The 380s are the bar's own checks, met after converting:**
  - SmallChange and LargeChange refuse 0 and -1;
  - Value refuses -1, outside 0 to 100, as "Where it matches the HScrollBar" has a Value outside the range raise 380.
- **Min and Max take -1 and 0 without complaint,** as the reversed ranges above allow.

**Enabled** (Boolean):

| Stored | Reads |
|---|---|
| `0`, `Empty`, `CSng(0)`, `"False"` | False |
| `1`, `-1`, `2`, `1.5`, `CByte(1)`, `CCur(1)`, `"True"` | True |
| `"abc"` | 13, unchanged |
| `Null` | 94 / 13, unchanged |

**MousePointer:** 4 to 15 and 99 are accepted and read back as stored, as a Long. 98 and 100 raise **380** and leave it at 0. With this page's 0 to 3, the accepted set is 0 to 15 and 99.

**`[_Value]` is Value under another name:**
- With Value 0 it read Integer 0, and with Value 37, Integer 37.
- `[_Value] = 12` raised nothing, and Value then read 12.
- The bar read as a value with no member named (its default member) gave Integer 12.

**`Refresh`** raised nothing, early-bound and late-bound.

**Not measured:**
- `AboutBox`, which opens a dialog;
- `hWnd` reads;
- `MouseIcon`.
