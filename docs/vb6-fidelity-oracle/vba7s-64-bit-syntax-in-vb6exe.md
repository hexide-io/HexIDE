## VBA7's 64-bit syntax in vb6.exe, in active code (2026-11-16, #1548, SP6)

**The question.** #1548's decision keeps the VB6 personality refusing VBA7's 64-bit syntax, as vb6.exe does. Inactive code is
never parsed (`conditional-compilation.md`), so a `Declare PtrSafe` behind `#If VBA7` builds. What does vb6.exe say when
the same syntax is active?

**The probe.** One `/make` per row, native and p-code, each a module whose only use of the syntax is the row's. Both builds
gave the same compile error on every row, on the line holding the syntax.

| In active code | vb6.exe |
|---|---|
| `Private Declare PtrSafe Function GetTickCount Lib "kernel32" () As Long` | compile error, **"Expected: Sub or Function"** |
| `Dim x As LongLong`, in a procedure | compile error, "User-defined type not defined" |
| `Dim x As LongPtr` | compile error, "User-defined type not defined" |
| `CLngLng(5)` | compile error, "Sub or Function not defined" |
| `CLngPtr(5)` | compile error, "Sub or Function not defined" |
| `5^`, the LongLong type character | compile error, "Syntax error" |
| `DefLngLng A-Z`, at module level | compile error, **"Invalid outside procedure"** |
| `#If Win64` … `#ElseIf VBA7` … `#Else`, the control | builds; takes the `#Else` branch: both read 0 |

**Readings:**
- **vb6.exe knows none of it.** Each word is read as something else:
  - after `Declare`, `PtrSafe` stands where `Sub` or `Function` must;
  - `LongLong` and `LongPtr` are user-defined type names it can't find;
  - `CLngLng` and `CLngPtr` are procedures it can't find;
  - `DefLngLng A-Z` is a statement, which can't stand at module level;
  - `^` after a number is the exponent operator with nothing after it.
- **The VB6 personality should give these same errors,** at the same points, rather than a generic refusal. VBA7's own rows, at
  64-bit, are in `longlong-and-longptr-in-64-bit-vba7.md` (oracle PR #3965).
