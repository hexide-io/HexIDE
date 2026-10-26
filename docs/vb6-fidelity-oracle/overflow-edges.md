## Overflow edges (2026-10-14, #234)

The three rows of #234 came from the 2026-09-03 type-pair pass, which built native code only. They were measured again
as native code, as p-code and in vb6.exe's IDE (`/runexit`). Every cell was logged under `On Error Resume Next` as
TypeName, value and Err.Number. **The IDE matched p-code on all 49 cells**, and native code differed only at unary
minus.

**Unary minus at a type's minimum: no change.** Native code wraps (`-n` of an Integer -32768 is `Integer: -32768`, Err
0). P-code and the IDE raise Err 6, as HexIDE already does. The same holds for a Long, and for `-CInt(-32768)`, whose
operand is typed. A Variant holding the minimum widens in all three: `Long: 32768`, and `Double: 2147483648` for the
Long.

**0 / 0 is Overflow, unless Decimal's division does it.** The obvious reading of the native row, that a Double's `0# /
0#` is the odd one out, is wrong in both directions.

| operands | 0 / 0 | 1 / 0 |
|---|---|---|
| Byte, Integer, Long, Single, Double, Currency, each typed | **Err 6** | Err 11 |
| Boolean `False / False`, `Empty / Empty`, a numeric String `"0" / "0"` | **Err 6** | |
| a Variant Integer or Double, and mixed `Integer / Double` either way | **Err 6** | |
| **Decimal / Decimal** | **Err 11** | Err 11 |
| **a Decimal beside Integer, Double or Currency, either side** | **Err 11** | |

`\` and `Mod` by zero are Err 11 whatever the dividend, and `0 ^ -1` is Err 5, as before. A Decimal on either side
makes the division Decimal's, and `CDec(3) / 2` is a **Decimal** (`Currency / Currency` is a Double). The Decimal
cells were run native and p-code; the rest were run in the IDE too.

**A Variant Single that overflows widens to Double, as a Variant Integer does.**

| probe | result |
|---|---|
| `vs + vs`, `vs = CSng(3E38)` | `Double: 6.00000001099551E+38` |
| `vs * vt`, `vt = CSng(10)` | `Double: 3.00000000549776E+39` |
| `vs - vt`, `vs = CSng(-3E38)`, `vt = CSng(3E38)` | `Double: -6.00000001099551E+38` |
| `vs / vt`, `vt = CSng(0.1)` | `Double: 2.99999996079427E+39` |
| `vs + s` and `s + vs`, `s As Single = 3E38` | `Double: 6.00000001099551E+38` |
| `CSng(3E38) + CSng(3E38)`, and `s + s` | **Err 6** |
| `vs + vi`, `vi = CInt(1)` | `Single: 3E+38`, no overflow |

So a Variant on either side lifts the ceiling, and two typed Singles raise, exactly as for the integral types. Each
value is the sum computed in Double from the Singles' own values, which is why it isn't `6E+38`. **`CSng` returns a
typed Single:** its two results raise where two Variant Singles widen. That is the same test #987 applied to the other
conversions, so `CSng` now sits in HexIDE's list of typed intrinsics.

**Found on the way:** every arithmetic operator refuses a numeric String with Err 13, where VB6 reads it as a Double.
`"0" / "0"` is Err 6 in VB6 for that reason. Filed as #1047, with its own measurements.

### HexIDE

Since #234:
- **`/`:** zero over zero raises Err 6 unless an operand is a Decimal, and any other division by zero raises Err 11.
- **Single overflow:** in `+`, `-`, `*` and `/`, it widens to Double unless both operands are typed. A result that is
  all typed stays typed, so `(s + 0) * 10` still raises.
- **`CSng`'s result** is typed.

Pinned in `OverflowEdgeTests`. One older test, `SingleOverflow_IsStillError6`, had put its first operand in a Variant
where the oracle row it cites has two `CSng` results. With the widening it gave a Double, and it now has the row's own
form.

What it still does not do: a Decimal division gives back a Double, where VB6 gives a Decimal (#230 has the result-type
table). A numeric String operand was refused too, until #1047 (*A String in arithmetic*, below).

