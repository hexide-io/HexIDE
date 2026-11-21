## Floating-point intermediates keep the x87's precision until stored (2026-11-21, #1214, SP6)

**The question.** .NET rounds every floating-point operation to an IEEE double. VB6 computes on the x87, whose registers hold a 64-bit significand and a far wider exponent range. Does vb6.exe keep an expression's intermediates that wide, and if so, where does it narrow them? #783 had one data point: `b + k / 86400#` matched a quotient held at 64 bits with only the sum rounded.

**The probe.** One project, each row its own function under `On Error Resume Next`. `Double` and `Single` operands are module-level variables set through a helper, so nothing folds at compile time. Each result is logged as its bytes, copied with `RtlMoveMemory` into two Longs and printed in hex. Built with `/make` on vb6.exe SP6 as native code and as p-code, all 22 rows logged the same lines in both.

**Two models,** each computed exactly with rational arithmetic and then rounded:
- **IEEE:** every operation rounded to a double, what .NET does.
- **x87:** every intermediate rounded to a 64-bit significand with the x87's exponent range, and only a stored result rounded to a double (or to a Single).

| Row | Statement | vb6.exe | IEEE model | x87 model |
|---|---|---|---|---|
| over_mul_div | `r = a * b / c`, each `1E+200` | `6974E718D7D7625A`, no error | overflow | `6974E718D7D7625A` |
| over_308 | `r = a * b / c`, `1E+308`, `10`, `10` | `7FE1CCF385EBC8A0`, no error | overflow | `7FE1CCF385EBC8A0` |
| under_mul_div | `r = a * b / c`, each `1E-200` | `16687E92154EF7AC` | `0` | `16687E92154EF7AC` |
| mul_sub | `r = a * b - c`, `0.1`, `3`, `0.3` | `3C80000000000000` | `3C90000000000000` | `3C80000000000000` |
| mul_sub_stored | `t = a * b` then `r = t - c` | `3C90000000000000` | `3C90000000000000` | `3C90000000000000` |
| mul_sub_arg | `a * b - c` passed `ByVal d As Double` | `3C80000000000000` | `3C90000000000000` | `3C80000000000000` |
| mul_sub_cmp | `If a * b - c > 4E-17` | False | True | False |
| clng_mul_sub | `CLng((a * b - c) * 1E+17)` | 3 | 6 | 3 |
| div_mul | `r = a / b * b`, `1`, `49` | `3FF0000000000000` (1) | `3FEFFFFFFFFFFFFF` | `3FF0000000000000` |
| div_mul_eq | `If a / b * b = a` | True | False | True |
| div_mul_stored | `t = a / b` then `r = t * b` | `3FEFFFFFFFFFFFFF` | `3FEFFFFFFFFFFFFF` | `3FEFFFFFFFFFFFFF` |
| sum10 | `r = a + a + ... + a`, ten `0.1`s in one expression | `3FF0000000000000` (1) | `3FEFFFFFFFFFFFFF` | `3FF0000000000000` |
| sum10_loop | `r = r + a` ten times in a `For` | `3FEFFFFFFFFFFFFF` | `3FEFFFFFFFFFFFFF` | `3FEFFFFFFFFFFFFF` |
| serial_783 | `r = a + b / 86400#`, `0.25`, `86399.6` | `3FF3FFFB253E463D` | `3FF3FFFB253E463E` | `3FF3FFFB253E463D` |
| sum_cmp | `If a + b = c`, `0.1`, `0.2`, `0.3` | False | False | False |
| sng_sum | `ss = sa + sb - sa`, Singles `16777216` and `1` | `3F800000` (1) | `00000000` | `3F800000` |
| sng_to_dbl | `r = sa + sb - sa`, into a Double | `3FF0000000000000` (1) | `0` | `3FF0000000000000` |
| sng_div_mul | `ss = sa / sc * sc`, `1`, `49` | `3F800000` | `3F800000` | `3F800000` |
| sng_div_mul_eq | `If sa / sc * sc = sa` | True | True | True |
| sng_mul | `r = sa * sb`, Singles `0.1` and `3`, into a Double | `3FD3333338000000` | `3FD3333340000000` | `3FD3333338000000` |

**Two rows about overflow, measured, not modelled:**
- **over_variant:** `vr = va * vb / vc`, three Variants each holding `1E+200`, raised **6**, and `vr` kept its old value.
- **over_stored:** `t = a * b` raised **6** at the store. Under `Resume Next`, `r = t / c` then gave `7FF0000000000000`, positive infinity, with no second error.

**What these show:**
- **Every row matches the x87 model, and 14 of the 20 modelled rows differ from IEEE.** In the six where the two models agree, vb6.exe agrees with both.
- **The range is the x87's.** An intermediate past a double's range doesn't overflow (`over_mul_div`, `over_308`), and one below it doesn't underflow (`under_mul_div`).
- **A store narrows, a comparison doesn't.** The value narrows to a double when it's stored in a variable (`mul_sub_stored`, `div_mul_stored`, `sum10_loop`). A comparison reads the wide value (`mul_sub_cmp`, `div_mul_eq`), and so does `CLng`. **Since overturned for comparisons:** those two rows can't tell, and rows built to tell show that a comparison narrows both sides to doubles first. `CLng` does read the wide value. See [Where a floating-point intermediate is narrowed](where-a-floating-point-intermediate-is-narrowed.md).
- **An argument narrows only the finished expression.** `mul_sub_arg` kept `a * b` wide until the whole of `a * b - c` was passed.
- **Single arithmetic is wide too.** `16777216 + 1 - 16777216` in Singles gives 1, where Single arithmetic gives 0. `0.1! * 3!` stored into a Double keeps bits a Single product wouldn't have.
- **Variant arithmetic is not wide.** Three Variants overflowed where three Doubles didn't. Reasoned, not checked: Variant operators go through the runtime's per-operation routines, which round each result to a double.

