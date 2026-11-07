## `Select Case` matching — the coercion rule (2026-09-03)

Having found that `Select Case` matching was the real defect, the rule itself had to be measured before it
could be widened. 30 cases; the result is a single rule with one genuinely surprising consequence.

> **VB6 coerces the CASE EXPRESSION to the SELECTOR's runtime type, then compares.**

> **Corrected 2026-10-13, #839's review round two:** for a selector with a FIXED type only, a declared
> variable, a literal, a typed Function, parameter, field or array element, or an expression of those. Every
> selector measured here was declared, apart from an unassigned Variant, so the rule was written for every
> selector. A Variant selector holding a value converts nothing: a Variant 2 does not match `Case 1.7`, and a
> Variant 1 does not match `Case "1.0"`. See *A typed selector and a Variant one*.

It does *not* compare numerically, and it is not symmetric. Two measurements settle it, and they disagree
with each other under any numeric reading:

| | VB6 |
|---|---|
| `Dim s As String : s = "1.0"` … `Case 1` | **ELSE** — `CStr(1)` is `"1"`, which is not `"1.0"` |
| `Dim s As String : s = "01"` … `Case 1` | **ELSE** — same reason |
| `Dim n As Long : n = 1` … `Case "1.0"` | **MATCH** — `CLng("1.0")` is 1 |
| `Dim n As Long : n = 2` … `Case 1.7` | **MATCH** — `CLng(1.7)` is 2 |

That last row is the one that pins the rule inside the numeric family: a numeric comparison of 2 against 1.7
is not equal, and VB6 matches anyway.

Everything else follows from it and was confirmed rather than assumed: a `Long` selector matches a bare
Integer literal; an `Integer` selector matches `1&`; `Double`, `Currency`, `Date` and `Boolean` selectors all
participate (`True` matches `Case -1`, a Date of serial 2 matches `Case 2`); an unassigned Variant matches
`Case 0`; the first of two matching arms wins; and a selector that is an *expression* is judged on its
runtime type, not its declared one. (**Corrected 2026-10-13:** an expression's type is fixed when its operands'
are, as `i + 1` is with a declared `i`, and a Variant when one is a Variant, as `v + 1` is; a Variant one
converts nothing. See *A typed selector and a Variant one*.)

### A failed coercion RAISES — it is not a non-match

| | VB6 |
|---|---|
| `Dim n As Integer` … `Case 40000` | **Err 6** (Overflow) — `CInt(40000)` cannot succeed |
| `Dim n As Long` … `Case "abc"` | **Err 13** (Type mismatch) |
| `Dim s As String` … `Case 40000` | **Err 0**, prints `ELSE` — `CStr` cannot fail, so this direction never raises |

Both of the raising cases were first observed as **`hung`** by the capture harness: a compiled exe with an
unhandled runtime error puts up a modal and waits, and the harness killed it. For programs this small,
"hung" *is* "raised". `On Error Resume Next` then converted the modal into an observable `Err.Number`.

A caveat recorded because it is easy to misread as a rule: under `On Error Resume Next` both raising cases
also print `MATCH`, because VB6 resumes **into the matched arm's body**. That is a resume-point behaviour,
not evidence that the arm matched — the unhandled form of the same program raises and stops.

### The rule is already implemented — under another name

`VbNumeric.CoerceOnStore` is this rule exactly, and was written and oracle-verified for coercion **on store**
(`Dim i As Integer : i = "12"`). String target takes anything and never fails; a numeric target parses
strings with Err 13 on garbage, rounds half-to-even, and range-checks with Err 6. Every measurement above
falls out of it. **VB6's coercion-on-store and its `Select Case` comparison are one rule**, which is why the
fix reuses it rather than restating it.

### The trap: `=` is a DIFFERENT rule

| | VB6 |
|---|---|
| `Select Case "1.0"` … `Case 1` | **does not match** |
| `If "1.0" = 1 Then` | **True** |
| `If "1" = 1 Then` | **True** |

The obvious tidy-up — give `Select Case` and `=` one shared comparison helper — is therefore wrong, and it
was the first thing suggested. The `=` operator compares numerically after coercing a String operand to a
number; `Select Case` coerces toward the selector. They agree on `"1"` and disagree on `"1.0"`, so a single
probe would have "confirmed" the wrong rule.

This also exposed a **separate defect**: HexIDE raises Type mismatch for `"1" = 1`, because
`ExpressionExecutor.GetTwoValuesSameTypesOrNull` has no String/numeric path and falls through to its throw.
VB6 says True. Filed separately — it is not a `Select Case` bug and must not be fixed by the same code.

