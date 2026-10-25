## `Time`, and eight Dies rows that already ran (2026-10-23, #1633)

**The question.** A sweep probed every name `MISSING_LANGUAGE.md` rated Dies through the interpreter on main, and eight
ran: `,`, `Access`, `Append`, `Output`, `Read`, `Shared`, `Write` and `Time`. The sweep says only that each ran, not that
it ran as vb6.exe runs it. Seven are `Print #` and `Open` forms that "Sequential files" (#1145) and "`Debug.Print` is the
same Print" (#1380) had already measured, row by row, so they needed no new probe, only a test for the rows nothing
pinned: `Access Read` and `Shared` taken on an Input file, a second Input of a file opened `Lock Read` 55, and Append
making a missing file. `Time` had no row here.

**The probe.** One program, built native and p-code with vb6.exe 6.00.8176 (the release build) and run on the MSVBVM60
6.00.9848 (SP6) runtime, en-GB. It reads `Time` into a Variant and logs what it is. Native and p-code agreed.

| Expression | vb6.exe |
|---|---|
| `TypeName(Time)`, `VarType` of it, `TypeName(Time())` | `Date`, 7, `Date` |
| `Int(CDbl(Time)) = 0`, `CDbl(Time) < 1` | True, True |
| `Year`, `Month`, `Day` of it | 1899, 12, 30 |
| `TypeName(Time$)`, `Len(Time$)`, `Time$ Like "##:##:##"` | `String`, 8, True |

So `Time` is a Date with no date part, and `Time$` its eight characters.

### In HexIDE (#1633)

- **`Time` and `Time()`** give each row above. `DateFunctionsTests` pins them.
- **`Time$`** is refused, "Type hint is not supported", as every `$` form of an intrinsic is (#742).
- **The eight rows** are re-rated: `,`, `Append`, `Output`, `Shared` and `Time` Supported; `Access`, `Read` and `Write`
  Partial, because `For Input Access Write` is vb6.exe's compile error and runs here, a Lock holds against this program
  only, and `Read Write` belongs to Random and Binary files, which are not implemented.

