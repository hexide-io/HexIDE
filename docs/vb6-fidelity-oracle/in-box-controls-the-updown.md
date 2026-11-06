## In-box controls: the UpDown (2026-10-27, #1093)

**The probes.**
- **The software:** VB6 SP6 (6.00.9782) with MSCOMCT2.OCX 6.01.9782, Microsoft Windows Common Controls-2 6.0 (SP6), on
  Windows 10 at 96 dpi.
  - The form line is `Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"`.
  - The block is `Begin MSComCtl2.UpDown`, `_Version` 393216.
- **The form:** Text1 (TabIndex 0), UpDown1 (TabIndex 1, at 3000, 120, 255 × 495, bound to nothing), Text2 and Label1.
- **The programs:** each ran in a Sub Main and logged each value with its `TypeName`.
  - Every error was read straight after its statement.
  - Each scenario ran on a fresh instance of the form.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.

### The object model

- **Its members,** from the type library:
  - **The range:** Value, Min, Max, Increment, Wrap.
  - **The buddy:** BuddyControl and BuddyProperty, both Variants; AutoBuddy, SyncBuddy, Alignment.
  - **The rest:** Orientation, Enabled, hWnd, OLEDropMode, OLEDrag.
  - **Hidden:** Acceleration, an Integer, and AboutBox.
- **Its events:** Change, UpClick, DownClick, MouseDown, MouseMove, MouseUp, and the OLE drag events.
  - There is no Click and no key event.
- **The constants:**
  - OrientationConstants: cc2OrientationVertical 0, cc2OrientationHorizontal 1. **This is the reverse of MSCOMCTL.OCX's**,
    whose ccOrientationHorizontal is 0.
  - AlignmentConstants: cc2AlignmentLeft 0, cc2AlignmentRight 1.
- **`TypeName`** is `UpDown`.
- **The default member is Value.** `v = UpDown1` read 0, a Long, and `UpDown1 = 4` set Value.

### Defaults

| Read | Logged |
|---|---|
| Min, Max, Value | 0, 10, 0, Longs |
| Increment | 1 |
| Wrap, AutoBuddy, SyncBuddy | False |
| Orientation | 0, vertical |
| Alignment | **1, right** |
| BuddyControl, BuddyProperty | empty Strings |
| Enabled, TabStop | True |
| OLEDropMode | 0 |
| Acceleration | 0, an Integer |

### Value, Min and Max

| Statement | Logged |
|---|---|
| `Value = 5` | Change |
| `Value = 5` again | **Change again** |
| `Value = 11`, past Max; `Value = -1`, below Min | **380**, and Value unchanged |
| `Value = 2.5`, `Value = 3.5` | 2, 4: **half rounds to even** |
| `Value = "x"` | 13 |
| `Max = 3` with Value 5 | Max 3, and **Value pulled down to 3, with Change** |
| `Min = 4` with Max 3; `Min = 3` with Max 3; `Min = 9` with Max 3 | **taken**: Min may equal Max or exceed it |
| `Value = 5` with Min 9 and Max 3 | taken. **20 and 0 are 380** |
| `Min = -40000` with Max 3 and Value 5 | Value pulled to 3, with Change |
| `Max = 2147483647` | taken |
| `Min = 10`, then `Max = 0`, from 0 to 10 with Value 0 | Value pulled up to 10, with Change, then kept. `Value = 4` is taken |

- **Value must lie between Min and Max, whichever is larger.**
- **Assigning Value always raises Change,** even with the value it already has.
- **A Min or Max that pulls Value in raises Change too.**

### The other properties

| Assignment | Logged |
|---|---|
| Increment 0, 3, 20, 100 | taken, more than the range included |
| Increment -1 | **380** |
| Wrap or Enabled -1, 1, 2 | True: any non-zero |
| **Orientation, any value** | **382**, at run time |
| Alignment 0 or 1 | taken at run time |
| Alignment -1, 2 | 380 |
| OLEDropMode 0 or 1 | taken. -1 and 2 are 380 |
| Acceleration 2 | taken |
| `hWnd = 1` | 450 |

**The two 382s read differently.** Through `CallByName`, Orientation's error said "Set not supported at runtime". Assigned
directly, through the same late-bound `Object` variable, it said "Property cannot be set at run time".

### The buddy

**BuddyControl takes a control or a control's name, and reads back as the name.**
- `Set BuddyControl = Text1` and `BuddyControl = "Text2"` both bound. Either reads back as the String `Text1` or `Text2`.
- A Label is taken too.
- `"NoSuch"`, `Set BuddyControl = Me` (the form) and `BuddyControl = 5` are 380.
- `Set BuddyControl = Nothing` and `BuddyControl = ""` unbind.

