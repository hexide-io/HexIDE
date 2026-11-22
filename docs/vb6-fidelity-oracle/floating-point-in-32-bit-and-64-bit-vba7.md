## Floating point in 32-bit and 64-bit VBA7 (2026-11-22, #1214)

**This page is not a vb6.exe measurement.** It runs rows from the #1214 pages in Excel's VBA7, 32-bit and 64-bit, beside what vb6.exe gave there. Those pages are [Floating-point intermediates keep the x87's precision until stored](floating-point-intermediates-keep-the-x87s-precision-until-stored.md) and [Where a floating-point intermediate is narrowed](where-a-floating-point-intermediate-is-narrowed.md).

**The question.** VB6 holds a floating-point expression's intermediates on the x87, at a 64-bit significand. 64-bit Windows code normally does its floating point in SSE2, which has nothing wider than a double. So does 64-bit VBA7 round every operation to a double, as .NET does? And do the math functions follow?

**The probe:**
- **The hosts:** Excel from Microsoft 365, build 16.0.20527.
  - one 32-bit (Click-to-Run x86, `VBE7.DLL` built for x86);
  - one 64-bit;
  - each on 64-bit Windows 11 in a virtual machine.

  VBA runs p-code only.
- **The method:**
  - One standard module per run, added to a fresh workbook through the VBA object model and run with `Application.Run`.
  - Operands were module-level variables, set from their bytes with `LSet` between user-defined types.
  - Results were read back the same way, as hex.
  - Each row was its own function under `On Error Resume Next`.
- **Two runs on each host:**
  1. 32 rows taken from the pages above;
  2. the 100 arguments of the trigonometry section through the same seven functions, the six `Sqr` ties, and 8 more Variant rows.
- **.NET's side:** `Math` in .NET 11.0.0 on Windows x64, on the same bytes.

**Typed arithmetic.** The IEEE column rounds each operation in its own type, as the first page's IEEE model does. Setups A, C, D and E are those of the narrowing page: `a - b` or `a + b` with `b` = 2^-60, and `a * b` with each `1E-200`.

| Row | Statement | vb6.exe | 32-bit VBA7 | 64-bit VBA7 | IEEE, as .NET |
|---|---|---|---|---|---|
| over_mul_div | `r = a * b / c`, each `1E+200` | `6974E718D7D7625A` | the same | the same | overflow |
| under_mul_div | `r = a * b / c`, each `1E-200` | `16687E92154EF7AC` | the same | the same | `0` |
| mul_sub | `r = a * b - c`, `0.1`, `3`, `0.3` | `3C80000000000000` | the same | the same | `3C90000000000000` |
| div_mul | `r = a / b * b`, `1`, `49` | `3FF0000000000000` | the same | the same | `3FEFFFFFFFFFFFFF` |
| div_mul_eq | `If a / b * b = a` | True | True | True | False |
| sum10 | ten `0.1`s summed in one expression | `3FF0000000000000` | the same | the same | `3FEFFFFFFFFFFFFF` |
| serial_783 | `r = a + b / 86400#`, `0.25`, `86399.6` | `3FF3FFFB253E463D` | the same | the same | `3FF3FFFB253E463E` |
| sng_sum | `ss = sa + sb - sa`, Singles `16777216` and `1` | `3F800000` | the same | the same | `00000000` |
| sng_mul | `r = sa * sb`, Singles `0.1` and `3`, into a Double | `3FD3333338000000` | the same | the same | `3FD3333340000000` |
| over_variant | `vr = va * vb / vc`, Variants each `1E+200` | error 6 | error 6 | error 6 | error 6 |
| dr_add | `r = a + b`, `1` and 2^-53 + 2^-80 | `3FF0000000000000` | the same | the same | `3FF0000000000001` |
| dr_sub | `r = a - b`, `1` and 2^-54 + 2^-81 | `3FF0000000000000` | the same | the same | `3FEFFFFFFFFFFFFF` |
| dr_mul1 | `r = a * b`, `3FF854CF44A8391C` and `3FF0D6956A336D6F` | `3FF99B20D028C45C` | the same | the same | `3FF99B20D028C45D` |
| dr_mul2 | `r = a * b`, `3FFE000719E04ADF` and `3FF300B8A360CC8E` | `4001D0B150B0E544` | the same | the same | `4001D0B150B0E545` |
| control | `If a - b - 1 < 0`, setup A | True | True | True | False |
| int | `r = Int(a - b)`, setup A | `0` | `0` | `0` | `1` |
| clng | `CLng(a - b)`, setup C | 1 | 1 | 1 | 2 |
| subscript | `xs(a - b)`, setup C | 10 | 10 | 10 | 20 |
| single | `ss = a + b`, setup D | `3F800001` | the same | the same | `3F800000` |
| sgn | `Sgn(a * b)`, setup E | 0 native, 1 as p-code | 1 | 1 | 0 |
| cmp | `If a - b = 1`, setup A | True | True | True | True |

