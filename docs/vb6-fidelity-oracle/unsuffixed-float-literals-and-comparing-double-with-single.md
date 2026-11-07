## Unsuffixed float literals, and comparing Double with Single (2026-09-03)

Two measurements, recorded together because the second was found while confirming the first. Harness:
`scripts/vb6-legality.ps1 -CaptureOutput`, four batches (33/19/13/9 cases), **two** known-illegal guard cases
per batch — one placed second, one last — with all four batches reporting exactly `illegal 2`.

### An unsuffixed floating-point literal is a Double. Unconditionally.

The oracle had **no row for this at all**: every float-typed row above uses a *suffixed* literal (`1.5#`,
`42!`), so the unsuffixed case had never been measured. HexIDE assumed Double on the strength of a source
comment (`ExpressionExecutor.cs:333`). The assumption is correct — this row makes it a measurement.

| probe | type |
|---|---|
| `TypeName(1.5)` / `TypeName(0.1)` / `TypeName(100.0)` | Double |
| `TypeName(.5)` — leading-dot form | Double |
| `TypeName(1E10)` — exponent, no decimal point | Double |
| `TypeName(1.5E10)` / `TypeName(1E-5)` | Double |
| `TypeName(3.402823E38)` — exactly Single's maximum | Double |
| `TypeName(1.7E308)` / `TypeName(1E39)` | Double — and both **compile**, which they could not if the literal were Single |
| `TypeName(-1.5)` | Double |
| `TypeName(1.5 + 1.5)` / `TypeName(1.5 / 2)` | Double |
| `Dim v: v = 1.5: TypeName(v)` | Double |
| `TypeName(1.5!)` / `TypeName(1.5#)` — positive controls | Single / Double |

It does not depend on magnitude, on the presence of a decimal point, on significant-digit count (18 digits is
still Double, silently truncated to Double precision), on unary minus, or on assignment into a Variant.

**Why the type name alone was not trusted.** `TypeName` cannot distinguish *parsed as Double* from *parsed as
Single, then widened*, so three independent probes ran alongside it:

| probe | result | what it proves |
|---|---|---|
| `Debug.Print 1.2345678901234 * 1` | `1.2345678901234` | 14 significant digits survive; a Single would have collapsed to ~7 |
| `Debug.Print 0.1 + 0.2 = 0.3` | **False** | the Double answer — Single rounding would have hidden the discrepancy |
| bare `Debug.Print 1.5` through a `ByVal Variant` helper | type `Double` | agrees without the program ever naming `TypeName`, and correctly reports `Single` for `1.5!` |

The legality row is the cleanest of the four, because it does not depend on `TypeName` at all: `1.7E308` and
`1E39` are compile-time overflows if the literal is a Single, and they compile.

### Comparing a Double with a Single happens at SINGLE precision

Found while confirming the above: `0.1 = CSng(0.1)` returns **True**, where a Double reading demands False.
That is not a defect in literal typing, and it is not a tolerance — it is a separate rule.

> **A `Double`-vs-`Single` comparison rounds the Double operand to Single first, then compares exactly.**
> `d = s` behaves as `CSng(d) = s`.

Exact rounding, not a tolerance, and one row settles which:

| probe | result |
|---|---|
| `Dim d As Double, s As Single: d = 0.1: s = 0.1: (d = s)` | True |
| `d = 0.100000006: s = 0.1: (d = s)` | **False** — the operands are only `4.5E-09` apart, inside a Single ulp at that magnitude |
| `CDbl(CSng(0.1))` | `0.100000001490116` |
| `CDbl(CSng(0.100000006))` | `0.100000008940697` — a *different* Single, which is exactly why the row above is False |
| `(0.1 = 0.1!)` — no conversion function anywhere in the expression | True |
| `(d = CDbl(s))` — an explicit `CDbl` on the Single operand | **False** — widening it by hand defeats the rule |

Measured scope rather than assumed: it holds for `=`, `<`, `>`, `>=` and `<>`, in both operand orders, for
literals and for declared variables alike, and on the **Variant** path — which is the shape HexIDE's
interpreter actually evaluates. It is *not* a general "compare at the narrower type" rule: `Double` vs
`Integer` and `Double` vs `Long` compare as expected (`1.4 = 1` is False).

**The asymmetry is the part worth remembering.** Arithmetic does the opposite:
`TypeName(0.1 - CSng(0.1))` is **Double** and retains the full `-1.49011611383365E-09` — folded and at run
time identically. **VB6 widens for arithmetic and narrows for comparison.**

