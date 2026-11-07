## Too few arguments to an intrinsic (2026-10-23, #1512)

Measured with `/make`, one project per call since the compile error stops the build, native and p-code agreeing on every
row. Each call was `x = <call>` with `x` a Variant. Eleven rows were measured separately with `scripts/vb6-oracle.ps1`,
and agree wherever the two overlap.

A compile error is the compiler's answer, not the runtime's. So these rows come from the oracle's VB6.EXE 6.00.8176 and
VBA6.DLL 6.0.8169, the release build, even though built EXEs run on SP6's MSVBVM60 (#1524). On SP6 (2026-10-24, #1663)
all 100 projects gave the same answers.

**"Syntax error":**

| calls | |
|---|---|
| `Abs()`, `Int()`, `Fix()`, `Sgn()` | the arithmetic intrinsics |
| `CBool()`, `CByte()`, `CCur()`, `CDate()`, `CDbl()`, `CInt()`, `CLng()`, `CSng()`, `CStr()`, `CVar()`, `CVErr()` | every conversion but `CDec` and `CVDate` |
| `Len()`, `LenB()`, `LBound()`, `UBound()` | |
| `InStr("a")`, `StrComp("a")` | one argument of two |
| `String()`, `String(3)` | none, and one of two |

**"Argument not optional":**

| calls | |
|---|---|
| `CDec()`, `CVDate()` | the two conversions outside the first set |
| `Chr()`, `Chr$()`, `ChrW()`, `Asc()`, `AscW()`, `Hex()`, `Oct()`, `Str()`, `Val()` | |
| `LCase()`, `UCase()`, `Trim()`, `Space()`, `Round()` | |
| `IsNull()`, `TypeName()`, `VarType()`, `Format()`, `Split()`, `Join()` | |
| `Mid("a")`, `Mid$("a")`, `Left("a")`, `Right("a")`, `Mid("abc", , 2)` | the length, or the start, left out |
| `Replace("a", "b")`, `IIf(True, 1)`, `DateAdd("d", 1)` | one argument short |

**Too many:** `Chr(65, 66)` is "Wrong number of arguments or invalid property assignment".

**None needed:** `Array()` compiles, and gives an empty `Variant()`.

**Comparing it with the named-argument rule.** The "Syntax error" set is the set that refuses a named argument with
"Syntax error" (#1465, `TakesNoNamedArguments`), name for name, with two differences:
- `String` takes its names, `Number` and `Character`, yet is "Syntax error" short of an argument, which
  `OmittedIsSyntaxError` already records.
- `LenB`'s named-argument row is unmeasured.

`CDec` and `CVDate` take names, and fall with the rest. This is a measured correlation across these 53 calls, not a rule
VB6 documents, so an intrinsic outside the table should be measured, not inferred.

### In HexIDE

Unchanged here: a call with too few arguments still fails as #1512 describes. These rows are what that fix pins.

