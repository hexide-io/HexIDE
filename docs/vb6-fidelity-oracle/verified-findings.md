## Verified findings

All rows below are **actual `vb6.exe` output** (`value | TypeName`). These are the source of the interpreter's
test expectations (`VbNumeric`, `OperatorTests`, `ArithmeticFidelityTests`, `DateCurrencyVariantTests`,
`LiteralTests`).

### Arithmetic result types — `+ − *`

Ladder (widest wins): **Byte < Integer < Long < Single < Double < Currency < Decimal**. Boolean and Empty
behave as Integer. `Byte` and `Boolean` promote to Integer *only when the other operand forces it* — `Byte+Byte`
stays `Byte`. A result outside the result-type's range is `Err 6` — there is no overflow auto-promotion.

> **⚠️ The "ladder" framing is OVERTURNED — see *Numeric type pairs* (2026-09-03) at the end of this file.**
> The individual rows below are all correct and were re-confirmed by the full 49-pair sweep. The
> generalisation drawn from them is not: `+` and `-` share this table, but **`*` differs from it in four
> cells, `/` is a third table, `\`/`Mod` a fourth, and `^` a fifth.** `Long + Single` is also `Double`, not
> Single, which no row here happened to cover. Two exceptions to the "no auto-promotion" sentence were also
> found: `Byte * Byte` wraps mod 256 silently, and unary minus of a type's minimum returns the minimum.
> **Corrected 2026-10-14:** the `Byte * Byte` wrap is native code only. P-code and the IDE raise 6, as for `Byte + Byte`;
> see *Numeric type pairs*.
> Read that section before implementing from this one.

> **That last sentence is about DECLARED types only.** Every row below was measured through declared
> variables, and the qualifier turns out to be load-bearing: with **Variant** operands the same expressions
> do not raise at all, they widen (Integer → Long → Double). See *Variant arithmetic promotes on overflow*
> further down, which is the common path, since most VB6 code does not declare types.

| expression | result | type |
|---|---|---|
| `CByte(100) + CByte(100)` | 200 | **Byte** |
| `CByte(200) + CByte(100)` | — | **Err 6 (overflow)** — proves Byte+Byte → Byte |
| `CByte(100) + 5` | 105 | Integer |
| `True + CByte(100)` | 99 | Integer (Boolean → Integer; `True` = −1) |
| `5 + 5` | 10 | Integer |
| `30000 + 30000` (via Integer vars) | — | **Err 6** |
| `5 + 3&` | 8 | Long |
| `3& + 3&` | 6 | Long |
| `5 + 2!` | 7 | Single |
| `2! + 2!` | 4 | Single |
| `5 + 2#` | 7 | Double |
| `5 * 5` | 25 | Integer |
| `30000 * 30000` (via vars) | — | **Err 6** |

### Division `/` (real division)

Result is **Single iff the widest operand is Single** (both operands in {Byte, Integer, Single}); otherwise
**Double**. Notably **int/int → Double** and **Long/anything → Double**.

| expression | result | type |
|---|---|---|
| `5 / 2` | 2.5 | **Double** (not Single!) |
| `5 / 2!` | 2.5 | Single |
| `5 / 2#` | 2.5 | Double |
| `5.5! / 2.5!` | 2.2 | Single |
| `40000 / 2` (Long/Int) | 20000 | Double |
| `CLng(5) / CLng(2)` | 2.5 | Double |
| `10@ / 4` | 2.5 | Double |

### Integer division `\` and `Mod`

