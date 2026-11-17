## Fixed-length Strings (2026-11-15, #179)

**The question.** #179 found `Dim buffer As String * 80` refused at every declaration in HexIDE. It left open:
- what an unassigned fixed-length String holds;
- how assignment pads and cuts, what `Len` says, and whether a comparison sees the padding;
- how one behaves in arrays, Type fields, parameters, `Mid`, `LSet` and `RSet`;
- which declarations compile.

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged the same lines. The rows that don't compile were
  each built on their own, as native code.
- **The record:** each String is logged with every character outside printable ASCII as its UTF-16 code in braces,
  and its `Len`, so `{0}` is a NUL and `{20}` a space.
- **The variables,** at module level unless a row says otherwise:
  - `fx` and `gx` are `String * 5`, and `kx` is `String * W` with `Const W = 6`;
  - `a(2)` is a `String * 3` array, and `b()` a dynamic `String * 4` one;
  - `u` and `u2` are a `Type` with an Integer and a `String * 4` field `s`.
- **Two runs:** an earlier run read `fx` in `Main`, where the harness's own `Dim f As Integer` file number shadowed it.
  Those rows were run again under other names, and only the second run's are recorded.

**Before any assignment, it holds NULs:**

| Declaration | Value |
|---|---|
| module-level `String * 5` | `[{0}{0}{0}{0}{0}] Len 5` |
| a procedure's `Dim t As String * 5` | `[{0}{0}{0}{0}{0}] Len 5` |
| `Static t As String * 3`, first call; the next call, after `t = "abcdef"` | `[{0}{0}{0}] Len 3`; `[abc] Len 3` |
| an element of `a(2) As String * 3`; after `ReDim b(1)`, `b(1)` | `[{0}{0}{0}] Len 3`; `[{0}{0}{0}{0}] Len 4` |
| a Type's `String * 4` field | `[{0}{0}{0}{0}] Len 4` |
| `String * W`, `Const W = 6` | `[{0}{0}{0}{0}{0}{0}] Len 6` |
| `a(2)` after `a(2) = "kk"` then `Erase a` | `[{0}{0}{0}] Len 3`: NULs again |

**What `fx` reports:** `TypeName` "String", `VarType` 8, `Len` 5 (a Long), and `LenB` 10, **an Integer**.

**Assignment pads with spaces on the right and cuts from the right.** Each row first sets `fx = "abcde"`:

| `fx = …` | Value |
|---|---|
| `"ab"`; `""`; `Empty` | `[ab{20}{20}{20}]`; five spaces; five spaces |
| `"abcdefg"` | `[abcde]` |
| `12`; `1.5`; `True`; `#1/2/2003#` | `[12{20}{20}{20}]`; `[1.5{20}{20}]`; `[True{20}]`; `[02/01]`, the en-GB date cut to 5 |
| `ChrW(&H3B1) & "b"` | `[{3B1}b{20}{20}{20}]`: kept as UTF-16 |
| `Null` | 94, and `fx` keeps `abcde` |
| `fx = fx & "x"`, `fx` holding `"ab"` | `[ab{20}{20}{20}]`: the x is cut off |

Arrays and Types cut and pad to their own width:
- `a(1) = "abcdef"` gives `[abc]`, and `b(0) = "z"` gives `[z{20}{20}{20}]`.
- `u.s = "ab"` gives `[ab{20}{20}]`.
- `u2 = u` followed by `u2.s = "rstuvw"` gives `u2.s` `[rstu]` and leaves `u.s` `[pq{20}{20}]`.
- `Len(u)` is 6: the Integer's 2 and the field's 4.

**The padding is part of the value.** With `fx` and `gx` both assigned `"ab"`:

| Expression | VB6 |
|---|---|
| `fx = "ab"`; `fx = "ab   "`; `fx = "ab" & Space$(3)`; `fx = gx` | **False**; True; True; True |
| `RTrim$(fx) = "ab"`; `fx > "ab"`; `fx < "ab "`; `StrComp(fx, "ab")` | True; True; False; 1 |
| `fx & "|"`; `Len(fx & "")`; `InStr(fx, " ")`; `Trim$(fx)`; `Left$(fx, 3)`; `fx Like "ab*"` | `[ab{20}{20}{20}|]`; 5; 3; `[ab]`; `[ab{20}]`; True |
| `CStr(fx)`; `v = fx` | `[ab{20}{20}{20}]`, a String of length 5, both |

**`Mid`, `LSet` and `RSet`,** each with `fx` holding `"abcde"`:

| Statement | Value |
|---|---|
| `Mid$(fx, 2, 2) = "XY"`; `Mid$(fx, 4) = "WXYZ"` | `[aXYde]`; `[abcWX]` |
| `LSet fx = "xy"`; `RSet fx = "xy"`; `RSet fx = "1234567"` | `[xy{20}{20}{20}]`; `[{20}{20}{20}xy]`; `[12345]` |

**Passed to a procedure,** with `fx` holding `"ab"`:

| Call | Value |
|---|---|
| `Sub P(s As String)`: `Len(s)` inside | 5: the padding goes too |
| that `P` assigning `s = "xyzxyzxyz"` | `fx` is `[xyzxy]`: written back, cut to the width |
| `Sub P(ByVal s As String)` assigning `s = "changed"` | `fx` unchanged |
| `Sub P(x As Variant)` assigning `x = "variantset"` | `fx` is `[varia]` |

Passing a fixed-length String to a ByRef `String` parameter compiles, whether the call is a statement (`R t`), a `Call`,
a function call `n = L(t)`, a parenthesised argument, a module-level variable, or a ByVal parameter.

**Declarations that don't compile:**

| Declaration | vb6.exe |
|---|---|
| `Sub P(s As String * 5)`, ByRef or `ByVal` | "Expected array" |
| `Function F() As String * 5`, `Property Get Q() As String * 5` | "Expected: end of statement" |
| `String * 0`, `String * 65527` | "Invalid length for fixed-length string" |
| `Dim x As String * n`, `n` a variable | "Constant expression required" |

`String * 65526` compiles.

**Readings:**
- **A fixed-length String starts as NULs, not spaces,** wherever it's declared, and `Erase` puts the NULs back.
  The guess in #179 that it space-pads holds only for assignment.
- **Every store pads with spaces or cuts from the right to the declared width.** That includes a ByRef String or
  Variant parameter writing back, `Mid`, `LSet` and `RSet`. Null is 94 and leaves the value alone.
- **The padding is part of the value everywhere it's read:** `Len`, comparison, concatenation, `CStr`, and a copy into
  a Variant.
- **The width must be a constant from 1 to 65,526.** A parameter or a return value can't be fixed-length.

**Measured, but no rule I'd defend:** `LenB(fx)` is an Integer, where `Len(fx)` is a Long. Perhaps the compiler folds
`LenB` of a fixed width to a constant; that wasn't checked.

**Not measured:**
- a fixed-length String in `Get` and `Put`, which `random-and-binary-files-get-put-and-len.md` already records;
- `Public` fixed-length Strings in an object module, recorded in `a-public-array-in-an-object-module.md`;
- `For Each` over an array of them, refused by VB6 as `a-udt-copied-whole-and-arrays-of-a-udt.md` records;
- API calls.
