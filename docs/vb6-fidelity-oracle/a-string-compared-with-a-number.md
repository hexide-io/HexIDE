## A String compared with a number (2026-11-14, #211)

**The question.** #211 measured `"1" = 1` and `"1.0" = 1` as True, and #839 measured a Variant `"abc" > 1` as True where a
typed String `"abc" > 1` is 13. That leaves open:
- whether the comparison is numeric or textual, and into which type a String converts;
- what each combination of typed and Variant operands does, on either side;
- what `<` and `<>` do.

`"2" < 10` tells numeric (True) from text (`"2" < "10"` is False). `"2.5" = 2` tells Double (False) from Integer (True).

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged the same lines.
- **The read:** each row is `v = <comparison>` into a Variant, logged as its value or the error number.
- **The variables** are module-level:
  - typed Strings `sT` "2", `sA` "abc", `s1` "1.0", `sE` "", `s25` "2.5", `s35` "3.5", `s4` "40000", `sc` "1.00001", `s300` "300";
  - Variants holding Strings, `vS` "2", `vA` "abc", `v1` "1.0", `vS25` "2.5";
  - Variants holding numbers, `vN` 10 (Integer) and `vL` 10 (Long);
  - typed numbers, `nL` Long 10, `nI` Integer 10, `nD` Double 10, `nC` Currency 10, `nB` Byte 10, `nI2` Integer 2, `nL2` Long 2, `nS` Single 2, `nC1` Currency 1;
  - `bT` True, `bF` False, `dt` 1 January 2000.

**A typed String, or a literal, against a typed number:**

| Comparison | VB6 |
|---|---|
| `sT < nL`, `sT < 10`, `nL > sT`, `"2" < 10`, `10 > "5"` | True each: numeric |
| `s1 = 1`, `"1.0" = 1`, `1 = "1"`, `sT = 2`, `s1 <> 1` | True, True, True, True, False |
| `nI > sT`, `nD > sT`, `nC > sT`, `nB > sT` | True each |
| `sT <> nL` | True |
| `sA < nL`, `nL = sA`, `sA <> nL`, `"abc" > 1`, `"abc" = 1` | 13 each |
| `sE = 0` | 13 |
| `s25 = 2`, `s25 = nI2`, `s25 = nL2`, `s25 = nS`, `"2.5" = 2`, `nI2 = "2.5"`; `s25 > nI2`, `s25 = 2.5` | False each; True, True: **converted to Double** |
| `s35 = 4` | False: not rounded to an Integer |
| `s4 > nI` ("40000" against an Integer), `s300 > nB` ("300" against a Byte) | True, True: no overflow |
| `sc = nC1` ("1.00001" against Currency 1) | **True: converted to Currency** |
| `bT = "True"`, `bT = sT` (True against "2"), `bF = "0"`; `bT = sA` | True, **True**, True; 13: **converted to Boolean** |
| `dt = "1/1/2000"`; `dt > sT` | True; True |

**A Variant holding a String, against a typed number:**

| Comparison | VB6 |
|---|---|
| `vS < nL`, `vS < 10`, `v1 = 1`, `vS = 2`, `vS25 > nI2` | True each: numeric |
| `vS = nL`, `vS25 = nI2` | False, False: Double, as above |
| `vA > nL`, `vA > 1`, `vA <> nL` | **True each, no error** |
| `vA = nL` | **False, no error** |

**A Variant holding a number, against a typed String or a literal:**

| Comparison | VB6 |
|---|---|
| `vN = "10"` | True |
| `vN > "5"`, `vL > "5"`, `vN = sT` | **False each: compared as text**, "10" against "5" and "2" |

**Two Variants:** `vS < vN` is False and `vN < vS` True: a number is less than a String, even a numeric one.

**Readings:**
- **A typed String against a typed number converts the String,** and raises 13 when it can't, `""` included. It converts
  to **Double** against Byte, Integer, Long, Single and Double. Against Currency, Boolean and Date it converts to that
  type: `"1.00001"` equals Currency 1, and `"2"` equals True.
- **A Variant holding a String, against a typed number, compares numerically when the String is a number.** When it
  isn't, the String is greater, with no error: `vA > nL` is True and `vA = nL` False.
- **A Variant holding a number, against a typed String, compares as text.**
- **Two Variants put a number below a String,** whatever the String holds.
- `<>` gives the opposite of `=` in every row measured, including the 13.

So the rule depends on which operand is a Variant, not only on the values. That's why one probe of `"1" = 1` can't tell
the rules apart. This is also not `Select Case`'s rule, which converts the Case value to the selector's type (#211).

**Not measured:**
- Decimal;
- a Variant holding a String against Currency, Boolean or Date;
- Empty, Null and Error against a String;
- `Option Compare Text`.
