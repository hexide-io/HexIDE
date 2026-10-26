## Bitwise And / Or / Xor / Not — the result-type ladder (2026-09-02, #166)

Measured with `scripts/vb6-oracle.ps1`. Every row below is `vb6.exe` output, not inference — and two of them
overturned what the implementation would otherwise have assumed.

### The result type is a ladder, not a widening

| left | right | result type |
|---|---|---|
| `Boolean` | `Boolean` | **Boolean** |
| `Boolean` | `Byte` | **Integer** |
| `Boolean` | `Integer` | Integer |
| `Boolean` | `Long` | Long |
| `Byte` | `Byte` | **Byte** |
| `Byte` | `Integer` | Integer |
| `Byte` | `Long` | Long |
| `Integer` | `Integer` | Integer |
| any | `Long` / `Single` / `Double` / `Currency` / numeric `String` | **Long** |

So the rule is: **both Boolean → Boolean; both Byte → Byte; otherwise the wider of the two, floored at
Integer.** Note the two exceptions that a plain "widen to the larger type" rule gets wrong — `Byte And Byte`
stays `Byte`, and `Byte And Boolean` jumps to `Integer` rather than staying `Byte`.

A numeric `String` yields **Long**, not Integer: `"12" And 10` is `8` as a `Long`.

### `Not` keeps its operand's width — including 8-bit for Byte

| expression | value | type |
|---|---|---|
| `Not CByte(5)` | **250** | **Byte** |
| `Not 5` | -6 | Integer |
| `Not CLng(5)` | -6 | Long |
| `Not True` | False | Boolean |
| `Not CDbl(5)` | -6 | Long |
| `Not "12"` | -13 | Long |

`Not CByte(5)` = 250 is the one to notice: `Byte` complements at **8 bits** (255 - 5), it does not widen to
Integer and give -6. The existing `TryUnpack<int>` comment — *"Byte widens to Integer (VB6: Byte arithmetic
promotes to Integer)"* — is correct for **arithmetic** and wrong for **bitwise**.

### Floating operands round to Long, banker's-style

| expression | value | why |
|---|---|---|
| `CDbl(2.5) And 3` | 2 | 2.5 → **2** (to even), `2 And 3` = 2 |
| `CDbl(3.5) And 3` | 0 | 3.5 → **4** (to even), `4 And 3` = 0 |
| `CDbl(2.4) And 3` | 2 | 2.4 → 2 |
| `CDbl(-2.5) And 3` | 2 | -2.5 → -2, `-2 And 3` = 2 |
| `CSng(2.5) And 3` | 2 | same for Single |

Round-half-to-even, which is .NET's `Math.Round` default — so the conversion is free rather than something
to hand-roll.

### Empty is an Integer zero; Null is NOT implemented and is not simple

| expression | value | type |
|---|---|---|
| `Empty And 5` | 0 | Integer |
| `Empty Or 5` | 5 | Integer |
| `Not Empty` | -1 | Integer |
| `Null And 5` | **Null** | Null |
| `Null And 0` | **0** | Integer |
| `Null Or 5` | **5** | Integer |
| `Null Or 0` | **Null** | Null |

`Empty` is implemented — it reduces to an Integer zero, which is all four of its rows.

**`Null` is measured here but deliberately NOT implemented**, because the rows do not fit the rule they
look like. A per-bit three-valued logic predicts `Null Or 5` is Null (the bits where 5 is zero stay
unknown); vb6.exe returns **5**. Whatever the real rule is, it is not the obvious one, and guessing it
would be exactly the mistake this document exists to prevent. `And`/`Or`/`Xor` continue to raise a type
mismatch on Null, unchanged, and the rule is left to the Null-propagation work that owns it.

### Boolean mixed with a number stays bitwise

`True And 2` is **2** (Integer), not `True`. `True` is -1, so `-1 And 2` = 2. `And` never becomes a logical
short-circuit operator: `True And True` is `True` only because `-1 And -1` is `-1`.
