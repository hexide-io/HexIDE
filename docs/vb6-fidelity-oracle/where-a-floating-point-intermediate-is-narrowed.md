## Where a floating-point intermediate is narrowed: comparisons, conversions and calls (2026-11-21, #1214, SP6)

**The question.** [Floating-point intermediates keep the x87's precision until stored](floating-point-intermediates-keep-the-x87s-precision-until-stored.md) found three things:
- an expression's intermediates are held at the x87's 64-bit significand;
- they narrow to a double at a store or an argument;
- "a comparison reads the wide value, and so does `CLng`".

**That last line didn't follow from its rows.** In `mul_sub_cmp`, `div_mul_eq` and `clng_mul_sub`, the wide value is itself exactly a double: 2^-55, 1, and the same 2.7755575615628914 either way, computed with the model. So narrowing it first would have changed nothing. The rows show the operands were computed wide. They can't show whether the comparison or `CLng` then read them wide.

**This probe asks it with values that tell:**
- **Setup A:** `a = 1` and `b = 2^-60`, set from its bytes. `a - b` is 1 - 2^-60 held wide, and exactly 1 as a double.
- **Setup B:** `a = 0.1`, `b = 3`, `c = 0.3`. `a * b - c` is 2^-55 if `a * b` stays wide, and 2^-54 if `a * b` is narrowed to `0.30000000000000004` first.
- **Setup C:** `a = 1.5` and `b = 2^-60`. `a - b` is 1.5 - 2^-60 wide, and exactly 1.5 as a double.

**The build:** one project, each row its own function under `On Error Resume Next`, with `Double` module-level operands set through a helper. Built with `/make` on vb6.exe SP6 as native code and as p-code. Every row logged the same in both.

| Statement | Setup | If it reads the wide value | If it narrows first | vb6.exe |
|---|---|---|---|---|
| `If a - b = 1` | A | False | True | **True** |
| `If a - b = 1#` | A | False | True | **True** |
| `If a - b = c`, `c` a Double holding 1 | A | False | True | **True** |
| `If a - b < 1` | A | True | False | **False** |
| `If a - b < c` | A | True | False | **False** |
| `If a - b <> 1` | A | True | False | **False** |
| `If 1 > a - b` | A | True | False | **False** |
| `If a - b < a - b / 2` | A | True | False | **False** |
| `r = (a - b < 1)` | A | True | False | **False** |
| `Select Case a - b` / `Case 1` | A | Case Else | Case 1 | **Case 1** |
| `If a - b - 1 < 0`: the control | A | True | True: -2^-60 is exact | True |
| `r = Int(a - b)` | A | 0 | 1 | **0** |
| `r = Fix(a - b)` | A | 0 | 1 | **0** |
| `r = CLng(a - b)` | C | 1 | 2 | **1** |
| `r = CInt(a - b)` | C | 1 | 2 | **1** |
| `r = Round(a - b)` | C | 1 | 2 | **2** |
| `r = Abs(a * b) - c` | B | 2^-55, `3C80000000000000` | 2^-54, `3C90000000000000` | **`3C80000000000000`** |
| `r = Sqr(a - b) - 1` | A | -2^-61, `BC20000000000000` | 0 | **0** |
| `r = (a - b) ^ 2 - 1` | A | -2^-59, `BC40000000000000` | 0 | **0** |
| `r = FMul() - c`, where `FMul = a * b` in a `Function … As Double` | B | `3C80000000000000` | `3C90000000000000` | **`3C90000000000000`** |
| `vr = a * b - vc`, `vc` a Variant holding 0.3 | B | `3C80000000000000` | `3C90000000000000` | **`3C90000000000000`**, a Double |
| `vr = va * b - c`, `va` a Variant holding 0.1 | B | — | `3C90000000000000` | `3C90000000000000`, a Double |

**What it shows:**
- **A comparison narrows each side to a double first,** in every form tried: `=`, `<`, `<>`, a literal on the left, both sides computed, a comparison stored as a Boolean, and `Select Case`.
  - The control row shows the operand itself was computed wide.
  - Per-operation doubles would have made `a - b` exactly 1 there, and `a - b - 1` zero.
- **`Int`, `Fix`, `CLng`, `CInt` and `Abs` read the wide value,** and `Abs` gives one back.
- **`Round`, `Sqr` and `^` narrow.** For `Sqr` and `^` the rows don't say whether the argument or the result is narrowed.
- **A `Function`'s result is narrowed,** as a store into its return value would be.
- **A Double operand of a Variant operator is narrowed** before the operator runs. `a * b` didn't stay wide into `- vc`.

**Overturned, and ours:** the page above said a comparison reads the wide value. It doesn't. A comparison narrows its operands to doubles, which is why its rows still came out as they did. Its `CLng` line was right, but unproven until `CLng(a - b)` above.

**Reasoned, not checked:** the five that read wide are the ones a compiler can do with a single x87 instruction (`FRNDINT`, `FISTP`, `FABS`). The ones that narrow go through a runtime call, or keep the value in memory. No disassembly was read.

**Not measured** (most since measured, in the next section):
- the other intrinsics (`Sgn`, `Exp`, `Log`, `Sin` and the rest);
- `\` and `Mod`;
- `CSng`;
- a `For` loop's bounds and step;
- an array subscript;
- `Currency` inside a `Double` expression.

### The rest: `\`, `Mod`, a `For` limit, a subscript, a Single, `Sgn`, `Log`, `Exp` and `Sin` (2026-11-21, #1214, SP6)

**The question.** The table above left these unmeasured.

**The probe** was the same harness. Two more setups:
- **Setup D:** `a = 1 + 2^-24`, set from its bytes, and `b = 2^-60`. `a + b` is 1 + 2^-24 + 2^-60 wide. Rounded straight to a Single that's `1.0000001` (`3F800001`). As a double first, it's exactly the tie 1 + 2^-24, which then rounds to `1` (`3F800000`).
- **Setup E:** `a = b = 1E-200`. `a * b` is 1E-400 wide, and 0 as a double.

| Statement | Setup | If it reads the wide value | If it narrows first | vb6.exe |
|---|---|---|---|---|
| `r = (a - b) \ 1` | C | 1 | 2 | **1** |
| `r = (a - b) Mod 3` | C | 1 | 2 | **1** |
| `For ii = 0 To a - b`, a Long counter: the iterations | C | 2 | 3 | **2** |
| `r = xs(a - b)`, where `xs(n)` holds `n * 10` | C | 10 | 20 | **10** |
| `ss = CSng(a + b)`, a Single | D | `3F800001` | `3F800000` | **`3F800001`** |
| `ss = a + b`, a Single | D | `3F800001` | `3F800000` | **`3F800001`** |
| `r = Log(a - b)` | A | -2^-60 | 0 | **0** |
| `r = Exp(a - b) - Exp(1#)` | A | about -2.4E-18 | 0 | **0** |
| `r = Sin(a - b) - Sin(1#)` | A | about -4.7E-19 | 0 | **0** |
| `r = Sgn(a * b)` | E | 1 | 0 | **0 native, 1 as p-code** |

**What it shows:**
- **Every conversion to an integer reads the wide value:** `\`, `Mod`, a `For` limit and a subscript, with `Int`, `Fix`, `CLng` and `CInt` above.
- **A Single is rounded from the wide value once, not through a double.** That's true of `CSng`, and of a store into a Single variable. So the x87 model's "narrow at a store" means narrow to the variable's own type, in one rounding.
- **`Log` narrows its argument.** If the argument had stayed wide, the result, -2^-60, would have survived as a double.
- **`Exp` and `Sin` narrow their argument or their result.** These rows can't tell which.
- **`Sgn` of a value below a double's range is the one native/p-code split here.** Native code gave 0, as if the value were narrowed first. P-code gave 1, reading it wide. HexIDE matches p-code, so its answer is 1.

**Not measured:**
- `Atn`, `Cos`, `Tan` and `Sqr` with a result that tells argument from result;
- `CCur` and `CDec`, and Currency inside a Double expression;
- a `For` loop's step, and a Double counter.