**Binding moves the UpDown to the buddy and narrows the buddy.** With Alignment 1:
- The UpDown went to the buddy's right edge: Left 1066.
- It took the buddy's height: 315, from 495.
- The buddy narrowed by the UpDown's width: 1200 to 945.

**With Alignment 0 the pair swaps sides.** The UpDown took the buddy's old Left, and the buddy moved right by the UpDown's
width, to 375. Alignment 1 put them back.

**What follows binding:**
- **Another buddy gives the first its width back.** Text1 went back to 1200 when Text2 was bound, and Text2 narrowed.
- **Unbinding puts the UpDown back where it was designed:** 3000, 120, 495 high. The buddy got its width back, plus
  anything it had gained since: a Text1 widened to 2000 while bound became 2255.
- **The UpDown doesn't follow a buddy moved later.** After `Text1.Move 500, 600` it stayed at 1066, 120.
  - Setting Alignment again re-attached it, at 2501, 600.
  - `UpDown1.Move` moved it away while still bound.

**SyncBuddy writes Value into the buddy.**
- **With no BuddyProperty, it writes the buddy's default property:** Text for a TextBox, Caption for a Label.
- **The buddy's Change comes first:** Text1_Change, then the UpDown's Change.
- **It writes only when Value is assigned.**
  - Turning SyncBuddy on wrote nothing: Text1 kept `2` with Value 8.
  - Binding a buddy wrote nothing.
- **Code that changes the buddy doesn't change Value.** `Text1.Text = "3"`, and `"abc"`, left Value at 8.
- Turning SyncBuddy off stopped the writes. Switching to another buddy kept SyncBuddy on.

**BuddyProperty names the property to write, and turns SyncBuddy on.**
- `BuddyProperty = "Tag"` was taken, set SyncBuddy True, and the next Value went into Text1's Tag.
- **`"text"`, lower case, was taken**, and the next Value went into Text.
- `""` and `"NoSuch"` are 380.
- **BuddyProperty always read back as an empty String,** whatever it had been set to, Tag, text and Caption included.
- **With no buddy, BuddyProperty and SyncBuddy are 35754, "BuddyControl property must be set first".**

**AutoBuddy binds the control before the UpDown in tab order.**
- `AutoBuddy = True` at run time bound Text1, TabIndex 0, for the UpDown at TabIndex 1, moving and narrowing as above.
- **Binding another buddy turns AutoBuddy off.**
- `AutoBuddy = False` kept the buddy it had.
- `AutoBuddy = True` again went back to Text1, and gave Text2 its width back.
- `BuddyControl = ""` turned it off too.

### Events from code

- **Change** comes from every assignment to Value, and from a Min or Max that pulls Value in.
- **UpClick and DownClick** were never raised from code.

### Events, with real input

**The probe:** UpDown1 at Min 0, Max 10, Increment 1, unless a row says otherwise. The program read the UpDown's and
Text1's window rectangles, and a driver on the guest's console clicked the halves of the UpDown and typed. Native and
p-code logged the same lines.

| Action | Events, in order |
|---|---|
| Click the top half of a vertical UpDown | MouseDown, **Change** with Value +1, MouseUp, **UpClick** |
| Click the bottom half | MouseDown, Change with Value -1, MouseUp, **DownClick** |
| A horizontal one (`Orientation = 1` in the file): the right half, the left half | UpClick with +1, DownClick with -1 |
| Click the bottom half at Min | **Change, with Value still 0**, then DownClick |
| With Wrap: the top half at Max, the bottom half at Min | Value 10 to 0, and 0 to 10, with Change and the click event |
| Increment 3: the top half at 8 | **10**: clamped at Max |
| the same again, at 10 | Change with Value 10, UpClick |
| the bottom half at 10 | **6**, not 7 |
| Increment 3 with Wrap: the top half at 10 | 0 |
| Hold the top half for 3 seconds, from 0 | **Change at every step**, 1 to 10, then Change at 10 again and again while held; **one UpClick, after MouseUp** |
| Press the top half, move off the control, release | Change at the press; **UpClick still raised** |
| Right-click | MouseDown and MouseUp with Button 2. No Change, no click event |
| The wheel over it | nothing |
| With Min 10 and Max 0, Value 5: the top half, the bottom half twice | 4, then 5, 6: **up moves toward Max**, whichever is larger |

- **Change comes at the press, and UpClick or DownClick after the release,** after MouseUp.
- **A click at the end of the range still raises Change**, with Value unchanged.

