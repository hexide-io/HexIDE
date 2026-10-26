## A running program's locale is the user's regional settings (2026-10-12, #777)

Measured to decide which culture the interpreter should format and read text in, and whether a two-digit year is
a fixed rule or a regional one. Real `vb6.exe` 6.00.8176 (the original release), guest locale **en-GB**, one expression batch
through `scripts/vb6-oracle.ps1`.

| Expression | Value | Type | Err |
|---|---|---|---|
| `CStr(#1/2/2003#)` | `02/01/2003` | String | |
| `CStr(1.5)` | `1.5` | String | |
| `CDate(CStr(#1/2/2003#))` | `02/01/2003` | Date | |
| `CDate("13/1/2003")` / `CDate("1/13/2003")` | `13/01/2003` / `13/01/2003` | Date | |
| `IsDate("13/1/2003")` | `True` | Boolean | |
| `CDbl("1.5")` / `CDbl("1,5")` | `1.5` / `15` | Double | |
| `IsNumeric("1,5")` | `True` | Boolean | |
| `DateSerial(0, 1, 1)` / `(29, 1, 1)` / `(30, 1, 1)` / `(35, 1, 1)` / `(49, 1, 1)` | `2000`, `2029`, `2030`, `2035`, `2049` (1 January) | Date | |
| `DateSerial(50, 1, 1)` / `(99, 1, 1)` | `1950`, `1999` (1 January) | Date | |
| `DateSerial(100, 1, 1)` | `01/01/100` | Date | |
| `CDate("1/1/29")` / `"1/1/30"` / `"1/1/35"` / `"1/1/49"` / `"1/1/50"` | 2029, 2030, 2035, 2049, 1950 | Date | |
| `DateValue("1/1/35")` | `01/01/2035` | Date | |

**What this overturned.** The documented rule for a two-digit year in `DateSerial` is fixed: 0 to 29 is the 2000s,
30 to 99 the 1900s. That was the obvious guess, and the one made while designing this change. It is not what
vb6.exe does: `DateSerial(30,
1, 1)` is 2030, and the split falls between 49 and 50, exactly as `CDate` reads a two-digit year. Both follow the
default two-digit-year window in Windows' regional settings, which ends in 2049. **Only that default was measured.**
Windows lets a user move the window, and VB6 presumably follows a moved one, since it gets the window from the same
place; but no run here had it moved, so that half is unmeasured. HexIDE takes the window from the locale's calendar,
so it would follow a moved window too.

