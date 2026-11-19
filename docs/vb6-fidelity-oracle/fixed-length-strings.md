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

## Arrays of fixed-length Strings, the width and parameters (2026-11-19, #179)

**The question.** After the first slice, HexIDE still refused these as unmeasured:
- an array of fixed-length Strings assigned whole, given a `ReDim … As`, or written and read by `Put` and `Get`;
- a width that names a constant other than an Integer or a Long;
- a fixed-length String passed ByRef to a parameter that is neither a String nor a Variant.

The section above left out what a String or Variant parameter holds after the callee stores into it, and arrays passed
to a parameter.

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code. Each case was its own program, so a compile error names its own
  row. Native and p-code logged the same line on every row except those under "Measured, but no rule I'd defend".
- **The record:** as above, `[…]` with codes in braces, then `Len`. A file's bytes were read back as hex. The rows run
  under `On Error Resume Next`, and `!ERR` after a step is the error that step raised.
- **The variables:** `d()` and `e()` are dynamic arrays, `a(2)` a static one, all `String * 3` unless a row says
  otherwise. `w` is a Variant, and `fx` a `String * 5`.

**The width can be any numeric constant, rounded half to even:**

| `Const W = …`, then `Private x As String * W` | `Len(x)` |
|---|---|
| `2.6`; `2.5`; `3.5` | 3; **2**; **4** |
| `3` declared As Byte, As Currency, As Double, As Single, As Date or As Variant | 3 |
| `"4"` | 4 |
| `True` | "Invalid length for fixed-length string" |

**Whole arrays.** `e(0)` holds "ab" unless a row says otherwise.

| Statement | vb6.exe |
|---|---|
| `d = e`, both dynamic | `d` is `(0 To 1)`: `[ab{20}]`, `[{0}{0}{0}]`. Then `d(0) = "abcdef"` gives `[abc]` and leaves `e(0)` as `[ab{20}]` |
| `d = a`, `a(1)` static | the same: a copy |
| `d = e`, with `e` never allocated and `d` allocated `(2)` | no error, and then `UBound(d)` raises 9: `d` is unallocated |
| `d = e` with `d` a `String * 4` or a `String * 2`; `String()` on either side; a static `d(1)` | **"Can't assign to array"** |
| `u.d = e`, a Type's field `d() As String * 3` | a copy, as above |
| `u.d = e`, the field `String * 4`, or `e` a `String()` | "Can't assign to array" |
| `u2 = u`, a Type with a static `a(1) As String * 3` | a copy: `u2.a(0) = "zzzz"` gives `[zzz]` and leaves `u.a(0)` alone |
| `w = e`; `w = a`; a Function As Variant returning `e`; `e` passed to `x As Variant`, ByRef or ByVal; `ReDim w(1) As String * 3` | **"Can't assign or coerce array of fixed-length string or user-defined type to Variant"** |
| `d = w`, `w` holding `Array("abcdef", "x")`, a `String()`, Empty or a String; `d = Split("abcdef,x", ",")` | **13** at run time. `d` keeps its bounds and contents, allocated `(4)` or never allocated |

**`ReDim` with `As`:**

| Declared | `ReDim …` | vb6.exe |
|---|---|---|
| `b() As String * 4` | `b(1) As String * 4` | allowed: `(0 To 1)` of four NULs, and `b(0) = "abcdefgh"` gives `[abcd]` |
| `b() As String * 4`, `b(0)` "ab" | `Preserve b(1) As String * 4` | `[ab{20}{20}]`, then four NULs |
| `b() As String * 4` | `b(1) As String * 6`; `b(1) As String` | "Can't change data types of array elements" |
| `b() As String` | `b(1) As String * 3` | "Can't change data types of array elements" |
| `z` not declared, with or without `Option Explicit` | `z(1) As String * 5` | declares it: `z(0) = "abcdefg"` gives `[abcde]`, and `z(1)` is five NULs |
| `Dim z() As String * 5` in the procedure | `z(1)` | the same |

