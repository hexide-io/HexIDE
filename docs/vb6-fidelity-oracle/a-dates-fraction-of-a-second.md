## A date's fraction of a second (2026-10-26, #783)

**The question.** A Date is a Double, and nothing stops it holding a fraction of a second: `CDate(0.5 + 0.6 / 86400)` is
12:00:00.6. VB6 never shows one. #783 found that it rounds to the nearest second rather than cutting the fraction off,
from eight rows: 0.5 and 2.5 s round up, 1.5 s down, and 23:59:59.6 carries into the next day. It guessed the rule was
the serial's exact binary value rounded to the nearest second, and declined to state a tie rule. What is the rule, and
which operations see the rounded instant rather than the serial?

**The probe.** Three Sub Main projects on vb6.exe SP6 under en-GB, each built native and p-code. Each serial is computed
at run time, `s = b + k / 86400#` from variables, so the compiler folds nothing. Each is stored in a Date, logged as
`CStr`, `Hour`, `Minute`, `Second`, `Day`, `Month`, `Year`, `Weekday`, `Format$` and `CDbl(d) = s`, and its eight bytes
are logged in hex, copied out with `RtlMoveMemory`. The rule was then fitted on the host against the exact double
vb6.exe made, not one recomputed:
- **Batch 1:** 322 serials. The bases were 0, 0.25, 0.5, 0.75, 1, 36526, 36526.5, 36526.75, 2958465, -1, -0.5, -0.25,
  -36526.25 and -657434. Each took fifteen half-seconds, from 0.5 to 59.5 s, plus 0.4, 0.6, 0.49, 0.51, 0.499999,
  0.500001, 86399.6 and -0.4 s.
- **Batch 2:** 75 serials on bases from 1,000,000 up to 2958464.999, which is 9999-12-30, plus 40 date functions on
  12:00:00.6 and on 1999-12-31 23:59:59.6.
- **Batch 3:** 21 serials straddling the top of the range and both ends of what is accepted, plus 29 cases of
  `DateAdd`, `Now`, `Time`, `TimeSerial`, `DateSerial`, `CDate` of text, `Put`/`Get` and `Write #`/`Input #`.

**Native and p-code agreed on every row,** apart from `Now`, `Time` and `Timer`, where time had moved on between the
two runs. Every Date kept its serial exactly: `CDbl(d) = s` was True for all 418.

### The rule

```
day   = the serial's whole part, toward zero          (a negative serial's day is its integer part, as ever)
s2    = serial + 1/172800 away from zero, in doubles  (half a second)
        unless s2 > 2958465, where s2 = serial        (no rounding once it would pass 9999-12-31 00:00)
time  = floor(|s2 - day| * 86400), in doubles         (whole seconds)
if time = 86400: time = 0, day = the next calendar day
```

**It reproduces all 418 serials across the three batches, with no exception.** Each part was forced by a row:
- **Half a second is added to the serial, not to its time of day.** That is why ties fall differently on different
  days. At day 0 every tie up to 9.5 s rounds up and 10.5 s rounds down; at 36526 only 2.5, 10.5 and 29.5 s round up;
  at 2958465 none does. The double sum lands on one side of a whole second or the other, depending on the serial's
  magnitude.
- **The day is taken before the half is added, and the carry goes forward.** `-1 + 0.4 s`, which is 1899-12-30
  23:59:59.6, shows 31/12/1899. Taking the day after adding the half, which moves a negative serial away from zero, puts
  it on 29/12/1899 instead. The same holds at the bottom of the range: 0100-01-01 23:59:59.6 shows 02/01/100.
- **The cap compares the half-added serial, and is exactly 2958465.** `2958464 + 86399.5 s`, whose half-added serial is
  exactly 2958465.0, rounds up to 31/12/9999. `2958464 + 86399.6 s`, whose half-added serial is 2958465.0000011576, stays
  at 23:59:59 on the 30th. So does the raw serial 2958464.999999, which is below 2958465 but whose half-added serial is
  not. On the 31st itself nothing rounds: 00:00:00.6 shows 00:00:00, and 23:59:59.99 shows 23:59:59.

**What it overturned.** The guess in #783 and in [Turning a number or a date into text](turning-a-number-or-a-date-into-text.md)
was that the serial's exact binary value is rounded to the nearest second. `0.5 + 0.5 / 86400`'s exact value is
43200.49999999999848 seconds into the day, below the half, and vb6.exe rounds it up. Other models, each measured
against all of batch 1 and rejected:

