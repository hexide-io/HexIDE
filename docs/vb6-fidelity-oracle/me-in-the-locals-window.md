## Me in the Locals window (2026-10-28, #2329)

**The question.** "What a Timer has at run time" found `Me` ending with `Item 1` and `Item 2`, one per Timer. Is that one
`Item` per control in general, in what order, and how does `Me` list controls of other kinds and a control array with a
gap in its indexes? Asked for #2329, which takes these rows as its test's expectation.

**The probe.** VB6's IDE, SP6 (6.00.9782). A Standard EXE whose form holds, in this order in the `.frm`: Label1, Text1,
Timer1, then a control array Command1 with elements 0 and 2. Every property is VB6's default apart from size, position,
captions, Text1's text, `TabIndex` and Timer1's `Interval` of 1000. `Form_Load` is `Stop` and nothing else. Paused there, the Locals window was widened and `Me`
expanded, then Command1 and its `Item 2`, then `Me`'s `Item 5`.

**What `Me` listed**, Expression, Value and Type exactly as written. A node with an expander is marked "+". `hDC` and
`hWnd` are handles, and `Left` and `Top` are where Windows placed the form; those four differ from run to run.

| Expression | Value | Type |
|---|---|---|
| `Me` + | | `Form1/Form1` |
| `[_Default]` | `<Wrong number of arguments or invalid property assignment>` | `Object` |
| `ActiveControl` | `Nothing` | `Control` |
| `Appearance` | `1` | `Integer` |
| `AutoRedraw` | `False` | `Boolean` |
| `BackColor` | `-2147483633` | `Long` |
| `BorderStyle` | `2` | `Integer` |
| `Caption` | `"Form1"` | `String` |
| `ClipControls` | `True` | `Boolean` |
| `Command1` + | | `CommandButton/CommandButton` |
| `ControlBox` | `True` | `Boolean` |
| `Controls` + | | `Object` |
| `Count` | `5` | `Integer` |
| `CurrentX` | `0` | `Single` |
| `CurrentY` | `0` | `Single` |
| `DrawMode` | `13` | `Integer` |
| `DrawStyle` | `0` | `Integer` |
| `DrawWidth` | `1` | `Integer` |
| `Enabled` | `True` | `Boolean` |
| `FillColor` | `0` | `Long` |
| `FillStyle` | `1` | `Integer` |
| `Font` + | | `Font/Font` |
| `FontBold` | `False` | `Boolean` |
| `FontItalic` | `False` | `Boolean` |
| `FontName` | `"MS Sans Serif"` | `String` |
| `FontSize` | `8.25` | `Single` |
| `FontStrikethru` | `False` | `Boolean` |
| `FontTransparent` | `True` | `Boolean` |
| `FontUnderline` | `False` | `Boolean` |
| `ForeColor` | `-2147483630` | `Long` |
| `HasDC` | `True` | `Boolean` |
| `hDC` | a handle | `Long` |
| `Height` | `3585` | `Single` |
| `HelpContextID` | `0` | `Long` |
| `hWnd` | a handle | `Long` |
| `Icon` + | | `Picture/Picture` |
| `Image` + | | `Picture/Picture` |
| `KeyPreview` | `False` | `Boolean` |
| `Label1` + | | `Label/Label` |
| `Left` | `1170` | `Single` |
| `LinkMode` | `0` | `Integer` |
| `LinkTopic` | `"Form1"` | `String` |
| `MaxButton` | `True` | `Boolean` |
| `MDIChild` | `False` | `Boolean` |
| `MinButton` | `True` | `Boolean` |
| `MouseIcon` + | | `Picture/Picture` |
| `MousePointer` | `0` | `Integer` |
| `Moveable` | `True` | `Boolean` |
| `Name` | `"Form1"` | `String` |
| `OLEDropMode` | `0` | `Integer` |
| `Palette` + | | `Picture/Picture` |
| `PaletteMode` | `0` | `Integer` |
| `Picture` + | | `Picture/Picture` |
| `RightToLeft` | `False` | `Boolean` |
| `ScaleHeight` | `3000` | `Single` |
| `ScaleLeft` | `0` | `Single` |
| `ScaleMode` | `1` | `Integer` |
| `ScaleTop` | `0` | `Single` |
| `ScaleWidth` | `4500` | `Single` |
| `ShowInTaskbar` | `True` | `Boolean` |
| `StartUpPosition` | `3` | `Integer` |
| `Tag` | `""` | `String` |
| `Text1` + | | `TextBox/TextBox` |
| `Timer1` + | | `Timer/Timer` |
| `Top` | `1170` | `Single` |
| `Visible` | `False` | `Boolean` |
| `WhatsThisButton` | `False` | `Boolean` |
| `WhatsThisHelp` | `False` | `Boolean` |
| `Width` | `4740` | `Single` |
| `WindowState` | `0` | `Integer` |
| `Item 1` + | | `Variant/Object/Label` |
| `Item 2` + | | `Variant/Object/TextBox` |
| `Item 3` + | | `Variant/Object/Timer` |
| `Item 4` + | | `Variant/Object/CommandButton` |
| `Item 5` + | | `Variant/Object/CommandButton` |

**Command1, expanded,** had two children, `Item 1` and `Item 2`, both of type `Variant/Object/CommandButton` and with no
Value. `Item 2`'s rows read `Index` `2` and `Left` `1440`, the second button.

**`Me`'s `Item 5`, expanded,** read `Index` `2`, `Left` `1440`, and the same `hWnd` as Command1's `Item 2`.

So:

- **`Me` lists the form's members and its controls as one list, sorted by name with case ignored** (`hDC` after
  `HasDC`, `hWnd` after `HelpContextID`), with `[_Default]` first. Each control is a node of its own, typed
  `Class/Class`: Label1 between KeyPreview and Left, Text1 and Timer1 between Tag and Top.
- **A control array is one node, typed as its element's class,** `CommandButton/CommandButton`, not as an array. Its
  children are `Item 1` to `Item n`, numbered from 1 whatever the elements' indexes are: here 0 and 2.
- **`Me` ends with one `Item` per control, `Item 1` to `Item 5`, after the sorted list.** They follow the `.frm`'s
  order, both array elements in place: Label1, Text1, Timer1, Command1(0), Command1(2). There are as many as `Count`
  reads, each typed `Variant/Object/<class>` with no Value.
