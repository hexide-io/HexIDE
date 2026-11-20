## The financial functions: arguments, domains and overflow (2026-11-19, #4883)

vb6.exe SP6, `/make`, every row built as native code and as p-code, on an en-GB machine. The two agree on every row except where a value overflows, divides by zero or is not a number; there each row gives p-code first, the build HexIDE follows, and native code in brackets. Results are Doubles, given with their bits in hex.

**The probe.** One Sub Main per function, every row under `On Error Resume Next`, logging the result's bits or the error. Arguments of other types reach the function through a helper, `V(x)`, a Function taking and returning a Variant, so each is converted at run time as a Variant holding it would be. A missing Optional Variant comes from `Miss()`, a Function returning its own omitted Optional parameter (`IsMissing(Miss())` is True, `VarType` 10). Infinities and NaN are made by copying bits into a Double with `RtlMoveMemory`: VB6 code has no other way to make one. The cash-flow functions get their arrays from wrappers that copy a ParamArray into a `Double` array.

- **A probe pitfall, ours:** the harness's Main declares a local `v`, and the helper was first called `V`. Inside Main, `V(Null)` then indexed that Variant and every such row read 13. The helper was renamed and every row run again; the rows here are the second run.

### Arguments: one rule for all 13 functions

Measured at every position of Pmt, FV, PV, NPer, IPmt, PPmt, Rate, SLN, SYD and DDB, and at NPV's rate, IRR's guess and both of MIRR's rates: 54 positions, required and optional alike. Each kind was compared with the same call given the plain number. The base calls were `Pmt(0.1, 10, 1000, 0, 0)`, `FV(0.1, 10, -100, -500, 0)`, `PV(0.1, 10, -100, 500, 0)`, `NPer(0.1, -100, 1000, 0, 0)`, `IPmt(0.1, 2, 10, 1000, 0, 0)`, `PPmt(0.1, 2, 10, 1000, 0, 0)`, `Rate(10, -100, 700, 0, 0, 0.1)`, `SLN(1000, 100, 5)`, `SYD(1000, 100, 5, 2)`, `DDB(1000, 100, 5, 2, 2)`, and NPV, IRR and MIRR of `-100, 50, 60` at 0.1 (MIRR's reinvestment rate 0.12).

| Argument | Required position | Optional position |
|---|---|---|
| `V(CDate(n))` | same as the number (37) | same as the number (17) |
| `V("n")` | same as the number (37) | same as the number (17) |
| `V(" n ")` | same as the number (37) | same as the number (17) |
| `V(True)` | same as the number (37) | same as the number (17) |
| `V(Empty)` | same as the number (37) | same as the number (17) |
| `V(CCur(n))` | same as the number (37) | same as the number (17) |
| `V(CDec(n))` | same as the number (37) | same as the number (17) |
| `V(Null)` | **94** (37) | **94** (17) |
| `V("abc")` | **13** (37) | **13** (17) |
| `V("")` | **13** (37) | **13** (17) |
| `V(Array(1))` | **13** (37) | **13** (17) |
| `CVErr(5)` | **13** (37) | **13** (17) |
| a Variant holding Nothing | **91** (37) | **91** (17) |
| a Variant holding a `Collection` | **450** (37) | **450** (17) |
| `Miss()` | **13** (37) | same as the number (17) |

Counts in brackets are positions. **Every converting kind gave the same bits as the call with the number**, so the conversion is `CDbl`'s: `True` is -1, Empty 0, a Date its serial number. In an optional position (FV, Type, Due, Guess, Factor), `Miss()` acts as the argument left out: the same bits as the call without it.

Written out directly, `Pmt("abc", 10, 1000)` and `Pmt(0.1, "", 1000)` compile and raise 13 at run time.

### What vb6.exe refuses before running

Each in a project of its own, with `da` a `Double` array, `la` a `Long` array, `va` a `Variant` array, `vd` a Variant, and `VDA()` a Function returning a Variant holding a `Double()`.