**Overturned:** #1214 asked whether VB6 keeps 80-bit intermediates or rounds each operation to 64 bits, and leaned towards neither. It keeps them, in native code and p-code alike, until a store.

**Resisted explanation:** after the failed store in `over_stored`, `t` read as infinity. Whether the store wrote infinity before the error was raised, or `t` held infinity some other way, the probe can't tell.

**HexIDE,** read from the code and not run: each operator returns its result as a value of its own (`ExpressionExecutor.MultiplyOrDivide` returns a `Vb6Value` from `VbNumeric.Mul`), so a Double expression is rounded at every step. That is the IEEE column, and `sum10`, `div_mul_eq` and `clng_mul_sub` would give different answers there.

### A single operation is rounded twice (2026-11-21, #1214, SP6)

**The question.** The rows above match the x87 model, but they don't single it out. Two other models also match all 20:
- intermediates held wider, as IEEE binary128 holds them, with a 113-bit significand;
- intermediates held exactly, as rationals.

Each was computed from the same operands, with a store rounding to a double. All three agree on every row above, so those rows say only that the intermediate is *at least* the x87's width.

**A single operation tells them apart:**
- **The x87 model rounds twice:** the result to a 64-bit significand, then again to a double at the store.
- **A wider or exact intermediate rounds once,** as .NET does.

The two differ when the first rounding lands exactly halfway between two doubles. Then the second rounding breaks the tie to the even one, where rounding once would have gone the other way.

**The probe:**
- **Operands** were set from their bytes with `RtlMoveMemory`, so no literal parsing is involved.
- **Each row was one statement** under `On Error Resume Next`, and the result was logged as its bytes.
- **The Variant rows** copied the same Doubles into Variants first.
- **The builds:** `/make` on vb6.exe SP6, as native code and as p-code. All 8 rows logged the same lines in both.

| Row | Statement | `a` | `b` | vb6.exe | x87 model | Rounded once |
|---|---|---|---|---|---|---|
| dr_add | `r = a + b` | 1 | 2^-53 + 2^-80, `3CA0000002000000` | `3FF0000000000000` | `3FF0000000000000` | `3FF0000000000001` |
| dr_sub | `r = a - b` | 1 | 2^-54 + 2^-81, `3C90000002000000` | `3FF0000000000000` | `3FF0000000000000` | `3FEFFFFFFFFFFFFF` |
| dr_mul1 | `r = a * b` | `3FF854CF44A8391C` | `3FF0D6956A336D6F` | `3FF99B20D028C45C` | `3FF99B20D028C45C` | `3FF99B20D028C45D` |
| dr_mul2 | `r = a * b` | `3FFE000719E04ADF` | `3FF300B8A360CC8E` | `4001D0B150B0E544` | `4001D0B150B0E544` | `4001D0B150B0E545` |
| dr_add_cbl | `r = CDbl(a + b)` | as dr_add | | `3FF0000000000000` | `3FF0000000000000` | `3FF0000000000001` |
| v_dr_add | `vr = va + vb`, Variants | as dr_add | | `3FF0000000000000`, a Double | `3FF0000000000000` | `3FF0000000000001` |
| v_dr_mul1 | `vr = va * vb`, Variants | as dr_mul1 | | `3FF99B20D028C45C`, a Double | `3FF99B20D028C45C` | `3FF99B20D028C45D` |
| v_dr_mul2 | `vr = va * vb`, Variants | as dr_mul2 | | `4001D0B150B0E544`, a Double | `4001D0B150B0E544` | `4001D0B150B0E545` |

- **The multiply operands** were found by searching random doubles in [1, 2) with the model for pairs whose two roundings differ. dr_mul1 and dr_mul2 are the first two found.
- **The same search over divisions `p / q`,** with `p` below 60 and `q` below 2000, found none. That's computed with the model, not measured.

**What it shows:**
- **vb6.exe matches the x87 model in all 8 rows, and rounding once in none.** So an intermediate holds exactly the x87's 64-bit significand: not more, as the earlier rows allowed.
- **Even a lone `a + b` or `a * b` stored at once can differ from .NET's answer in the last bit,** for operands like these.
- **`CDbl` adds nothing.** It narrows to a double, as the store does.
- **Variant arithmetic is rounded the same way, one operation at a time:** to 64 bits, then to a double. The rows above found that a Variant operation overflows as a double does (`over_variant`). Together the two fit a Variant operator computed on the x87 and stored into the Variant at once (reasoned from the two, not measured as one).

**Overturned:**
- **The obvious guess was that a single operation, stored at once, gives what IEEE doubles give.** It doesn't, whenever the x87's first rounding lands on a tie.
- **The rows above left "at least 64 bits" open.** It's exactly 64.
