## Turning a number or a date into text (2026-10-12, #772, #771)

`CStr`, `&`, storing into a String, `Format` with no mask and `Str` all turn a value into text. Two runs of
`scripts/vb6-oracle.ps1` measured them, about 110 rows, on vb6.exe 6.00.8176 (the original release) in an **en-GB** guest
(decimal point `.`, short date `dd/MM/yyyy`, long time `HH:mm:ss`). Each row is `CStr(...)` unless it says
otherwise, and every one came back a String. `CSng(x)` rounds a Double literal to a Single, as written.

**A Single: seven significant digits.**

| Probe | Result | Probe | Result |
|---|---|---|---|
| `CSng(1234567)` | `1234567` | `CSng(0.1)` | `0.1` |
| `CSng(12345678)` | `1.234568E+07` | `CSng(0.0001)` | `0.0001` |
| `CSng(1000000)` | `1000000` | `CSng(0.00001)` | `0.00001` |
| `CSng(10000000)` | `1E+07` | `CSng(0.000012345)` | `1.2345E-05` |
| `CSng(99999990)` | `9.999999E+07` | `CSng(1.5E-10)` | `1.5E-10` |
| `CSng(15000000)` | `1.5E+07` | `CSng(1E-38)` | `9.999999E-39` |
| `CSng(1E+15)` / `CSng(1E+38)` | `1E+15` / `1E+38` | `CSng(-1) / 3` | `-0.3333333` |
| `CSng(123456.78)` | `123456.8` | `CSng(2) / 3` | `0.6666667` |
| `CSng(9999999.5)` | `1E+07` | `CSng(0.5)` / `CSng(100)` | `0.5` / `100` |

| Below 1, where E notation starts | Result | Digits after the point, written fixed |
|---|---|---|
| `CSng(0.0000001)` | `0.0000001` | 7 |
| `CSng(0.00000001)` | `1E-08` | 8 |
| `CSng(0.0000015)` | `0.0000015` | 7 |
| `CSng(0.00000015)` | `1.5E-07` | 8 |
| `CSng(0.0012345)` | `0.0012345` | 7 |
| `CSng(0.00012345)` | `1.2345E-04` | 8 |
| `CSng(0.1234567)` | `0.1234567` | 7 |
| `CSng(0.01234567)` | `1.234567E-02` | 8 |

| Ties and carries | Result |
|---|---|
| `CSng(1234566.5)` (an exact tie; half-to-even gives 1234566) | `1234567` |
| `CSng(1234567.5)` | `1234568` |
| `CSng(999999.95)` (stored as 999999.9375) | `999999.9` |
| `CSng(1.0000005)` | `1` |

**A Double: fifteen.**

| Probe | Result | Probe | Result |
|---|---|---|---|
| `0.1 + 0.2` | `0.3` | `0.0001` / `0.00001` | `0.0001` / `0.00001` |
| `1 / 3` | `0.333333333333333` | `0.000012345` | `0.000012345` |
| `2 / 3` | `0.666666666666667` | `0.00001 / 3` | `3.33333333333333E-06` |
| `123456789012345#` | `123456789012345` | `1E-100` | `1E-100` |
| `1234567890123456#` | `1.23456789012346E+15` | `4.94065645841247E-324` | `4.94065645841247E-324` |
| `1E+14` | `100000000000000` | `-0.1 - 0.2` | `-0.3` |
| `1E+15` / `1E+16` | `1E+15` / `1E+16` | `100#` / `1.5` | `100` / `1.5` |
| `999999999999999#` | `999999999999999` | `1 / 7` | `0.142857142857143` |
| `9999999999999999#` | `1E+16` | `22 / 7 * 1000000#` | `3142857.14285714` |
| `2 ^ 53` | `9.00719925474099E+15` | `1.5E+15` / `-1E+15` | `1.5E+15` / `-1E+15` |
| `1E+21` / `1E+100` | `1E+21` / `1E+100` | `1.79769313486231E+308` | `1.79769313486231E+308` |