| Expression | vb6.exe, native and p-code alike |
|---|---|
| `Pmt(0.1, 10)` | compile error: Argument not optional |
| `Pmt(0.1, 10, 1000, 0, 0, 0)` | compile error: Wrong number of arguments or invalid property assignment |
| `Pmt(0.1, , 1000)` | compile error: Argument not optional |
| `Pmt(0.1, 10, 1000, , )` | compile error: Syntax error |
| `IPmt(0.1, 2, 10)` | compile error: Argument not optional |
| `Rate(10, -100, 700, 0, 0, 0.1, 1)` | compile error: Wrong number of arguments or invalid property assignment |
| `NPV(0.1)` | compile error: Argument not optional |
| `NPV(0.1, da, 1)` | compile error: Wrong number of arguments or invalid property assignment |
| `NPV(, da)` | compile error: Argument not optional |
| `IRR()` | compile error: Argument not optional |
| `IRR(da, 0.1, 1)` | compile error: Wrong number of arguments or invalid property assignment |
| `MIRR(da, 0.1)` | compile error: Argument not optional |
| `SLN(1, 2)` | compile error: Argument not optional |
| `SLN(1, 2, 3, 4)` | compile error: Wrong number of arguments or invalid property assignment |
| `SYD(1, 2, 3)` | compile error: Argument not optional |
| `DDB(1, 2, 3)` | compile error: Argument not optional |
| `DDB(1, 2, 3, 4, 5, 6)` | compile error: Wrong number of arguments or invalid property assignment |
| `IRR(la)` | compile error: Type mismatch: array or user-defined type expected |
| `IRR(va)` | compile error: Type mismatch: array or user-defined type expected |
| `IRR(vd)` | compile error: Type mismatch: array or user-defined type expected |
| `NPV(0.1, la)` | compile error: Type mismatch: array or user-defined type expected |
| `NPV(0.1, vd)` | compile error: Type mismatch: array or user-defined type expected |
| `IRR(5)` | compile error: Type mismatch: array or user-defined type expected |
| `Pmt("abc", 10, 1000)` | run time: 13 |
| `Pmt(0.1, "", 1000)` | run time: 13 |
| `NPV(0.1, va)` | compile error: Type mismatch: array or user-defined type expected |
| `MIRR(da, , 0.1)` | compile error: Argument not optional |
| `IRR(da, )` | compile error: Syntax error |
| `SLN(1, , 3)` | compile error: Argument not optional |
| `IRR(VDA())` | compile error: Type mismatch: array or user-defined type expected |
| `NPV(0.1, VDA())` | compile error: Type mismatch: array or user-defined type expected |

### Arrays at run time

| Call | vb6.exe |
|---|---|
| NPV(0.1, a) with `a` an undefined `Double` array | **5** |
| IRR of an undefined `Double` array | **5** |
| MIRR(a, 0.1, 0.12) of an undefined `Double` array | **5** |
| NPV(0.1, m) with `m` `(0 To 1, 0 To 1)` holding -100, 50, 60, 10 | **5** |
| IRR(m) | **5** |
| MIRR(m, 0.1, 0.12) | **5** |
| NPV(0.1, b) with `b` `(1 To 3)` holding -100, 50, 60 | -4.507888805409472, `C012081400C4F3E8` |

### Domains and overflow

`NPVx(r, …)`, `IRRo(…)`, `IRRx(g, …)` and `MIRRx(f, r, …)` pass their trailing values as a `Double` array: `IRRx(g, …)` is `IRR(a, g)`, `IRRo` leaves the guess out, and `MIRRx(f, r, …)` is `MIRR(a, f, r)`. `NPVn(r, n, x)` is NPV of `n` elements of `x`, and `MIRRn(n)` is MIRR at 0.01 and 0.01 of -1000 followed by `n - 1` ones.