Operands are **banker's-rounded to an integer first**, then the integer op. Result is **Integer only when both
operands are Byte/Integer/Boolean; otherwise Long**. `\`/`Mod`/`/` by zero → **`Err 11` (Division by zero)**.

| expression | result | type |
|---|---|---|
| `5 Mod 2` | 1 | Integer |
| `5.5 Mod 2` | 0 | **Long** (5.5 → 6; 6 Mod 2) |
| `5.5! Mod 2` | 0 | Long |
| `-5.5 Mod 2` | 0 | Long |
| `7 \ 2` | 3 | Integer |
| `7.6 \ 2` | 4 | **Long** (7.6 → 8) |
| `7.5 \ 2` | 4 | Long (7.5 → 8, half-to-even) |

### `^`, unary `−`, `CInt` rounding

| expression | result | type / note |
|---|---|---|
| `2 ^ 10` | 1024 | Double (always) |
| `-iv` where `iv` is Integer −32768 | −32768 | **Integer, no error** — two's-complement wrap (we treat as overflow; documented divergence) |
| `CInt(2.5), CInt(3.5), CInt(-2.5), CInt(0.5)` | 2, 4, −2, 0 | banker's (round-half-to-even) |

### Date

Serial epoch is **1899-12-30** (`CDbl(#1/1/2000#)` = **36526**).

| expression | result | type |
|---|---|---|
| `#1/1/2000#` | 2000-01-01 | Date |
| `#1/1/2000# + 31` | 2000-02-01 | Date |
| `#1/1/2000# - 1` | 1999-12-31 | Date |
| `#1/10/2000# - #1/1/2000#` | 9 | **Double** (day difference) |
| `#1/1/2001# > #1/1/2000#` | True | Boolean |

**Runtime error codes** (bug-hunt error-fidelity fixes — the interpreter must raise these **trappable** errors, not
raw .NET exceptions). Pinned via the `On Error Resume Next` + `Err.Number` harness:

| operation | `Err.Number` | note |
|---|---|---|
| `ReDim a(-2)` · `ReDim a(2 To 0)` · `ReDim a(5 To 1)` | **9** | inverted/negative bounds → Subscript out of range |
| `ReDim a(0 To 0)` | 0 (ok) | a size-1 array — the empty case `(0,-1)` is also valid |
| `UBound(a, 2)` on a rank-1 array · `LBound(a, 0)` | **9** | dimension < 1 or > rank |
| `CDate(1E30)` · `CDate(2958466)` · `CDate(-657435)` | **6** | serial outside Date range → **Overflow** (NB **not** 13) |
| `CDate(0)` | 0 (ok) | serial 0 = 1899-12-30 00:00 |

### Currency (`@`)

**Currency dominates Double** in `+ − *` (the biggest surprise). Fixed 4 dp, banker's rounding, hardware range
→ `Err 6`. `/` still drops to Double.

| expression | result | type |
|---|---|---|
| `10@ + 5` | 15 | Currency |
| `10@ + 20@` | 30 | Currency |
| `10@ + 1.5` | 11.5 | **Currency** (not Double!) |
| `10@ * 3` | 30 | Currency |
| `1.23455@` | 1.2346 | Currency (4-dp, 5→even 6) |
| `1.23445@` | 1.2344 | Currency (4-dp, 4 stays even) |
| `9e14@ + 1e14@` | — | **Err 6** |

### Variant — Empty / Null

| expression | result | type |
|---|---|---|
| `Empty & "x"` | `x` | String |
| `Empty + 5` | 5 | Integer (Empty acts as 0) |
| `Null + 1` | Null | Null (propagates) |
| `Null & "x"` | `x` | String (`&` treats Null/Empty as "") |

### Hex `&H` / Octal `&O` — unsigned bit-patterns (NOT colours)

No suffix: fits 16 bits → Integer via **16-bit two's-complement**; else fits 32 bits → Long via **32-bit
two's-complement**. `&` forces the Long reading (no 16-bit wrap); `%` forces the Integer reading. **Octal wraps
identically to hex.**

| literal | result | type |
|---|---|---|
| `&HFF` | 255 | Integer |
| `&H7FFF` | 32767 | Integer |
| `&H8000` | −32768 | Integer (16-bit wrap) |
| `&HFFFF` | −1 | Integer |
| `&H10000` | 65536 | Long |
| `&HFFFFFFFF` | −1 | Long |
| `&HFFFF&` | 65535 | Long (`&` forces Long) |
| `&H8000&` | 32768 | Long |
| `&HFFFF%` | −1 | Integer (`%` forces Integer) |
| `&O17` | 15 | Integer |
| `&O77777` | 32767 | Integer |
| `&O177777` | −1 | Integer (16-bit wrap) |
| `&O37777777777` | −1 | Long (32-bit wrap) |

VB6 colours are numeric `OLE_COLOR` (`0x00BBGGRR`, or `0x80……` = system colour). A hex value assigned to a
colour property is converted at the property boundary (`VBColor.FromOle`), not at literal time.

### Math intrinsics (interpreter-core Phase 3.3)

| expression | result | type |
|---|---|---|
| `Abs(-4)` | 4 | **Integer** (Abs preserves the operand type) |
| `Abs(-4.5)` | 4.5 | Double |
| `Int(-2.5)` | −3 | Double (**floor** toward −∞) |
| `Fix(-2.5)` | −2 | Double (**truncate** toward zero) |
| `Int(2.7)` / `Fix(2.7)` | 2 / 2 | Double |
| `Sgn(-5)` | −1 | Integer |
| `Sqr(9)` | 3 | Double |
| `Round(2.5)` / `Round(3.5)` | 2 / 4 | Double (**banker's** rounding) |
| `Round(2.567, 2)` | 2.57 | Double |
| `TypeName(Sqr(9))` | `Double` | — |

**`Rnd` — the exact 24-bit LCG (pinned).** Fresh state (no `Randomize`) is seed `&H50000`; each draw advances
`seed = (seed * &H43FD43FD + &HC39EC3) And &HFFFFFF` and returns `seed / 2^24` as a **Single**. Verified first
draws: `0.7055475, 0.533424, 0.5795186` (state after draw 1 = `11837123`, and `11837123 / 2^24 = 0.7055475`
bit-exact). `Rnd(0)` returns the last value **without advancing**; `Rnd(<0)` reseeds from the argument.
`Randomize [n]` only varies the starting seed — our reseed fold is deterministic-but-not-bit-identical to VB6's
(documented divergence; the fixed-seed sequence itself *is* bit-exact). **Later measurement, 2026-10-12:**
`Randomize n` does not *restart* the sequence either. The same `n` twice gives two different draws in VB6, and
only `Rnd(-1)` beforehand makes it repeat; see "A form-based game's runtime surface".

### Inspection intrinsics (interpreter-core Phase 3.4)

`TypeName`/`VarType` (`vbEmpty=0, vbNull=1, vbInteger=2, vbLong=3, vbSingle=4, vbDouble=5, vbCurrency=6,
vbDate=7, vbString=8, vbObject=9, vbBoolean=11, vbVariant=12, vbDecimal=14, vbByte=17`; arrays add
`vbArray=8192` and `TypeName` appends `()`): `TypeName(Array-of-Integer) = "Integer()"`, `VarType = 8194`;
a `Variant` array (`Array(1,2,3)`) → `"Variant()"`, `8204`.

`IsNumeric` is **far more permissive than expected** (all verified True unless noted):

| input | IsNumeric | note |
|---|---|---|
| `True` (Boolean) | **True** | Booleans are numeric |
| `Empty` | **True** | Empty coerces to 0 |
| a `Date` value | **False** | dates are *not* numeric |
| `Null` | False | |
| `"&HFF"`, `"&O17"` | **True** | hex/octal *strings* |
| `"1,234"` | **True** | thousands separators |
| `"(12)"` | **True** | accounting negative |
| `"12-"` | **True** | trailing sign |
| `"  12  "`, `"-12"`, `"+12"`, `"1E3"` | True | whitespace / signs / exponent |
| `"$12"` | False | currency symbol rejected |
| `"123&"`, `""`, `"abc"` | False | trailing type-char / empty / non-numeric |

`IsDate`: a `Date`, or a **string** parseable as date/time (`"1/2/2020"`, `"2020-01-02"`, `"10:30:00 AM"`) →
True; a **bare number is False** (`IsDate(40000) = False`, even though `CDate(40000)` works); `Empty`/`Null` →
False. `IsEmpty`/`IsNull`/`IsObject`/`IsArray` each report only their own subtype (`IsEmpty(0)=False`,
`IsNull(Empty)=False`, `IsObject(Nothing)=True`).

> **Approximations (documented in-code):** VB6's `IsNumeric` string grammar is locale-influenced and lenient
> about comma grouping and accounting notation; HexIDE strips a surrounding `()` and one trailing sign, removes
> group separators, and parses the remainder — the common cases match but exotic comma placements may differ.

### Array intrinsics (interpreter-core Phase 3.5)

`Array`/`Split`/`Join`/`Filter` are all **0-based** and 1-D. Verified:

| expression | result |
|---|---|
| `Array(10, 20, 30)` | Variant array, `LBound 0`, `UBound 2`, `TypeName "Variant()"`, `VarType 8204` |
| `Split("a,b,c", ",")` | **`String()`** array (not Variant), `["a","b","c"]` |
| `Split("a,,b", ",")` | `["a","","b"]` — empty middle elements **preserved** |
| `Split("")` | **empty array** — `LBound 0`, `UBound −1` |
| `Split("one two three")` | default delimiter is a **space** → `["one","two","three"]` |
| `Split("a-b-c-d", "-", 2)` | limit 2 → `["a","b-c-d"]` (remainder in the last element) |
| `Join(Array("a","b","c"), "-")` | `"a-b-c"` |
| `Join(Array("a","b","c"))` | `"a b c"` — default delimiter is a **space** |
| `Filter(Array("apple","banana","cherry"), "an")` | `["banana"]` (keep matches) |
| `Filter(…, "an", False)` | `["apple","cherry"]` (drop matches) |
| `Filter(…, "an", True, vbTextCompare)` | case-insensitive substring match |
| `Filter(Array("apple","pear"), "xyz")` | **empty array** — `LBound 0`, `UBound −1` |

> **Deferral:** `Array()`'s lower bound follows `Option Base` in VB6; HexIDE's builtin registry is static and
> has no access to the module's `Option Base`, so `Array()` is always 0-based (correct for the default; a
> documented divergence under `Option Base 1`). `Split`/`Join`/`Filter` are always 0-based regardless — no
> divergence there.

### Mid statement + Erase (2026-08-10 doc-debt sweep)

The **Mid statement** `Mid(target, start[, length]) = replacement` overwrites characters **in place** — the target's
total length **never changes**. Chars written = `min(length‖remaining, Len(replacement), Len(target) − start + 1)`.
`start` is 1-based; **out of `[1, Len(target)]` → Err 5** (Invalid procedure call).

| statement (target `s`) | result |
|---|---|
| `s="ABCDEF": Mid(s,2,3)="xy"` | `AxyDEF` — replacement shorter than length: only its 2 chars written |
| `s="ABCDEF": Mid(s,2,3)="wxyz"` | `AwxyEF` — replacement truncated to `length`=3 |
| `s="ABCDEF": Mid(s,3)="xyz"` | `ABxyzF` — no length: to end / replacement length |
| `s="ABC": Mid(s,2)="XYZ"` | `AXY` — clamps to the 2 remaining chars |
| `Mid(s,0)=…` · `Mid(s,Len(s)+1)=…` | **Err 5** |

**Erase** — a **dynamic** array is *freed* (undimensioned): afterward `UBound`/`LBound`/index → **Err 9**, and it can
be `ReDim`'d again. A **fixed** array keeps its bounds and resets every element to the type default (Integer → 0,
String → `""`). *(HexIDE residual: a fixed array of UDT/class elements isn't reset — the `VBArray` lacks the
interpreter context — scalar fixed arrays are faithful.)*

