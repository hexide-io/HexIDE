## LongLong and LongPtr in 64-bit VBA7 (2026-11-14, #1548)

**This page is not a vb6.exe measurement.** VB6 has neither type. These rows come from VBA7 in a 64-bit Office, the host
#1548 needs for the VBA personalities. Which bitness HexIDE's VBA personalities run as is still Jen's decision
(#1548), and 32-bit VBA7 isn't measured here.

**The question.** What a `LongLong` is in 64-bit VBA7: its range and overflow, how it rounds and converts, what it makes
with each other type, how its literals are written, and what `LongPtr`, `DefLngLng`, `DefLngPtr` and the compiler
constants give.

**The probe.**
- **The host:** Excel from Microsoft 365, x64, 16.0.20527, on Windows 11 Pro in the lab VM. Its user locale writes dates
  day first. VBA runs p-code only, so there is no native build to compare.
- **The run:** each group was one standard module, added to a fresh workbook through the VBA object model and run with
  `Application.Run`.
- **The read:** each row is `v = <expression>` into a Variant under `On Error Resume Next`, logged as value and
  `TypeName`, or as the error number.

| Row | Expression | 64-bit VBA7 |
|---|---|---|
| T1 | `Dim p As LongPtr`, `p = 5`: `TypeName`, `VarType`, `LenB` | LongLong, 20, 8 |
| T2 | the same with `As LongLong` | LongLong, 20, 8 |
| V1 | `VarType(CLngPtr(5))` | 20 |
| C1 | `CLngPtr(2.5)` | 2, LongLong |
| C2–C5 | `CLngLng(2.5)`, `(3.5)`, `(-2.5)`, `(-3.5)` | 2, 4, -2, -4, LongLong |
| C6 | `CLngLng("9223372036854775807")` | 9223372036854775807, LongLong |
| C7 | `CLngLng("9223372036854775808")` | err 6 |
| C8 | `CLngLng("-9223372036854775808")` | -9223372036854775808, LongLong |
| C9 | `CLngLng("-9223372036854775809")` | err 6 |
| C10 | `CLngLng(9.2233720368547758E+18)`, 2^63 as a Double | err 6 |
| C11 | `CLngLng(-9.2233720368547758E+18)` | -9223372036854775808, LongLong |
| C12 | `CLngLng("&H7FFFFFFFFFFFFFFF")` | 9223372036854775807 |
| C13 | `CLngLng("&HFFFFFFFFFFFFFFFF")` | -1 |
| C14 | `CLngLng("&H123456789")` | 4886718345 |
| C15 | `CLng(CLngLng(3000000000#))` | err 6 |
| C16 | `CInt(CLngLng(5))` | 5, Integer |
| C17 | `CDbl(CLngLng("9007199254740993"))` | 9.00719925474099E+15, Double |
| C18 | `CStr(CLngLng("9007199254740993"))` | 9007199254740993 |
| A1 | `CLngLng(5) + CInt(1)` | 6, LongLong |
| A2 | `+ CLng(1)` | 6, LongLong |
| A3 | `+ CSng(1)` | 6, **Double** |
| A4 | `+ CDbl(1)` | 6, Double |
| A5 | `+ CCur(1)` | 6, Currency |
| A6 | `+ CDec(1)` | 6, Decimal |
| A7 | `+ CByte(1)` | 6, LongLong |
| A8 | `+ True` | 4, LongLong |
| A9 | `+ "1"` | 6, **Double** |
| A10 | `+ #1/1/2000#` | 6 January 2000, Date |
| A11 | `CLngLng(5) * CLngLng(5)` | 25, LongLong |
| A12 | `CLngLng(5) / CLngLng(2)` | 2.5, Double |
| A13 | `CLngLng(5) \ CLngLng(2)` | 2, LongLong |
| A14 | `CLngLng(5) Mod CLngLng(3)` | 2, LongLong |
| A15 | `CLngLng(5) ^ 2` | 25, Double |
| A16 | `-CLngLng(5)` | -5, LongLong |
| A17 | `CLngLng(5) And CLng(3)` | 1, LongLong |
| A18 | `CLngLng(5) & "x"` | 5x, String |
| A19 | `CLngLng(5) = 5#` | True |
| A20 | `CLngLng("9007199254740993") = 9007199254740992#`, 2^53 + 1 against 2^53 | **True** |
| A21 | `Dim a As Long`, `a = 2147483647`, `a = a + 1` | err 6 |
| A22 | `Dim x As LongLong`, `x = 2147483647`, `x = x + 1` | 2147483648, LongLong |
| O1 | `CLngLng("9223372036854775807") + 1` | err 6 |
| O2 | `CLngLng("4611686018427387904") * 2` | err 6 |
| O3 | a Variant holding `CLng(2147483647)`, plus 1 | 2147483648, **Double** |
| O4 | a Variant holding `CInt(32767)`, plus 1 | 32768, Long |
| O5 | a Variant holding the largest LongLong, plus 1 | no error; 9.22337203685478E+18, **Double** |
| L1 | the literal `3000000000` | 3000000000, **Double** |
| L2 | the literal `2147483648` | 2147483648, Double |
| K1 | `5^` | 5, LongLong |
| K2 | `&HFF^` | 255, LongLong |
| K3 | `&H7FFFFFFFFFFFFFFF^` | 9223372036854775807, LongLong |
| K4 | `&HFFFFFFFFFFFFFFFF^` | -1, LongLong |
| K5 | `&H8000000000000000^` | -9223372036854775808, LongLong |
| K6 | `&HFFFFFFFF^` | **4294967295**, LongLong |
| K7 | `&HFFFFFFFF` | -1, Long |
| K8 | `Dim x^`, `x = 5` | LongLong |
| H1 | `Hex$(CLngLng(-1))` | FFFFFFFFFFFFFFFF |
| H2 | `Oct$(CLngLng(-1))` | 1777777777777777777777 |
| H3 | `Hex$(CLngLng("1099511627776"))`, 2^40 | 10000000000 |
| H4 | `Hex$(CLng(-1))` | FFFFFFFF |
| S1 | `Str(CLngLng(5))` | " 5" |
| S2 | `Format$(CLngLng("123456789012"), "#,##0")` | 123,456,789,012 |
| S3 | `CStr(CLngLng("-9223372036854775808"))` | -9223372036854775808 |
| D1 | `DefLngLng A-Z`, then an undeclared `x = 5` | LongLong |
| D2 | `DefLngPtr A-Z`, the same | LongLong |
| K | `#If` on `VBA7`, `VBA6`, `Win64`, `Win32`, `Win16`, `Mac` | `VBA7`, `VBA6`, `Win64` and `Win32` true; `Win16` and `Mac` false |
| P1 | `Private Declare Function GetTickCount Lib "kernel32" () As Long`, without `PtrSafe` | the module doesn't compile: `Application.Run` answers that the macro can't be run |
| P2 | the same with `PtrSafe`, then `TypeName(GetTickCount())` | Long |

**Readings:**
- **`LongPtr` is `LongLong` here,** in every way read: `TypeName`, `VarType` 20, `LenB` 8, `CLngPtr`'s result, and
  `DefLngPtr`.
- **`LongLong` is a signed 64-bit integer. It overflows with 6** in typed arithmetic and in `CLngLng`. 2^63 as a Double is
  6, and -2^63 converts.
- **`CLngLng` rounds half to even,** as `CLng` does.
- **It ranks between Long and Single.** With Byte, Boolean, Integer or Long it gives a LongLong. With Single, Double or a
  numeric String it gives a Double, and with Currency, Decimal or Date, that type.
- **A Variant that overflows doesn't become a LongLong.** A Variant Long that passes its range becomes a Double, as in VB6,
  and so does a Variant LongLong, with no error. An Integer becomes a Long.
- **A decimal literal past Long's range is a Double,** even here, and `^` makes a `LongLong` literal. `&HFFFFFFFF^` is
  positive, where `&HFFFFFFFF` is a Long -1.
- **Comparing with a Double goes through the Double.** 2^53 + 1 equals 2^53, though `CStr` and `CDbl` show the two
  values differ.
- **`Hex$` and `Oct$` write all 64 bits** of a negative LongLong.
- **Every 64-bit constant is set:** `VBA7`, `VBA6`, `Win64` and `Win32`.
- **A `Declare` needs `PtrSafe`.** Without it the module doesn't compile, so the message text wasn't read.

**Not measured:** 32-bit VBA7, where `LongPtr` should be `Long` and `LongLong` refused; `LongLong` in a UDT's layout;
`LongLong` as an array's bounds or a `For` counter.
