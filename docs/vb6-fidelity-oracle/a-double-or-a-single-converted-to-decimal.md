## A Double or a Single converted to Decimal (2026-11-15, #4012)

**The question.**
- **Why it came up:** on .NET 11, HexIDE's Double-to-Decimal conversion keeps the Double's full binary value. So
  `CDec(1.00005)` became `1.0000500000000001055155962604`, and `FormatNumber(0.015)` 0.01 (#4012's spike, PR #4014). On
  .NET 10 the conversion rounded, and the tests matched VB6.
- **The working assumption, not a measurement:** VB6 rounds to 15 significant digits. Also unknown: which way the
  15th digit rounds, what a Single does, and how the Format functions, `Round` and implicit Decimal arithmetic see the
  result.
- **And `CDate`:** `CDate("24.0")` raised "Not a legal OleAut date" on .NET 11, so the probe also covers `CDate` of
  numeric strings.

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged the same lines.
- **The read:** each row is `v = <expression>` into a Variant, logged as `CStr` and `TypeName`.
- **The guest:** en-GB, so FormatCurrency's symbol is £, which the ANSI log can't show.
- **The CDate rows:** each logs `CDbl` of the date and the date itself, or the error.

**A Double to Decimal: 15 significant digits.**

| Expression | VB6, every row Decimal |
|---|---|
| `CDec(0.1)`, `CDec(0.015)`, `CDec(1.00005)`, `CDec(-0.015)`, `CDec(2.675)`, `CDec(1.005)` | 0.1, 0.015, 1.00005, -0.015, 2.675, 1.005 |
| `CDec(0.1 + 0.2)`, `CDec(0.30000000000000004)` | 0.3, 0.3 |
| `CDec(1 / 3)`, `CDec(2 / 3)` | 0.333333333333333, 0.666666666666667 |
| `CDec(1.0000000000000002)` | 1 |
| `CDec(1234567890123456#)`, `CDec(12345678901234567#)`, `CDec(9007199254740993#)` | 1234567890123460, 12345678901234600, 9007199254740990 |
| `CDec(123456789.123456789)`, `CDec(123456789012345.6)`, `CDec(0.1234567890123456)` | 123456789.123457, 123456789012346, 0.123456789012346 |
| `CDec(1E-20)` | 0.00000000000000000001 |
| `CDec(1E+28)`, `CDec(7.9E+28)`; `CDec(8E+28)` | 1 and 28 zeros, 79 and 27 zeros; 6 |

**Which way the 15th digit rounds.** These integers are exact in a Double, so the 16th digit is exactly 5:

| Expression | VB6 |
|---|---|
| `CDec(1234567890123445#)`, `CDec(-1234567890123445#)` | 1234567890123440, -1234567890123440 |
| `CDec(1234567890123455#)`, `CDec(-1234567890123455#)` | 1234567890123460, -1234567890123460 |
| `CDec(1234567890123465#)` | 1234567890123460 |
| `CStr(1234567890123445#)`, `CStr(1234567890123455#)` | **1.23456789012345E+15**, 1.23456789012346E+15 |

**A Single to Decimal: 7 significant digits.**

| Expression | VB6, Decimal |
|---|---|
| `CDec(CSng(0.1))`, `CDec(CSng(1.00005))`, `CDec(CSng(1) / 3)` | 0.1, 1.00005, 0.3333333 |
| `CDec(CSng(16777217))` (a Single holds 16777216) | 16777220 |
| `CDec(CSng(12345665))`, `CDec(CSng(-12345665))`, `CDec(CSng(12345675))` | 12345660, -12345660, 12345680 |

**A Double meeting a Decimal in arithmetic** is converted the same way:

| Expression | VB6, Decimal |
|---|---|
| `CDec(1) * 0.1`, `CDec(0) + 0.015`, `CDec(1) * 1.00005`, `CCur(1) * CDec(1.00005)` | 0.1, 0.015, 1.00005, 1.00005 |
| `CDec(1) + 0.1 + 0.2`, `CDec(0.1) * 3` | 1.3, 0.3 |
| `CDec(1) / 3` | 0.3333333333333333333333333333: Decimal division keeps 28 places |

**What the Format functions and `Round` see:**

| Expression | VB6 |
|---|---|
| `FormatNumber(0.015)`, `FormatCurrency(0.015)`, `FormatPercent(0.00015, 2)` | "0.02", "£0.02", "0.02%" |
| `FormatNumber(1.005, 2)`, `FormatNumber(2.675, 2)`, `FormatNumber(0.125, 2)` | "1.01", "2.68", "0.13" |
| `Format$(2.355, "0.00")`, `Format$(1.005, "0.00")`, `Format$(0.015, "0.00")` | "2.36", "1.01", "0.02" |
| `Round(2.675, 2)`, `Round(0.015, 2)` | 2.68, 0.02, Double |
| `FormatNumber(1234.5678, 20)`, `FormatNumber(0.1, 20)`, `Format$(0.1, "0.00000000000000000000")` | "1,234.56780000000000000000", "0.10000000000000000000", "0.10000000000000000000" |
| `CStr(0.1 + 0.2)`, `CStr(1 / 3)` | "0.3", "0.333333333333333" |

**`CDate` of a numeric string** gives the serial number, from 1 January 100 to 31 December 9999:

| String | VB6 |
|---|---|
| `"24.0"`, `"24"`, `"24.5"` | 24, 24, 24.5: 23 January 1900, at midnight, midnight and noon |
| `"-1.5"`, `"0.0"`, `"36526.25"`, `"1E3"` | 29 December 1899 at noon; 30 December 1899; 1 January 2000 at 06:00; 26 September 1902 |
| `"2958465"`, `"2958465.99999"`; `"2958466"` | 31 December 9999, at midnight and 23:59:59; 13 |
| `"-657434"`; `"-657435"` | 1 January 100; 13 |

**Readings:**
- **The assumption is confirmed, and refined. A Double converts to Decimal at 15 significant digits, rounding half to
  even.** 1234567890123445 keeps its 4, and 1234567890123465 keeps its 6. A Single converts at 7 significant digits,
  also half to even.
- **`CStr` of the same Double rounds half away from zero.** `CStr(1234567890123445#)` ends 345, where `CDec` ends 344(0).
  So the two conversions don't share a rounding rule, and an implementation can't use one for the other.
- **The Format functions see the 15-digit value.** `FormatNumber(1.005, 2)` is 1.01, which the Double's exact
  binary value, 1.00499999999999989…, would make 1.00.
- **`Round` doesn't.** This line first said `Round(2.675, 2)` is 2.68 "for the same reason". That was wrong, and rdcore
  caught it on 2026-11-16 from a measured `Round(2.345, "2")` of 2.35, which half to even on the 15-digit value would make
  2.34. The rows below settle it.
- **`CDate` of a numeric string is the serial number,** and a number outside 1 January 100 to 31 December 9999 is 13,
  not 6.

### `Round` scales in Double, then rounds half to even (2026-11-16)

**The probe.** It's the same build and guest, native code and p-code, which logged the same lines. Each row's
`x * 100`, computed in Double, is shown beside it, along with what half to even on the 15-digit value would give.

| Expression | `x * 100` in Double | 15-digit value, half to even | VB6 |
|---|---|---|---|
| `Round(0.545, 2)`, `Round(1.225, 2)`, `Round(2.345, 2)`, `Round(2.365, 2)` | 54.50000000000001, 122.50000000000001, 234.50000000000003, 236.50000000000003 | 0.54, 1.22, 2.34, 2.36 | **0.55, 1.23, 2.35, 2.37**, Double |
| `Round(0.575, 2)`, `Round(1.015, 2)`, `Round(2.135, 2)`, `Round(1.275, 2)` | 57.49999999999999, 101.49999999999999, 213.49999999999997, 127.49999999999999 | 0.58, 1.02, 2.14, 1.28 | **0.57, 1.01, 2.13, 1.27**, Double |
| `Round(2.665, 2)`, `Round(2.675, 2)`, `Round(0.015, 2)`, `Round(0.125, 2)`, `Round(1.005, 2)` | 266.5, 267.5, 1.5, 12.5, 100.49999999999999 | 2.66, 2.68, 0.02, 0.12, 1 | 2.66, 2.68, 0.02, 0.12, 1, Double |
| `Round(2.5)`, `Round(3.5)` | | | 2, 4 |
| `Round(CDec(2.345), 2)`, `Round(CCur(2.345), 2)`, `Round(CDec(0.575), 2)` | | | 2.34 Decimal, 2.34 Currency, 0.58 Decimal |
| `Round(CSng(0.545), 2)` | | | 0.55, Single |
| `FormatNumber(2.345, 2)`, `FormatNumber(0.575, 2)` | | | "2.35", "0.58" |

**Readings:**
- **`Round` of a Double multiplies by 10^n in Double, then rounds half to even.** Every one of the 13 Double rows fits
  that, including the eight where it disagrees with half to even on the 15-digit value, which none of them fits.
- **The Double's exact binary value isn't the rule either.** That rule makes `Round(2.675, 2)` 2.67, because 2.675 is
  2.67499999999999982… exactly, and `Round(0.575, 2)` would still be 0.57.
- **A Decimal or a Currency rounds its exact decimal value, half to even:** 2.345 to 2.34, and 0.575 to 0.58.
- **`FormatNumber` disagrees with `Round` on the same Double.** It formats the 15-digit value, half away from zero, so
  `FormatNumber(0.575, 2)` is "0.58" where `Round(0.575, 2)` is 0.57.

**Measured, but no rule I'd defend.** Beyond Decimal's 28 decimal places:
- `CDec(2.5E-28)`, `CDec(3.5E-28)` and `CDec(1.5E-28)` give 0.0000000000000000000000000002, …3 and …1. That's consistent
  with truncation at the 28th place, not with rounding either way.
- But `CDec(1.23456789012345E-15)` gives 0.0000000000000012345678901235, where truncating the 15-digit value at 28
  places would end 1234.

The binary values of those Doubles may explain it. It wasn't measured further.

**In HexIDE.** .NET 10's `(decimal)double` happened to round to 15 significant digits. .NET 11's doesn't, and that's the
spike's nine failures. For #4012's migration, the interpreter's Double-to-Decimal conversion needs to make VB6's rule
explicit: 15 significant digits, half to even, from the Double's exact value, and 7 for a Single. It can't borrow
`CStr`'s formatting, which rounds the other way.

**Not measured:**
- Decimal to Double;
- `CCur` of a Double at its 4 decimal places;
- the scale-28 rows' rule.