### Date/Time intrinsics (interpreter-core Phase 3.6)

Return types (verified): `Year/Month/Day/Hour/Minute/Second/Weekday/DatePart` → **Integer**;
`DateAdd/DateSerial/TimeSerial/DateValue/Now/Date` → **Date**; `DateDiff` → **Long**; `Timer` → **Single**.

| behaviour | verified |
|---|---|
| `Weekday(<Sunday>)` default | `1` (a **vbSunday** base); `Weekday(<Sunday>, vbMonday)` → `7` |
| `DateSerial(2020, 13, 1)` | `2021-01-01` (month rolls over) |
| `DateSerial(2020, 2, 30)` | `2020-03-01`; `DateSerial(2020, 3, 0)` → `2020-02-29` (day 0 = last of prev month) |
| `TimeSerial(25, 0, 0)` | `01:00:00` (hours roll over) |
| `DateAdd("m", 1, #1/31/2020#)` | `2020-02-29` — **month-end clamp** (not Mar 2) |
| `DateAdd("yyyy", 1, #2/29/2020#)` | `2021-02-28` |
| `DateAdd("yyyy", 100000, Now)` | **Err 5** (Invalid procedure call) — past the Date range |
| `TimeSerial(9999999, 0, 0)` | **Err 6** (Overflow) — arg past `Integer` / the Date range. **NB the two overflow codes DIFFER**: DateAdd → 5, TimeSerial → 6 (do not assume both are 6) |
| `DateDiff("d", #1/1/2020#, #12/31/2020#)` | `365`; `DateDiff("m", #1/15#, #3/10#)` → `2`; `DateDiff("yyyy", #12/31/2020#, #1/1/2021#)` → `1` |
| `DateDiff("h", 8:30, 9:15)` | **`1`** — DateDiff counts interval **boundaries crossed**, not the floored difference (0.75 h still crosses one hour boundary) |
| `DatePart("q", …)` / `DatePart("y", #2/1#)` / `DatePart("ww", #1/1/2020#)` / `DatePart("ww", #12/31/2020#)` | `1` / `32` / `1` / `53` (vbFirstJan1 + Sunday-start) |
| `MonthName(1[,True])` / `MonthName(12)` | `January` / `Jan` / `December` |
| `DateValue("March 15, 2020")` / `TimeValue("2:30:45 PM")` | `2020-03-15` / `14:30:45` |

