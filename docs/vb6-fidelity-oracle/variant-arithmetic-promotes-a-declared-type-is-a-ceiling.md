## Variant arithmetic promotes; a declared type is a ceiling (2026-09-02, #122)

Re-measured because the rule as originally filed was wrong in a way that would have shipped a silent bug.

| probe | measured |
|---|---|
| `TypeName(30000)` | `Integer` — a literal is Integer |
| `a = 30000` (undeclared) | Variant, subtype `Integer` |
| `a = 30000 : b = 3 : a * b` | **`Long` / 90000** |
| `a = 30000 : b = 30000 : a + b` | **`Long` / 60000** |
| `a = 2000000000 : b = 3 : a * b` | **`Double` / 6000000000** |
| `a = 30000 : a * 3` (Variant × literal) | **`Long` / 90000** |
| `Dim a As Integer` × Variant `b` | **`Long` / 90000** — either side lifts it |
| Variant `b` × `Dim a As Integer` | **`Long` / 90000** — order irrelevant |
| **`30000 * 3` (literal × literal)** | **Err 6** |
| `Dim a As Integer` + literal `30000` | **Err 6** |
| `Dim a As Integer, b As Integer` … `a * b` | **Err 6** |
| `(a + 0) * 3`, `a` declared Integer | **Err 6** — fixedness **propagates** |
| untyped `Fact(10)` / `Fact(13)` | `3628800` **Long** / **Double** |

### The rule

> **If either operand is a Variant, the ceiling lifts and the result widens Integer → Long → Double by
> magnitude. If both operands are typed — declared *or literal* — the type is a ceiling and overflow is
> Err 6.**

Driven by **operand provenance**, not by the result. #122 originally recorded it as *"driven by the result,
not the operands"* with `30000 * 3` → 90000 at the head of its table; that row is wrong, and building to it
would have promoted `Dim i As Integer : i = i + 1` past 32767 in silence — the common shape, not a rare one.

Two consequences for any implementation:

- **A literal is fixed, not a Variant.** `Dim a As Integer` plus the literal 30000 raises Err 6.
- **Fixedness propagates through sub-expressions**, so it must ride on the *value*: `(a + 0)` keeps its
  ceiling. It cannot be looked up at the operator, because an operand may be a call result with no slot to
  interrogate — which is exactly the shape of `Fact = n * Fact(n - 1)`, the case #122 was filed from.

### And a correction to a wrong expectation of my own

`CStr(6000000000#)` is **`6000000000`**, not `6E+09`. VB6 does not switch a Double of that magnitude to
scientific notation. Recorded because a test was briefly written against the invented form.
