## Numeric type pairs — five arithmetic tables, and comparison is a sixth (2026-09-03)

The single largest measurement in this file, and it **overturns the premise every earlier numeric entry was
written under.** *Arithmetic result types* above records "Ladder (widest wins)", and that framing is wrong:
there is no one ladder. `+` and `-` share a table, `*` differs from it in four cells, `/` is a third table,
`\`/`Mod` a fourth, `^` a fifth — and comparison is a sixth that is not a ladder at all. The rows above are
individually correct; the generalisation drawn from them was not.

**Method.** `scripts/vb6-legality.ps1 -CaptureOutput` against real `vb6.exe`. Ten batches, **2066 records**,
all seven operators × all 49 ordered pairs over `{Byte, Integer, Long, Single, Double, Currency, Decimal}`,
using real typed variables (`By=6 In=7 Lo=8 Sg=9 Db=10 Cu=11 De=CDec(12)`) so no literal-typing rule
interferes. Every batch carried a known-illegal guard in **position 2** and another **last**; all twenty
guards failed to compile, every case's output was distinct by hash, and record counts matched emitted probe
counts exactly. Six batches also carried a positive control (`CSng`/`CDbl`/`CInt`/`CLng`/`CCur`/`CDec`/`CByte`
→ their own type), correct in all six.

*Transport note:* this was the first run on the JSON-per-line capture format. All 2066 records parsed, **0
failures**, with tab, embedded quote, backslash, CR, LF and NUL all surviving intact and each still one line.

### `+` and `-` — identical tables

| L\R | Byte | Integer | Long | Single | Double | Currency | Decimal |
|---|---|---|---|---|---|---|---|
| **Byte** | Byte | Integer | Long | Single | Double | Currency | Decimal |
| **Integer** | Integer | Integer | Long | Single | Double | Currency | Decimal |
| **Long** | Long | Long | Long | **Double** | Double | Currency | Decimal |
| **Single** | Single | Single | **Double** | Single | Double | Currency | Decimal |
| **Double** | Double | Double | Double | Double | Double | **Currency** | Decimal |
| **Currency** | Currency | Currency | Currency | **Currency** | **Currency** | Currency | Decimal |
| **Decimal** | Decimal | Decimal | Decimal | Decimal | Decimal | Decimal | Decimal |

**`Long` + `Single` is `Double`.** Long's 31 bits exceed Single's 24-bit mantissa, and VB6 refuses to lose
them. This is a *result*, not a coercion nicety: `Lo 16777217 + Sg 0` → `Double 16777217`, where a Single
result would give 16777216.

### `*` — **not** the same table as `+`

| L\R | Byte | Integer | Long | Single | Double | Currency | Decimal |
|---|---|---|---|---|---|---|---|
| **Byte** | Byte | Integer | Long | Single | Double | Currency | Decimal |
| **Integer** | Integer | Integer | Long | Single | Double | Currency | Decimal |
| **Long** | Long | Long | Long | **Double** | Double | Currency | Decimal |
| **Single** | Single | Single | **Double** | Single | Double | **Double** | Decimal |
| **Double** | Double | Double | Double | Double | Double | **Double** | Decimal |
| **Currency** | Currency | Currency | Currency | **Double** | **Double** | Currency | Decimal |
| **Decimal** | Decimal | Decimal | Decimal | Decimal | Decimal | Decimal | Decimal |

Four cells differ from `+`: **`Currency × Single` and `Currency × Double` are `Double`**, where
`Currency + Single` and `Currency + Double` are `Currency`. Re-measured at magnitudes 2 and 3 to rule out
overflow avoidance — it reproduces at any size. Proof the types really are what they say:

```
Cu 1@ + Db 1E30   ->  Err 6          (overflows, because the result type IS Currency)
Cu 1@ * Db 1E30   ->  Double 1E+30   (does not, because it IS Double)
```

So **Currency's rank is operator-dependent**: above Double for `+`/`-`, below Single and Double for `*`.

### `/` — a third table

| L\R | Byte | Integer | Long | Single | Double | Currency | Decimal |
|---|---|---|---|---|---|---|---|
| **Byte** | Double | Double | Double | **Single** | Double | Double | Decimal |
| **Integer** | Double | Double | Double | **Single** | Double | Double | Decimal |
| **Long** | Double | Double | Double | Double | Double | Double | Decimal |
| **Single** | **Single** | **Single** | Double | **Single** | Double | Double | Decimal |
| **Double** | Double | Double | Double | Double | Double | Double | Decimal |
| **Currency** | Double | Double | Double | Double | Double | Double | Decimal |
| **Decimal** | Decimal | Decimal | Decimal | Decimal | Decimal | Decimal | Decimal |

`/` **never yields Currency** — even `Currency / Currency` is Double. It yields Decimal whenever either
operand is Decimal; Single only when every operand is Byte/Integer/Single and at least one is Single;
Double otherwise. Note the shape is the *opposite* of the obvious guess: `Single / Single` is **Single**
while `Byte / Byte` is **Double**.

### `\` and `Mod` — identical tables

`Byte \ Byte` → **Byte**. Any pair drawn from `{Byte, Integer}` → **Integer**. **Everything else → Long**,
including `Decimal \ Decimal`, `Currency Mod Currency` and `Single \ Single`.

Operands are banker's-rounded first: `2.5 \ 1 = 2`, `3.5 \ 1 = 4`, `-2.5 \ 1 = -2`, `10.6 Mod 3 = 2`.

### `^` — Double in all 49 cells

Including `Decimal ^ Decimal`. `0# ^ -1` → **Err 5** (not 6, not 11); `1E308 ^ 2` → Err 6.