**Recorded as resisting explanation.** No mechanism is offered here for that asymmetry, only a model that fits
all 20 relevant rows. Per this file's standing rule it is written down as measured rather than tidied into a
plausible rule — an honest "measured, no rule I would defend" beats a laundered generalisation, which has
already cost this project one silent bug.

Constant folding is **exonerated rather than implicated**: the folded and run-time forms agree to the digit,
and the effect reproduces with no conversion function present in the expression at all.

### Double vs Currency is the ladder, not a second anomaly

`TypeName(0.100001 - CCur(0.1))` is **Currency**, and `Double`-vs-`Currency` comparison likewise happens at
Currency's 4-decimal scale (`0.10001 = CCur(0.1)` True, `0.1001 = CCur(0.1)` False). This is **not** a second
anomaly — it is precisely the ladder already recorded under *Arithmetic result types* above, in which Currency
and Decimal sit **above** Double. Comparison simply follows the coercion.

It is recorded next to the Single case only to keep the two apart: with Currency, arithmetic and comparison
agree with each other. With Single, they do not.

### A Currency beside a Double: `+` and `-` convert the Double first, `*` gives a Double (#966)

Measured 2026-10-24 on vb6.exe SP6 (VB6.EXE 6.00.9782, MSVBVM60 6.00.9848) with `/make`, en-GB guest, one project
built native and p-code with every line identical; measured by the interpreter session for #966. Each expression was
logged with `TypeName` and `CStr`, then stored into a `c As Currency` and logged again. `d` is a typed Double.

| `d` | Nearest Double | `CCur(1) * d` | `c = CCur(1) * d` | `CCur(0) + d` | `CCur(1) - d` |
|---|---|---|---|---|---|
| 1.00005 | above | Double 1.00005 | 1.0001 | Currency 1.0001 | Currency -0.0001 |
| 2.00005 | below | Double 2.00005 | 2 | Currency 2 | Currency -1 |
| 0.00015 | below | Double 0.00015 | 0.0001 | Currency 0.0001 | Currency 0.9999 |
| 0.00025 | above | Double 0.00025 | 0.0003 | Currency 0.0003 | Currency 0.9997 |
| 1.00015 | above | Double 1.00015 | 1.0002 | Currency 1.0002 | Currency -0.0002 |
| 3.00005 | below | Double 3.00005 | 3 | Currency 3 | Currency -2 |

The operands in the other order give the same answers, and so does a Variant holding the Double.
`CCur(10) / (1 / d)` is a Double, and its store rounds as the `*` column's does.

| Expression | Result | Stored into a Currency |
|---|---|---|
| `1@ * 1.00005`, `1.00005 * 1@` | Double 1.00005 | 1.0001 |
| `0@ + 0.00015` | Currency 0.0001 | 0.0001 |
| `1@ + 0.00005` | Currency 1.0001 | 1.0001 |
| `2@ + 0.00005` | Currency 2.0001 | 2.0001 |
| `1@ * 1.00005!`, `CCur(1) * CSng(1.00005)` | Double 1.00004994869232 | 1 |
| `CCur(1) * CDec(1.00005)` | Decimal 1.00005 | 1 |

- **In `+` and `-` the Double is converted to Currency on its own, by its binary value, and the sum is taken in
  Currency.** `2@ + 0.00005` decides it: summed as Doubles, 2.00005 sits just below its tie and would round to 2,
  but 0.00005 on its own sits just above its tie and becomes 0.0001, so the answer is 2.0001.
- **`*` and `/` give a Double and do not round.** Only the store into a Currency rounds, and it rounds by the binary
  value, as `c = 1.00005` does. This is the ladder's exception for `*` that the operator tables record (#230).
- HexIDE implements the `+` and `-` rows since #966, and the `*` rows since #230.

- HexIDE implements the `+` and `-` rows since #966. `*` still gives a Currency, which is #230's cell.

### Consequences for the interpreter

`ExpressionExecutor.GetTwoValuesSameTypesOrNull` uses **one** widening ladder for both arithmetic and
comparison, and its `NumericRank` ladder is `Byte < Integer < Long < Single < Currency < Decimal < Double`.
Two divergences follow, both in the wrong-value class:

- **Comparison never narrows.** `0.1 = CSng(0.1)` is True in VB6 and False in HexIDE.
- **`NumericRank` contradicts the ladder recorded in this very file.** The measured ladder puts Currency and
  Decimal *above* Double; the code puts Double at the top. So `Double + Currency` yields Currency in VB6 and
  Double in HexIDE, and likewise for Decimal.

The second is the more uncomfortable of the two: it was never an unmeasured assumption. The correct ladder has
been sitting in *Arithmetic result types* since the first oracle pass, and the code simply does not implement
it. A measured fact that no test asserts is only marginally better than an unmeasured one.

