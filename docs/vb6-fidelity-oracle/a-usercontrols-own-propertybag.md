## A UserControl's own PropertyBag (2026-11-23, #4741, SP6)

**The question.** What does the `PropertyBag` a running UserControl gets in `ReadProperties` hold, and how does each
line saved in the `.frm` read back? The standalone bag is on [A PropertyBag at run time](a-propertybag-at-run-time.md).

**The rig:**
- **Builds:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, on the oracle guest's console, an en-GB Windows 10. Each
  probe ran three ways:
  - `/make`, which loads the control at design time, so its code logs during the build;
  - the made EXE, as native code and as p-code;
  - the IDE, through `vb6.exe /runexit`.
- **The program:** a Standard EXE starting at `Sub Main`, with a private `UserControl1` and a `Form1` holding one
  instance, `UC1`. UC1's block in the `.frm` held the lines VB6 writes for an instance (`Height`, `Left`, `TabIndex`,
  `Top`, `Width`, `_ExtentX`, `_ExtentY`), then hand-written lines, one per value shape.
- **The control** logged `Initialize`, `InitProperties`, `ReadProperties`, `WriteProperties` and `Terminate` to a file,
  each with `Ambient.UserMode`. In `ReadProperties` it read each name under `On Error Resume Next`, with `Set` when
  `IsObject(PropBag.ReadProperty(name))`, and logged `TypeName`, `VarType` and `CStr`.
- **`Sub Main`:** `Load Form1`, `Form1.Show`, a call to a method of UC1 that ran `PropertyChanged "S1"` and a bare
  `PropertyChanged`, `Unload Form1`, then `Set Form1 = Nothing`.

### Where the running control's bag comes from

| Run | What `ReadProperties` reads |
|---|---|
| **The IDE** (`/runexit`) | **the `.frm`'s lines,** with UserMode True. No `WriteProperties` came during the run. |
| **`/make`** | UC1 ran Initialize, then `ReadProperties` with the `.frm`'s lines and `WriteProperties`, both with UserMode False, then Terminate. That was once for the one instance, during the build. |
| **The made EXE,** native and p-code alike | **only what that build-time `WriteProperties` wrote,** plus `_ExtentX` and `_ExtentY`. |

**The obvious guess, that a running control reads its `.frm`, holds only in the IDE.** Here `WriteProperties` wrote
`S1 = "written"` and nothing else of the `.frm`'s. Under `/runexit`, `S1` read `"abc"`, as saved, and every other line
read back too. In both EXEs, `S1` read `"written"`, and every other saved name raised 327.

### How each saved line reads, in the IDE

`/make`'s design-time `ReadProperties` read every row the same as the IDE did, and the rows didn't depend on native or
p-code. Each row is the text after the `=`.

**Strings:**

| Saved | Reads as |
|---|---|
| `"abc"` | String `abc` |
| `""` | String, empty |
| `"say ""hi"""` | String `say "hi"` |
| `"12"` | String `12` |
| `"x" 'comment` | String `x` |
| `"a" "b"` | String `a`: what follows the closing quote is ignored |
| `   "  pad  "` | String `  pad  `, its inner spaces kept |

**Whole numbers, with no point and no exponent:**

| Saved | Reads as |
|---|---|
| `5`, `-3`, `0`, `-0`, `-32768` | Integer |
| `32768`, `-32769`, `40000`, `65535`, `-2147483648` | Long |
| `2147483648`, `3000000000`, `4294967296`, `99999999999999`, `999999999999999`, `-999999999999999` | Double |
| `1000000000000000`, `-1000000000000000`, `9007199254740992`, `9007199254740993`, `12345678901234567890`, `100000000000000000000`, `79228162514264337593543950335` | Decimal |
| `79228162514264337593543950336`, `100000000000000000000000000000` | Double |

So it's Integer, then Long, then Double up to 15 digits, then Decimal, then Double past Decimal's range. It's never Single.

**Whole values written with a point or an exponent:**

| Saved | Reads as |
|---|---|
| `1.`, `1.0`, `1.00000`, `0.`, `-1.`, `1E+0`, `0.1E+1`, `1E2`, `1.5E1`, `1D+3`, `1.5E+3`, `1e5`, `8388609.`, `9999999.` | Single |
| `10000000.`, `12345678.`, `16777215.`, `16777216.`, `16777217.0`, `16777218.`, `-16777215.`, `-16777216.`, `123456789.`, `1E+10`, `1E+14`, `1.5E+14`, `99999999999999.`, `999999999999999.` | Double |
| `1000000000000000.`, `1E+15`, `1E+16`, `1E+20`, `1E+28`, `7.9228162514264337593543950335E+28` | Decimal |
| `1E+300` | Double |