### Format — numeric (interpreter-core Phase 3.7.1)

`Format`/`Format$` is being built facet-by-facet (3.7.1 numeric → 3.7.2 sections/scientific/scaling → 3.7.3
date/time → 3.7.4 string/Boolean). Numeric findings:

| call | result | note |
|---|---|---|
| `Format(2.5, "0")` | `3` | **half-away-from-zero** — NOT `CInt`'s banker's (`CInt(2.5)=2`) |
| `Format(0.5, "0")` / `Format(1.5, "0")` | `1` / `2` | " |
| `Format(2.345, "0.00")` | `2.35` | rounds the **decimal** value, not the raw double (2.3449… would floor to 2.34) |
| `Format(0.5, "#.##")` / `Format(0.5, "0.##")` | `.5` / `0.5` | `#` omits an insignificant zero; `0` forces it |
| `Format(0, "#")` / `Format(0, "0")` | `` (empty) / `0` | " |
| `Format(5, "000")` | `005` | `0` pads |
| `Format(1234.5678, "#,##0.00")` | `1,234.57` | grouping + 2 dp |
| `Format(0.5, "0%")` | `50%` | `%` scales ×100 |
| `Format(1234.5, "$#,##0.00")` | `$1,234.50` | `$` and other non-token chars are **literals** |
| `Format(1234.5678, "General Number"/"Fixed"/"Standard"/"Percent")` | `1234.5678` / `1234.57` / `1,234.57` | named numeric |

