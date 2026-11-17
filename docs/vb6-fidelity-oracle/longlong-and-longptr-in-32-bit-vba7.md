## LongLong and LongPtr in 32-bit VBA7 (2026-11-17, #1548)

**The question.** What does 32-bit VBA7 do with the 64-bit names: `LongPtr`, `LongLong`, `CLngPtr`, `CLngLng`, the `^`
type character, `DefLngPtr` and `DefLngLng`, and `PtrSafe`? The 64-bit answers are on
[the 64-bit page](longlong-and-longptr-in-64-bit-vba7.md).

**The probe:**
- **The software:** Excel from Microsoft 365, **32-bit** (Click-to-Run platform x86, Current Channel 2610, build
  16.0.20527.20196, `VBE7.DLL` 7.01.1158 built for x86), on 64-bit Windows 11, en-GB.
- **The method:** one Excel process per row, started through its COM object model. Each row is a module holding
  `Public Function P() As String`, which assigns the row's expression to a Variant under `On Error GoTo` and returns its
  `TypeName` and value, or the error. A row whose module doesn't compile stops on VBA's own compile-error box, so the
  screen was captured and the message read from it.

| Row | Source | 32-bit VBA7 |
|---|---|---|
| T1 | `Dim q As LongPtr`, `q = 5`: `TypeName`, `VarType`, `LenB` | **Long, 3, 4** |
| T2 | `Dim x As LongLong` | **"Compile error: User-defined type not defined"**, `x As LongLong` highlighted |
| V1 | `VarType(CLngPtr(5))` | 3 |
| C1, C2 | `CLngPtr(2.5)`, `CLngPtr(3.5)` | 2, 4, Long: half to even |
| C3 | `CLngPtr("2147483647")` | 2147483647, Long |
| C4 | `CLngPtr("2147483648")` | err 6 |
| C5 | `CLngPtr(-2147483648#)` | -2147483648, Long |
| C6 | `CLngLng(5)` | **"Compile error: Sub or Function not defined"**, `CLngLng` highlighted |
| A1 | `CLngPtr(5) + CInt(1)` | 6, Long |
| A2 | `Dim q As LongPtr`, `q = 2147483647`, `q = q + 1` | err 6 |
| A3 | `CLngPtr(5) * CDbl(2)` | 10, Double |
| H1 | `Hex$(CLngPtr(-1))` | FFFFFFFF |
| K1 | `v = 5^` | **"Compile error: Syntax error"**, the line highlighted |
| K2 | `5^ + 1` | **5, Double**: `^` is the operator, `5 ^ (+1)` |
| K3 | `2^3` | 8, Double |
| K4 | `y^2`, y a Long holding 3 | 9, Double |
| K5 | `Dim z^` | **"Compile error: Syntax error"** |
| D1 | `DefLngLng A-Z` | the module doesn't compile: `Application.Run` answers that the macro can't be run (the message wasn't read) |
| D2 | `DefLngPtr A-Z`, then an undeclared `x = 5` | Long |
| W1 | `#If Win64 ... #ElseIf VBA7 ...` | VBA7 true, **Win64 false** |
| W2 | `#If Win32` | true |
| W3 | `#If VBA6` | **true** |
| W4 | `v = VBA6`, under `Option Explicit` | doesn't compile: a compiler constant isn't a name code can read (the message was hidden behind another window) |
| W5, W6 | `#If Mac`, `#If Win16` | false, false |
| P1 | `Private Declare Function GetTickCount Lib "kernel32" () As Long`, **without** `PtrSafe` | compiles and runs: Long |
| P2 | the same with `PtrSafe` | Long |
| P3 | `PtrSafe ... As LongPtr` | Long |

**Readings:**
- **`LongPtr` is `Long` here,** in every way read: `TypeName`, `VarType` 3, `LenB` 4, `CLngPtr`'s result and range
  (C3 to C5), its overflow (A2), and `DefLngPtr`.
- **`LongLong` doesn't exist here,** and refuses as an unknown name does: `As LongLong` is "User-defined type not
  defined", and `CLngLng` "Sub or Function not defined". vb6.exe gives the same two messages (oracle PR #4247).
- **`^` is only the operator here.** `5^` and `Dim z^` are syntax errors, `5^ + 1` is 5 to the power 1, and `2^3` and
  `y^2` are exponentiation, where 64-bit VBA7 refuses both.
- **`PtrSafe` is optional:** a `Declare` without it compiles and runs, where 64-bit VBA7 refuses the module.
- **The constants:** `VBA7`, `VBA6` and `Win32` true; `Win64`, `Mac` and `Win16` false. 64-bit VBA7 differs only in
  `Win64` (row K on the 64-bit page).

**Not measured:** the message for `DefLngLng`; `LongPtr` in a UDT's layout, as an array bound or a `For` counter;
`CLngPtr` of a Currency, Decimal or Date.