So it's Single up to seven digits, Double up to 15, then Decimal, then Double past Decimal's range. `1D+3` is accepted
as an exponent. `9999999.` is Single and `10000000.` Double, while `16777215.` is Double, so the boundary is seven
digits, not 2^24.

**Values with a fraction:**

| Saved | Reads as |
|---|---|
| `1.5`, `1.50`, `0.1`, `.5`, `00.5`, `0.5`, `-1.5`, `1.5E+0`, `0.0001`, `1E-4`, `0.00010`, `0.00001E+1`, `-1.5E-3`, `10000000.5`, `123456789012345.1234`, `922337203685477.5807` | Currency |
| `1.23456`, `0.00001`, `0.00005`, `1.5E-4`, `0.12345`, `99999.99999`, `1.000001`, `3.14159265358979`, `37623.1702083333`, `-2.5E-05`, `922337203685478.5` | Decimal |
| `1E-30`, `123456789012345678901234567890.5` | Double |

So it's Currency when four places or fewer remain once trailing zeros go and the value fits Currency, then Decimal,
then Double.

**Zero with digits after the point:** `0.0`, `0.00`, `0.000` and `-0.0` read as Currency 0, where `0.` reads as Single
0 and `1.0` as Single 1. A zero seems to keep its trailing zeros, but that's a reading of four rows, not a rule
anything else tests.

**Hex and octal:**

| Saved | Reads as |
|---|---|
| `&H10` | Integer 16 |
| `&HFF&` | Integer 255 |
| `&H0` | Integer 0 |
| `&HFFFFFFFF`, `&HFFFFFFFF&` | Integer -1 |
| `&HFFFF`, `&HFFFF&` | Long 65535 |
| `&H8000` | Long 32768 |
| `&H7FFFFFFF` | Long 2147483647 |
| `&H80000000` | Long -2147483648 |
| `&H8000000F&` | Long -2147483633 |
| `&O17` | Integer 15 |
| `&O177777` | Long 65535 |

So the digits are taken as 32 bits, signed, and read as an Integer where that fits. A trailing `&` changes nothing.

**A comment after a number:** `-1  'True` reads as Integer -1, `0   'False` as Integer 0 and `5 'comment` as Integer 5.
**A Boolean is saved and read as a number,** not as True or False.

**Refused when the form loads:**
- **"had an invalid value":** `+5`, `1#`, `1!`, `1@`, `True`, `abc`, `1,5`, `&H100000000` and `-&H10`;
- **"could not be loaded":** an empty value and `"abc`, which has no closing quote.

VB6 wrote each to the form's `.log`, and `/make` failed. In the IDE, the property was absent from the bag and raised
327. That run then stopped at a dialog after `ReadProperties`, and the probe didn't capture the dialog.

### A Font block

UC1's block held:

```text
BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}
   Name            =   "Arial"
   Size            =   12
   Charset         =   0
   Weight          =   700
   Underline       =   0   'False
   Italic          =   -1  'True
   Strikethrough   =   0   'False
EndProperty
```

`ReadProperty("Font")` was an object, TypeName `Font`: Arial, Size 12, **Bold True** (from Weight 700, with no Bold
line), Italic True, Underline False, Strikethrough False, Weight 700, Charset 0. `ReadProperty("FONT")` gave the same.

### The rest of the bag, the same in every run

- **`TypeName(PropBag)`** is `PropertyBag`.
- **Names are case-insensitive:** `s1` and `FONT` found `S1` and `Font`.
- **A name saved twice in the `.frm`:** the first line wins. `Dup = 1`, then `Dup = 2`, reads Integer 1.
- **The extender's lines aren't in the bag.** `Height`, `Left`, `Top`, `Width`, `TabIndex` and `Name` each raise 327,
  "Data value named 'Height' not found". `_ExtentX` and `_ExtentY` are in it: Integer 2646 in the IDE and in `/make`'s
  design-time read, and Long 2646 in the EXEs.