| Below 1, against fifteen | Result |
|---|---|
| `0.000000000000001` | `0.000000000000001` |
| `0.0000000000000001` | `1E-16` |
| `0.000000000000015` | `0.000000000000015` |
| `0.0000000000000015` | `1.5E-15` |
| `0.123456789012345` | `0.123456789012345` |
| `0.0123456789012345` | `1.23456789012345E-02` |
| `-0.00000015` | `-0.00000015` |

| Ties | Result |
|---|---|
| `1234567890123445#` (an exact tie; half-to-even gives ...44) | `1.23456789012345E+15` |
| `1234567890123455#` | `1.23456789012346E+15` |

**Negative zero, Currency, Decimal.**

| Probe | Result |
|---|---|
| `-0#`, `0# * -1`, `CSng(0) * -1` | `0` |
| `-1E-200 * 1E-200` (underflow; no error) | `0` |
| `CCur(1.5)`, `CCur(100)`, `CCur(-2.5)` | `1.5`, `100`, `-2.5` |
| `CCur(1234567.8912)`, `CCur(0.0001)` | `1234567.8912`, `0.0001` |
| `CCur("922337203685477.5807")` | `922337203685477.5807` |
| `CCur(1.5) * 2`, `CCur(1.25) + CCur(1.25)`, `CCur("1.50")`, `CCur(0) * -1` | `3`, `2.5`, `1.5`, `0` |
| `CDec("1.10")`, `CDec("1.10") * 1`, `CDec("100.00")` | `1.1`, `1.1`, `100` |
| `CDec(1) / 3` | `0.3333333333333333333333333333` |
| `CDec("0.00000000000000000001")` | `0.00000000000000000001` |
| `CDec("79228162514264337593543950335")` | `79228162514264337593543950335` |

**A Date.**

| Probe | Result |
|---|---|
| `#1/2/2003#` | `02/01/2003` |
| `#1/2/2003 10:30:00 AM#` / `... 10:30:05 PM#` | `02/01/2003 10:30:00` / `02/01/2003 22:30:05` |
| `#1/2/2003 12:00:01 AM#` / `... 12:00:00 PM#` | `02/01/2003 00:00:01` / `02/01/2003 12:00:00` |
| `#1/2/2003 12:00:00 AM#` | `02/01/2003` |
| `#10:30:00 AM#` | `10:30:00` |
| `#12:00:00 AM#`, `CDate(0)`, `#12/30/1899#` | `00:00:00` |
| `CDate(0.5)`, `CDate(1 / 86400)` | `12:00:00`, `00:00:01` |
| `#12/30/1899 10:30:00 AM#` | `10:30:00` |
| `CDate(-1)`, `CDate(-1.5)` | `29/12/1899`, `29/12/1899 12:00:00` |
| `#1/1/100#`, `DateSerial(100, 1, 1)` | `01/01/100` |
| `DateSerial(999, 12, 31)` | `31/12/999` |
| `#12/31/9999 11:59:59 PM#` | `31/12/9999 23:59:59` |

**The other paths.**

| Probe | Result |
|---|---|
| `"[" & CSng(1) / 3 & "]"`, `"[" & (0.1 + 0.2) & "]"`, `"[" & 1E+15 & "]"` | `[0.3333333]`, `[0.3]`, `[1E+15]` |
| `"[" & #1/2/2003# & "]"`, `"[" & #10:30:00 AM# & "]"`, `"[" & CCur(1.5) & "]"` | `[02/01/2003]`, `[10:30:00]`, `[1.5]` |
| `Format(CSng(1) / 3)`, `Format(1E+15)`, `Format(CSng(0.000012345))`, `Format(0.1 + 0.2)` | `0.3333333`, `1E+15`, `1.2345E-05`, `0.3` |
| `Format(#1/2/2003#)`, `Format(#10:30:00 AM#)`, `Format(#1/2/2003 10:30:00 AM#)` | `02/01/2003`, `10:30:00`, `02/01/2003 10:30:00` |
| `Str(0.5)`, `Str(-0.5)`, `Str(0)` | ` .5`, `-.5`, ` 0` |
| `Str(CSng(1) / 3)`, `Str(1E+15)`, `Str(0.00000015)`, `Str(CSng(10000000))` | ` .3333333`, ` 1E+15`, ` .00000015`, ` 1E+07` |
| `Str(CCur(1.5))` | ` 1.5` |
| `CStr(True)` | `True` |