| Expression | vb6.exe, p-code [native where it differs] |
|---|---|
| `Pmt(-2, 2, 1000)` | **11** |
| `Pmt(-1, 10, 1000)` | -0, `8000000000000000` |
| `Pmt(-1.5, 2, 1000)` | -500.0, `C07F400000000000` |
| `Pmt(-1.5, 3, 1000)` | 166.66666666666666, `4064D55555555555` |
| `Pmt(-1.5, 2.5, 1000)` | **6** |
| `Pmt(0.1, -10, 1000)` | 62.745394882511526, `404F5F6919797F13` |
| `Pmt(0.001, 20000, 1000)` | -1.0000000020818547, `BFF00000008F1060` |
| `Pmt(0.1, 20000, 1000)` | **6** |
| `Pmt(10, 400, 1000)` | **6** |
| `Pmt(0.1, 10, 1E+308)` | -1.6274539488251153e+307, `FFB72CFB8FACA15C` |
| `Pmt(-0.1, 10, 1E+308)` | -5.3533993278762975e+306, `FF9E7E75219704CD` |
| `Pmt(0, 1E-300, 1E+300)` | **6** |
| `Pmt(0, 10, 1.7E+308, 1.7E+308)` | -3.4e+307, `FFC8357426C5C92B` |
| `FV(-2, 2, -100)` | -0, `8000000000000000` |
| `FV(-1, 10, -100)` | 100.0, `4059000000000000` |
| `FV(-1.5, 2.5, -100)` | **6** |
| `FV(0.1, 10, -1E+308)` | **6** |
| `FV(0, 10, -1E+308)` | **6** |
| `FV(0.1, 20000, -100)` | **6** |
| `PV(-1, 10, -100)` | **11** [native +Inf, `7FF0000000000000`] |
| `PV(-2, 2, -100)` | -0, `8000000000000000` |
| `PV(-1.5, 2.5, -100)` | **6** [native NaN, `7FF8000000000000`] |
| `PV(0.1, 10, -1E+308)` | **6** [native +Inf, `7FF0000000000000`] |
| `PV(0.1, -20000, -100)` | **11** [native -Inf, `FFF0000000000000`] |
| `NPer(0.1, -50, 1000)` | **5** |
| `NPer(0.1, 100, -1000)` | **5** |
| `NPer(-1, -100, 1000)` | **5** |
| `NPer(-2, -100, 1000)` | **5** |
| `NPer(0.1, -100, 1000, -1000)` | 0.0, `0000000000000000` |
| `NPer(0.1, -100, 0, 0)` | 0.0, `0000000000000000` |
| `NPer(1E-300, -100, 1000)` | **6** [native NaN, `FFF8000000000000`] |
| `NPer(0.1, 1E-300, 1E+300)` | -14471.155898929987, `C0CC4393F47F02E3` |
| `IPmt(-1, 2, 10, 1000)` | -0, `8000000000000000` |
| `IPmt(-2, 2, 2, 1000)` | **11** |
| `IPmt(-1.5, 2, 2.5, 1000)` | **6** |
| `IPmt(0.1, 2, 20000, 1000)` | **6** |
| `IPmt(0.1, 1.5, 10, 1000)` | -96.93746954780329, `C0583BFF804633E8` |
| `IPmt(0.1, 2, 10, 1E+308)` | -9.372546051174883e+306, `FFAAB1A33F9B2C58` |
| `PPmt(-1, 2, 10, 1000)` | 0.0, `0000000000000000` |
| `PPmt(-2, 2, 2, 1000)` | **11** |
| `PPmt(-1.5, 2, 2.5, 1000)` | **6** |
| `PPmt(0.1, 2, 20000, 1000)` | **6** |
| `PPmt(0.1, 2, 10, 1E+308)` | -6.901993437076269e+306, `FFA3A853DFBE1660` |
| `Rate(10, -100, 700, 0, 0, -1)` | **5** |
| `Rate(10, -100, 700, 0, 0, -2)` | **5** |
| `Rate(10.5, -100, 700)` | 0.07781795397872984, `3FB3EBE09F6159E2` |
| `Rate(10.5, -100, 700, 0, 0, -1.5)` | **6** |
| `Rate(20000, -1, 100)` | **6** |
| `Rate(10, -1E+308, 1E+308)` | **6** |
| `SLN(1000, 100, 0)` | **5** |
| `SLN(1E+308, -1E+308, 1)` | **6** [native +Inf, `7FF0000000000000`] |
| `SLN(1000, 100, -5)` | -180.0, `C066800000000000` |
| `SYD(1000, 100, 0, 1)` | **5** |
| `SYD(1E+308, -1E+308, 5, 1)` | **5** |
| `SYD(1000, 100, 5, 6)` | **5** |
| `DDB(1000, 100, 0, 1)` | **5** |
| `DDB(1E+308, -1E+308, 5, 1)` | **5** |
| `DDB(1000, 100, 5, 6)` | **5** |
| `DDB(1000, 100, 5, 1, 0)` | **5** |
| `NPV(-2, -100, 50, 60)` | 90.0, `4056800000000000` |
| `NPV(-1.5, -100, 50, 60)` | -80.0, `C054000000000000` |
| `NPV(1E+200, -100, 50, 60)` | **6** [native -1.0000000000000001e-198, `96D322E220A5B17F`] |
| `NPV(1E+300, -100, 50, 60)` | **6** [native -1e-298, `8210BE08D0527E1D`] |
| `NPVn(-0.999999, 60, 1)` | **11** [native +Inf, `7FF0000000000000`] |
| `NPVn(-0.9, 400, 1)` | **11** [native +Inf, `7FF0000000000000`] |
| `NPV(0.1, 1E+308, 1E+308)` | 1.7355371900826445e+308, `7FEEE4C225C7C1BE` |
| `NPV(0.1, Bits(&H7FF00000, 0), 100)` | +Inf, `7FF0000000000000` |
| `IRRo(-100, -50)` | **5** |
| `IRRo(0, 0)` | **5** |
| `IRRo(0, 100)` | **5** |
| `IRRo(-100, 0)` | **5** |
| `IRRo(-100)` | **5** |
| `IRRo(100)` | **5** |
| `IRRx(-1, -100, 110)` | **5** |
| `IRRx(-2, -100, 110)` | **5** |
| `IRRo(1, -1, 1, -1, 1, -1, 1)` | **5** |
| `IRRo(-100, 1E+308)` | **5** |
| `IRRx(1E+300, -100, 110)` | **5** |
| `IRRo(0, -100, 110)` | 0.10000000000000003, `3FB999999999999C` |
| `IRRo(-100, Bits(&H7FF00000, 0))` | **5** |
| `MIRRx(0.1, -1, -100, 50, 60)` | **5** |
| `MIRRx(0.1, -2, -100, 50, 60)` | -0.683772233983162, `BFE5E1764EDBDB78` |
| `MIRRx(-2, 0.1, -100, 50, 60)` | 0.0723805294763609, `3FB28787C6F7D7A2` |
| `MIRRx(0.1, 0.1, -100, -50)` | -1.0, `BFF0000000000000` |
| `MIRRx(0.1, 0.1, 0, 0)` | **11** |
| `MIRRx(0.1, 0.1, -100)` | **5** |
| `MIRRx(0.1, 0.1, 0, 100)` | **11** |
| `MIRRx(0.1, 0.1, -100, 0)` | -1.0, `BFF0000000000000` |
| `MIRRn(3)` | -0.9551669764570802, `BFEE90BA55C3468B` |
| `MIRRn(10001)` | 0.009767465678138161, `3F8400F70D382091` |
| `MIRRx(0.1, 0.1, -100, 1E+308)` | 1e+306, `7F76C8E5CA239029` |
| `MIRRx(1E+300, 0.1, -100, 50)` | **6** |
| `MIRRx(0.1, 0.1, -100, Bits(&HFFF80000, 0))` | **11** |

