## The byte-string functions (2026-10-25, #1510)

**The question.** `ChrB`, `AscB`, `LenB`, `MidB`, `LeftB`, `RightB` and `InStrB` were not defined in HexIDE. What does each
give, and what happens to a String of an odd number of bytes?

**The probe.** One Sub Main project on vb6.exe SP6 under en-GB, built native and p-code: 94 expressions, each evaluated
under `On Error Resume Next` and logged with `Err`, plus the `MidB` statement, a Byte array and `InputB`. A helper, `Hx`,
logs a String's bytes by assigning it to a Byte array. A second batch put each literal argument to `LenB` in a project of
its own, since a compile error stops a whole project. **Native and p-code agreed on every row.**

**A String is a run of bytes, two to a character with the low byte first,** and each of these counts bytes:

| Expression | Result |
|---|---|
| `LenB("abc")`; `LenB(ChrB(65))`, `Len(ChrB(65))` | 6, a Long; 1, 0 |
| `ChrB(65)`; `TypeName`, `VarType` | the one byte 41; String, 8 |
| `ChrB(255)`; `ChrB(256)`, `ChrB(-1)`; `ChrB(65.5)`, `ChrB(66.5)`, `ChrB("66")` | FF; error 6; all three the byte 42 |
| `AscB("A")`; `AscB(ChrW(&H416))`; `AscB("")` | 65, a **Byte**; 22, the low byte; error 5 |
| `ChrB(65) & ChrB(0) = "A"` | True |
| `MidB("abc", 2, 3)`; `MidB("abc", 2)`; `MidB("abc", 6)`; `MidB("abc", 7)` | 00 62 00; 00 62 00 63 00; 00; empty |
| `MidB("abc", 0, 1)`, `MidB("abc", 1, -1)`; `MidB("abc", 1, 0)` | error 5; empty |
| `LeftB("abc", 3)`; `LeftB("abc", 10)`; `LeftB("abc", -1)` | 61 00 62; all six bytes; error 5 |
| `RightB("abc", 3)`; `RightB("abc", 10)`; `RightB("abc", -1)` | 00 63 00; all six bytes; error 5 |
| `InStrB("abc", "b")`; `InStrB("abc", ChrB(0))`; `InStrB(ChrB(1) & "a", "a")` | 3, a Long; 2; 2: a match may start mid-character |
| `InStrB(4, "abc", "b")`; `InStrB(0, "abc", "b")` | 0; error 5 |
| `InStrB(1, "ABC", "b", vbTextCompare)` | **0**: the compare is ignored, the search is by byte |
| `InStrB("abc", "")`; `InStrB(6, "abc", "")`; `InStrB(7, "abc", "")`; `InStrB("", "")` | 1; 6; **0**, where InStr finds an empty string at any start; 0 |
| `LenB(Null)`, `MidB(Null, 1)`, `LeftB(Null, 1)`, `RightB(Null, 1)`, `InStrB(Null, "a")` | Null |
| `AscB(Null)`, `ChrB(Null)`, `MidB$(Null, 1)` | error 94 |

**`LenB` of a variable is its size, as `Len` gives it,** and of a Variant twice its text:

| Operand | `LenB` |
|---|---|
| Integer, Long, Double, Currency, Date, Byte, Boolean, Single | 2, 4, 8, 8, 8, 1, 2, 4 |
| a String of four characters | 8 |
| a Variant holding 12345 | 10, where `Len` is 5 |
| `Type T1: a As Integer: b As Long`; `Type T2: a As Byte: b As Double` | 8 and 12, where `Len` is 6 and 9: the Type's padded size in memory |
| `Empty`; `Null` | 0, a Long; Null |
| `5`, `5&`, `1.5`, `True`, `2 + 3`, `CInt(5)` | **"Variable required - can't assign to this expression"**, a compile error. So is `Len(5)` |

**A String of an odd number of bytes is an ordinary String.** `ChrB(65) & "B"` is the three bytes 41 42 00, and
`Len` of it is 1. The plain functions treat the odd byte in two ways:
- **Dropped:** `UCase`, `Trim$`, `Replace` and `StrReverse` work in whole characters and lose it: `UCase(ChrB(97))` is
  empty, and `Replace` of the bytes 41 42 43 is 41 42.
- **Kept:** concatenation, `CStr`, `Left$` past the end, and `Mid` to the end keep it: `Mid` of 41 42 43 from 2 is the
  byte 43. `Right(…, 1)` of 41 42 43 is 42 43, counted back from the last byte, not the last whole character.
- **Compared as bytes:** `ChrB(65) = "A"` is False and `ChrB(65) < "A"` True, a prefix sorting first. `StrComp(ChrB(65),
  "A", vbTextCompare)` is -1. `StrConv("AB", vbFromUnicode)` is the two bytes 41 42, one character to `AscW` (16961).

**The `MidB` statement** replaces bytes in place and never changes the length:
- `s = "abc": MidB(s, 2, 2) = "x"` gives 61 78 00 00 63 00.
- `MidB(s, 5) = "xyz"` gives 61 00 62 00 78 00.
- A start past the end, `MidB(s, 7) = "x"`, is error 5.

**A Byte array and `InputB`.** `s = b` for a Byte array of 65, 66, 67 is the three bytes, `Len` 1, and `ba = ChrB(65)` is a
Byte array 0 To 0. `InputB(2, #f)` of a file holding 41 42 43 gives 41 42 and `Loc` 2, and in Input mode `InputB(3, #f)`
gives all three.

**Measured, but no rule I would defend:** a second `InputB(2, #f)` from that file, one byte from its end, gave 43 0A
with no error, the 0A not in the file.

### In HexIDE (#1510, first part)

- **`LenB`, `AscB` and `InStrB`** are built as measured. `LenB` of a declared variable takes its size from the same path
  as `Len`, and of a number that is not a variable it is the same compile error, `True` included. Of a user-defined type
  it is refused: HexIDE does not lay a Type out in memory.
- **`MidB`, `LeftB` and `RightB`** are built wherever their result is an even number of bytes, which pair up into
  characters wherever they start: `MidB("abc", 2, 2)` is the character &H6200.
- **Refused:** a result of an odd number of bytes, and so `ChrB`, which is always one. HexIDE's strings are whole UTF-16
  characters and cannot hold one. That representation is the rest of #1510, with the `MidB` statement and `InputB`.
- **`ByteStringTests`** pins each row HexIDE can run, its errors, and the refusals.