> **Rounding, pinned:** `Format` uses `MidpointRounding.AwayFromZero` on a `decimal` view of the value; this is
> the single most important divergence from the `C*` conversions (banker's). Culture supplies the decimal/group
> separators (and currency symbol for named `Currency`) — HexIDE uses `CurrentCulture` (faithful); tests stay on
> `.`/`,` masks, which agree across en-* and Invariant.

Advanced numeric (3.7.2):

| call | result | note |
|---|---|---|
| `Format(-1234.5, "#,##0.00;(#,##0.00)")` | `(1,234.50)` | the **negative section is fed the ABSOLUTE value** — it must supply its own sign/parens |
| `Format(0, "#,##0.00;(#,##0.00)")` | `0.00` | with 2 sections, zero uses the **positive** section |
| `Format(0, "0.00;(0.00);0.0000")` / `Format(0, "0;0;Empty")` | `0.0000` / `Empty` | 3rd section = zero; a placeholder-free section is a literal |
| `Format(1234.5678, "0.00E+00")` | `1.23E+03` | `E+` always prints the exponent sign |
| `Format(1234.5678, "0.00E-00")` | `1.23E03` | `E-` prints the sign only for a **negative** exponent |
| `Format(0.00012345, "0.00E+00")` | `1.23E-04` | |
| `Format(1234567, "#,##0,")` | `1,235` | a **trailing comma** (end of the integer part) scales ÷1000 each |
| `Format(1234567, "#,##0.0,")` | `1,234,567.0` | a comma **after** the decimal does NOT scale (dropped) |

**Out-of-decimal-range magnitudes** (`|x| > ~7.9e28` — beyond `System.Decimal`). VB6 does **not** error (verified):

| expression | vb6.exe | note |
|---|---|---|
| `Format(1E30)` / `Format(1E30, "General Number")` | `1E+30` | General/no-mask → **scientific** for very large/small |
| `Format(-2.5E29)` | `-2.5E+29` | " |
| `Format(1E300, "Scientific")` | `1.00E+300` | scientific named/mask → mantissa per the mask |
| `Format(1E30, "0.00E+00")` | `1.00E+30` | " |
| `Format(1E30, "0.00")` | `1000000000000000000000000000000.00` | a **fixed** mask FULLY EXPANDS (VB6's double-precision digit model) |
| `Format(1E28, "0.00")` | `10000000000000000000000000000.00` | `1E28` is *within* decimal range — the precise engine handles it |
| `Format(0.5, "0." & 20 zeros)` | `0.50000000000000000000` | a **wide fixed mask zero-pads** past the value's precision — VB6 does **not** error |
| `Format(1.5, "0." & 30 zeros)` | `1.500000000000000000000000000000` | " (30 fractional places, all-zero tail) |

> **HexIDE divergence (approximation):** the numeric Format engine is `decimal`-based, so it can't feed a magnitude
> beyond decimal range. HexIDE renders **every** numeric format of such a value in scientific notation (`G15`) —
> matching VB6 for General/Scientific, but **approximating** a fixed mask (`0.00`) as scientific rather than VB6's
> full expansion. The point of the fix (bug-hunt HIGH) was to stop `(decimal)d` throwing an **uncatchable**
> `OverflowException`; the fixed-mask expansion is a parked refinement.

Date/time (3.7.3), anchor `#3/5/2020 2:07:09 PM#` (a Thursday):

| token/mask | result | note |
|---|---|---|
| `d`/`dd`/`ddd`/`dddd` | `5` / `05` / `Thu` / `Thursday` | day |
| `m`/`mm`/`mmm`/`mmmm` | `3` / `03` / `Mar` / `March` | month |
| `yy`/`yyyy` | `20` / `2020`; `y` alone → `65` | year / **day-of-year** |
| `h`/`hh` | `14` / `14` | **24-hour** — no AM/PM token present |
| `h AM/PM` | `2 PM` | an AM/PM token switches `h` to **12-hour** |
| `AM/PM`·`am/pm`·`A/P`·`a/p` | `PM`·`pm`·`P`·`p` | |
| `hh:mm` / `hh:mm:ss` | `14:07` / `14:07:09` | **`m`/`mm` right after `h`/`hh` = MINUTE**; a separator between them doesn't reset it |
| `mm` standalone / `m/d/yy` | `03` / `3/5/20` | month when not after an hour token |
| `n`/`nn` | `7` / `07` | `n` is **always** minute |
| `q` / `w` / `ww` | `1` / `5` / `10` | quarter / weekday (vbSunday base) / week-of-year |
| `Format(0, "yyyy-mm-dd")` | `1899-12-30` | a **number under a date mask is its OLE serial** (serial 0 = 1899-12-30) |

String / Boolean / default (3.7.4):

| call | result | note |
|---|---|---|
| `Format("abc", "@@@@@")` | `  abc` | `@` = char-or-**space**, right-aligned |
| `Format("abc", "&&&&&")` | `abc` | `&` = char-or-**nothing** |
| `Format("abcde", "@@@")` | `abcde` | excess chars **overflow** — never truncated |
| `Format("abc", "!@@@@@")` | `abc  ` | `!` left-aligns |
| `Format("5551234", "(@@@) @@@-@@@@")` | `(   ) 555-1234` | literals kept; chars fill right-to-left |
| `Format("HeLLo", "<")` / `Format("hello", ">")` | `hello` / `HELLO` | `<`/`>` force case |
| `Format(True/5, "Yes/No")` / `Format(0, "Yes/No")` | `Yes` / `No` | Boolean formats: nonzero/True → first word |
| `Format(0, "True/False")` / `Format(-1, "On/Off")` | `False` / `On` | |
| `Format("abc", "0.00")` | `abc` | a **non-numeric string under a numeric mask is returned unchanged** |
| `Format(True)` / `Format(1234.5678)` (no format) | `True` / `1234.5678` | default = General Number / the string / General Date / True|False |

**Format engine complete** (3.7.1–3.7.4): numeric (named + custom, sections, scientific, scaling), date/time
(all custom tokens + named), string (`@ & < > !`), Boolean named formats, and the no-format default dispatch —
all `vb6.exe`-verified. `Format$` runs as `Format` with its result made a String since #1071 (see "The library's `$` forms").

> **Environment-dependent (not a hardcode target):** `DateSerial`'s two-digit-year window follows the OS/culture
> setting — this machine (window 1950–2049) gives `DateSerial(30,…) = 2030` and `DateSerial(75,…) = 1975`. VB6
> reads the registry two-digit-year setting; HexIDE uses .NET's `Calendar.ToFourDigitYear` (same default), so
> both track the host. Not asserted in CI. `WeekdayName`'s **default** `firstdayofweek` is likewise
> system-influenced (this machine returned `Monday` for `WeekdayName(1)`); HexIDE defaults it to the documented
> `vbSunday`, so only the explicit-`firstdayofweek` forms are asserted.

---