### Unary minus

`Byte` → **Integer**. Every other type preserves itself. (`Boolean` → Integer.)

### Comparison is a per-pair table, and it NARROWS

Constructed pairs equal at one candidate precision and different at the other, both operand orders, `=`,
`<`, `>`, `<>`.

| pair | probe | `=` | compared at |
|---|---|---|---|
| Single / Double | `16777216!` vs `16777217#` | **True** | **Single** |
| Single / Long | `16777216!` vs `16777217&` | False | **Double** |
| Double / Long, / Integer, / Byte | `100.5#` vs `100` | False | Double |
| Currency / Double | `1@` vs `1.00001#` | **True** | **Currency (4 dp)** |
| Currency / Single | `1@` vs `1.00001!` | **True** | **Currency (4 dp)** |
| Currency / Long, / Integer, / Byte | `100.0001@` vs `100` | False | Currency |
| Decimal / Double | `CDec("1.0000000000000000001")` vs `1#` | False | **Decimal** |
| Decimal / Currency, / Single | — | False | Decimal |

So the rule is *compare in the narrower or exact domain*, with **one escalation**: `Single`/`Long` goes to
Double, because neither type contains the other. `Double` loses to everything except that pair.

**It is exact rounding, not a tolerance.** `d = 0.100000006` and `s = 0.1` compare **False** despite being
`4.5E-09` apart, because they round to *different* Singles (`0.100000008940697` vs `0.100000001490116`).

**It is round-half-to-even, agreeing with the explicit conversion.** Using the only two values a Double holds
exactly on a 4-dp tie:

```
Cu 0.0312 = Db 0.03125  -> True     CCur(0.03125) -> 0.0312   (312.5 -> 312)
Cu 0.0313 = Db 0.03125  -> False
Cu 0.0937 = Db 0.09375  -> False    CCur(0.09375) -> 0.0938   (937.5 -> 938)
Cu 0.0938 = Db 0.09375  -> True
```

**The Single narrowing applies the exponent range too:** `sg 0 = db 1E-300` is **True** (the Double flushes to
zero); `sg 1E-30 = db 1E-300` is False; `sg 0 < db 1E-300` is **False**.

