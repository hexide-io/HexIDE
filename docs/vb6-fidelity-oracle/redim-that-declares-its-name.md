## A ReDim that declares its name (2026-11-16, #1495)

**The question.** #1495 found that a `ReDim` on a name nothing declared is HexIDE's "Unknown variable". VB6 declares the
array there, even under `Option Explicit`. Open questions:
- the type and bounds that declaration gives;
- a second `ReDim`, and `ReDim Preserve`;
- a read before the `ReDim`;
- module level;
- a name the module declares.

**The probe.** VB6 SP6, `/make`, native code and p-code, which logged the same lines. Every row is in a `Function` in a
standard module with `Option Explicit`, unless a row says otherwise. The rows that don't compile were each built on their
own.

| Code | vb6.exe |
|---|---|
| `ReDim q(2)` | `TypeName` `Variant()`, `UBound` 2 |
| `ReDim q(2) As Long` | `Long()`, 2 |
| `ReDim q(1 To 3) As String`, then `q(3) = "z"` | `String()`, bounds 1 to 3, `q(3)` "z" |
| `ReDim q(2)`, `q(1) = "x"`, then `ReDim q(5)` | `Variant()`, 5, and `q(1)` Empty |
| `ReDim p(3)`, `p(2) = 7`, then `ReDim Preserve p(4)` | `Variant()`, 4, and `p(2)` 7 |
| `ReDim mArr(4)`, where the module has `Dim mArr() As Long` and the procedure declares nothing | resizes the module's array: `Long()`, 4, read as 4 from another procedure |

**Compile errors,** each built on its own:

| Code | vb6.exe |
|---|---|
| `ReDim Preserve p(3)` on a name never declared | "Variable not defined", **with or without `Option Explicit`** |
| `n = UBound(q)` above `ReDim q(2)`, in the same procedure | "Variable not defined", at the read |
| without `Option Explicit`: `UBound(q)` and `TypeName(q)` above `ReDim q(2)` | "Invalid ReDim", at the `ReDim` |
| `ReDim q(2)`, then `ReDim q(3) As Long` | "Can't change data types of array elements" |
| `ReDim q(2)`, then `Dim q() As Long` | "Duplicate declaration in current scope" |
| `ReDim z(2)` at module level | "Invalid outside procedure" |

**Readings:**
- **A `ReDim` on a new name declares a dynamic array local to its procedure,** of its `As` type or `Variant`, and sizes
  it. A later plain `ReDim` resizes it, and a later `ReDim Preserve` keeps its elements.
- **The declaration covers only the code below it.** A read above it is "Variable not defined" under `Option Explicit`,
  and without it the read makes an implicit Variant, which the `ReDim` then can't declare ("Invalid ReDim").
- **`ReDim Preserve` never declares.** On a new name it's "Variable not defined" even without `Option Explicit`.
- **A name the module declares is the module's.** A procedure that doesn't declare it resizes the module's array.

#1495's earlier comment measured the element type under `DefInt`: `ReDim i4(2)` is `Integer()`, from the `DefType`
range, where `ReDim a4(2)` is `Variant()`.

**Not measured:** a name declared `Public` in another module, and `ReDim` of a name used as a procedure's parameter.
