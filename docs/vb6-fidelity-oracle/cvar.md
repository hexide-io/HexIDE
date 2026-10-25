## CVar (2026-10-23, #1466)

Measured on vb6.exe, native and p-code identical, under en-GB. Each program `CVarTests` pins was made as written.

**CVar returns its argument as a Variant, with the argument's own type.** It converts nothing.

| probe | result |
|---|---|
| `TypeName(CVar(5))`, `CVar("5")`, `CVar(Null)`, `CVar(Empty)` | Integer, String, Null, Empty |
| `TypeName(CVar(n))`, `n As Integer` | Integer |
| `TypeName(CVar(2.5))`, `CVar(70000)`, `CVar(True)`, `CVar(#1/2/2000#)` | Double, Long, Boolean, Date |
| `TypeName(CVar(a))`, `a(1) As Integer` | `Integer()`: the typed array, not a Variant array |
| `TypeName(CVar(Array(1, 2)))` | `Variant()` |
| `TypeName(CVar(c))`, `c` a Collection | Collection: the object itself |
| `TypeName(CVar(k))`, `k` a class with a default `Value` of 7 | K1, and `IsObject(CVar(k))` True: the object, not its default member. `CVar(k) + 1` is 8 and `VarType(CVar(k))` 2, both of which read the default member |
| `TypeName(CVar(Nothing))` | Nothing |
| `v = CVar(a)`, then `v(0) = 9` | `a(0)` is still 1: the Variant holds a copy |

**The result reads as a Variant's** (#987's probe):
- `CVar(5) * 7000` and `CVar(n) * 7000` are a Long 35000, where `5 * 7000` is 6.
- `CVar(5)` against `Case Is < 5.4` takes the Case.
- `CVar("9")` against `Case Is > 10` takes Case Else, read as 9.
- `CVar("5") + 1` is a Double 6, and `CVar("abc") + 1` is 13.

**Two compile-time surprises:**
- **`CVar$` compiles, and ignores the `$`.** `TypeName(CVar$(5))` is Integer and `CVar$("9")` takes Case Else, as `CVar` does. `CVar$(Null)` is Null, not 94, so it does not behave as the `$` forms that answer a String do.
- **`CVar()` is "Syntax error", not "Argument not optional"** (HexIDE: #1512). `CVar(Expression:=5)` is "Syntax error" too (#1465).

### In HexIDE (#1466)

`d["CVar"]` returns its argument unchanged. It is listed:
- in `ExpressionExecutor.VariantIntrinsics`, so the result reads as a Variant's;
- in `DollarIgnored`, for `CVar$`;
- in `VB6BuiltIns.TakesTheArgumentItself`, so an object reaches it as the object, as it reaches `TypeName`.

A copy of an array is made where the result is stored, as for any Let of an array (#970).