**A comparison NEVER raises.** Twenty-two out-of-range comparisons (`Byte` vs `Integer 300`, `Currency` vs
`1E30`, `Decimal` vs `1E30`, `Single` vs `1E300`) all returned a Boolean with `Err.Number = 0`, and still
ordered correctly. Contrast `CCur(1E30)` and `CSng(1E300)`, which both raise **Err 6**. So whatever performs
this narrowing is not a checked conversion.

`<`, `>` and `<>` agree with `=` on every pair, in both operand orders. `<=` and `>=` were **not probed**.

**`<=` and `>=`, measured on 2026-10-27 (#229).** Native and p-code agreed. Each pair was typed, both orders, and two of
them were repeated as Variants:

| Pair | `=` | `<` | `>` | `<=` | `>=` | reversed `<=` | reversed `>=` |
|---|---|---|---|---|---|---|---|
| Single 0.1, Double 0.1 | T | F | F | T | T | T | T |
| Single 16777216, Long 16777217 | F | T | F | T | F | F | T |
| Currency 0.0312, Double 0.03125 | T | F | F | T | T | T | T |
| Decimal 0.10000000000000001, Double 0.1 | F | F | T | F | T | T | F |
| Single 100.5, Integer 100 | F | | | F | T | T | F |

So `<=` and `>=` narrow as `=` does, pair by pair, and agree with `<` and `=` in both orders.

### Overflow — typed raises, with two silent exceptions

| probe | result | Err |
|---|---|---|
| `Integer 30000 + 30000`, `Integer 300 * 300` | — | 6 |
| `Byte 200 + 200`, `Byte 1 - 2` | — | 6 |
| **`Byte 20 * 20`** | **`Byte 144`** | **0** |
| `Byte` `16*16` / `100*3` / `255*2` / `255*255` / `13*23` | `0` / `44` / `254` / `1` / `43` | 0 |
| `Long 2e9 + 2e9`, `Long 1e5 * 1e5` | — | 6 |
| `Single 1E30 * 1E30`, `Double 1E300 * 1E300` | — | 6 |
| `Currency 9e14 + 9e14`, `Decimal 7.9E28 * 10` | — | 6 |
| **`-n` where `n As Integer = -32768`** | **`Integer -32768`** | **0** |
| **`-m` where `m As Long = LongMin`** | **`Long -2147483648`** | **0** |
| `CInt(0) - n`, `CLng(0) - m` | — | 6 |
| `IntMin \ -1`, `LongMin \ -1` | — | 6 |
| `x / 0`, `x \ 0`, `x Mod 0` (Integer/Double/Currency/Decimal) | — | **11** |
| **`0# / 0#`** | — | **6**, not 11 |
| `1E30 \ 1`, `1E30 Mod 3` | — | 6 |

**`Byte * Byte` silently wraps mod 256** while `Byte + Byte` and `Byte - Byte` raise. Every value above is the
true product mod 256.

> **Corrected 2026-10-14, #971's review round four: native code only.** These rows were measured with native builds.
> Built as p-code and run in the IDE, each pair below raises 6, as `Byte + Byte` does; native code still wraps, as
> above:
>
> | `Dim a, c As Byte` | Native | P-code and the IDE |
> |---|---|---|
> | `20 * 20`; `16 * 16`; `255 * 255`; `13 * 23`; `200 * 200` | `144`; `0`; `1`; `43`; `64`, Byte, Err 0 | 6, each |
> | `200 + 200` | 6 | 6 |
>
> So multiply is not an exception to Byte's ceiling in the build the IDE runs. It looks like native code omitting
> one overflow check, though that is a guess, not a measurement. The unary-minus row beside it was measured natively
> too and has not been re-run as p-code. **Unary minus of a type's minimum silently returns the minimum**, while subtracting it
from zero raises. Both were disbelieved on first measurement and re-run in independent cases.

### The Variant path is the typed path plus promotion

A full 49-pair × 3-operator diff of Variant-built against typed-built tables: **0 differences in 147 cells.**
All 12 comparison pairs agree too, in both orders and mixed Variant/typed. Overflow is the **only** divergence:

| Variant probe | result | typed equivalent |
|---|---|---|
| `CInt(30000) + CInt(30000)` | `Long 60000` | Err 6 |
| `CByte(200) + CByte(200)` | `Integer 400` | Err 6 |
| **`CByte(20) * CByte(20)`** | **`Integer 400`** | **`Byte 144`** |
| `CLng(2e9) + CLng(2e9)` | `Double 4000000000` | Err 6 |
| `CSng(3E38) + CSng(3E38)` | `Double 6.00000001099551E+38` | Err 6 |
| `-CInt(-32768)` | `Long 32768` | `Integer -32768` |
| `CCur(1e9) * CCur(1e9)`, `CDec("7.9E28") * CDec(10)` | — | Err 6 — **neither promotes** |

Promotion ladder: `Byte → Integer → Long → Double`, plus `Single → Double`. Currency and Decimal are terminal.

### Why a wrong result type is a wrong answer

```
Cu 1@ + Db 0.00005        -> Currency 1.0001    (a Double result would be 1.00005)
Cu 0@ + Db 0.12345        -> Currency 0.1235    (Db 0.12345 alone is Double 0.12345)
(Cu 0@ + Db 0.12345) * 2  -> Currency 0.247     (a Double result would be 0.2469)
Lo 16777217 + Sg 0        -> Double 16777217    (a Single result would be 16777216)
```

### Boolean and Date participate, and are not type errors

`Bool+Int → Integer(6)`, `Bool+Bool → Integer(-2)`, `Bool*Int → Integer(-7)`, `Bool+Lng → Long`,
`Bool+Dbl → Double`, `Bool+Cur → Currency`, and `bo = CInt(-1)` → **True**.
`Date±Int → Date`, `Date+Dbl → Date`, `Date+Cur → Date`, `Date+Date → Date`, but
**`Date-Date → Double`** and **`Date*Int → Double`**. `dt = CDbl(dt)` → True.

**Boolean in a comparison, measured on 2026-10-27 (#229).** Native and p-code agreed. True compares as the Integer -1
and False as 0, typed and as Variants, with no error:
- `True = -1`, `True <= -1` and `True >= -1` are True, and `True < -1` is False.
- `True < 0` is True, `True = 1` is False, `True > -1.5` is True, and `True = CDbl(-1)` is True.
- `False = CByte(0)` and `False = CCur(0)` are True.
- **`False < True` is False**, because -1 < 0.

### What resisted explanation — recorded, not tidied

**(a) The Currency-vs-Double comparison narrows to 4 dp but never overflows, and no mechanism is offered.**
A specific hypothesis was formed and **tested and falsified**: that the narrowing is a masked x87 store
(`FMUL 10000; FISTP qword`), whose overflow would yield the "integer indefinite" pattern
`0x8000000000000000` — which *is* Currency's minimum. That predicts `cuMin = 1E30` → True and
`cu1 < 1E30` → False. Measured: **False and True respectively.** The prediction fails. "Round the Double to
4 dp in the Double domain, then compare as Double" fits every measurement, but it fits without evidence and
is *not* stated as a rule. The falsified hypothesis is kept because the next reader will form it too.

**(b) Single and Currency narrow by visibly different mechanisms.** The Single side behaves exactly like an
*unchecked* C `(float)` cast — `1E-300` flushes to zero, `1E300` neither raises nor saturates. The Currency
side cannot be an unchecked scaled-int64 cast, because (a) rules that out. Two narrowings, two mechanisms,
one describable and unexplained.

**(c) `Byte * Byte` wraps while `Byte + Byte` and `Byte - Byte` raise.** (**Corrected 2026-10-14:** native code only;
p-code and the IDE raise 6. See the correction above.) No rule explains why multiply is the
exception. Confirmed across seven value pairs in two independent cases. **This is the most dangerous single
fact here**, because HexIDE raises where VB6 runs — a false rejection, the top severity class.

**(d) The comparison domain for `Single` vs `Integer`/`Byte` is UNDETERMINED.** The probe separates "integer
domain" from "float domain" but not Single from Double, and no Byte- or Integer-valued pair can distinguish
them, because every Integer value is exact in Single. Recorded as unmeasured rather than assumed — do not
guess it when implementing.

*Added 2026-10-27 (#229):* this one cannot be measured, because there is nothing to observe. A Single is exact in
Double, and an Integer or a Byte is exact in both. So the two domains give the same answer for every such pair, and
an implementation may use either.

**(e) The comparison tie rule is only resolved for dyadic values.** `0.03125` and `0.09375` are the only 4-dp
tie values a Double holds exactly, and both show half-to-even. A non-dyadic "tie" like `1.00005` is not a tie
in binary at all, so `Cu 1@ = Db 1.00005` → False is not evidence about the rule. Worth keeping as an oddity:
`Cu 1@ + Sg 0.00005!` → `Currency 1` while `Cu 1@ + Db 0.00005#` → `Currency 1.0001` — the Single and Double
representations of `0.00005` fall on opposite sides of the midpoint.

### Expectations overturned, including our own

Recorded because the wrong guess is what the next reader will also make:

- **`*` was expected to share `+`'s table.** It does not. The measurement was re-run at a different magnitude
  on the assumption it must be overflow avoidance. It is not.
- **`Single / Single` was expected to be Double**, since `/` is "the floating divide". It is Single — and
  `Byte / Byte` is Double. The rule is the opposite shape to the guess.
- **`Decimal / anything` was expected to be Double**, on the same reasoning. It is Decimal.
- **`Byte * Byte` was expected to raise like `Byte + Byte`.** It wraps, natively. In p-code and the IDE it raises, as
  expected (2026-10-14), so the surprise was the native build's.
- **`-n` at `n = -32768` was expected to raise.** It returns `-32768`.
- **The Single/Long comparison was expected to narrow to Single**, matching the Single/Double case. It
  escalates to Double instead — so the comparison defect in the interpreter is wrong in *both* directions and
  cannot be fixed by inverting a ladder.

### Consequences for the interpreter

HexIDE has **two** ladders, not one — a fact worth stating because reading only `ExpressionExecutor` suggests
otherwise, and that misreading has already produced one incorrect issue.

- **Arithmetic** uses `VbNumeric.CoreRank` (`VbNumeric.cs:26`) — `By<In<Lo<Sg<Db<Cu<De`. Its comment is
  correct that Currency dominates Double, and correct **only for `+` and `-`**. **27 cells diverge**: `Lo±Sg`
  and `Lo*Sg` (should be Double), `Cu*Sg` and `Cu*Db` (should be Double), all 13 Decimal cells of `/` (should
  be Decimal), `Lo/Sg`, and `By\By` / `By Mod By` (should be Byte). Filed as #230.
- **Comparison** uses `ExpressionExecutor.NumericRank` (`:100`) — `By<In<Lo<Sg<Cu<De<Db` — and widens where
  VB6 narrows. **4 of 21 pairs diverge**, in both directions. Filed as #229.
- **`Byte * Byte`** and **unary minus at a type minimum** raise where VB6 succeeds. Filed as #233, #234. (**Corrected
  2026-10-14:** VB6's native build succeeds at `Byte * Byte`, and its p-code build and the IDE raise, as HexIDE does.
  Unary minus has since been re-measured, as p-code and in the IDE: it is native code's too, and HexIDE matches the IDE.
  See *Overflow edges*.)
- **`0#/0#`** reports 11 where VB6 reports 6, and **Variant Single overflow** raises where VB6 promotes. #234. (**Fixed
  2026-10-14,** after re-measuring both as p-code and in the IDE, which widened the first and found an exception to it.
  See *Overflow edges*.)

The shape of the fix these measurements point to: **per-operator arithmetic tables and a separate per-pair
comparison table, both generated from the rows above rather than from a rank function.** All 49 pairs × 7
operators are recorded, so the tables can be written directly from measurement instead of inferred from a
rule — which is what produced the wrong generalisation this section had to overturn.

---

