## Declared type, Variant subtype, and overflow promotion (2026-08-22)

Three behaviours turn on the same distinction — **was this operand statically typed, or a Variant?** — and
HexIDE currently cannot tell, because a declared variable does not keep its type.

### A declared variable reports its declared type

| `Dim x As T`, `x = 3`, `TypeName(x)` | VB6 | HexIDE |
|---|---|---|
| `Integer` | `Integer` | `Integer` ✓ |
| `Long` | `Long` | **`Integer`** |
| `Double` | `Double` | **`Integer`** |
| `Single` | `Single` | **`Integer`** |
| `Byte` | `Byte` | **`Integer`** |
| `Currency` | `Currency` | **`Integer`** |
| `Boolean` / `String` | `Boolean` / `String` | ✓ |

The assignment overwrites the slot with the *literal's* subtype and the declared type is lost. `Dim` itself
is right — it seeds a correctly-typed zero — so this is purely an assignment-side loss.

### …and the declared type drives the result type

| expression | VB6 |
|---|---|
| `declInt * declLong` (30000 * 3) | `90000` **Long** — the ladder already widens; no promotion needed |
| `declInt * declDbl` (3 * 2) | `6` **Double** |
| `declByte * declInt` (100 * 3) | `300` **Integer** |
| `declByte * declByte` (200 + 200) | **Err 6** — Byte+Byte stays Byte |

`declInt * declLong` is the one that shows the cost: HexIDE raises Err 6 there, not because promotion is
missing but because it thinks the Long operand is an Integer.

### Variant arithmetic widens instead of overflowing

Byte → Integer → Long → Double, and **stops**. Currency, Decimal and Double are terminal — overflowing one
of those is Err 6 with no promotion.

| expression (Variant operands) | result | type |
|---|---|---|
| `CByte(200) + CByte(100)` | 300 | **Integer** |
| `30000 * 3` | 90000 | **Long** |
| `2147483647 + 1` | 2147483648 | **Double** |
| `2000000000 * 3` | 6000000000 | **Double** |
| `CSng(3E+38) * CSng(3)` | 9.0E+38 | **Double** |
| `CCur(9E+14) * 10` | — | **Err 6** |
| `CDec("7.9E+28") * 10` | — | **Err 6** |
| `1E+308 * 10` | — | **Err 6** |
| `variant * declInt` (30000 * 3) | 90000 | **Long** — one Variant operand is enough |
| `variant * literal` (30000 * 3) | 90000 | **Long** |
| `declInt * declInt` / literal-only | — | **Err 6** — no Variant, no widening |

Operators that widen: `+`, `-`, `*`, `\` (`CInt(-32768) \ CInt(-1)` = 32768 **Long**), unary `-`, and `Abs`.
`/` and `^` are always Double anyway. Assigning a widened result back into a declared variable range-checks
normally: `i = 30000 * 3` is Err 6, `l = 30000 * 3` is 90000.

> **This corrects the qualifier in *Arithmetic result types* above.** That table was measured entirely
> through declared variables, and its conclusion — "there is no overflow auto-promotion" — holds only there.


### Coercion-on-store — what a declared variable does to the value you put in it

| assignment | result | rule |
|---|---|---|
| `Dim s As String : s = 5` | `"5"` String | anything stringifies |
| `s = True` | `"True"` String | |
| `Dim b As Boolean : b = 5` | `True` | CBool: any non-zero |
| `b = "True"` | `True` | strings parse |
| `b = "banana"` | **Err 13** | and raise when they cannot |
| `Dim i As Integer : i = "12"` | `12` | a numeric target parses a String too |
| `i = "abc"` | **Err 13** | |
| `i = 12.6` / `i = 12.5` | `13` / **`12`** | half-to-even, not away-from-zero |
| `i = True` | `-1` | |
| `Dim b As Byte : b = 300` | **Err 6** | the declared range is enforced on store |
| `Dim x As T : x = Null` | **Err 94** | every T, String included |
| `x = Empty` | `0` / `""` | |
| `Dim l As Long : l = 3 : l = l + 1` | `4` **Long** | the declared type survives arithmetic |
| `Dim a(1 To 3) As Long : a(1) = 5` | `5` **Long** | array elements carry the element type |
| `a(2) = 30000 : a(2) * 3` | `90000` **Long** | …and widen like one |

### ByRef takes its type from the caller, because VB6 will not let it differ

`Call TakesLong(v)` with `v As Variant` and `Sub TakesLong(ByRef x As Long)` is a **compile error** —
*"ByRef argument type mismatch"*. So the two sides always agree, and a ByRef alias can inherit the caller's
declared type with nothing to convert at the boundary. (HexIDE does not reject it: compile-time argument
type checking needs a bound AST, which is the language engine's job, not HexIDE's.)


