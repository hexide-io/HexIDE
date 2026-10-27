## Assigning to a control property (2026-10-12, #768)

Measured to decide what `Label1.Caption = 5` stores, and what every other property does with a value of another
type. Real `vb6.exe` 6.00.8176 (the original release), guest locale **en-GB** (decimal point `.`, thousands separator `,`).

**Harness.** One Standard EXE, `Startup="Form1"`, with `Label1`, `Text1`, `Command1`, `Check1` and a disabled
`Timer1` (Interval 500). Four `/make` runs over PowerShell Direct, each `Form_Load` writing a log and ending
with `End`. The exe ran in the guest's session 0. Nothing here needs painting, so the interactive-session rig was
not needed. Every probe is one line under `On Error Resume Next`:

```vb
Label1.Caption = "(unset)": Err.Clear: Label1.Caption = 5: e = Err.Number: W "Label.Caption = 5", e, Label1.Caption
```

So each row resets the property first, and a refused assignment shows as the reset value, unchanged. `W` logs
the value read back, its `TypeName`, and the `Err.Number` the assignment raised.

### A String property takes CStr of anything

| Assignment | Reads back | Err |
|---|---|---|
| `Label1.Caption = 5` / `= -7` / `= Variant 42` | `5` / `-7` / `42` | 0 |
| `= 1.5` | `1.5` (the locale's decimal separator) | 0 |
| `= CSng(1) / 3` | `0.3333333` | 0 |
| `= 1E+20` / `= 12345678901234#` / `= CCur(2.5)` | `1E+20` / `12345678901234` / `2.5` | 0 |
| `= 0.1 + 0.2` / `= CSng(1E+7)` | `0.3` / `1E+07` | 0 |
| `= True` / `= False` | `True` / `False` | 0 |
| `= Empty` | (empty string) | 0 |
| `= #1/2/2003#` / `= #10:30:00 AM#` / `= #1/2/2003 10:30:00 AM#` | `02/01/2003` / `10:30:00` / `02/01/2003 10:30:00` | 0 |
| `= Null` | `(unset)`, **unchanged** | **94** |
| `Text1.Text`, `Command1.Caption`, `Check1.Caption`, `Me.Caption` `= 5` | `5` | 0 |
| `Text1.Text = Null`, `Me.Caption = Null` | unchanged | **94** |
| `Label1.Tag = 5` / `= True` / `= CSng(1) / 3` | `5` / `True` / `0.3333333`, **`TypeName` String** | 0 |
| `Label1.Tag = Null` | unchanged | **94** |

**This is coercion-on-store, exactly** (see that section): a String property behaves as `Dim s As String : s = x`,
Null included. And **`Tag` is a String**, not a Variant: `TypeName(Label1.Tag)` is `String` after `Tag = 5`.

### A numeric property converts, and refuses what it cannot convert before changing

| Assignment | Reads back | Err |
|---|---|---|
| `Label1.Left = "300"` | `300` (`Single`) | 0 |
| `Label1.Left = "abc"` / `= ""` | unchanged | **13** |
| `Label1.Left = Null` | unchanged | **94** |
| `Label1.Left = Empty` / `= True` | `0` / `-1` | 0 |
| `Label1.Left = 1.5` / `= "1.5"` / `= 2.5` / `= 2.4` | **`2`** / **`2`** / **`2`** / `2` | 0 |
| `Label1.Left = "1,5"` | **`15`** | 0 |
| `Label1.Left = 1E+20` | unchanged | **6** |
| `Timer1.Interval = "1000"` / `= 2.5` / `= 3.5` | `1000` / `2` / `4` (`Long`) | 0 |
| `Timer1.Interval = "abc"` / `= Null` / `= 3000000000#` | unchanged | 13 / 94 / **6** |
| `Timer1.Interval = 65535` | `65535` | 0 |
| `Timer1.Interval = 65536` / `= True` | unchanged | **380** |

### An enum or colour property converts, then range-checks with 380

| Assignment | Reads back | Err |
|---|---|---|
| `Check1.Value = 1` / `= "1"` / `= 2` | `1` / `1` / `2` (`Integer`) | 0 |
| `Check1.Value = 1.5` / `= 1.6` / `= 2.5` | `2` / `2` / `2` | 0 |
| `Check1.Value = 2.6` / `= 3` / `= -1` / `= True` | unchanged | **380** |
| `Check1.Value = "abc"` / `= ""` / `= Null` / `= 40000` | unchanged | 13 / 13 / 94 / **6** |
| `Label1.MousePointer = n`, n from 0 to 15, and 99 | `n` | 0 |
| `Label1.MousePointer = -1`, `16`, `17`, `98`, `True` | unchanged | **380** |
| `Label1.MousePointer = "2"` / `= 40000` | `2` / unchanged | 0 / 6 |
| `Check1.Appearance = 0` / `= 1` | `0` / `1` | 0 |
| `Check1.Appearance = -1`, `2`, `3`, `True` | unchanged | **380** |
| `Label1.BackColor = "255"` / `= 255.6` / `= "&HFF"` | `255` / `256` / `255` (`Long`) | 0 |
| `Label1.BackColor = True` / `= -2` | unchanged | **380** |
| `Label1.BackColor = 1E+20` / `= Null` | unchanged | 6 / 94 |

The OLE_COLOR range, probed byte by byte:

| High byte | Low 24 bits accepted | Refused (380) |
|---|---|---|
| `&H00` (RGB) | any | |
| `&H01` (palette index) | `&H000000` to `&H00FFFF` | `&H010000` and above (`&H1010000`, `&H1FFFFFF`) |
| `&H02` (palette-relative RGB) | any (`&H2000000`, `&H2000001`, `&H2FFFFFF`) | |
| `&H80` (system colour) | `&H00` to `&H18` | each of `&H80000019` to `&H80000020` (probed one by one for #774), `&H80000030`, `&H80000100` |
| `&H03`, `&H04`, `&H7F`, `&HC0`, `&HFF` | | all (`&H3000000`, `&H4000000`, `&H7FFFFFFF`, `&HC0000000`, `&HFF000000`) |

That is OLE's own colour validation, as far as it was probed. `&H18` is `COLOR_INFOBK`, the last entry of the
table VB6 exposes; the refusal from `&H19` on is measured, not inferred from Windows' `COLOR_*` numbering, which
has a hole at `&H19` and goes on past it.

**A palette colour is kept as given** (measured for #774): `&H1000001` reads back `16777217`, `&H100000F`
`16777231`, `&H100FFFF` `16842751`, `&H2000001` `33554433` and `&H2FFFFFF` `50331647`, each the same Long that was
assigned. What it paints as was not measured.

### The rule

**A property assignment is coercion-on-store into the property's type, plus a range check that a declared variable
does not have.** The conversion raises 13, 94 or 6 exactly as `Dim x As T : x = value` would. A value that converts
but is not one the property accepts is **380, Invalid property value**, and that is how `True` fails for an enum:
it converts to -1. The order matters and is measured: `Check1.Value = 40000` is **6**, not 380, so the Integer
conversion comes before the range check. Nothing is changed when either step refuses.

### What this overturned, or found

- **`Left` is not a Single inside, though it reads back as one.** `Left = 1.5` reads back 2, and so does 2.5
  (half to even). `Left = 1E+20`, which a Single holds easily, is Overflow. A position is held as a whole number
  of twips in a Long. That twips are the unit is inferred from the default ScaleMode, since the probe did not
  change it.
- **`"1,5"` is fifteen**, not one and a half, and not an error: in the en-GB locale the comma is a thousands
  separator, and VB6 skips it. HexIDE's string-to-number rule allows none and raises 13 (#770). Not probed: a
  locale whose decimal separator is the comma.
- **`MousePointer` 4, 5, 13 and 14 are accepted.** `VBCursorType`'s comment called them gaps in VB6's numbering.
  They are not (#120). Their names and pointers are measured under "MousePointer's names and pointers".
- **`Tag` is a String.** HexIDE declared it as an object and stored whatever it was given.
- **`CStr` of a Date leaves out what is zero.** A midnight date has no time, and a time has no date. HexIDE wrote
  both (#771). A Single shows seven significant digits and a Double fifteen. HexIDE showed more (#772). Both were
  fixed by #779; see "Turning a number or a date into text".

### Measured for the review of #774

The same harness, three more `/make` runs, with an `HScrollBar` added (Min 0, Max 100, and a second set in the
`.frm` to Min 100, Max 0).

**A scroll bar's `Value` is range-checked, from a String too, and either way round:**

| Assignment (Min 0, Max 100, reset to 10) | Reads back | Err |
|---|---|---|
| `HScroll1.Value = "50"` / `= 100` / `= "100"` | `50` / `100` / `100` | 0 |
| `= 101` / `= 150` / `= "150"` / `= -1` / `= "-1"` | `10`, **unchanged** | **380** |

| Assignment (Min 100, Max 0, reset to 100) | Reads back | Err |
|---|---|---|
| (as loaded) `Min`, `Max` | `100`, `0` | 0 |
| `Value = 50` / `= 0` / `= 100` / `= "50"` | `50` / `0` / `100` / `50` | 0 |
| `= 150` / `= -1` / `= True` | unchanged | **380** |
| `= "abc"` | unchanged | 13 |
| `= Empty` | `0` | 0 |
| `= 2.5` / `= 99.5` | `2` / `100` (half to even) | 0 |

So a bar whose Min exceeds its Max is legal, and the range check takes the two either way.

**A Boolean property is CBool**, and CBool of Empty is False:

| Expression or assignment | Value | Err |
|---|---|---|
| `CBool(Empty)` | `False` | 0 |
| `CLng(Empty)` / `CDbl(Empty)` | `0` (Long) / `0` (Double) | 0 |
| `CBool("")` | | 13 |
| `Command1.Enabled = Empty` / `= 0` (reset to True) | `False` / `False` | 0 |
| `Command1.Enabled = Null` / `= ""` | `True`, **unchanged** | **94** / **13** |

**.NET's NaN and Infinity are not VB6 numbers:**

| Expression or assignment | Value | Err |
|---|---|---|
| `CDbl("NaN")`, `CDbl("Infinity")`, `CDbl("-Infinity")`, `CLng("NaN")` | | **13** |
| `CDbl("1E+400")` | | **6** |
| `Label1.Left = "NaN"` / `"Infinity"` / `"-Infinity"` / `"1.#INF"` / `"1.#QNAN"` | unchanged | **13** |
| `Val("NaN")` | `0` (Double) | 0 |

**Hex/octal Strings use signed widths, and numeric conversions refuse a trailing `&`:**

| Expression | Value | Err |
|---|---|---|
| `CLng("&H8000000F")` / `CDbl("&H8000000F")` / `Val("&H8000000F")` | `-2147483633` (Long / Double / Double) | 0 |
| `CLng("&HFFFFFFFF")` / `CLng("&O37777777777")` | `-1` | 0 |
| `CLng("&H100000000")` | | 6 |
| `CDbl("&H100000000")` | `4294967296` | 0 |
| `CLng("&HFFFF")` / `CLng("&H8000")` / `CLng("&H10000")` | `65535` / `32768` / `65536` | 0 |
| `CInt("&HFFFF")` | `-1` (Integer) | 0 |
| `CDbl("&H10&")`, `CLng("&HFFFF&")`, `CLng("&H8000000F&")` | | **13** |
| `Text1.BackColor = "&H8000000F"` / `= "-2147483633"` | `-2147483633` (Long), a system colour | 0 |
| `Text1.BackColor = "&H8000000F&"` | unchanged | 13 |

**Width follows value, including leading zeros (#788, follow-up measurement).** Provenance: the
issue's measurement comment reports 36 Strings passed through CInt, CLng, CByte, CDbl, CCur,
CSng, Val and CDec on vb6.exe SP6, native and p-code agreeing (PR #2279, debb22e9).
The following extends the table above; it does not establish behaviour beyond the measured forms.

| Unsigned digit value V | CInt | Val | Other conversions |
|---|---|---|---|
| V ≤ &HFFFF | signed 16 bits | signed 16 bits | V, then destination range check |
| &H10000 ≤ V ≤ &HFFFFFFFF | signed 32 bits, then range check | signed 32 bits | signed 32 bits, then destination range check |
| V > &HFFFFFFFF | low 32 bits, signed, then range check | low 32 bits, signed | CByte uses low 32 bits then range check; CLng raises 6; CDbl/CCur/CSng/CDec use V whole |

Thus CInt/Val of `"&H8000"` give -32768, of `"&HFFFF"` -1; CLng gives
32768 and 65535 respectively, and CByte of the latter raises 6. CInt of `"&H10000"`
raises 6, of `"&HFFFFFFFF"` gives -1, of `"&H100000000"` gives 0, and of
`"&HFFFFFFFFF"` gives -1. Leading zeros do not alter these readings. Octal follows
the same rule: `"&O100000"` corresponds to `"&H8000"`, and `"&O40000000000"`
to `"&H100000000"`. The result subtype is the conversion's named type; Val returns Double.

A space before or after `"&H10"` is accepted as 16. Each conversion raises 13 for
`"&H 1"`, `"&H"` and `"&HG"`; Val instead returns 1, 0 and 0 respectively.

**A colour reads back as its Long, and a hex literal is typed by its value:**

| Statement, after `Text1.BackColor = &HFF` | Value | Err |
|---|---|---|
| `Text1.BackColor` | `255` (Long) | 0 |
| `Text1.BackColor = 255` / `Text1.BackColor = vbRed` | `True` / `True` | 0 |
| `Text1.BackColor + 1` | `256` (Long) | 0 |
| `Text1.BackColor = &HFF00` | unchanged | **380** |

**A control where a value is wanted is its default property:**

| Statement | Result | Err |
|---|---|---|
| `s = Text1` / `s = Label1` (s As String) | `"Text1"` / `"Label1"`, the Text and the Caption | 0 |
| `v = Text1` / `v = Command1` / `v = Check1` (v As Variant) | `"Text1"` String / `False` Boolean / `0` Integer | 0 |
| `Label1 = "x"` / `Text1 = "y"` / `Check1 = 1` | Caption `x` / Text `y` / Value `1` | 0 |

**`ListIndex` is an Integer with a range of -1 to ListCount - 1**, the same in a ListBox and a ComboBox (one more
`/make` run, three items added in `Form_Load`, and an empty ListBox beside them):

| Assignment (three items, reset to 0) | Reads back | Err |
|---|---|---|
| `List1.ListIndex = 2` / `= -1` / `= "1"` | `2` / `-1` / `1` | 0 |
| `= True` / `= Empty` | `-1` / `0` | 0 |
| `= 1.5` / `= 2.5` | `2` / `2` (half to even) | 0 |
| `= 3` / `= "3"` / `= -2` | `0`, **unchanged** | **380** |
| `= 40000` | unchanged | **6**: an Integer overflows before the range is checked |
| `= Null` / `= "abc"` | unchanged | 94 / 13 |
| `Combo1.ListIndex = 2` / `= "1"` / `= -1` | `2` / `1` / `-1` | 0 |
| `Combo1.ListIndex = 3` / `= -2` | unchanged | 380 |
| `List2.ListIndex = -1`, empty list | `-1` | 0 |
| `List2.ListIndex = 0` / `= -2`, empty list | `-1`, unchanged | 380 |

**`CBool` reads a number in a String as `CDbl` does** (one more expression batch): `CBool("NaN")`,
`CBool("Infinity")`, `CBool("-Infinity")` and `CBool("1.#INF")` are 13, `CBool("1E+400")` is 6, and `CBool("&H10")`
is `True`. `CStr(Empty)` is `""`.

### What the review's measurements overturned

- **`&HFF00` is not a green.** A test written for #774 restored `&HFF00` through a Variant and expected it back.
  VB6 refuses it with 380, because four hex digits make an Integer literal, and `&HFF00` is -256, which is no
  colour. The Long `&HFF00&` is the green. That was our own expectation, and it was wrong.
- **A trailing `&` belongs to the literal, not to a String.** HexIDE trimmed it from `"&H10&"`; VB6 raises 13.
- **The width a hex String wraps at follows the target.** `CLng("&HFFFF")` is 65535, and `CInt("&HFFFF")` is -1.
  Whether `CInt` wraps at 16 bits for more than four digits was not measured.
- **`CDbl("1E+400")` was Infinity in HexIDE**, where VB6 raises 6. .NET parses it to Infinity without complaint,
  exactly as it parses the word `"Infinity"`.
- **`Enabled = Empty` is False with no error.** HexIDE raised 13, because its CBool sent Empty through the
  String-to-number parse. The code comment beside it already said False.

### HexIDE, after #774

Property assignment goes through `VbNumeric.CoerceOnStore` and the measured range checks above, in
`AvaloniaInteroperability.ConvertForAssignment` (and `CheckScrollBarValue` for a scroll bar's `Value`).
`HexIDE.Integration.Tests` `PropertyAssignmentTests` covers, through real controls and the interpreter:
- the String-property rows, the Single, Double, Currency and Decimal ones included, since #779;
- the position, `Interval`, scroll bar `Value`, `Enabled`, `CheckBox.Value`, `MousePointer` (read-back and errors, since #769) and
  `BackColor` rows, the colour checked as stored and as left unchanged;
- copying a colour from one control to another, through a Variant, and into a caption;
- Null into `Tag`, and the two `Tag` idioms below.

`ConversionFunctionsTests` covers CBool of Empty, the NaN and Infinity spellings, and `CDbl("1E+400")`.

`ListIndex` is range-checked as measured, and it and a scroll bar's `Value` convert as an Integer, so
`ListIndex = 40000` is 6. That the scroll bar's `Value` is 6 there too is inferred from its `TypeName`, Integer, and
was not measured. `Tag = Empty` stores `""`; it used to end the run as Internal error 51. `CBool` reads its number
through `CDbl`'s parse.

A Date into a String property is written as `CStr` writes it, in the program's locale: `Label1.Caption = #1/2/2003#`
reads back `02/01/2003` under en-GB, as measured above. It was refused until the interpreter had the user's locale
(#777, #794), because before that it would have put the month first on every machine.

**Refused with `NotImplementedException`, rather than answered wrong:**
- **A palette colour** (#786), from any type. HexIDE has no colour that holds one.
- **Superseded by #787:** `Value` on a scroll bar whose Min exceeds its Max now maps onto an ascending
  Avalonia range in reverse, preserving the loaded value and the measured assignments above.
- **A control, an array or an object on the right** of a property assignment. For a control, VB6 reads its default
  property (#790). (Superseded for a class instance and the in-box objects by #840, "An object used as a value": a
  class with no default member raises 438, one with a default member is refused naming #174, and Err stands for its
  Number. A control and an array are still refused here.)
- **A colour read from a Timer or a scroll bar** (#801). VB6 gives neither control a colour, but both bind one as
  every templated control does, so the read would give black. Until #795 a Label's and a TextBox's colours were
  refused here too, because neither loaded them where a read could find them; both read back now, as measured
  under "A Label's colours, read back and painted".

**Where HexIDE still differs:**
- **`Tag` holds what it is given**, not a String: `Tag = 5` reads back the Integer 5, where VB6 has `"5"`. Making
  it a String waits on #211, because `If Tag = 1` and `Tag + 1` rely on it. An unset `Tag` reads back `""`, and
  Null is 94, as in VB6.
- ~~**Positions are not rounded to whole twips** (#791).~~ They are since #933, which holds them in twips at the
  property: see *A control's position at run time*.
- **A comma in a numeric String** (#770).
- **Hex/octal Strings** now use the measured widths and refuse a trailing `&` (#788).
- **Reading a control as a value** outside a property assignment (`s = Text1`) gives a .NET type name (#790).
- **Not measured:** `Label1.Caption = o` with `o` set to `Nothing` never reaches the conversion in HexIDE; what
  VB6 raises there is unmeasured.