### Infinite and NaN arguments

Each position of each base call above, given +Inf (`7FF0000000000000`), -Inf (`FFF0000000000000`) or NaN (`FFF8000000000000`, the x87 default NaN).

| Function, position | +Inf | -Inf | NaN |
|---|---|---|---|
| Pmt, 1 | **6** | **6** | **6** |
| Pmt, 2 | **6** | 0.0, `0000000000000000` | **5** |
| Pmt, 3 | -Inf, `FFF0000000000000` | +Inf, `7FF0000000000000` | NaN, `FFF8000000000000` |
| Pmt, 4 | -Inf, `FFF0000000000000` | +Inf, `7FF0000000000000` | NaN, `7FF8000000000000` |
| Pmt, 5 | **6** | **6** | **6** |
| FV, 1 | **6** | **6** | **6** |
| FV, 2 | +Inf, `7FF0000000000000` | -1000.0, `C08F400000000000` | **6** |
| FV, 3 | -Inf, `FFF0000000000000` | +Inf, `7FF0000000000000` | NaN, `FFF8000000000000` |
| FV, 4 | -Inf, `FFF0000000000000` | +Inf, `7FF0000000000000` | NaN, `7FF8000000000000` |
| FV, 5 | **6** | **6** | **6** |
| PV, 1 | **6** [native NaN, `7FF8000000000000`] | **6** [native NaN, `7FF8000000000000`] | **6** [native 500.0, `407F400000000000`] |
| PV, 2 | **6** [native NaN, `7FF8000000000000`] | **11** [native -Inf, `FFF0000000000000`] | **6** [native NaN, `7FF8000000000000`] |
| PV, 3 | -Inf, `FFF0000000000000` | +Inf, `7FF0000000000000` | NaN, `7FF8000000000000` |
| PV, 4 | -Inf, `FFF0000000000000` | +Inf, `7FF0000000000000` | NaN, `7FF8000000000000` |
| PV, 5 | **6** | **6** | **6** |
| NPer, 1 | **5** | **5** | **5** |
| NPer, 2 | **6** [native NaN, `FFF8000000000000`] | **6** [native NaN, `FFF8000000000000`] | **6** [native NaN, `FFF8000000000000`] |
| NPer, 3 | **5** | -Inf, `FFF0000000000000` | **6** [native NaN, `FFF8000000000000`] |
| NPer, 4 | **5** | +Inf, `7FF0000000000000` | **5** |
| NPer, 5 | **6** | **6** | **6** |
| IPmt, 1 | **6** | **6** | **6** |
| IPmt, 2 | **5** | **5** | **5** |
| IPmt, 3 | **6** | **5** | **5** |
| IPmt, 4 | **6** | **6** | NaN, `7FF8000000000000` |
| IPmt, 5 | +Inf, `7FF0000000000000` | -Inf, `FFF0000000000000` | NaN, `7FF8000000000000` |
| IPmt, 6 | **6** | **6** | **6** |
| PPmt, 1 | **6** | **6** | **6** |
| PPmt, 2 | **5** | **5** | **5** |
| PPmt, 3 | **6** | **5** | **5** |
| PPmt, 4 | **6** | **6** | NaN, `7FF8000000000000` |
| PPmt, 5 | -Inf, `FFF0000000000000` | +Inf, `7FF0000000000000` | NaN, `7FF8000000000000` |
| PPmt, 6 | **6** | **6** | **6** |
| Rate, 1 | **6** | **5** | **5** |
| Rate, 2 | **5** | **5** | **6** |
| Rate, 3 | **5** | **5** | **6** |
| Rate, 4 | **5** | **5** | **6** |
| Rate, 5 | **6** | **6** | **6** |
| Rate, 6 | **6** | **6** | **6** |
| SLN, 1 | +Inf, `7FF0000000000000` | -Inf, `FFF0000000000000` | NaN, `FFF8000000000000` |
| SLN, 2 | -Inf, `FFF0000000000000` | +Inf, `7FF0000000000000` | NaN, `FFF8000000000000` |
| SLN, 3 | 0.0, `0000000000000000` | -0, `8000000000000000` | **5** |
| SYD, 1 | +Inf, `7FF0000000000000` | -Inf, `FFF0000000000000` | NaN, `FFF8000000000000` |
| SYD, 2 | -Inf, `FFF0000000000000` | **5** | **5** |
| SYD, 3 | **6** [native NaN, `FFF8000000000000`] | **5** | **6** [native NaN, `FFF8000000000000`] |
| SYD, 4 | **5** | **5** | **5** |
| DDB, 1 | **6** | 0.0, `0000000000000000` | **6** [native 0.0, `0000000000000000`] |
| DDB, 2 | 0.0, `0000000000000000` | **5** | **5** |
| DDB, 3 | **6** | **5** | **6** [native 900.0, `408C200000000000`] |
| DDB, 4 | **5** | **5** | **5** |
| DDB, 5 | 0.0, `0000000000000000` | **5** | **5** |
| NPV, 1 | 0.0, `0000000000000000` | 0.0, `0000000000000000` | **5** |
| IRR, 2 | **5** | **5** | **5** |
| MIRR, 2 | **11** | **11** | **5** |
| MIRR, 3 | **6** | **6** | **5** |