- **A missing name** without a default raises **327**, "Data value named 'U1' not found". With a default, the default
  comes back in its own type: `7` Integer, `"x"` String, `Empty` Empty.
- **A saved value comes back as saved,** whatever the default's type:
  - `N1 = 5` read with `"9"` or `9.5` gives Integer 5;
  - `S4 = "12"` with `0` gives String `12`;
  - `B1 = -1` with `False` gives Integer -1;
  - `S2 = ""` with `"d"` gives an empty String.
- **`PropBag.WriteProperty` inside `ReadProperties`** raises **425**, "Invalid object use", and the bag is unchanged.
- **`PropBag.Contents`** raises **70**, "Permission denied".
- **During the run,** `PropertyChanged "S1"` and a bare `PropertyChanged` raised nothing. Neither of them, nor `Unload`,
  raised `WriteProperties`, in the IDE or in either EXE.

### What the build-time WriteProperties stores, as the made EXE reads it

`WriteProperties` wrote each value below, and the EXE's `ReadProperties` read it back. Native code and p-code agreed.

| Written | Read back in the EXE |
|---|---|
| `CSng(1.5)` | Single 1.5 |
| `0.1#` | Double 0.1 |
| `True` | Boolean True |
| `#1/2/2003 4:05:06 AM#` | Date, 2 January 2003 04:05:06 |
| `CCur(1.5)` | Currency 1.5 |
| `CByte(7)` | **Integer** 7 |
| `5&` | Long 5 |
| `5` | Integer 5 |
| `Empty` | **String**, empty |
| `"x y"` | String `x y` |
| `3`, with a default of `3` | not written: 327 |
| `Ambient.Font` | a Font: MS Sans Serif, 8.25, Weight 400 |
| `T_Dup` written as `1`, then `2` | Integer 2: here the last write wins |

### The order a form's save writes a project UserControl's instance in (2026-11-28, #5844, IDE, SP6)

**The question.** #5844: HexIDE's project save writes the properties of a project UserControl's instance in
alphabetical order. What order does VB6's IDE write them in?

**The rig.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console by the IDE step driver.
- **The project,** `MvceUc`, was a Standard EXE with #5844's `Form5`, holding `Widget1`, and its `UserControl1`:
  - **`UserControl1`** has public `Label` (String) and `Level` (Long) properties. Each `Property Let` calls
    `PropertyChanged`, and `ReadProperties` reads both.
  - **`Widget1`'s block,** as read: Height, Left, TabIndex, Top, Width, `_ExtentX`, `_ExtentY`, `Label = "hello"` and
    `Level = 7`.
- **Two runs,** each in a fresh vb6.exe. In the first, `WriteProperties` wrote `Label` then `Level`. In the second, it
  wrote `Level` then `Label`.
- **Each save:** Shift+F7 opened `Form5`'s designer, and Tab selected `Widget1`. Ctrl+Right and Ctrl+Left followed,
  then Ctrl+S. VB6 wrote `Form5.frm` 14.6 and 14.8 s after it was copied in.

| `WriteProperties` writes | `Widget1`'s block as VB6 saved it |
|---|---|
| `Label`, then `Level` | byte-identical to the input: Height, Left, TabIndex, Top, Width, `_ExtentX`, `_ExtentY`, Label, Level |
| `Level`, then `Label` | the same, but `Level` before `Label` |

**So VB6 writes the instance in three runs:**
1. the extender's properties, in name order;
2. `_ExtentX` and `_ExtentY`;
3. the control's own properties, in the order its `WriteProperties` writes them.

This is the order the in-box ProgressBar showed (`in-box-controls-the-progressbar.md`, *The save*). The second run
rules out name order for the control's own properties.

**Not measured:**
- a property `WriteProperties` writes at its default, which VB6 is expected to leave out;
- extender properties other than these five, such as Tag, ToolTipText and Visible, for a project UserControl;
- a UserControl from another project or an OCX.

### Not measured

- **An F5 run with the form open in the designer,** where a design-time instance of the control is live. These IDE
  rows come from `/runexit`, which never opened a designer.
- **What the IDE writes to the `.frm`** for each type when it saves. The rows above read lines written by hand.
- **Other saved shapes:** a line that points into the `.frx` (`"Form1.frx":0000`), any `BeginProperty` block but the
  Font, and a Font block with lines missing.
- **The bag of an instance added at run time,** which raises `InitProperties` instead.
- **What happens after the IDE's load-error dialog.**
