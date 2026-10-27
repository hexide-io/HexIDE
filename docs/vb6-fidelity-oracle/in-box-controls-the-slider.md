## In-box controls: the Slider (2026-10-27, #1091)

**The probes.**
- **The software:** VB6 6.00.8176 SP6, with MSCOMCTL.OCX 6.01.9782, on Windows 10 at 96 dpi.
  - The form line is `Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"`.
  - The block is `Begin MSComctlLib.Slider`, `_Version` 393216.
- **The form:** VB6's own double-clicked Slider block from `corpus/inbox-controls/pending/defaults/`, moved to 120, 120 and
  widened to 4500.
- **The programs:** each ran in a Sub Main and logged each value with its `TypeName`.
  - Every error was read straight after its statement.
  - Questions that earlier statements could disturb each ran on a fresh instance of the form.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.

### The object model

- **Its members,** from the type library:
  - **The range:** Min, Max, Value, SmallChange, LargeChange.
  - **The ticks:** TickStyle, TickFrequency, GetNumTicks.
  - **The selection:** SelectRange, SelStart, SelLength, ClearSel.
  - **The tip shown while dragging:** Text, TextPosition.
  - **The rest:** Orientation, BorderStyle, Enabled, hWnd, MousePointer, MouseIcon, OLEDropMode, Refresh, OLEDrag.
  - **Hidden:** `_Value`, the default member, and DoClick.
- **Its events:** Change, Scroll, Click, KeyDown, KeyPress, KeyUp, MouseDown, MouseMove, MouseUp, and the OLE drag events.
- **The constants:**
  - Orientation: ccOrientationHorizontal 0, ccOrientationVertical 1.
  - TickStyle: sldBottomRight 0, sldTopLeft 1, sldBoth 2, sldNoTicks 3.
  - TextPosition: sldAboveLeft 0, sldBelowRight 1.
- **`TypeName`** is `Slider`.
- **The default member is Value.** `v = Slider1` read 0, a Long, and `Slider1 = 4` set Value.

### Defaults

| Read | Logged |
|---|---|
| Min, Max, Value | 0, 10, 0, Longs |
| SmallChange, LargeChange | 1, 5 |
| TickStyle, TickFrequency | 0, 1 |
| GetNumTicks | 11 |
| Orientation, TextPosition, BorderStyle, MousePointer, OLEDropMode | 0 |
| SelectRange | False |
| SelStart, SelLength | 0, 0 |
| Text | empty |
| Enabled, TabStop | True |

### Value, Min and Max

| Statement | Logged |
|---|---|
| `Value = 5` | Change |
| `Value = 5` again | **no Change** |
| `Value = 11`, past Max | **10**, no error, **Change** |
| `Value = -1`, below Min | **0**, no error, Change |
| `Value = 2.5`, `Value = 3.5` | 2, 4: **half rounds to even** |
| `Value = "7"` | 7. `Value = "x"` is **13** |
| `Max = 0` with Min 0; `Min = 10` with Max 10 | **380**: **Max must be more than Min**, not equal |
| `Min` above Max, `Max` below Min | 380 |
| `Min = 7` with Value 5 and Max 10 | Min 7, and **Value pulled up to 7, with no Change** |
| `Max = 4` with Value 7 | Max 4, and **Value pulled down to 4, with no Change** |
| `Min = -100`, `Max = 100000` | taken |

### The ticks and the steps

| Assignment | Logged |
|---|---|
| SmallChange, LargeChange, TickFrequency 0 | taken |
| SmallChange, LargeChange, TickFrequency -1 | **380** |
| SmallChange 20, LargeChange 20, TickFrequency 20 | taken, more than the range |
| `GetNumTicks = 3` | **450**: read-only |

