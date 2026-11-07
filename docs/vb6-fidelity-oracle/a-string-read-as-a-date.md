## A String read as a Date (2026-10-22, #967, #800)

**The question.** HexIDE read a String into a Date by two rules. A store (`d = s`) read it as a number, so every date string was 13. `CDate` read it as a date with no numeric fallback, so a serial was 13, and it took a dot as a date separator (#800). What one rule does VB6 apply, and which other conversions share it?

**The probes.** For each string, a `Sub Main` logs `CDate(s)`, then a store into a Date, then `IsDate(s)`, each value as `TypeName`, `CStr` and `CDbl`. Four projects were built under en-GB, native and p-code, and every line both builds logged was identical. The table gives `CStr`, with `CDbl` in brackets. A string with no year in it gives a date in the current year: these rows were measured in 2026.

**What it shows:**
- **`CDate` and a store into a Date give one answer for every string.**
- **A date or a time comes first, in the user's locale.** Failing that, VB6 takes the number `CDbl` would read as a serial. That covers `&H` and `&O`, an `e` or `d` exponent, parentheses or a trailing minus as a negative, a leading plus, a trailing point and thousands commas.
- **A serial outside the Date range is 13, not 6.** `"2958466"`, `"-657435"` and `"1e400"` are all 13, where `CDate(3000000#)` is 6.
- **Under en-GB a dot between digits separates a time's parts, never a date's.**
  - `"1.5"` is 01:05, `"1.5.3"` 01:05:03, and `"1.5 PM"` 13:05.
  - `"1.60"`, `"24.0"` and `"25.5"` are no time, so they read as the numbers they are.
  - `"1.2.2003"` is neither, so it is 13.
- **A comma between numbers separates a date's parts,** with or without spaces around it. `"1,5"` is 1 May, `"13,1"` and `"1,13"` are 13 January, and `"1,5,2003"` is 1 May 2003.
- **Two numbers that are no day and month either way round are a month and a year, or failing that a year and a month.** A year below 100 is in the regional window.
  - `"1,000"`, `"1,00"` and `"1 000"` are January 2000, `"12,345"` is December 345, and `"0,5"` is May 2000.
  - `"31,13"` is neither, so it is the number 3113.
  - The order holds across 24 more pairs (below): `"31,2"` and `"2,31"` are both February 2031, `"29,2"` is February
    2029 because 29 February this year does not exist, `"1,50"` is 1950 and `"5,100"` May 100. `"0,13"`, `"13,13"` and
    `"0,0"` are neither, so they are the numbers 13, 1313 and 0.
- **`IsDate` is True exactly when the date reading succeeds.** A number is never a date to it, but a dotted time is: `IsDate("0.5")` is True, and `CDate("0.5")` is 00:05, not noon.

| String | `CDate(s)`, and `d = s` with `d` a Date | `IsDate(s)` |
|---|---|---|
| `"1/2/2003"` | `01/02/2003 (37653)`, a Date | True |
| `"abc"` | Err 13 | False |
| `"36526"` | `01/01/2000 (36526)`, a Date | False |
| `"36526.5"` | `01/01/2000 12:00:00 (36526.5)`, a Date | False |
| `" 2 "` (padded) | `01/01/1900 (2)`, a Date | False |
| `"-1"` | `29/12/1899 (-1)`, a Date | False |
| `"1.2.2003"` | Err 13 | False |
| `"13.1.2003"` | Err 13 | False |
| `"1-2-2003"` | `01/02/2003 (37653)`, a Date | True |
| `"2003-01-02"` | `02/01/2003 (37623)`, a Date | True |
| `"1 Feb 2003"` | `01/02/2003 (37653)`, a Date | True |
| `"Feb 1, 2003"` | `01/02/2003 (37653)`, a Date | True |
| `"10:30"` | `10:30:00 (0.4375)`, a Date | True |
| `"1/2/2003 10:30"` | `01/02/2003 10:30:00 (37653.4375)`, a Date | True |
| `""` (empty) | Err 13 | False |
| `"  "` (two spaces) | Err 13 | False |
| `"&H10"` | `15/01/1900 (16)`, a Date | False |
| `"1e3"` | `26/09/1902 (1000)`, a Date | False |
| `"3000000"` | Err 13 | False |
| `"1,5"` | `01/05/2026 (46143)`, a Date | True |
| `"True"` | Err 13 | False |
| `"12/31/2003"` | `31/12/2003 (37986)`, a Date | True |
| `"0"` | `00:00:00 (0)`, a Date | False |
| `"0.5"` | `00:05:00 (3.47222222222222E-03)`, a Date | True |
| `"31/12/2003"` | `31/12/2003 (37986)`, a Date | True |
| `"2003/1/2"` | `02/01/2003 (37623)`, a Date | True |
| `"1/2"` | `01/02/2026 (46054)`, a Date | True |
| `"1.5"` | `01:05:00 (4.51388888888889E-02)`, a Date | True |
| `"$5"` | Err 13 | False |
| `"(3)"` | `27/12/1899 (-3)`, a Date | False |
| `"2."` | `01/01/1900 (2)`, a Date | False |
| `"  36526  "` (padded) | `01/01/2000 (36526)`, a Date | False |
| `"1.30"` | `01:30:00 (0.0625)`, a Date | True |
| `"23.59"` | `23:59:00 (0.999305555555556)`, a Date | True |
| `"24.0"` | `23/01/1900 (24)`, a Date | False |
| `"25.5"` | `24/01/1900 12:00:00 (25.5)`, a Date | False |
| `"1.60"` | `31/12/1899 14:24:00 (1.6)`, a Date | False |
| `"1.5.3"` | `01:05:03 (4.51736111111111E-02)`, a Date | True |
| `"1.5 PM"` | `13:05:00 (0.545138888888889)`, a Date | True |
| `"10.30.15"` | `10:30:15 (0.437673611111111)`, a Date | True |
| `"0.25"` | `00:25:00 (1.73611111111111E-02)`, a Date | True |
| `"1.2.3"` | `01:02:03 (4.30902777777778E-02)`, a Date | True |
| `"100.5"` | `09/04/1900 12:00:00 (100.5)`, a Date | False |
| `"-1.5"` | `29/12/1899 12:00:00 (-1.5)`, a Date | False |
| `"1.5e1"` | `14/01/1900 (15)`, a Date | False |
| `"1.05"` | `01:05:00 (4.51388888888889E-02)`, a Date | True |
| `"12.5.30"` | `12:05:30 (0.503819444444444)`, a Date | True |
| `"1,2003"` | `01/01/2003 (37622)`, a Date | True |
| `"1,5,2003"` | `01/05/2003 (37742)`, a Date | True |
| `"1,5 10:30"` | `01/05/2026 10:30:00 (46143.4375)`, a Date (HexIDE raised 13 until #1445) | True |
| `"13,1"` | `13/01/2026 (46035)`, a Date | True |
| `"1,13"` | `13/01/2026 (46035)`, a Date | True |
| `"1,5,3"` | `01/05/2003 (37742)`, a Date | True |
| `"2958465"` | `31/12/9999 (2958465)`, a Date | False |
| `"2958466"` | Err 13 | False |
| `"-657434"` | `01/01/100 (-657434)`, a Date | False |
| `"-657435"` | Err 13 | False |
| `"2958465.5"` | `31/12/9999 12:00:00 (2958465.5)`, a Date | False |
| `"1.5.2003 10:30"` | Err 13 | False |
| `"10:30.5"` | `10:30:05 (0.43755787037037)`, a Date | True |
| `"1:2:3"` | `01:02:03 (4.30902777777778E-02)`, a Date | True |
| `"1 2 2003"` | `01/02/2003 (37653)`, a Date | True |
| `"1/2/3"` | `01/02/2003 (37653)`, a Date | True |
| `"12:00 PM"` | `12:00:00 (0.5)`, a Date | True |
| `"13:00 PM"` | `13:00:00 (0.541666666666667)`, a Date | True |
| `"1e400"` | Err 13 | False |
| `"1."` | `31/12/1899 (1)`, a Date | False |
| `".5"` | `12:00:00 (0.5)`, a Date | False |
| `"1d2"` | `09/04/1900 (100)`, a Date | False |
| `"&O17"` | `14/01/1900 (15)`, a Date | False |
| `"1/2/2003 1.5"` | `01/02/2003 01:05:00 (37653.0451388889)`, a Date | True |
| `"Jan 2003"` | `01/01/2003 (37622)`, a Date | True |
| `"2003"` | `25/06/1905 (2003)`, a Date | False |
| `"30/2/2003"` | Err 13 | False |
| `"13.1.2003"` | Err 13 | False |
| `"1/2/2003"` | `01/02/2003 (37653)`, a Date | True |
| `"1,5"` | `01/05/2026 (46143)`, a Date | True |
| `"1.5"` | `01:05:00 (4.51388888888889E-02)`, a Date | True |
| `"1,000"` | `01/01/2000 (36526)`, a Date | True |
| `"1,000.5"` | `26/09/1902 12:00:00 (1000.5)`, a Date | False |
| `"12,345"` | `01/12/345 (-567616)`, a Date | True |
| `"123,456"` | `03/01/2238 (123456)`, a Date | False |
| `"1,5.5"` | `14/01/1900 12:00:00 (15.5)`, a Date | False |
| `"£5"` | Err 13 | False |
| `"5%"` | Err 13 | False |
| `"+5"` | `04/01/1900 (5)`, a Date | False |
| `"5-"` | `25/12/1899 (-5)`, a Date | False |
| `"1 000"` | `01/01/2000 (36526)`, a Date | True |
| `"1,00"` | `01/01/2000 (36526)`, a Date | True |
| `"31,12"` | `31/12/2026 (46387)`, a Date | True |
| `"31,13"` | `09/07/1908 (3113)`, a Date | False |
| `"0,5"` | `01/05/2000 (36647)`, a Date | True |
| `"1,5,2003 10:30"` | `01/05/2003 10:30:00 (37742.4375)`, a Date | True |
| `"Feb 1,2003"` | `01/02/2003 (37653)`, a Date | True |
| `"1, 5"` | `01/05/2026 (46143)`, a Date | True |
| `"1 ,5"` | `01/05/2026 (46143)`, a Date | True |
| `"2003,1,2"` | `02/01/2003 (37623)`, a Date | True |
| `"1,2,3,4"` | `18/05/1903 (1234)`, a Date | False |
| `"10:30,5"` | Err 13 | False |
| `"1.5,2003"` | `31/12/1899 12:28:51 (1.52003)`, a Date (HexIDE: Err 13, #1446) | False |
| `"5,1.5"` | `19/02/1900 12:00:00 (51.5)`, a Date | False |
| `"1,5 PM"` | Err 13 | False |

**Two numbers,** measured afterwards for the review, native and p-code identical:

| String | `CDate(s)`, and `d = s` with `d` a Date | `IsDate(s)` |
|---|---|---|
| `"31,2"` | `01/02/2031 (47880)`, a Date | True |
| `"31/2"` | `01/02/2031 (47880)`, a Date | True |
| `"32,12"` | `01/12/2032 (48549)`, a Date | True |
| `"5,0"` | `01/05/2000 (36647)`, a Date | True |
| `"0,13"` | `12/01/1900 (13)`, a Date | False |
| `"13/2003"` | Err 13 | False |
| `"2,31"` | `01/02/2031 (47880)`, a Date | True |
| `"2/31"` | `01/02/2031 (47880)`, a Date | True |
| `"12,99"` | `01/12/1999 (36495)`, a Date | True |
| `"1,50"` | `01/01/1950 (18264)`, a Date | True |
| `"6 99"` | `01/06/1999 (36312)`, a Date | True |
| `"13,5"` | `13/05/2026 (46155)`, a Date | True |
| `"31,1"` | `31/01/2026 (46053)`, a Date | True |
| `"45,6"` | `01/06/2045 (53114)`, a Date | True |
| `"99,12"` | `01/12/1999 (36495)`, a Date | True |
| `"100,5"` | `01/05/100 (-657314)`, a Date | True |
| `"5,100"` | `01/05/100 (-657314)`, a Date | True |
| `"0,0"` | `00:00:00 (0)`, a Date | False |
| `"0/1"` | `01/01/2000 (36526)`, a Date | True |
| `"1/0"` | `01/01/2000 (36526)`, a Date | True |
| `"13,13"` | `05/08/1903 (1313)`, a Date | False |
| `"29,2"` | `01/02/2029 (47150)`, a Date | True |
| `"2,29"` | `01/02/2029 (47150)`, a Date | True |
| `"30 2"` | `01/02/2030 (47515)`, a Date | True |

`"£5"` was sent as cp1252 byte 0xA3, and the guest's log shows it as `?`. Both VB6 and HexIDE give 13.

**The other doors,** from the same probes, native and p-code identical:

| Statement | VB6 |
|---|---|
| `Const C As Date = "36526"` / `"1.5"` / `"1,13"` | 01/01/2000 / 01:05:00 / 13 January this year, each a Date |
| `Const C As Date = "2958466"` / `""` / `"1.2.2003"` | compile error "Type mismatch", each |
| `Sub P(ByVal d As Date)`, given `"1/2/2003"` / `"36526"` / `"abc"` | 01/02/2003 / 01/01/2000, each a Date / 13 |
| `a(0) = "1/2/2003": a(1) = "36526"`, `a` a Date array | 01/02/2003 / 01/01/2000 |
| `u.d = "1,5"`, `d` a Date field | 1 May this year, a Date |
| `F = s` in a Function `As Date`, given `"0.5"` | 00:05:00, a Date |
| `CVDate("36526")` / `CVDate("1.5")` | 01/01/2000 / 01:05:00 |
| `DateAdd("d", 1, "36526")` / `"1.5"` / `"1,000"` / `"2958466"` | 02/01/2000 / 31/12/1899 01:05:00 / 02/01/2000 / 13 |
| `Year("36526")`, `Year("1,000")`, `Month("0,5")`, `Hour("1.5")`, `Weekday("36526")` | 2000, 2000, 5, 1, 7 |
| `DateDiff("d", "36526", "36527")`, `DatePart("yyyy", "1,000")` | 1, 2000 |
| `DateValue("36526")` / `DateValue("1/2/2003 10:30")` | 13 / 01/02/2003 |
| `TimeValue("36526.5")` / `TimeValue("1.5")` | 13 / 01:05:00 |
| `Format("36526", "dd/mm/yyyy")` / `Format("1.5", "hh:nn")` | 01/01/2000 / 01:05 |
| `CDate(36526.5)` / `CDate(3000000#)` / `CDate(2958465.99999)` | noon on 01/01/2000 / 6 / 31/12/9999 23:59:59 |

So every door that converts a String into a Date reads it as `CDate` does. The exceptions are `DateValue` and `TimeValue`, which take date text only and no serial.

**A second locale can't be probed from inside the program.** `SetThreadLocale 1031`, for de-DE, moved `GetThreadLocale` from 2057 to 1031. Afterwards, `"13.1.2003"` was still 13, `"1,5"` still 1 May and `"1.5"` still 01:05. VB6's conversion follows the user locale, not the thread's. So measuring de-DE means changing the guest's regional settings, which affects everyone sharing it, and that was not done.

### Yearless day and month followed by a time (#1445, 2026-10-24)

The issue's follow-up measured VB6 SP6 (VB6.EXE 6.00.9782, MSVBVM60 6.00.9848), built with `/make`
on an en-GB guest. Native and p-code agreed. Under `On Error Resume Next`, the probe logged
`Format$(CDate(s), "yyyy-mm-dd hh:nn:ss")` and `IsDate(s)`. The year was 2026; yearless inputs
mean the year of execution, rather than a fixed 2026. The issue also measured a store into a Date
with the same answer as `CDate`.

| String | Formatted `CDate` | `IsDate` |
|---|---|---|
| `"1,5 10:30"` | `2026-05-01 10:30:00` | True |
| `"1/5 10:30"` | `2026-05-01 10:30:00` | True |
| `"1-5 10:30"` | `2026-05-01 10:30:00` | True |
| `"1,5 10:30:15"` | `2026-05-01 10:30:15` | True |
| `"1/5 10:30 PM"` | `2026-05-01 22:30:00` | True |
| `"31/12 23:59"` | `2026-12-31 23:59:00` | True |
| `"1,5"` | `2026-05-01 00:00:00` | True |
| `"1/5"` | `2026-05-01 00:00:00` | True |
| `"1.5 10:30"` | Err 13 | False |

Thus comma, slash and hyphen accept the time after a yearless date, including seconds and PM;
a full stop does not. The issue's full-year control, `"1,5,2003 10:30"`, is 1 May 2003 at 10:30.
Month-first locales, including en-US, remain unmeasured.

### In HexIDE (#967, #800)

- **One reading for every door,** `VB6BuiltIns.StringToDate`.
  - It tries `TryReadDate` in the program's locale first. Failing that, it takes `ToNum`'s number as a serial. If neither works, or the serial is outside the range, the result is 13.
  - `CDate` uses it, and so does `CVDate`, which is new. So do a store into a declared Date variable or array element, a Date Const, and every date function through `AsDate`, `Format` included.
  - `DateValue` and `TimeValue` read date text only.
- **What `TryReadDate` now does:**
  - A dot between digits becomes the time separator, unless the locale's date separator is a dot.
  - A comma between digits becomes the date separator, unless the locale's decimal separator is a comma.
  - Two numbers that .NET can't read as a day and a month are read as a month and a year, then as a year and a month.
- **#1445:** a yearless numeric date followed by a time reads the date prefix through the same locale reader as the untimed form, then adds the time. `StringToDateTests` pins the follow-up rows through all three routes.
- **Every row above matches,** pinned in `StringToDateTests` and `ConstAsTypeTests`, except these:
  - `"1.5,2003"` is 13 (#1446).
  - A `ByVal` Date parameter, a UDT's Date field and a Date Function's return read it so too, since #874, #221 and #1444.
- **Not measured:**
  - A locale whose date separator is a dot, or whose decimal separator is a comma. HexIDE leaves those to .NET's reading.
  - A month-first locale.

