## Write # and Input # (2026-10-23, #1521)

Measured with `/make` in ten batches, native and p-code identical on every row. All of them are run-time results of
built EXEs on SP6's MSVBVM60 (#1524), and none is a compile error. The guest's locale was en-GB, except for one batch run
under de-DE: the culture was switched with `Set-Culture`, then restored and checked from a fresh session. A written file
was read back byte by byte with Binary `Get`. A file `Input #` read was written first with `Print #f, text;`. Each typed
variable was set to a known value first, so that an error could be told from a store.

### What Write # writes

| statement | bytes written |
|---|---|
| `Write #f, 5, -5, 0, 32767, 70000` | `5,-5,0,32767,70000` CR LF |
| `Write #f, 2.5, -2.5, 0.5, -0.5, 1E+20, 1.5E-07` | `2.5,-2.5,.5,-.5,1E+20,.00000015` |
| `Write #f, CSng(0.1), CDbl(0.1), CCur(2.5), CCur(-0.25), CDec(1.5), CByte(7)` | `.1,.1,2.5,-.25,1.5,7` |
| `Write #f, 1E-10, 1.5E-20, -1E-05, 0.0001, 0.00001` | `.0000000001,1.5E-20,-.00001,.0001,.00001` |
| `Write #f, 1E-15, 1E-16, 1.5E-14, 1.5E-15, 1.25E-14, 0.000123456789012345` | `.000000000000001,1E-16,.000000000000015,1.5E-15,1.25E-14,1.23456789012345E-04` |
| `Write #f, 1E+15, 1E+16, 1.23456789012345E+15, 123456789012345#, 12345.6789` | `1E+15,1E+16,1.23456789012345E+15,123456789012345,12345.6789` |
| `Write #f, 999999999999999#, 1E+15 - 1, 1234567890123456#, -1E-15, -1E+15` | `999999999999999,999999999999999,1.23456789012346E+15,-.000000000000001,-1E+15` |
| `Write #f, CSng(1 / 3), CDbl(1 / 3), 0.1 + 0.2, CSng(1E+20), CSng(1E-10)` | `.3333333,.333333333333333,.3,1E+20,1E-10` |
| `Write #f, CSng(1.5E-06), CSng(1.5E-07), CSng(1234567), CSng(12345678), CSng(0.0000001), CSng(0.000001)` | `.0000015,1.5E-07,1234567,1.234568E+07,.0000001,.000001` |
| `Write #f, CCur("922337203685477.5807"), CCur("-0.0001"), CCur(100)` | `922337203685477.5807,-.0001,100` |
| `Write #f, CDec("79228162514264337593543950335"), CDec("0.0000000000000000000000000001"), CDec("1E+20"), CDec(-0.5)` | `79228162514264337593543950335,.0000000000000000000000000001,100000000000000000000,-.5` |
| `Write #f, "a", "", "b c", "x,y", " lead"` | `"a","","b c","x,y"," lead"` |
| `Write #f, "say ""hi"""` and `Write #f, "a""b", "c"` | `"say ""hi"""` and `"a""b","c"`: an inner quote is doubled |
| `Write #f, True, False` | `#TRUE#,#FALSE#` |
| `Write #f, #1/2/2003#, #1/2/2003 1:45:00 PM#, #1:45:00 PM#, #12/30/1899#` | `#2003-01-02#,#2003-01-02 13:45:00#,#1899-12-30 13:45:00#,#1899-12-30#` |
| `Write #f, #1/1/1800#, #1/1/100#, #12/31/9999 11:59:59 PM#, CDate(-1.5), CDate(0.5), #1/2/2003 12:00:00 AM#` | `#1800-01-01#,#0100-01-01#,#9999-12-31 23:59:59#,#1899-12-29 12:00:00#,#1899-12-30 12:00:00#,#2003-01-02#` |
| `Write #f, v, w, "end"`, with `v` Null and `w` Empty | `#NULL#,,"end"` |
| `Write #f, CVErr(5)` in a Variant | `#ERROR 5#` |
| `Write #f, "a"; "b"; 5` | `"a","b",5`: a `;` writes a comma as `,` does |
| `Write #f, "a",` and `Write #f, "a";` | `"a",` and no line break |
| `Write #f, "a";`, then `Write #f, "b"` | `"a","b"` CR LF: the next Write goes on with no further comma |
| `Write #f,` | CR LF |
| `Write #f, "l1" & vbCrLf & "l2", 1` | `"l1` CR LF `l2",1`: a line break in a string is written as it is |
| `Write #f, "a", Spc(2), "b"` and `Write #f, "a", Tab(6), "b"` | `"a",  ,"b"` and `"a", ,"b"` |
| `Write #f, ChrW(&H100) & ChrW(&H416), ChrW(&H141)` | `"A?","L"`: best fit, as `Print #` (#1511) |

**How a number is written.** No leading zero, and a point whatever the locale. A Double is fixed while the fixed form needs
at most 15 digits, counting the whole part's digits, or a fraction's with its leading zeros. Otherwise it is written as
`1.5E-20`: its significant digits, `E`, a sign, and at least two exponent digits. A Single is the same at 7 digits.
Currency and Decimal are always fixed: `CDec("1E+20")` is 21 digits. So `1E-10` is `.0000000001` as a Double but `1E-10` as
a Single. Write # does not write as `Str` or `CStr` do.

**A date** is `#yyyy-mm-dd#`, with ` hh:mm:ss` after it where the time is not midnight. A time alone is written on the zero
date, never by itself.

### Under de-DE

| | de-DE |
|---|---|
| `CStr(2.5)` and `Str(2.5)` | `2,5` and ` 2.5` |
| `Write #f, 2.5, -0.25, 1E+20, CCur(2.5), CDec(1.5), CSng(0.1), #1/2/2003#, True` | `2.5,-.25,1E+20,2.5,1.5,.1,#2003-01-02#,#TRUE#` |
| `Print #f, 2.5; CCur(2.5)` | ` 2,5  2,5 `: Print # follows the locale |
| `Input #` of `2.5,"1,5",2.12345` into Variants | Currency 2,5, String 1,5, Decimal 2,12345 |
| `Input #` of `2.5` into a Double | 2,5, so the point read as a point |
| `Input #` of `"2,5"` into a Double | 0 |

Neither statement follows the locale.

### How Input # reads a field

- **Blanks** (spaces and tabs) before a field are skipped.
- **Where a field ends.** A field ends at a comma or a CR, which it takes with it, CR LF as one. A lone LF is an ordinary
  character: `1` LF `2` CR `3` into three Variants is 1, 3, then 62, and `a` LF `b,c` into Strings is `a` LF `b`, then `c`.
- **After a quoted field, a `#` form or a number**, blanks are skipped and a comma or line end is taken. Anything else
  begins the next field: `"a" junk,b` is a, junk, b; `"a""b","c"` is a, b, c; `5 x,6` is 5, x, 6; `5 ,6` is 5, 6.
- **A quoted field** runs to the next quote, or to the end of the file, line breaks included: `"open` CR LF is
  `open` CR LF, with no error. So a doubled quote ends it. **What Write # writes for a string holding a quote does not
  read back**: `"say ""hi"""` reads as `say `.
- **The line end after a line's last field is taken too.** So `Input #f, n` of `3` CR LF `line1` leaves `Line Input #`
  `line1`. After a comma it is not: `8,` CR LF `line` leaves `Line Input #` an empty line, and `5,6` leaves it `6`.

**What a field is read as depends on the variable:**

- **A numeric variable** (Byte, Integer, Long, Single, Double, Currency) reads every field as a number, which a blank,
  comma or CR ends. Quotes and `#` count as characters, not as delimiters: `#ERROR 5#,#abc#` into two Integers is 0 and 5,
  and `a b,5` is 0 and 0.
- **A String** reads a quoted field, or else the text to the comma or CR, with trailing blanks dropped: `1 2,3` is `1 2`,
  `12abc,5` is `12abc`, and ` a ` is `a`.
- **A Variant, a Boolean or a Date** go by the first character. A quote begins a quoted field and `#` a `#` form, which runs
  to the closing `#`, or else to the comma: `#,5` is `#`, then 5. A digit, sign, point or `&` begins a number, which a blank
  ends, so `1 2` is two fields. Anything else is text to the comma: `hello world`.

### What a Variant becomes

| field | Variant |
|---|---|
| `5`, `-5`, `-0`, `+5`, `5.`, `1.00000`, `5.0` | Integer |
| `70000`, `32768`, `2147483647` | Long |
| `2147483648`, `123456789012`, `922337203685477`, `999999999999999` | Double: a whole number beyond Long with at most 15 digits |
| `1234567890123456`, `9999999999999999`, `100000000000000000000`, `12345678901234567890` | Decimal: 16 to 29 digits |
| `123456789012345678901234567890` | Double 1.23456789012346E+29: past Decimal |
| `2.5`, `.5`, `-1.5`, `1.50`, `2.10000`, `0.12340`, `1.2345`, `99999.9999`, `123456789012.5`, `922337203685477.5807` | Currency: at most 4 places once trailing zeros are dropped |
| `2.12345`, `1.23456`, `-.00001`, `.00000015`, `1234567890123456.5` | Decimal: more places, or past Currency |
| `1.123456789012345678901234567890` | Decimal 1.1234567890123456789012345679, rounded to 29 digits |
| `.000000000000000000000000000012` | Double 1.2E-29: still more than 28 places after rounding |
| `0.0`, `0.0000` | Currency 0 |
| `0.00000`, `-0.00000` | Decimal 0 |
| `1e3`, `1d3`, `1E+20`, `1E+300` | Double |
| `1E-400` | Double 0 |
| `1E+400` | String `1E+400`: too large for a Double |
| `&H10`, `&O17`, `&HFFFF`, `&H8000`, `&O177777` | Integer 16, 15, -1, -32768, -1 |
| `&H10000`, `&H7FFFFFFF` | Long |
| `&HFFFFFFFF`, `&H1FFFFFFFF` | Integer -1: 32 bits, higher ones dropped, then 16 where it fits |
| `12abc`, `5%` | Integer 12 and 5: what follows the number is dropped, to the comma |
| `-abc`, `.`, `-&H10`, `(5)`, `abc`, `TRUE` | String, as written |
| `#TRUE#`, `#FALSE#`, `#NULL#`, `#ERROR 13#` | Boolean, Boolean, Null, Error 13 |
| `#2003-01-02#`, `#2003-01-02 13:45:00#`, `#13:45:00#`, `#1/2/2003#` | Date. Inside `#`s the month comes first, as in a date literal: `#1/2/2003#` is 2 January |
| `#abc#`, `#` | String, as written |
| `""` | String, empty |
| nothing before the comma, or only blanks | Empty |

**Measured, but no rule I would defend:** `0.00000` and `-0.00000` are Decimal 0, where `1.00000` is an Integer 1 and
`0.0000` a Currency 0. The table holds if zero keeps the places it was written with, while trailing zeros after any
other digit are dropped. That reading fits all these rows, and nothing more was probed.

### What a typed variable becomes

| variable | field | result |
|---|---|---|
| Integer | `2.5`, `3.5`, `2.5"` | 2, 4, 2: rounded half to even, as a Let rounds |
| Integer | `abc`, `"5"`, `""`, `#TRUE#`, `#NULL#`, `-`, nothing | 0, no error |
| Integer | `1E3`, `&HFFFF` | 1000, -1 |
| Integer | `70000`, `-32769`, `1E+309` | 6, and the variable keeps what it had |
| Long | `1E3`; `2147483648`; `"5"` | 1000; 6; 0 |
| Byte | `255`; `300`, `-1`; `"5"` | 255; 6; 0 |
| Single | `0.1`, `1.5E-07`; `1E+39`; `"2.5"` | 0.1, 1.5E-07; 6; 0 |
| Double | `2.5`, `1E3`, `.5`, `5`; `1,5` | 2.5, 1000, 0.5, 5; 1 |
| Double | `abc`, `"5"`, `"2.5"`, `""`, `#TRUE#`, `#2003-01-02#`, `#2.5#`, `x2.5`, `-` | 0, no error |
| Double | `1E+309`, `1E+400` | 6, kept |
| Currency | `2.12345`; `abc`, `"5"`; `1E+309` | 2.1234; 0; 6 |
| String | `""`, ` a `, `5`, `#2003-01-02#`, `#NULL#`, `#TRUE#`, `#ERROR 5#` | empty, `a`, `5`, and each `#` form as written |
| Boolean | `#TRUE#`, `#FALSE#` | True, False |
| Boolean | `True`, `abc`, `"True"`, `""`, nothing | False, no error |
| Boolean | `1`, `0`, `-1`, `2` | 6, kept: a number in any form |
| Boolean | `#NULL#`, `#2003-01-02#` | 13, kept |
| Date | `#2003-01-02#`, `#13:45:00#` | the date, the time |
| Date | `abc`, `"2003-01-02"`, `"abc"`, `""`, nothing | the zero date, no error |
| Date | `2003-01-02`, `02/01/2003`, `37000` | 6, kept: each begins with a number |
| Date | `#NULL#`, `#TRUE#` | 13, kept |

A Boolean or a Date takes the field as a Variant would, and then:
- a String or Empty is False, or the zero date;
- a number is 6;
- a Boolean or a Date of the other kind, or Null, is 13.

A numeric variable reads 0 from anything with no number at its start, a quoted field included, so `Write #` of a String
holding `"5"` reads back into an Integer as 0.

### At the end of the file

- **A Variant** is emptied, then the statement raises 62. That holds for one that held `"kept"`, whether the file was
  empty or `1` CR LF had already been read, and for the third of three Variants reading `1,2`.
- **A typed variable** keeps what it had, and the statement raises 62: Integer, String, Date and Boolean were measured.

### In HexIDE (#1521)

- `VbWrite.ItemText` formats a value. `VisitWriteStmt` writes a comma for each separator, and moves for `Spc` and `Tab` as
  `Print #` does.
- `VbFiles.ReadField` reads a field as `InputReading` says, from the variable's type.
- `VbInput` converts the field. Its number reader is its own, invariant and decimal-exact, and is not `CDbl`'s
  locale-aware one (#1047).

Refused, untrappably and by name:
- `Input #` into anything but a plain variable, as `Line Input #` refuses;
- `#ERROR n#` into a Variant, since HexIDE has no Error values (`CVErr`);
- `Write #` of an object, array or user-defined type, which was not measured.
- a date between `#`s in any shape but the four measured (`#yyyy-mm-dd#`, `#yyyy-mm-dd hh:mm:ss#`, `#hh:mm:ss#`,
  `#m/d/yyyy#`). .NET's lenient parser would read `#1/2#` as this year's 2 January. A form no date parser takes stays
  the String as written, as `#abc#` measured.