### The rule

- **Single and Double.** Take the exact binary value to 7 (Single) or 15 (Double) significant digits, rounding
  an exact tie **away from zero**, and drop trailing zeros. Write it in fixed notation unless that needs more
  digits than the precision allows, either before the point (the exponent is at least 7 or 15) or after it (the
  zeros after the point plus the significant digits exceed 7 or 15). Otherwise write `d.dddE±xx`: the exponent
  always signed and at least two digits. So *where* E notation starts below 1 depends on how many digits the
  number has: `0.0012345` but `1.2345E-04`.
- **Negative zero** is `0`.
- **Currency and Decimal**: every digit held, no trailing zeros, never E notation.
- **A Date**: the locale's short date, a space, and its long time. The time is left out at midnight and the date
  is left out on the zero date (30/12/1899), so the zero date at midnight is `00:00:00`. A year below 1000 is
  not padded.
- **`&`, storing into a String, and `Format` with no mask** write exactly what `CStr` writes.
- **`Str`** writes the same digits without the `0` before the point, and puts a space where a non-negative
  number's sign would be.

### What this overturned

- **The tie rule.** VB6's numeric conversions round half to even (`CInt(2.5)` is 2), and .NET's `"E"` format
  also rounds an exact tie to even. The display rounds it away: the only rows that failed against an
  implementation built on .NET's format were the two ties.
- **E notation below 1 is not C's `%G`.** `%G` switches when the exponent is below -4, so it would write
  `0.00001` as `1E-05` and `0.01234567` as `0.01234567`; VB6 writes the first fixed and the second as
  `1.234567E-02`. The threshold counts digits, not the exponent.
- **The year is not zero-padded**, although the en-GB pattern is `yyyy`: `01/01/100`, not `01/01/0100`.
- The earlier note in *A form-based game's runtime surface* measured five Single rows and concluded "seven
  significant digits". That held; it did not reach E notation, ties or the other types.

### Not measured

- Another locale. Every row is en-GB, whose decimal point is `.`; that `CStr` takes the point and the date
  patterns from the locale is VB6's documented behaviour, not something these runs show. `Str` always using a
  period is documented likewise.
- `Str` of a Currency below 1, or of a Date. `Debug.Print`'s own spacing, which the documentation gives as a
  space in place of a positive number's sign and one after every number, is a different dialect and was not
  probed here.

### Measured for the review of #779

A third run, same guest and locale.

