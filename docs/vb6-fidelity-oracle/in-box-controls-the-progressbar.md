## In-box controls: the ProgressBar (2026-10-25, #1082)

**Why the ProgressBar.** It is the pilot of the in-box ActiveX controls (epic #1081): the controls VB6 ships in its own
OCXs, which a VB6 developer thinks of as built in. It has the fewest members of any of them, so it carries the parts
every one of them needs: a library record, the `Object=` line, the type name, the extents, and a member table. The probes
cover both of its libraries:

| Library | File | GUID, version | `_Version` it writes |
|---|---|---|---|
| `MSComctlLib` (6.0) | MSCOMCTL.OCX | `{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}` 2.0 | 393216 |
| `ComctlLib` (5.0) | COMCTL32.OCX | `{6B7E6392-850A-101B-AFC0-4210102A8DA7}` 1.3 | 327682 |

**The probes.**
- **Run time:** a form holding one ProgressBar with only its geometry, read late-bound through `pb As Object` under
  `On Error Resume Next`, from `Sub Main`. Built with `/make` as native code and as p-code, for each library, in the
  guest's session 0. All four builds gave the same rows wherever the library has the member.
- **Early-bound:** each member a ProgressBar may lack, in a build of its own, `Debug.Print ProgressBar1.<member>`.
- **The save:** hand-written forms opened in VB6's IDE on the console, one property changed through the Properties
  window, and saved with Ctrl+S. The inputs and VB6's saves are `corpus/inbox-controls`.
- **The drawing:** running bars captured on the console and read pixel by pixel.
- **The type libraries:** `scripts/dump-typelib-types.ps1`, `dump-typelib-members.ps1` and
  `dump-typelib-constants.ps1` over both OCXs, kept in `corpus/inbox-controls/typelib`.

**Reading, the defaults.** Each row is the value, its `TypeName`, and `Err.Number`:

| Read | Logged |
|---|---|
| Value, Min, Max | `0 Single`, `0 Single`, `100 Single` |
| the default member, `v = pb` | `0 Single`: it is Value |
| Orientation, Scrolling, Appearance, BorderStyle, MousePointer, OLEDropMode | `0`, `0`, `1`, `0`, `0`, `0`, each a Long |
| Enabled | `True` |
| `TypeName(pb)`, `TypeName(pb.Object)` | `ProgressBar`, `ProgressBar` |
| `TypeOf pb Is MSComctlLib.ProgressBar` | `True` |
| `hWnd <> 0`, `Refresh` | `True`, no error |
| Name, Left, Top, Width, Height | `ProgressBar1`, then 240, 240, 2655 and 375 as Singles |
| Index | error 343: not an array |
| TabIndex, DragMode, Align | `0 Integer`, `0 Integer`, `0 Integer` |
| WhatsThisHelpID | `0 Long` |
| Tag, ToolTipText | empty Strings |
| Container.Name, Parent.Name | `Form1` |
| TabStop, HelpContextID, CausesValidation | 438. Early-bound, each is the compile error "Method or data member not found" |
| Font, BackColor, ForeColor, Text, SetFocus | 438 |
| Negotiate | 393, "Get not supported at runtime" |
| MouseIcon Is Nothing, DragIcon Is Nothing | `False`, `False`: each holds an empty picture |
| `ZOrder 0`, `OLEDrag`, `Move 100, 100` | no error. After the Move, Left reads 0. Align had been set to 2 by an earlier row |

**Writing Value, Min and Max.** Each row starts from Min 0, Max 100, Value 0, and shows the error and then Value, Min
and Max:

| Assignment | Logged |
|---|---|
| `Value = 50`, `= 100`, `= 50.25` | no error; 50, 100, 50.25 |
| `Value = 150`, `= -1`, `= 100.5` | 380, Value left at 0 |
| `Value = 1/3`, read back | `0.3333333 Single` |
| `Value = "40"`, `= "1e1"` | no error; 40, 10 |
| `Value = "abc"` | 13 |
| `Value = True` | 380: True is -1 |
| `Value = Null` | 13, where an intrinsic's numeric property gives 94 |
| `Value = Empty` | no error; 0 |
| `Value = 1E+39` | 6, Overflow: past a Single |
| `pb = 30`, the default member | no error; Value 30 |
| `Min = 100` (Max), `Min = 200` | 380 |
| `Max = 0` (Min), `Max = -5` | 380 |
| `Min = -100`, `Min = -0.001` | 380: Min can't go below 0 |
| `Min = 0.5`, `Min = 99.99` | no error; and Value, which was below, moves up to it |
| `Max = 0.5`, `= 40000`, `= 1E+30`, `= 3.4E+38` | no error |
| `Max = Empty` | 380: it is 0, which is Min |
| `Max = Null`, `Min = Null`, `Min = "abc"` | 13 |
| Value 50, then `Max = 40` | no error; Value 40, Max 40 |
| Value 50, then `Min = 60` | no error; Value 60, Min 60 |
| Value 50, then `Max = 50` | no error; Value 50 |
| Max 200, Min 150, then `Max = 160` | no error; Value 150 |
| Max 200, Min 150, then `Max = 100` | 380; nothing changes |

So Min stays at 0 or above and below Max, and Max above Min. A Min or Max that passes Value carries it with them.

**Writing the rest.** Each row is the error and then the value read back:

| Assignment | Logged |
|---|---|
| `Orientation = 1`, `Scrolling = 1`, `BorderStyle = 1`, `Appearance = 0` | no error, and each reads back |
| `Orientation = 2`, `Scrolling = 5`, `BorderStyle = 2`, `OLEDropMode = 2` | 380, the property left as it was |
| `Appearance = 2`, `Appearance = -1` | 380 |
| `Orientation = 1.5` | 380: rounded half to even, 1.5 is 2 |
| `Orientation = 0.5` | no error; 0 |
| `Orientation = "1"` | no error; 1 |
| `Orientation = True` | 380 |
| `Orientation = Null`, `= "abc"` | 13 |
| `Orientation = 1E+10` | 6: past a Long |
| `MousePointer = 15`, `= 99` | no error |
| `MousePointer = 16`, `= 98`, `= 100`, `= -1` | 380 |
| `MousePointer = Null`, `BorderStyle = Null` | 13 |
| `Enabled = False`, `= 5` | no error; False, True |
| `Enabled = Null`, `= "abc"` | 13 |
| `WhatsThisHelpID = 5`, `DragMode = 1`, `Align = 2`, `TabIndex = 0` | no error, and each reads back |
| `ToolTipText = "tip"` | no error; `tip` |
| `Value = 25`, then `Refresh` | no error; Value still 25 |

**The constants.** MSCOMCTL.OCX's type library holds 181 constants in 42 enums, and COMCTL32.OCX's 155 in 33. The
152 that both declare have the same values, and COMCTL32's other three are `ccOverlayParamTooLarge`, `sldHorizontal` and
`sldVertical`. Each is a Long. Measured:

| Expression | Logged |
|---|---|
| `ccScrollingSmooth`, `ccFixedSingle`, `cc3D` | 1, 1, 1 |
| `MSComctlLib.ccOrientationVertical` | 1 |
| `ScrollingConstants.ccScrollingStandard` | 0 |
| `MSComctlLib.ScrollingConstants.ccScrollingSmooth` | 1 |

Without the `Object=` reference, `ccScrollingSmooth` is an undeclared variable.

**5.0 against 6.0.** A ComctlLib ProgressBar answered every row above in the same way, with three exceptions. It has no
Orientation, Scrolling or Refresh: each is 438 late-bound. Its type library has no 6.0 constants either, such as
`ccScrollingSmooth`. It has `sldVertical` (1) in an `OrientationConstants` of its own.

**Visible: what resisted explanation.** `pb.Visible` read False after `Load Form1`, and still False after
`Form1.Show`. The probe ran in session 0, where Show may not make a window visible at all. So the row records what the
session reported, not a rule about the control. It wasn't repeated on the console.

**The save.** VB6 writes an in-box control's block in three runs:

```
   Begin MSComctlLib.ProgressBar pb
      Height          =   375
      Left            =   120
      TabIndex        =   0
      Tag             =   "x"
      Top             =   120
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   393216
      BorderStyle     =   1
      Appearance      =   1
      MousePointer    =   99
      Enabled         =   0   'False
      OLEDropMode     =   1
      Max             =   50
      Orientation     =   1
      Scrolling       =   1
   End
```

1. **The extender's properties, in name order:**
   - Align, written with its comment, `'Align Bottom`;
   - DragMode, Height, Left;
   - TabIndex, which is always written;
   - Tag, ToolTipText, Top, Visible, WhatsThisHelpID and Width.
2. **`_ExtentX`, `_ExtentY` and `_Version`.**
3. **The control's own properties, in the control's order,** whatever order the file had them in:
   - BorderStyle;
   - Appearance, which is always written, even at its default of 1;
   - MousePointer, Enabled, OLEDropMode;
   - Min, Max, Orientation and Scrolling.

   Every other property is written only where it differs from its default. Enabled carries its `'False` comment, while
   an enum value carries none.
- **Value is never written.** A hand-written `Value = 20` was gone after VB6's save, and Value isn't in the Properties
  window. So every ProgressBar starts a run at 0.
- **The extents are HIMETRIC,** `(px × 2540 + 48) / 96` rounded down, which is rounding half up. VB6 set 3493 for
  132 px, 4128 for 156 px, 4683 for 177 px and 4763 for 180 px.
- **VB6 keeps the extents it read until the control is sized.** A hand-written block whose extents disagreed with its
  Width and Height kept them through a save. A block with none got the control's default, 2646 by 1323 (100 by 50 px).
  Sizing a control then sets them from its new size.
- **A 5.0 control stays a 5.0 control.** It is written back as `ComctlLib.ProgressBar` with `_Version 327682`.
- **Not measured: MouseIcon's position.** The probe's icon didn't load, so where VB6 writes MouseIcon among the control's
  own properties is unknown.

**The Properties window.** VB6's list for a ProgressBar, in its order:
- (About), (Custom), (Name);
- Align, Appearance, BorderStyle, DragIcon, DragMode, Enabled, Height, Index, Left;
- Max, Min, MouseIcon, MousePointer, Negotiate, OLEDropMode, Orientation, Scrolling;
- TabIndex, Tag, ToolTipText, Top, Visible, WhatsThisHelpID and Width.

There is no Value. The designer draws the bar full, whatever its Min and Max.

**The drawing.** This is the Windows progress bar without visual styles, which is what a VB6 program without a manifest
gets:
- **The frame:**
  - Appearance 1 is a 2 px sunken edge:
    - at the top and left, the button shadow outside and the dark shadow inside;
    - at the bottom and right, the highlight outside and the light inside.
  - BorderStyle 1 adds a 1 px ring of the button face outside that edge.
  - A flat bar with BorderStyle 1 has a 1 px window-frame line.
  - A flat bar with BorderStyle 0 has no frame.
- **The fill area** is the frame's interior, less 1 px all round.
- **The fill** is `floor(length × (Value − Min) / (Max − Min) + 0.5)` pixels of it, in the highlight colour. It runs
  from the left, or upward from the bottom when the bar is vertical.
- **Smooth,** the fill is one bar.
- **Standard,** the fill is a row of blocks:
  - each block is `max(1, ⌊2 × thickness / 3⌋)` long, with 2 px between them;
  - there are `ceil(fill / (block + 2))` of them;
  - the last block is cut 1 px short of the area's end. That was measured across; the vertical bar is taken to do the
    same.
- **A disabled bar is drawn as an enabled one.**

**In HexIDE (#1082):**
- **Runs:**
  - A form holding either library's ProgressBar opens, runs and saves, byte for byte. All 13 of VB6's saves in
    `corpus/inbox-controls/saved` are in the round-trip lane.
  - Every row above, apart from the exceptions listed below.
  - The constants, bare and qualified by library or enum, and an enum as a declared type, `Dim s As
    ScrollingConstants`.
  - The member table, which marks each member with the libraries that declare it. It is checked against the dumped
    type libraries.
- **Refused as not implemented:**
  - MouseIcon and hWnd;
  - OLEDrag, and the OLE drag-and-drop events;
  - Align, DragIcon, DragMode and WhatsThisHelpID at run time;
  - the default member, `pb = 30`, which is no control's yet (#790).
- **A member a ProgressBar lacks** is refused with "Method or data member not found", whether the program is
  early-bound or late-bound. That is VB6's answer early-bound. Late-bound, VB6 gives 438.
- **Not measured: Click and double-click.** Click is raised on a release over the control, and DblClick isn't raised.
- **A form holding an in-box control HexIDE doesn't model yet** is refused with a message naming the control's issue:
  "HexIDE cannot create ListView1 (MSComctlLib.ListView), which isn't implemented yet (#1085)."