**Passing one to a parameter:**

| Argument | Parameter | vb6.exe |
|---|---|---|
| `fx`; an element `a(1)` of a `String * 5` array; a Type's `String * 5` field | ByRef `Integer`, `Long`, `Single`, `Double`, `Currency`, `Byte`, `Boolean`, `Date` or `Object` | "ByRef argument type mismatch" |
| `(fx)`, in parentheses | ByRef `Integer` | runs, and `fx` keeps `[12{20}{20}{20}]` |
| `fx` | `n() As String`; `n() As Variant` | "Type mismatch: array or user-defined type expected" |
| an array of `String * 3` | `x() As String` | "Type mismatch: array or user-defined type expected" |
| a `String()` | `x() As String * 3` | "Type mismatch: array or user-defined type expected" |
| an array of `String * 3`, static or dynamic | `x() As String * 3` | compiles. The callee reads `[ab{20}]`, and its `x(0) = "abcdef"` leaves the caller's element `[abc]` |
| a dynamic one, `(1)` | `x() As String * 3`, the callee doing `ReDim x(3)` | the caller's array is `(0 To 3)` on return, and `x(3) = "abcdef"` is `[abc]` |

`Sub P(s As String * 5)` is "Expected array" (above). The width is allowed only on an array parameter.

**Inside the callee, a String or Variant parameter holds a plain String until the call returns.** `fx` holds "ab":
- **`Sub P(s As String)`:** `s` reads `[ab{20}{20}{20}]`, Len 5. After `s = "xyzxyzxyz"` its `Len` is 9, and after
  `s = s & "q"` it is 10. The caller gets `[xyzxy]`.
- **`Sub P(x As Variant)`:** `TypeName(x)` is String and `VarType(x)` 8. **`x` stays a String whatever is stored:**

| In the callee | then `x` | `fx` after the call |
|---|---|---|
| `x = "variantset"` | String, Len 10 | |
| `x = 12` | **String** `"12"` | `[12{20}{20}{20}]` |
| `x = 1.5` | String `"1.5"` | `[1.5{20}{20}]` |
| `x = Empty` | String `""` | |
| `x = Null` | **94**, and `x` stays a String | |

**`Put` of an array of them.** `a(0)` is "ab", `a(1)` "wxyz", and `a(2)` never assigned:

| `Put #n, 1, a` | Bytes |
|---|---|
| Binary, `a` static or dynamic `(2)`; Random `Len = 32`, `a` static | `61 62 20 77 78 79 00 00 00`: each element at its width, NULs as `00`, and no length |
| Random `Len = 32`, `a` dynamic `(2)` | `01 00`, `03 00 00 00`, `00 00 00 00`, then the same nine bytes |
| Random `Len = 32`, `a` dynamic `(1 To 2, 0 To 1)`, `a(1, 0)` "ab", `a(2, 1)` "cd" | `02 00`; `02 00 00 00` `00 00 00 00`; `02 00 00 00` `01 00 00 00`; then `61 62 20`, `00 00 00`, `00 00 00`, `63 64 20` |
| Random `Len = 4`, `a` static `(2)` | **59**, and nothing written |
| Binary, a Type of an Integer 1 and `a(1) As String * 3`, `a(0)` "ab" | `01 00 61 62 20 00 00 00`, and `Len(u)` is 8 |

A dynamic array in a Random file is preceded by its descriptor: the number of dimensions as an Integer, then each
dimension's count and lower bound as Longs, the last dimension first. The elements follow, the first index fastest.

**`Get` into one:**