| Probe | Result |
|---|---|
| `Format(#1/2/2003#, "General Date")`, `... #10:30:00 AM# ...`, `... #1/2/2003 10:30:00 AM# ...` | `02/01/2003`, `10:30:00`, `02/01/2003 10:30:00` |
| `Format(CDate(0), "General Date")` | `00:00:00` |
| `Format(x, "c")` for the same three dates | the same three texts |
| `Format(CSng(1) / 3, "General Number")`, `Format(1 / 3, "General Number")` | `0.3333333`, `0.333333333333333` |
| `Format(1E+15, ...)`, `Format(0.1 + 0.2, ...)`, `Format(CSng(0.000012345), ...)`, `Format(CCur(1.5) * 2, ...)`, all `"General Number"` | `1E+15`, `0.3`, `1.2345E-05`, `3` |
| `Left(CSng(1) / 3, 20)`, `Mid(1 / 3, 1)`, `Left(#1/2/2003#, 20)` | `0.3333333`, `0.333333333333333`, `02/01/2003` |
| `Len(1 / 3)`, or `Len` of any numeric expression | does not compile: "Variable required - can't assign to this expression" |
| `Len(v)` for `v` declared Single, Double, Currency, Integer, Date; an Empty Variant | `4`, `8`, `8`, `2`, `8`; `0` (Integer, Long for the Variant) |
| `CStr(CDate("10:30:00"))`, `CDbl(#10:30:00 AM#)`, `CDbl(CDate("10:30:00"))` | `10:30:00`, `0.4375`, `0.4375` |
| `CStr(CDate("2003-01-02"))` | `02/01/2003` |
| `CStr(CDate(0.5 + n / 86400))` for n = 0.4, 0.5, 0.6, 1.5, 2.5 | `12:00:00`, `12:00:01`, `12:00:01`, `12:00:01`, `12:00:03` |
| `Second(...)` for n = 0.6 and 0.5 | `1`, `1` |
| `CStr(CDate(1 - 0.4 / 86400))` (23:59:59.6 on the zero date) | `31/12/1899` |
| `CDbl("1E+400")`, `Val("1E+400")`, `CSng("1E+39")` | Err 6 each |

- **"General Date", the `c` token and "General Number" write exactly what `CStr` writes.** The named formats were never different paths.
- **`Len` is not a string function on a number.** It measures a numeric variable's storage and refuses a numeric expression outright (#780). So the earlier draft's `Len(CSng(1) / 3)` row, which HexIDE answered 9, was not valid VB6.
- **A fraction of a second rounds to the nearest second; it is not cut off** (#783). The rounding can carry into the next day. Of the halves, 0.5 and 2.5 s round up and 1.5 s rounds down. That fits rounding the serial's exact binary value, where `0.5 + 1.5 / 86400` falls just short of the half. I would not call it a tie rule from three rows.
  **Overturned by #783's sweep (2026-10-26):** `0.5 + 0.5 / 86400` is also short of the half in binary, and rounds up. The rule, fitted to
  418 serials, is in [A date's fraction of a second](a-dates-fraction-of-a-second.md#a-dates-fraction-of-a-second-2026-10-26-783).
- **A string too large for a Double is an Overflow**, not infinity (#782).

### HexIDE, after #772 and #771

`VbDisplay.ToText` implements the rule, and `CStr`, `&`, storing into a String, the string functions, `MsgBox`,
`InputBox`, `Err` and `App.Title`, the Immediate window's text, and `Format` with no mask all go through it.
`Str` uses it too, then drops the leading zero. It takes the locale from `CultureInfo.CurrentCulture`. Every row
above is a test, asserted in en-GB explicitly (`VbDisplayTests`). A time-only literal, and `CDate` of a
time-only string, used to land on today's date rather than the zero date, so `CDbl(#10:30:00 AM#)` was
today's serial plus 0.4375 rather than 0.4375; both now land on the zero date.

After the review: `Format`'s "General Date", "General Number" and `c` token, `AddItem`, and Break When Value
Changes' display also go through `VbDisplay`; the watch compares the full value, so a change below the digits it
shows still breaks. Three things the locale does not supply, deliberately: the calendar, which is always Gregorian,
VB6's default; the sign, which is always an ASCII `-`; and a value VB6 cannot make, which is spelled `1.#INF`,
`-1.#INF` or `1.#QNAN` as `Format` already spelled it.

**The running IDE formatted in the invariant culture until #777.** `App.Initialize` pins the invariant culture for
every thread, so `CStr(#1/2/2003#)` showed `01/02/2003` in the IDE on any machine, where VB6 shows the user's own
short date. Since #777 the interpreter has a locale of its own, taken from the user's culture before the pin; see
"A running program's locale is the user's regional settings".