**Also found.** `CDate("1/13/2003")` is 13 January under en-GB: when the locale's order makes an impossible date,
VB6 tries the other order (#793, measured further below). And `CDbl("1,5")` is 15, the comma read as a thousands
separator, which HexIDE refuses with 13 (#770).

**Not measured: a comma-decimal locale.** The guest is en-GB, whose decimal separator is the invariant one's, so
nothing here shows a comma. VB6's documentation says `CStr` writes the locale's separator and `Str` and `Val` always
use a period.

### HexIDE, after #777

`VbLocale` is the interpreter's locale. The IDE sets it from the user's culture on the line before it pins its
threads to the invariant culture, and Standalone sets it the same way. Everything a program writes as text
(`CStr`, `&`, a String variable, `Format`, `MonthName`, `WeekdayName`) and everything it reads back from text
(`CDbl` and a String stored into a number, `CBool`, `CDate`, `IsDate`, `IsNumeric`, the date functions given a
String) uses it, and so does `DateSerial`'s two-digit-year window. Source literals do not: they stay invariant
(#781), and so do `Str` and `Val`. So the IDE now shows `CStr(#1/2/2003#)` as the user's short date, and text a
program wrote reads back as the same value in any locale. `VbLocaleTests` pins the en-GB rows above and the
round trip under de-DE. A test sets the locale with `VbLocale.Use`, so it no longer depends on the machine's culture.

Two parts of the user's culture are not used, because #779 had already kept them out of writing, and reading must
agree with writing. `VbLocale.ForProgram` gives the program the user's culture with both replaced:

- **The calendar is always Gregorian.** th-TH counts in the Buddhist era, fa-IR in the Persian calendar, ar-SA in
  Umm al-Qura. Read with the culture's own calendar:
  - `CDate(CStr(#1/2/2003#))` under th-TH was the year 1460, with no error;
  - `DateSerial(30, 1, 1)` was 2530;
  - ar-SA raised a CLR exception no handler could trap on the zero date.

  ar-SA's Gregorian date pattern also ends in an era that ICU writes as U+0645, which is also its PM designator. So
  a written date read back as noon until the era was dropped from the patterns. This is ICU's data, not VB6's, and
  nothing here measured VB6 under a non-Gregorian locale.
  **Platform-data correction (#1045):** the issue records ar-SA's first month as January (`يناير`) on WSL,
  Ubuntu 26.04 with ICU 78, observed 2026-10-14 and reproduced 2026-10-24. Its default calendar there is
  Gregorian, so the locale test now compares the platform's own and Gregorian month names before asserting
  they differ. This corrects a test premise, not a VB6 measurement or runtime behaviour.
- **A sign is always ASCII.** ICU puts a bidi mark before the minus sign for 43 cultures, so `IsNumeric("-5")` was
  False there. Windows' locale data, which VB6 reads, has no such mark.

`Format`'s `>` and `<` case with invariant rules, as `UCase` and `LCase` do. Whether VB6 cases by the locale under
tr-TR is unmeasured for all four (#797).

### A date the locale's order cannot make (2026-10-12, #793)

`scripts/vb6-oracle.ps1`, two batches, guest locale **en-GB** (day first). Every Date shown is how vb6.exe printed
it. The guest's clock was in 2026.

| Expression | Value | Err |
|---|---|---|
| `CDate("13/1/2003")` | `13/01/2003` | |
| `CDate("1/13/2003")` | `13/01/2003`: month 13 is impossible, so read month first | |
| `CDate("12/1/2003")` | `12/01/2003`: possible as it stands, so not swapped | |
| `CDate("12/13/2003")` | `13/12/2003` | |
| `IsDate("1/13/2003")` | `True` | |
| `CDate("13/1")` / `CDate("1/13")` | `13/01/2026` / `13/01/2026` | |
| `CDate("1/12")` | `01/12/2026`: 1 December | |
| `CDate("1/13/2003 10:30")` | `13/01/2003 10:30:00` | |
| `CDate("1/13/03")` / `"1/13/30"` / `"1/13/12"` | `13/01/2003` / `13/01/2030` / `13/01/2012` | |
| `CDate("1/13/203")` | `13/01/203` | |
| `CDate("1-13-2003")` / `"13-1-2003"` | `13/01/2003` / `13/01/2003` | |
| `CDate("1 13 2003")` / `"1/13-2003"` | `13/01/2003` / `13/01/2003` | |
| `DateValue("1/13/2003")` / `CVDate("1/13/2003")` | `13/01/2003` / `13/01/2003` | |
| `Day("1/13/2003")` / `Month("1/13/2003")` | `13` / `1` (Integer) | |
| `CDate("Jan 13 2003")` | `13/01/2003` | |
| `CDate("2003/1/13")` | `13/01/2003` | |
| `CDate("13/13/2003")` | | 13 |
| `IsDate("13/13/2003")` | `False` | |
| `CDate("31/2/2003")` / `"2/31/2003"` / `"30/2/2003"` | | 13 / 13 / 13 |
| `IsDate("31/2/2003")` | `False` | |
| `CDate("2003/13/1")` | | 13 |
| `CDate("13/2003")` | | 13 |
| `CDate("0/13/2003")` / `"32/1/2003"` | | 13 / 13 |
| `CDate("1.13.2003")` / `"13.1.2003"` / `"1.2.2003"` | | 13 / 13 / 13 |

So the first two numbers are read the other way round when the locale's order fails and the other order succeeds.
The separator between them can be `/`, `-`, a space or a mix. A time or a two-digit year after them does not matter,
and neither does whether a year is there at all. It never rescues a date impossible both ways, a year written
first, or a month and a year.

**A dot is not a date separator under en-GB at all**, not even for a date possible as it stands: `CDate("1.2.2003")`
is Type mismatch. .NET reads it as 1 February, so HexIDE did too, from before #777 until #800 was fixed with #967
(*A String read as a Date*, which has the dot rule).

**Not measured:** a month-first locale. HexIDE swaps the same way under one, which is the obvious reading of the
rule but has not been run.

**HexIDE.** `VB6BuiltIns.TryReadDate` tries the locale's order, then the first two numbers swapped, for `CDate`,
`DateValue`, `IsDate` and the date functions given a String. `VbLocaleTests` pins the rows above.