**The keyboard.** Tabbing from Text1 gave the UpDown the focus, with GotFocus. **The Up arrow then moved the focus back to
Text1**, the control before it, and raised no Change. The UpDown takes no keys. With the buddy focused, the Up and Down
arrows were Text1's own KeyDown and changed nothing.

**Holding the button.** With Max 1000, the first repeat came 0.53 s after the press, and then one step every 0.11 s, by 1,
for the whole 7 seconds: 59 steps, each with Change. **There is no acceleration.** Acceleration was 0, its default, and
UpClick came once, after MouseUp.

**An Increment above 1 snaps to its multiples first.** One click, one Change:
- **A Value that isn't a multiple of Increment goes to the next multiple in the direction of travel, and then a whole
  Increment beyond it.**
  - Increment 3, up: 0 to 3, 3 to 6, 1 to 6, 2 to 6, 4 to 9, 5 to 9, 7 to 12.
  - Increment 3, down: 3 to 0, 9 to 6, 4 to 0, 5 to 0, 8 to 3, 10 to 6, 20 to 15.
  - Increment 2: up from 1 is 4, and down from 3 is 0.
  - Increment 4: 6 is 12 up and 0 down.
  - Increment 5, up: 1, 4 and 5 all give 10, and 6 gives 15. Down: 6 gives 0, and 99 gives 90.
- **The multiples are of Increment itself, not counted from Min.** With Min 1, 2 or -1 the same values gave the same
  answers: 1 to 6, 2 to 6, 3 to 6, 4 to 9 up, and 7 to 3, 20 to 15 down.
- **Below zero the answers leave the grid.** With Min -10 and Increment 3:
  - up, -10 gave -5, -7 gave -2, -5 gave -1 and -6 gave -3;
  - down, -5 gave -10.
- **One formula fits every row, negatives included.** Write r for |Value| mod Increment, computed on the magnitude:
  - a step that raises Value gives Value + Increment, plus Increment - r when r isn't 0;
  - a step that lowers it gives Value - Increment - r.

  On a positive Value this is the snap described above. On a negative one the magnitude makes it land between
  multiples.
- The result is then clamped to the range: up from 8 with Max 10 gave 10.
- **With Min above Max, up still moves toward Max, so it lowers Value:** Min 20, Max 0, Value 10 gave 6 up and 15 down,
  by the same formula.

**Wrap only from the end itself.**
- With Increment 3 and Wrap, up from 10, the Max, gave 0.
- **Down from 1 gave 0, not a wrap:** a step that would pass Min stopped at it.
- With Increment 1, up from the Max wrapped to Min, and down from Min to Max.

**A buddy with SyncBuddy: the click reads the buddy's text first.**
- **The text is converted as VB converts a String to a Long,** then clamped to the range, then stepped.
- **Text that won't convert counts as 0.**

Increment 1, Min 0, Max 10, Value 4, up, unless the row says otherwise:

| Text1 held | Value after | Read as |
|---|---|---|
| `7`, set from code | 8 | 7 |
| `5`, typed, Value 1 | 6 | 5: **the buddy's text, not Value** |
| ` 3`, `  7  `, `7 ` | 4, 8, 8 | spaces ignored |
| `3.7` | 5 | 4: rounded |
| `2.5`, `3.5` | 3, 5 | 2, 4: **half to even** |
| `&H5` | 6 | 5: **hexadecimal** |
| `1e1` | 10 | 10, clamped |
| `1,5` | 10 | 10 or more, clamped: as VB reads it in this guest's en-GB user locale, 15, the comma a thousands separator |
| `$3` | 4 | **3**: the currency sign taken |
| `(2)`, with Min -10 | -1 | **-2**: parentheses negate |
| `-5` with Min -10; with Min 0 | -4; 1 | -5; clamped to 0 |
| `42`, down | 9 | 42, clamped to 10 |
| `9x`, `x`, `Text1`, empty | 1 | **0** |
| `9x` with Min -10, down | -1 | 0 |
| `9x` with Min 5, up; down | 6; 5 | 0, clamped to 5 |

The written text is Value's own: `3.7` became `5`, `&H5` became `6`.

### Increment 0, Acceleration, and a buddy property other than Text (2026-10-27)

The same probe: clicks and holds on the guest's console, with the program logging each event and the time since the
case was set. Native and p-code logged the same events. One 8-second hold gave 70 steps natively and 69 as p-code, by
timing.

**Increment 0: a click does nothing but the click event.** With Min 0, Max 10 and Value 4:
- the top half raised **UpClick alone, with no Change**, and Value stayed 4;
- the bottom half raised DownClick alone.