| File | `Get #n, 1, a` | `a` afterwards |
|---|---|---|
| Binary, `abcdefghi` | `a` static `(2)` | `[abc]`, `[def]`, `[ghi]`, and `EOF` False |
| the same | `a` dynamic, allocated `(1)` | `[abc]`, `[def]`: only as many as it holds |
| Binary, `abcd` | `a` static `(2)` holding `xxx`, `yyy`, `zzz` | `[abc]`, `[d{0}{0}]`, `[{0}{0}{0}]`, and `EOF` True |
| Binary, `ab` | a scalar `String * 5` holding `xxxxx` | `[ab{0}{0}{0}]` |
| Random `Len = 32`, `Put` from a dynamic `String * 3` `(1)` holding "ab", "cd" | a dynamic `String * 3`, never allocated, or allocated `(4)` | `(0 To 1)`: `[ab{20}]`, `[cd{20}]` |
| the same | a dynamic `String * 2`, never allocated | `(0 To 1)`: `[ab]`, `[{20}c]` |

Reading past the end gives NULs, not the old text. In a Random file the descriptor sets the bounds, and the elements
are then read at the target's own width, from the bytes in order.

**Text outside the ANSI code page** (1252 here) goes through it as a plain String's does. `Put` to a Binary file:

| Text | In a String | In a `String * 3` | In an element of a `String * 3` array |
|---|---|---|---|
| `ChrW(&H3B1) & "b"`, α | `61 62` | `61 62 20` | `61 62 20` |
| `ChrW(&H416) & "b"`, Ж | `3F 62` | `3F 62 20` | `3F 62 20` |
| `ChrW(&H101) & ChrW(&H20AC)`, ā and € | | | `61 80 20` |

**Overturned:** `random-and-binary-files-get-put-and-len.md` reads "`?` where it has no byte", from Ж. Neither α nor ā
has a byte in code page 1252, yet both are written as `a`. So a character with a lookalike is written as that, and `?`
only where there's none. That's Windows' best-fit conversion, reasoned, not checked.

**Measured, but no rule I'd defend:** an array of another width passed to `x() As String * 3`. It compiles, and native
code and p-code disagree:

| Caller's array | The callee reads | native | p-code |
|---|---|---|---|
| `String * 4`: `abcd`, `efgh`, `ijkl` | `x(0)`, `x(1)`, `x(2)` | `[abc]`, `[def]`, `[ghi]` | `[abc]`, `[efg]`, `[ijk]` |
| the same, after the callee's `x(1) = "XYZ"` | the caller's three | `[abcX]`, `[YZgh]`, `[ijkl]` | `[abcd]`, `[XYZh]`, `[ijkl]` |
| `String * 2`: `ab`, `cd`, `ef` | `x(0)`, `x(1)` | `[abc]`, `[def]` | `[abc]`, `[cde]` |

Native code reads the caller's characters as one run, at the callee's width. P-code steps by the caller's width and
reads the callee's. Both read characters that belong to the next element. That looks like memory read past an
element, not a design, so it's recorded here and not offered as a rule.

**Readings:**
- **An array's width is part of its type.** A whole-array copy, a `ReDim … As` and an array parameter must all name
  the same width. Otherwise it's a compile error: "Can't assign to array", "Can't change data types of array elements"
  or "Type mismatch: array or user-defined type expected". The one exception is the row above that I wouldn't defend.
- **A copy is element by element and independent.** An unallocated source leaves the target unallocated.
- **Such an array never goes into a Variant:** every way of putting it there is a compile error. **A Variant's array
  never comes out into one either:** that's 13 at run time, with the target untouched.
- **A fixed-length String can't be passed ByRef to another scalar type.** A String or Variant parameter takes a plain
  String, and a Variant parameter keeps it a String. The width applies when the call returns.
- **In a file, each element takes its width in bytes,** as a scalar does. A dynamic array in a Random file gets the
  descriptor that other arrays get.
- **The width is any numeric constant** that rounds, half to even, to 1 through 65,526.

**Not measured:**
- a Type holding a fixed-length String array, in a Random file, and read back with `Get` in either kind;
- a double-byte code page;
- `Erase` of a dynamic one, and `ReDim Preserve` of a two-dimensional one.