### What the rows suggest, and what resisted

- **The p-code errors fit the floating-point exception raised during the call, reasoned rather than measured as a rule.** A nonzero value divided by zero is 11, as in `PV(-1, 10, -100)` (a power of 0 divided into) and `Pmt(-2, 2, 1000)` (a power of 1, so `power - 1` is 0). An overflow is 6, even in a value spilled to Double partway through: `NPV(1E+200, …)` is 6 in p-code, while native code, holding the power in the x87's wider registers, returns a tiny finite value. 0/0 and a negative base raised to a fractional power are NaN, and 6.
- **Native code checks none of that** and hands back +Inf, -Inf or NaN, which is why the two builds part only in these rows.
- **NPer, Rate, IRR, SLN, SYD and DDB raise 5 of their own first** in the rows shown. `NPer(0.1, 100, -1000)` divides by zero and is 5, not 11, and `SYD(1E+308, -1E+308, 5, 1)` and `DDB(1E+308, -1E+308, 5, 1)` overflow and are 5, not 6. Where each check sits is not measured.
- **An infinite or NaN argument is often not an error at all**, in p-code too: `Pmt(0.1, 10, +Inf)` is -Inf. The rows record it. VB6 code reaches one only through an API call.
- **`MIRR` of a finance rate of -2 returns a value,** 0.0723805294763609, although `-2` makes the discounted negative flows change sign. No rule here for it.

### In HexIDE

On `upstream/main` `b79e59bcb`, each of these cases refused, citing #4883, except `Pmt`'s Null and numeric String rate, which were already measured. The work is briefed on #4883.