That differs from a click at the end of the range, which does raise Change with Value unchanged.

**Acceleration has no effect.** The button was held on the top half from 0, with Max 10000:

| Acceleration | Held | Changes | Steps |
|---|---|---|---|
| 0, the default | 3 s; 8 s | 24; 69 to 70 | **all by 1** |
| 1 | 3 s | 24 | all by 1 |
| 5 | 3 s; 8 s | 24; 69 | **all by 1** |
| 100 | 8 s | 69 | **all by 1** |

- **The timing never changed.** In every hold, the first repeat came about 0.53 s after the first Change, then one step
  every 0.11 s.
- **Acceleration read back as set**: 1, 5 and 100. It is stored and ignored.
- One UpClick came after the release, as before.

**A buddy written through another property is also read through it.** With Increment 1, Min 0, Max 10, Value 2:

| Buddy | Its property before the click | The click | Logged |
|---|---|---|---|
| Label1, `BuddyProperty = "Caption"` | Caption `7` | top half | Label1_Change with Caption `8`, then Change with Value 8, then UpClick |
| the same | Caption `7` | bottom half | Label1_Change with `6`, Change with 6, DownClick |
| **Label1, SyncBuddy True and no BuddyProperty** | Caption `7` | top half | the same as with `"Caption"`: **8** |
| **Text1, `BuddyProperty = "Tag"`** | Text `9`, **Tag `3`** | top half | Change with **Value 4**. **Tag became `4`, and Text stayed `9`**. No Text1_Change |
| the same | Text `9`, **Tag `abc`** | top half | **Value 1, and Tag `1`**: Tag that won't convert counts as 0, as for Text |

- **The click reads the named property, not Text and not Value,** converts it as for Text, steps, and writes the result
  back into the same property.
- **The buddy's own Change comes before the UpDown's,** as with Text. Writing a Tag raises no Change on the TextBox.

### Drawing

Screenshots at 96 dpi, one fresh form per state:
- **Alone,** the vertical UpDown is two stacked buttons with black triangles. The horizontal one, from `Orientation = 1`
  in the file, is two side by side, pointing left and right.
- **Bound,** it takes the buddy's height and sits flush against it, on the right with Alignment 1 and on the left with 0.
  The vertical one's triangles shrink to fit.
- **A horizontal UpDown, bound, became 255 wide and the buddy's height:** two thin buttons whose arrows are dots. Alone,
  it was 495 × 255.
- **Enabled False** greys both triangles, etched.
- **At Max** nothing changes: the up button isn't greyed.

### The saved files

**The programs:**
- **The save:** a form written by hand, opened in VB6's IDE and saved. VB6's save is in
  `corpus/inbox-controls/saved/updown/`. It held:
  - Text1, then UpDown1 with every property but the buddy's off its default and its lines reversed: horizontal, 495 ×
    255;
  - UpDown2, with only its position, extents and `_Version`;
  - Text2, then UpDown3, as minimal as UpDown2.
- **In the IDE, before the save:**
  - UpDown3's AutoBuddy and then its SyncBuddy were stepped to True in the Properties window;
  - a fourth UpDown was double-clicked from the toolbox.
- **The read:** the hand-written form and VB6's save were read back at run time, natively and as p-code. They logged the
  same values.
- **The buddy's lines:** in later passes,
  - UpDown3's BuddyProperty was chosen in the Properties window;
  - ten hand-written single-UpDown projects, plus VB6's save with that BuddyProperty, were each built and read back,
    native and p-code alike.

**VB6's blocks, as it wrote them:**

```
   Begin MSComCtl2.UpDown UpDown1 
      Height          =   255
      Left            =   3000
      TabIndex        =   1
      Top             =   120
      Width           =   495
      _ExtentX        =   873
      _ExtentY        =   450
      _Version        =   393216
      Value           =   5
      Alignment       =   0
      Increment       =   2
      Max             =   50
      Min             =   -5
      Orientation     =   1
      Wrap            =   -1  'True
      Enabled         =   0   'False
      OLEDropMode     =   1
   End
```

```
   Begin VB.TextBox Text2 
      Height          =   315
      Left            =   120
      TabIndex        =   3
      Text            =   "Text2"
      Top             =   1800
      Width           =   945
   End
   Begin MSComCtl2.UpDown UpDown3 
      Height          =   315
      Left            =   1066
      TabIndex        =   4
      Top             =   1800
      Width           =   255
      _ExtentX        =   450
      _ExtentY        =   556
      _Version        =   393216
      AutoBuddy       =   -1  'True
      BuddyControl    =   "Text2"
      BuddyDispid     =   196610
      OrigLeft        =   3000
      OrigTop         =   1800
      OrigRight       =   3255
      OrigBottom      =   2295
      SyncBuddy       =   -1  'True
      BuddyProperty   =   0
   End
```