**GetNumTicks counts Min, Max, and every multiple of TickFrequency from Min in between.**
- With Min 0 and Max 10, TickFrequency 1, 2, 3, 4, 7, 10 and 11 gave 11, 6, 5, 4, 3, 2 and 2.
- With Min -10 and Max 10, TickFrequency 3 gave 8: -10, -7, -4, -1, 2, 5, 8 and 10.
- Min 5 with Max 10 gave 6. Min 1 with Max 2 gave 2.
- **TickFrequency 0 gave 2:** the ends only.
- Every row fits `Int((Max - Min - 1) / TickFrequency) + 2`, with TickFrequency 0 counting as no ticks between.

### The enumerations

| Property | Taken | 380 |
|---|---|---|
| TickStyle | 0 to 3 | -1, 4, 99 |
| Orientation, TextPosition, BorderStyle, OLEDropMode | 0, 1 | -1, 2 and up |
| MousePointer | 1, 2, 3, 4, 99 | -1 |

**Orientation can be set at run time, and it swaps Height and Width.**
- 4500 × 675 became 675 wide and 4500 high, at the same Left and Top. Setting it back swapped them again.
- 3000 × 400 became 405 wide and 3000 high: the new width rounded to whole pixels.

### The selection

**With SelectRange False, SelStart is Value.**
- Setting Value moved SelStart with it.
- **`SelStart = 3` moved Value to 3, with no Change.**
- `SelStart = 20` is 380.
- `SelLength = 4` is **380**. SelLength stays 0, and ClearSel changes nothing.

**With SelectRange True, the selection is separate from Value.**
- SelStart 2 with SelLength 3, then `Value = 8`: the selection stayed at 2 and 3.
- **A SelStart that would carry the selection past Max cuts SelLength.** `SelStart = 8` with SelLength 3 gave SelLength 2,
  ending at Max 10, with no error.
- **But a SelLength that runs past Max is 380.** `SelLength = 3` from 8 was refused.
- SelLength or SelStart past the range, or negative, is 380.
- **Lowering Max cuts the selection too.**
  - Max 4 over a selection from 2 to 5 gave SelLength 2.
  - **Max 1, below SelStart 2, left SelStart 2 and SelLength -1.**
- **ClearSel sets SelStart to -1** and SelLength to 0. SelectRange stays True.
- Setting SelectRange False after a selection set SelStart back to Value and SelLength to 0.

### Events from code

- **Change** is raised by a Value that changes, whether assigned or clamped by its own assignment.
  - Not by assigning the same value.
  - Not by a Min or Max that pulls Value in.
  - Not by SelStart moving Value.
- **Scroll is never raised from code.**
- **The hidden DoClick raises Click.**
- Refresh raised nothing.

### Events, with real input