**Variant arithmetic.** The Variants held Doubles.

| Statement | Operands | vb6.exe | 32-bit and 64-bit VBA7 | x87 model | Rounded once |
|---|---|---|---|---|---|
| `vr = va + vb` | as dr_add | `3FF0000000000000` | **`3FF0000000000001`** | `3FF0000000000000` | `3FF0000000000001` |
| `vr = va - vb` | as dr_sub | not measured | **`3FEFFFFFFFFFFFFF`** | `3FF0000000000000` | `3FEFFFFFFFFFFFFF` |
| `vr = va * vb` | as dr_mul1 | `3FF99B20D028C45C` | **`3FF99B20D028C45D`** | `3FF99B20D028C45C` | `3FF99B20D028C45D` |
| `vr = va * vb` | as dr_mul2 | `4001D0B150B0E544` | **`4001D0B150B0E545`** | `4001D0B150B0E544` | `4001D0B150B0E545` |
| `vr = va * b`, `b` a Double | as dr_mul1 | not measured | `3FF99B20D028C45D` | `3FF99B20D028C45C` | `3FF99B20D028C45D` |
| `r = va * vb`, into a Double | as dr_mul1 | not measured | `3FF99B20D028C45D` | `3FF99B20D028C45C` | `3FF99B20D028C45D` |
| `vr = a * b`, Doubles into a Variant | as dr_mul1 | not measured | `3FF99B20D028C45C` | `3FF99B20D028C45C` | `3FF99B20D028C45D` |
| `vr = a * b + vz`, `vz` a Variant holding 0 | as dr_mul1 | not measured | `3FF99B20D028C45C` | `3FF99B20D028C45C` | `3FF99B20D028C45D` |

Each result was a Double. Both bitnesses gave the same on every row.

**The math functions,** on the 100 arguments of the trigonometry section, prepared as it prepared them:

| Function | 32-bit VBA7 same as vb6.exe | 64-bit VBA7 same as .NET | 64-bit VBA7 same as vb6.exe |
|---|---|---|---|
| `Sin` | 100 | 100 | 80 |
| `Cos` | 100 | 100 | 77 |
| `Tan` | 100 | **78** | 53 |
| `Atn` | **97** | 100 | 95 |
| `Exp` | 100 | 100 | 100 |
| `Log` | 100 | 100 | 100 |
| `Sqr` | 100 | 100 | 100 |

- **64-bit `Tan` is its own `Sin` divided by its own `Cos`.** Each was rounded to a double and divided as doubles. That held on all 100. .NET's `Math.Tan` gives the same on 78. On the other 22 it differs by 1 ulp.
- **32-bit `Atn` differs from vb6.exe on 3 arguments, by 1 ulp each:** `0.6499247187292126`, `0.49263435305708536` and `15.707963267948966` (5π).
  - Rounding the exact arctangent once or twice gives vb6.exe's value on all three.
  - So the three match neither model. No rule found.

**`Sqr` and `^ 0.5` at the six ties:**