- **The order is VB6's:**
  - `_Version` comes straight after the extents;
  - then Value, Alignment, Increment, Max, Min, Orientation, Wrap, Enabled, OLEDropMode;
  - a bound UpDown has AutoBuddy, BuddyControl, BuddyDispid, the four `Orig` lines, SyncBuddy and BuddyProperty between
    `_Version` and the rest.
- **Each property is written only when off its default.**
- **The double-clicked UpDown is 255 × 750**, with `_ExtentX = 450` and `_ExtentY = 1323`, and nothing after `_Version`.
- **Binding in the designer moved and narrowed as at run time, and the save keeps it:**
  - Text2 is written 945 wide;
  - UpDown3 is written at its bound place and height, 1066 and 315.
- **`OrigLeft`, `OrigTop`, `OrigRight` and `OrigBottom` are the rectangle the UpDown had before it was bound**, in twips:
  3000, 1800, 3255, 2295.
- **`BuddyControl` is the buddy's name.**
- **`BuddyProperty` is a dispatch ID.**
  - 0, the default member, while the window shows "(Default)".
  - **Choosing Left in the window wrote `BuddyProperty = 65540`**, &H10004, and left `BuddyDispid` at 196610.
  - The window's list began (None), (Default), Name, Index, BackColor, ForeColor, Left, Top.
- **`BuddyDispid` is 196610**, &H30002, for a TextBox buddy with the default property. What it identifies isn't
  measured. Choosing Left didn't change it.
- There is no `.frx`.

**What each buddy line does when the form loads.** Each project held Text1, then UpDown1 at 3000, 120, 255 × 495:

| Hand-written | Loaded as | `Value = 7`, then unbinding |
|---|---|---|
| VB6's lines for Text1, the UpDown left unbound in size and place | bound, AutoBuddy and SyncBuddy True, **but nothing moved: 3000, 120, and Text1 still 1200 wide** | Text1 `7`. **Unbinding widened Text1 to 1455**, by the UpDown's width, though binding had never narrowed it |
| without BuddyDispid; or `BuddyDispid = 0` | the same | the same: **BuddyDispid isn't needed** |
| `BuddyProperty = 65540` | the same | **Text1's Left became 7** |
| VB6's save with Left chosen | bound | Text2's Left became 7 |
| `BuddyProperty = "Tag"` | bound | **438** |
| BuddyControl and SyncBuddy alone, no BuddyProperty line | bound, AutoBuddy False | **438**: a missing BuddyProperty isn't the default member |
| VB6's lines without the four `Orig` lines | bound | Text1 `7`. **Unbinding put the UpDown at 0, 0, 30 × 30** |
| `BuddyControl = "NoSuch"`, the rest as VB6 writes it | **unbound, AutoBuddy and SyncBuddy False**, no error | |
| `AutoBuddy = -1` alone | **unbound, AutoBuddy False**: it doesn't bind at load | |
| `SyncBuddy = -1` alone | SyncBuddy False | |

- **A file's buddy lines move nothing at load.** VB6 writes the bound place and sizes into the file itself, for the UpDown and
  for the buddy, so they come back from the file's own lines.
- **Unbinding puts the UpDown at its `Orig` rectangle, and widens the buddy by the UpDown's width**, whether or not the
  buddy was narrowed. Without `Orig` lines the rectangle is 0, 0, 30 × 30.
- A buddy that doesn't exist, or AutoBuddy or SyncBuddy without one, loads unbound, silently.

### In HexIDE

HexIDE registers MSCOMCT2.OCX as `MSComCtl2`, using the identity in VB6's save above (#1093). The Components
dialog can add and remove its reference. Part A implements the runtime object model; part B implements
pointer input, hold repeat, buddy text conversion and vertical/horizontal drawing without pinned pixel
metrics. The follow-up measurements in #1093 (issuecomment-6446090132, native and p-code)
add zero Increment clicks without Change, stored-and-ignored Acceleration, and synchronized reads and writes
through Label Caption (including its default member) and TextBox Tag. Other runtime buddy properties remain refused. Part C loads saved properties and buddy bindings without moving controls, preserves the file blocks
verbatim, and offers the measured toolbox default. Loaded blocks remain read-only in the designer;
unmeasured saved BuddyProperty DISPIDs refuse explicitly.