**The probe:** the Slider at Min 0, Max 10, SmallChange 1, LargeChange 5, Text `the tip`, run on the guest's console.
- The program asked the control for its channel and thumb rectangles (`TBM_GETCHANNELRECT`, `TBM_GETTHUMBRECT` sent to
  Slider1's hWnd, a window of class `Slider20WndClass`). A driver clicked, dragged and typed at those points.
- Native and p-code logged the same lines, mouse coordinates apart.

| Action | Events, in order |
|---|---|
| Click the channel right of the thumb, at 8 | MouseDown, **Scroll** (Value already 5), MouseUp, Click, **Change**. **Value moved by LargeChange, not to the point clicked** |
| the same, from 5 | Value 10 |
| Click the channel left of the thumb | Value down by LargeChange |
| Press and hold on the channel at 9, from 0 | MouseDown, Scroll at 5, Scroll at 10, then MouseUp, Click, **one Change** |
| **Drag the thumb from 0 to 7** | MouseDown; **Scroll at every value, 0 through 7, while dragging**; then on release MouseUp, Click, **Scroll again, then Change** |
| Click the thumb, not moving it | MouseDown, Scroll, MouseUp, Click, Scroll, **Change, with Value unchanged at 4** |
| Drag from 4 to 8 and back to 4 | Scroll for each value both ways, then **Change at 4**, the value it started at |
| Drag off the right end and release there | Scroll up to 10, then MouseUp, Scroll, Change. **No Click**, the button having come up off the control |
| Drag far below the control | Value stayed where the thumb had got to, 9 |
| Right arrow, Down arrow | KeyDown, Scroll, KeyUp, Change: **+1**. **Down raises Value**, as Right does |
| Left arrow, Up arrow | the same, **-1** |
| Left arrow at 0 | KeyDown, Scroll, KeyUp, **Change, though Value stayed 0** |
| Page Down, Page Up | +5, -5, with Scroll and Change |
| **Home, End** | KeyDown, **Change**, KeyUp, **Change**: **no Scroll, and Change twice**, even with Value already 0 |
| The wheel, one notch up, then down | **Scroll only, no Change.** Up lowered Value, 10 to 9, and down raised it |
| Right-click | MouseDown and MouseUp with Button 2, Click. No change of value |
| With SelectRange and a selection from 2 to 5, click the channel | pages as before; the selection stays |
| Tab, Shift+Tab | the focus leaves and comes back |

- **While dragging, the Text tip shows beside the thumb:** above it with TextPosition 0.
- **Change comes from user input whether or not Value changed.** Clicking the thumb, Left at Min, Home at Min and a drag
  back to the start each raised it. Code raises Change only when Value really changes.
- **Scroll is the event that tracks the thumb.** Change comes once, when the gesture ends.

### Drawing

Screenshots on Windows 10 at 96 dpi, one fresh form per state, native code:
- **TickStyle 0** puts the ticks below and points the thumb down.
  - **1** puts them above and points it up.
  - **2** draws both, with a square thumb.
  - **3** draws none, the thumb still pointing down.
- **Orientation 1** stands the Slider upright with Min at the top. The ticks are on the right with TickStyle 0, and on the
  left with 1.
- **BorderStyle 1** draws a single line round the control.
- **Enabled False fills the thumb with a dither.** The channel and the ticks don't change.
- **A selection** widens the channel into a white band and fills the selected range blue. A small triangle marks the
  ticks at each end.
- **TickFrequency 3 on 0 to 20** drew 8 ticks: 0, 3, 6, 9, 12, 15, 18 and 20.
- **The focus** is a dotted rectangle round the whole control.

### The saved files

**The programs:**
- **The save:** a form written by hand, opened in VB6's IDE, dirtied with a comment and saved. VB6's save is in
  `corpus/inbox-controls/pending/slider/`.
  - Slider1 had every property off its default, Text included, with its lines in reverse order.
  - Slider2 had only its position, extents and `_Version`.
  - Slider3 was upright: `Orientation = 1`, 675 wide and 3000 high, Value 4.
  - Slider4 had `Value = 6` and nothing else.
  - A fifth was double-clicked from the toolbox.
- **The read:** the hand-written form and VB6's save were read back at run time, natively and as p-code. All four runs
  logged the same values, apart from the fifth Slider, which only the save holds.
- **Single lines:** seven more hand-written projects, one Slider each, each with one questionable line or pair.
- **The Properties window:** in later passes over VB6's save, values were set there and the form saved again. Those saves
  are described below, not kept.

**VB6's block, as it wrote it:**

```
   Begin MSComctlLib.Slider Slider1 
      Height          =   675
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   4500
      _ExtentX        =   7938
      _ExtentY        =   1191
      _Version        =   393216
      BorderStyle     =   1
      MousePointer    =   2
      Enabled         =   0   'False
      OLEDropMode     =   1
      LargeChange     =   10
      SmallChange     =   2
      Min             =   -5
      Max             =   50
      SelectRange     =   -1  'True
      SelStart        =   3
      SelLength       =   12
      TickStyle       =   2
      TickFrequency   =   5
      Value           =   30
      TextPosition    =   1
   End
```

- **The order is VB6's, whatever order the lines were read in.**
  - **`_Version` comes straight after the extents**, as the MSFlexGrid's does and the TabStrip's doesn't.
  - Then BorderStyle, MousePointer, Enabled, OLEDropMode, LargeChange, SmallChange, Min, Max, SelectRange, SelStart,
    SelLength, TickStyle, TickFrequency, Value, TextPosition.
  - Slider3's are `Orientation = 1` then `Value = 4`, after `_Version`.
- **Each property is written only when off its default.** Slider2 and the double-clicked Slider hold only the extender's
  lines, the extents and `_Version`. The double-clicked one is 1500 × 675.
- **`Value = 30` read before `Max = 50` loaded as 30.** The control reads its properties in its own order, not the file's.
- **Text isn't saved.** The hand-written `Text = "the tip"` loaded nothing, and VB6's save left it out.
  - Text isn't in the Properties window.
  - The window lists (About), (Custom), (Name), BorderStyle, CausesValidation, DragIcon, DragMode, Enabled, Height,
    HelpContextID, Index, LargeChange, Left, Max, Min, MouseIcon, MousePointer, OLEDropMode, Orientation, SelectRange,
    SelLength, SelStart, SmallChange, TabIndex, TabStop, Tag, TextPosition, TickFrequency, TickStyle, ToolTipText,
    Top, Value, Visible, WhatsThisHelpID and Width.
- **There is no `.frx`.** None of these Sliders had a MouseIcon, so where one goes isn't measured.

**When SelStart is written.** With SelectRange False, SelStart reads as Value, but a file's Value doesn't make VB6 write
it:
- Slider4, read with `Value = 6` and saved untouched, came back as `Value = 6` alone.
- **A Value set in the Properties window is saved with a SelStart beside it.**
  - Slider2's Value set to 6 there was saved as `SelStart = 6` then `Value = 6`.
  - Slider4's Value typed as 699 and then pulled down by a Max of 3 was saved as `Max = 3`, `SelStart = 3`, `Value = 3`.

**The Properties window**, on VB6's save:

| Done there | Result |
|---|---|
| Slider2's Orientation stepped to 1 | **Height and Width swapped, and the extents with them**: 675 × 4500, `_ExtentX = 1191`, `_ExtentY = 7938`, then `Orientation = 1` |
| Value typed as 699 | **10, with no message**: clamped, as from code |
| Value typed as -2 | 0, with no message |
| Max typed as 0, with Min 0 | **"Invalid property value"**, and Max stayed 10 |
| Max typed as 3, with Value 10 | Max 3, and **Value pulled down to 3** |

**What a line read from the file does.** Each was read back at run time, native and p-code alike, and each project built
and ran without an error:

| Hand-written | Loaded as |
|---|---|
| `Max = 50`, `Value = 99` | **Value 50**: clamped, silently |
| **`Min = 10`, `Max = 10`** | **Min 10, Max 10**, which code refuses with 380. GetNumTicks 2 |
| **`Min = 20`**, Max left at 10 | **Min 20, Max 10, Value 20**: Min above Max. GetNumTicks 2 |
| SelectRange False, `Value = 6`, `SelStart = 2`, `SelLength = 3` | Value 6, **SelStart 6, SelLength 0**: the file's selection ignored |
| SelectRange True, `SelStart = 8`, `SelLength = 10` | SelStart 8, **SelLength 2**: cut at Max, as from code |
| `TickStyle = 7` | **TickStyle 0**, silently |
| `Orientation = 1` with a 4500 × 675 extent | Orientation 1, **still 4500 wide and 675 high**: a file's Orientation swaps nothing |

So a file can hold a range no assignment can make. Min equal to or above Max loads, and a reader that checks the range
as the setters do would refuse a form VB6 runs.

### In HexIDE

HexIDE doesn't model the Slider (#1091). The designer draws it as an empty box (#157). Running a form that holds one is
refused, as for any control HexIDE can't create (#857), and the refusal names #1091 as the issue that tracks it.