| `a` | vb6.exe, and 32-bit VBA7, `Sqr` and `^` | 64-bit `Sqr` | 64-bit `a ^ 0.5` | .NET `Math.Pow(a, 0.5)` |
|---|---|---|---|---|
| `400CE9014CDAA2A1` | `3FFE6A76FB2263BA` | `3FFE6A76FB2263B9` | `3FFE6A76FB2263BA` | `3FFE6A76FB2263BA` |
| `3FF975DE35D20EBC` | `3FF42EEE9E585BEA` | `3FF42EEE9E585BE9` | `3FF42EEE9E585BE9` | `3FF42EEE9E585BEA` |
| `400BB28DDE283846` | `3FFDC567C6235F26` | `3FFDC567C6235F25` | `3FFDC567C6235F25` | `3FFDC567C6235F26` |
| `4006C04435758C94` | `3FFAFB6A66B66F4E` | `3FFAFB6A66B66F4F` | `3FFAFB6A66B66F4E` | `3FFAFB6A66B66F4F` |
| `400C93FD5B9232FE` | `3FFE3D9D2F9E28CA` | `3FFE3D9D2F9E28CB` | `3FFE3D9D2F9E28CB` | `3FFE3D9D2F9E28CA` |
| `3FFCBB64481FA6DE` | `3FF570DE80631134` | `3FF570DE80631135` | `3FF570DE80631135` | `3FF570DE80631135` |

64-bit `Sqr` is .NET's, rounded once. 64-bit `^ 0.5` agrees with rounding once on 4 rows and with rounding twice on 2. It agrees with .NET's `Math.Pow` on 2. No rule found.

**What it shows:**
- **64-bit VBA7 keeps the x87's arithmetic.** Every typed row is the same in vb6.exe and in both bitnesses of VBA7, bit for bit:
  - the x87's range;
  - a 64-bit significand, with a single operation rounded twice;
  - a comparison narrowing both sides;
  - the integer conversions reading the wide value;
  - a Single rounded from the wide value once.
- **Variant arithmetic differs from vb6.exe in both bitnesses.** VBA7 rounds a Variant operation once, to a double, as .NET does, where vb6.exe rounds it twice. Typed operands are computed wide and narrowed before the Variant operator, as in vb6.exe.
- **The math functions follow the bitness:**
  - 32-bit VBA7's are vb6.exe's, apart from those three `Atn` arguments.
  - 64-bit VBA7's `Sin`, `Cos`, `Atn`, `Exp`, `Log` and `Sqr` give what .NET's `Math` gives on Windows x64. Its `Tan` is `Sin` over `Cos`.
- **`Exp` and `Log` were the same everywhere** on these arguments.
- **`Sgn` below a double's range is 1 in both,** as in vb6.exe's p-code.

**Overturned:** the obvious guess was ours, and it was on #1214 and the Team Desk. It said 64-bit VBA uses SSE2 and so rounds each operation to a double, which would have made the wide real a per-target choice. It doesn't: its typed arithmetic is the x87's. Only its math functions moved. They give .NET's results on Windows x64, apart from `Tan` and `^`.

**Reasoned, not checked:**
- 64-bit VBA7's p-code engine still does arithmetic with x87 instructions, which 64-bit Windows keeps available.
- Its math functions call a library.

No disassembly was read.

**For HexIDE,** reasoned from these rows:
- **The wide real that #1214 settled on serves all three targets** for typed arithmetic.
- **What differs by target:**
  - Variant operators round once in VBA7, and twice in vb6.exe.
  - VB6 and 32-bit VBA7 take the x87's math functions (#5259).
  - 64-bit VBA7 takes the rounded-once results these rows match, with `Tan` as `Sin` over `Cos`.
- **.NET's `Math` on Linux and macOS calls each platform's own library.** That wasn't compared, so "what .NET gives" here means on Windows x64 only.

**Not measured:**
- Excel for Mac, on Apple silicon, which has no x87;
- Word, Access and other hosts, which load the same `VBE7.DLL` (reasoned, not checked);
- `Currency` and `Decimal` in these expressions;
- 64-bit `^` with other exponents.
