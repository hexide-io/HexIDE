## Assumptions overturned by the oracle

These are the cases where building from documentation/memory would have been **wrong** — the justification for
always checking:

1. **`/` int/int is `Double`, not `Single`.** The planning pass asserted "true VB6 = Single"; the oracle showed
   Double, and the *existing* `DivisionOperator` test was already correct — the oracle **saved a wrong rewrite.**
2. **`Byte + Byte → Byte`** (overflows at 255), not → Integer. Found via a runtime-overflow modal.
3. **Currency dominates Double** in `+ − *` (`10@ + 1.5 → Currency`). The documented "Currency < Double" ladder
   is wrong for arithmetic result types.
4. **Octal `&O` two's-complement-wraps like hex** (`&O177777 → −1`). An earlier naive octal impl (magnitude
   only) was wrong and had to be unified with hex.
5. **Unary `−` of `Int16.MinValue` wraps to −32768 with no error** (a two's-complement quirk).
6. **`IsNumeric` accepts hex/octal strings, thousands separators, accounting parentheses and trailing signs**
   (`"&HFF"`, `"1,234"`, `"(12)"`, `"12-"` all True) but **rejects a currency symbol** (`"$12"` False) — much
   more permissive than a naive "does it parse as a number" would suggest.
7. **`IsNumeric(True) = True` and `IsNumeric(Empty) = True`, but `IsNumeric(<Date>) = False`** — Booleans/Empty
   are numeric; Dates are not.
8. **`Abs` preserves the operand type** (`Abs(-4) → Integer`), and **`Int` floors while `Fix` truncates**
   (`Int(-2.5) = -3`, `Fix(-2.5) = -2`) — they diverge only for negatives.
9. **A bare number is not a date to `IsDate`** (`IsDate(40000) = False`) even though `CDate(40000)` converts.
10. **`DateDiff` counts interval boundaries crossed, not the floored difference** (`DateDiff("h", 8:30, 9:15) = 1`,
    not `0`) — a naive "difference in units, truncated" would be wrong. And **`DateAdd` clamps month-ends**
    (`Jan 31 + 1 month = Feb 29`, not Mar 2).
11. **`For Each` over a multi-dimensional array is column-major — the FIRST subscript varies FASTEST.** A
    `(1..2, 1..3)` array yields `11,21,12,22,13,23`, i.e. `(1,1),(2,1),(1,2),(2,2),(1,3),(2,3)`. The
    interpreter-core spec's own note said "row-major, first subscript slowest" — **the oracle corrected the
    spec.** A naive nested flatten (row-major) would have shipped the wrong order.
12. **VB6's parser tolerates absurd expression nesting** — `/make` compiles **4096** nested parentheses
    (`x = (((…1…)))`) with no error (probed at 64→4096, all succeed); its parser is effectively unbounded here.
    HexIDE's recursive-descent parser overflows the C# stack near ~600, so it **cannot** match this — it installs a
    `ParseDepthGuard` (rule-depth 300, ~6× above any real code's ~50) that rejects deeper nesting as a clean
    "nesting too deep" compile error instead of an uncatchable crash. A **deliberate, documented divergence** on
    degenerate input (see `docs/interpreter-gaps.md`); no real VB6 program approaches it.
13. **A `Type` named like the project does not shadow the project in a qualified type name.** Read literally,
    MS-VBAL §5.6.4 puts user-defined types among the candidates of the type binding context, so in
    `New verify.Class` with a `Public Type verify` in scope, `verify` would bind to the `Type` and `.Class`
    would then fail. VB6 compiles it and builds the class, and does the same after `As`. The reading of the
    spec was the obvious one and it is wrong. See
    [Qualified type names](qualified-type-names-a-type-never-shadows-the-qualifier.md#qualified-type-names-a-type-never-shadows-the-qualifier-2026-09-19).

---

