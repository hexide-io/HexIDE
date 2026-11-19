## A free-standing StdFont: values of every type, and the font a Size, Charset or Name reads (2026-11-19, #4926, SP6)

**Each member converts its value to its own type first, with VB's usual errors.** Then it takes a range: Weight 0 to
1000, Charset 0 to 255, Size above 0 and below 24576, and a Name of up to 31 characters. Beyond that the font changes to
one Windows has: Size reads a whole number of pixels, and a Charset or a Name can bring another font with it.

**The probe.**
- **Build:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0. Two programs, of 213 and 11
  rows; each logged every row identically in the two builds.
- **Each row:** a fresh `Set f = New StdFont`, with `f` declared `As StdFont`. It reads MS Sans Serif, 8.25, Bold False,
  Weight 400, Charset 0 and no flags.
- **The assignment:** one, `f.<member> = v`, from a Variant `v` holding the value, under `On Error Resume Next`. Its
  `TypeName` is the value's form in the tables: `2` is an Integer, `2.5` a Double, `CLng(12)` a Long, and so on.
- **Logged:** the error, then every member read back.
- **The machine:** the guest is Windows 10 Enterprise LTSC 2021 with its own fonts. Every row that reads a font other than
  the one named depends on which fonts the machine has.
- **Context:** the earlier rows are on [a-controls-font-object.md](a-controls-font-object.md#a-free-standing-stdfonts-assignments-2026-11-18)
  and [a-free-standing-stdfonts-bold-and-weight.md](a-free-standing-stdfonts-bold-and-weight.md) (#4807).

**A failed assignment changed nothing, in every row.** Members a row doesn't mention read as they started.

### Name

| Assignment | Error | Reads afterwards |
|---|---|---|
| `Empty`, `True`, `5.5` | none | unchanged |
| `"arial"`, `"ARIAL"` | none | **Arial**, spelt as the font spells it |
| `"Arial "`, `" Arial"` | none | unchanged: MS Sans Serif |
| `"MS Sans Serif"`, `"ms sans serif"` | none | MS Sans Serif |
| `"Microsoft Sans Serif"`, `"Segoe UI"`, `"Lucida Console"`, `"MS Serif"`, `"Small Fonts"`, `"Courier New"`, `"Tahoma"` | none | that name, 8.25, Weight 400, Charset 0 |
| `"System"` | none | System, **9.75, Bold True, Weight 700** |
| `"Terminal"` | none | Terminal, **6, Bold True, Weight 700, Charset 255** |
| `"Fixedsys"` | none | Fixedsys, **9** |
| `"Webdings"` | none | Webdings, **Charset 2** |
| `"Marlett"` | none | Marlett, **Weight 500, Charset 2** |
| `String(31, "A")` | none | unchanged |
| `String(32, "A")`, `String(33, "A")`, `String(40, "A")` | **380** | unchanged |

### Size

Every Size below is an Integer unless it says otherwise.

| Assignment | Error | Reads afterwards |
|---|---|---|
| `1` | none | **Arial**, 1.5 |
| `2`, `3`, `4`, `5`, `6`, `7` | none | **Small Fonts**, 2.25, 3, 3.75, 5.25, 6 and 6.75 |
| `8`, `10`, `12`, `15`, `17`, `18`, `24`, `30` | none | MS Sans Serif, 8.25, 9.75, 12, 15, 17.25, 18, 24 and 30 |
| `9`, `11`, `13`, `14`, `16`, `19`, `20` | none | **Arial**, 9, 11.25, 12.75, 14.25, 15.75, 18.75 and 20.25 |
| `21` to `29`, but not `24` | none | **Arial**, 21, 21.75, 23.25, 24.75, 26.25, 27, 27.75 and 29.25 |
| `31` to `40` | none | **Arial**, 30.75, 32.25, 33, 33.75, 35.25, 36, 36.75, 38.25, 39 and 39.75 |
| `48`, `72`, `96`, `144` | none | **Arial**, the same size |
| `500`, `1000`, `2000`, `2159`, `2161`, `3000` | none | **Arial**, 500.25, 999.75, 2000.25, 2159.25, 2160.75 and 3000 |
| `3001`, `5000`, `10000`, `20000`, `24575` | none | **Arial**, 3000.75, 5000.25, 9999.75, 20000.25 and 24575.25 |
| `24576`, `24577`, `30000`, `32767` | **6** | unchanged |
| `0.1`, `0.5`, `0.75` | none | **Arial**, 1.5 |
| `8.25` | none | MS Sans Serif, 8.25 |
| `9.5` | none | MS Sans Serif, 9.75 |
| `10.25` | none | **Arial**, 10.5 |
| `12.3` | none | MS Sans Serif, 12 |
| `12.5`, `12.6`, `12.75` | none | **Arial**, 12.75 |
| `-0.5` | **380** | unchanged |
| `CSng(10.5)`, `CCur(10.5)` | none | **Arial**, 10.5 |
| `CByte(12)`, `CLng(12)` | none | MS Sans Serif, 12 |
| `CLng(32768)` | **6** | unchanged |
| `"12.5"` | none | **Arial**, 12.75 |
| `" 12"` | none | MS Sans Serif, 12 |
| `"1e1"` | none | MS Sans Serif, 9.75 |
| `"&H10"` | none | **Arial**, 15.75 |
| `""` | **13** | unchanged |
| `Empty`, `True`, `False` | **380** | unchanged |
| `#1/2/1900#`, a Date | none | **Small Fonts**, 3 |

### Bold, Italic, Underline and Strikethrough

| Assignment | Error | Reads afterwards |
|---|---|---|
| `Bold =` `2`, `-2`, `0.4`, `0.5`, `1.5`, `-0.5`, `"true"`, `"1"`, `CSng(1)`, `CByte(1)`, `CLng(-1)` | none | Bold True, Weight 700, Name **Arial** |
| `Bold =` `"False"`, `"0"`, `Empty`, `CCur(0)` | none | Bold False, Weight 400, Name **Arial** |
| `Bold =` `"x"`, `""` | **13** | unchanged |
| `Italic =` `-1`, `1`, `2`, `0.5`, `"True"`, `"true"`, `"1"`, `CByte(1)`, `CLng(1)` | none | Italic True, Name MS Sans Serif |
| `Italic =` `0`, `"False"`, `"0"`, `Empty` | none | unchanged |
| `Italic =` `"x"`, `""`, `Null` | **13** | unchanged |
| `Underline =` or `Strikethrough =` `True`, `1`, `2`, `"True"` | none | that flag True |
| `Underline =` or `Strikethrough =` `Empty` | none | unchanged |
| `Underline =` or `Strikethrough =` `"x"`, `Null` | **13** | unchanged |

### Weight

| Assignment | Error | Reads afterwards |
|---|---|---|
| `1`, `99`, `101`, `200`, `300`, `399`, `401`, `450`, `500`, `549`, `550.4`, `550.5`, `CByte(255)`, `False`, `Empty` | none | Weight 400, Bold False, MS Sans Serif |
| `552`, `650`, `699`, `701`, `800`, `899`, `999`, `550.6`, `551.5`, `CCur(600)` | none | Weight 700, Bold True, MS Sans Serif |
| `"700"` | none | Weight 700, Bold True, Name **Arial** |
| `-2`, `1001`, `2000`, `32766`, `True`, and `-32768`, a Long | **380** | unchanged |
| `CLng(32768)`, `CLng(-32769)` | **6** | unchanged |
| `"x"`, `""`, `Null` | **13** | unchanged |

### Charset

| Assignment | Error | Reads afterwards |
|---|---|---|
| `3`, `4`, `77`, `86`, `161`, `162`, `163`, `177`, `178`, `186`, `204`, `238`, `254` | none | that Charset, Name **Arial** |
| `129`, `136` | none | that Charset, Name **Ebrima** |
| `130` | none | Charset 130, Name **Nirmala UI Semilight**, **Weight 350** |
| `134` | none | Charset 134, Name **SimSun** |
| `222` | none | Charset 222, Name **Leelawadee UI** |
| `"2"`, `2.5`, `CByte(2)` | none | Charset 2, Name Wingdings |
| `128.5`, `CCur(128)` | none | Charset 128, Name MS PGothic |
| `False`, `Empty` | none | unchanged |
| `-2`, `32767`, `True` | **380** | unchanged |
| `CLng(65536)` | **6** | unchanged |
| `"x"`, `Null` | **13** | unchanged |

### What these say, read as measured

**The value is converted to the member's type before anything else.** Bold, Italic, Underline and Strikethrough take a
Boolean, Weight and Charset an Integer, Size a Currency and Name a String.
- **Booleans:** `0.4`, `2`, `"1"` and `"true"` read as True.
- **Integers:** `550.5` is 550, so Weight 400, and `550.6` is 551, so 700. Charset `2.5` is 2 and `128.5` is 128, as
  `CInt` rounds.
- **Currency:** a Size of `"&H10"` is 16, `"1e1"` is 10 and `#1/2/1900#` is 3.
- **Names:** `Empty`, `True` and `5.5` change nothing and raise nothing, as a name no font has does.
- **The conversion's own errors come first:** `"x"`, `""` and `Null` are 13, and a Long beyond an Integer is 6.

**Then each member takes a range, and 380 outside it.**
- **Weight:** 0 to 1000. 0 and 1000 are on the earlier pages, with −1 and 32767.
- **Charset:** 0 to 255.
- **Size:** above 0. From 24576 up it's 6, not 380.
- **Name:** up to 31 characters. A longer name is 380, even though a name no font has is otherwise accepted.
- **True is −1:** so it's 380 for Weight, Charset and Size, and False is 0, which only Size refuses.

**Size reads a whole number of pixels, at 96 to the inch.** Every Arial row fits this: the size times 4/3, rounded, then
times 3/4, so 13 is 17.33 pixels, rounded to 17, read as 12.75. Two pixels is the least, so 0.1 to 1 read 1.5. The
overflow at 24576 fits it too: that is 32768 pixels.

**Which font a size reads: no rule I would defend.**
- **MS Sans Serif keeps** 8.25, 9.75, 12, 15, 17.25, 18, 24 and 30: pixel heights of 11, 13, 16, 20, 23, 24, 32 and 40.
- **Small Fonts** takes 2 to 7.
- **Arial** takes the rest measured.
- This lists the rows. Which pixel heights MS Sans Serif has is not something these rows show.

**Overturned: "Weight reads only 400 or 700"** ([a-controls-font-object.md](a-controls-font-object.md#a-free-standing-stdfonts-assignments-2026-11-18)).
That holds for MS Sans Serif. A font that has other weights reads them: Marlett reads 500, and Charset 130's Nirmala UI
Semilight reads 350.

**Not measured:**
- any assignment to a font in another state than fresh;
- `Dim f As Object`, where the object, not VB, converts the value;
- Sizes between those above, and a size at exactly half a pixel, such as 1.875;
- Charsets not listed, and names not listed;
- Decimal values, and Dates for any member but Size;
- a control's own Font;
- another machine's fonts.

**In HexIDE (#4926),** read from the code on main at 4cf20ffbb, not run: `TrySetStandalone`
(`IDE/HexIDE.Runtime/AvaloniaInterop/ControlFont.cs:171`) builds #4807's rows only. Every other row here reaches the
refusal at `:166-167`.