| Model | Rows it gets wrong, of 322 |
|---|---|
| the exact binary value, rounded half up | 89 |
| the time of day times 86400 in doubles, rounded half up; or half to even | 67; 98 |
| the whole serial times 86400 in doubles, rounded half up | 91 |
| hours, minutes and seconds peeled off in turn, with a 1E-11 epsilon and a strict half (Wine's reimplementation) | 98 |
| the time of day, or the whole serial, times 86400 at x87 extended precision | 89 |
| half a second added to the serial, the day taken after it | 29: the negatives' carry, and the top of the range |

### The accepted range

| Serial | vb6.exe |
|---|---|
| 2958465.99999999, and 2958466 less a millisecond | accepted, 31/12/9999 23:59:59 |
| 2958466 | Err 6 |
| -657434.99999999, and -657435 plus a millisecond | accepted, and shows 02/01/100: 0100-01-01 23:59:59.999 rounds to the next day |
| -657435 | Err 6 |

### What sees the rounded instant, and what sees the serial

| Operation | Reads | Measured |
|---|---|---|
| `CStr`, `Str$`, `Format$` (no mask, "General Date", "Long Date", "Short Time", `dddd ww`, `hh:nn:ss AMPM`), `CVar` shown, `Print #` | the rounded instant | 1999-12-31 23:59:59.6 shows `01/01/2000`; 12:00:00.6 shows `12:00:01 PM` |
| `Write #` | the rounded instant | `#2000-01-01#`, `#1899-12-30 12:00:01#`; read back with `Input #`, 12:00:01 exactly |
| `Hour`, `Minute`, `Second`, `Day`, `Month`, `Year`, `Weekday`, `DatePart` (all ten intervals) | the rounded instant | `DatePart` of 1999-12-31 23:59:59.6: 2000, 1, 1, 0, 0, 0, Saturday, week 1, day 1, quarter 1 |
| `DateDiff` | the rounded instants | 12:00:00 to 12:00:00.4 is 0 seconds, to 12:00:00.6 is 1; 1999-12-31 to 23:59:59.6 that day is 1 day |
| `TimeValue`, `DateValue` | the rounded instant, then its part | `TimeValue(12:00:00.6)` is exactly 12:00:01; `DateValue` of 1999-12-31 23:59:59.6 is 2000-01-01 |
| `DateAdd` with `yyyy`, `q`, `m` | the rounded instant | each lands on a whole second: 1999-12-31 23:59:59.6 plus a month is exactly 2000-02-01, and 12:00:00.6 on 30/12/1899 plus a month is exactly 30/01/1900 12:00:01 |
| `DateAdd` with `y`, `d`, `w`, `ww`, `h`, `n`, `s` | the serial | serial + n days, hours, minutes or seconds, bit for bit, the fraction kept |
| `=`, `<` and the rest; `Select Case`; `+`, `-`; `CDbl`, `CLng`, `Int`, `Fix` | the serial | 12:00:00.4 = 12:00:00 is False, and > is True; as a `Select Case` on a Date or a Variant, 12:00:00.4 is not `Case` 12:00:00, nor `Case h To h`, nor `Case #12:00:00 PM#`, and is `Case Is > h`; `Int` and `Fix` of 1999-12-31 23:59:59.6 are 31/12/1999, beside `DateValue`'s 01/01/2000; `CLng` is 36526 |
| storing a Double, `CDate` of one, `CVar` | the serial | kept exactly, all 418 |
| `Put #` and `Get #` | the serial | bit for bit |

`Int(e)` at 31/12/1999 beside `DateValue(e)` at 01/01/2000, for the same e, is the pair that shows the split is real.

**Also measured:**
- `DateAdd`'s count is truncated, not rounded: `DateAdd("s", 0.6, d)` and `DateAdd("d", 0.5, d)` both return d, bit for bit.
- `TimeSerial(12, 0, 0.6)` is exactly 12:00:01, `TimeSerial(12, 0, 1.5)` 12:00:02, and `DateSerial(2000, 1, 1.5)` 2000-01-02.
  The arguments are rounded. A tie that would tell half up from half to even was not measured.
- `Int` and `Fix` of a Date return a Date: `CStr(Int(e))` prints `31/12/1999`, where a Double would print 36525. Only a
  date after the epoch was measured. For one before it, neither the type nor which day `Int` lands on was probed. HexIDE
  floors the serial there and keeps the Date type, as it does after the epoch.
- `Now` and `Time` are whole seconds; `Timer` is not.
- `CDate("12:00:00.6")` is Err 13, and `IsDate` of it, with a point or a comma, is False. Text never carries a fraction of a
  second.
- `CDate(CStr(d)) = d` is False for d = 12:00:00.6: the text is the rounded instant.

### A side finding: the arithmetic that made the serials

Five of the 322 serials in batch 1, every one with k = 86399.6, differ in their last bit from IEEE double arithmetic's
`b + k / 86400`. All 322 match if the quotient is held at the x87's 64-bit precision and only the sum is rounded to a
double. That is #1214's question, and the rows are recorded there. It is no part of the rounding: the rule above was
fitted to the serials vb6.exe actually made.

### In HexIDE (#783)

- **A Date carries its serial.** `VbDate` holds the Double the program made, and the Date payload is that, not a .NET
  `DateTime`, which keeps milliseconds and could not hold the tie direction the rows above turn on.
- **`VbDate.Instant` is the rule above**, and everything in the table that reads the rounded instant reads it: `VbDisplay`
  (so `CStr`, `Print #`, `Format$` with no mask and the debugger), the date functions through `AsDate`, `Write #`,
  `DateValue`, `TimeValue`, and `DateAdd`'s calendar intervals. Arithmetic, comparison, `CDbl`, `Put #`/`Get #`, `CDate`
  of a number and `DateAdd`'s other intervals use the serial, the last along `VbDate.Continuous`, the line a date before
  the epoch is counted on.
- **Also fixed:** `DateAdd`'s count is cut toward zero, where it was rounded to even, and a count past a Long is
  Overflow. `Int` and `Fix` of a Date return a Date, where they returned a Double: the `CStr(Int(e))` row prints
  31/12/1999, not 36525. `Now` and `Time` are whole seconds, and so is `FileDateTime`, which was not measured and keeps the
  second it showed while a fraction was cut off.
- **`DateFractionOfASecondTests`** builds every serial as the exact Double vb6.exe made, from its logged bits, and must show
  and read it as vb6.exe did, line for line, as must every function case that does not read the clock.
- **Two rows are left out**, though each is right to the second: `Format$(d, "hh:nn:ss AMPM")`, where HexIDE does not
  know the `AMPM` token (#2040), and `Format$(e, "Long Date")`, where .NET's ICU pattern for en-GB adds the weekday
  Windows leaves out (#2041).
