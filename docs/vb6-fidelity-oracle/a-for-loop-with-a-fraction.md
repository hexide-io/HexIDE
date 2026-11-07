## A For loop with a fraction (2026-10-24, #983)

**The question.** HexIDE stopped the run at any For whose start, limit or step was still fractional for its counter:
- a Variant or undeclared counter, as in `For x = 0 To 1 Step 0.25`, the common shape;
- a Single, Double, Currency or Date counter.

What does each counter do, and in what type does it count?

**The probe.** 80 cases in four Sub Main projects, one a batch, on vb6.exe SP6 under en-GB, each built native and p-code.
- **The shape.** Each case is a Sub that runs one For under `On Error Resume Next`. Each pass logs the counter's
  `TypeName` and `CStr`. After the loop it logs the counter, the number of passes and `Err`. A guard leaves the loop
  after 15 passes, so a loop that would run for ever shows as 15.
- **The fourth batch** runs a For twice in one call, the second time with its limit raising, to see a fractional counter
  step after a header that raised.
- **Native and p-code agreed on every row except the ones marked.** HexIDE follows p-code, the build the VB6 IDE runs.

**A Double, Currency or Date counter adds the step each time round, in its own type:**

| Counter, header | Passes | After |
|---|---|---|
| Double, `For x = 0 To 1 Step 0.1` | 0, 0.1, … 0.9, 1: **11** | 1.1 |
| Double, `For x = 1 To 2 Step 0.1` | 1, 1.1, … 1.9: **10** | 2, just over: the tenths accumulate |
| Double, `For x = 1 To 0 Step -0.25` | 1, 0.75, 0.5, 0.25, 0 | -0.25 |
| Double, `For x = 0.5 To 3`, `For x = 1 To 2.5` | 0.5, 1.5, 2.5; 1, 2 | 3.5; 3 |
| Double, `For x = 1.5 To 1` | none | 1.5 |
| Double, `For x = 0.5 To 1 Step 0` | 0.5 for ever | |
| Double, `For x = 1E+15 To 1E+15 + 2 Step 0.5` | 1E+15 for ever, as CStr shows it | |
| Currency, `For x = 0 To 1 Step 0.1` | 0, 0.1, … 1: 11, exactly | 1.1 |
| Currency, `For x = 0 To 1 Step 1 / 3` | 0, 0.3333, 0.6666, 0.9999 | 1.3332: the step is a Currency first |
| Currency, `For x = 0 To 0.0003 Step 0.00005` | 0, 0.0001, 0.0002, 0.0003 | 0.0004: the step became 0.0001, by its binary value (#966) |
| Date, `For x = #1/1/2000# To #1/2/2000# Step 0.25` | every 6 hours from 01/01/2000 to 02/01/2000 | 02/01/2000 06:00:00 |
| Date, `For x = 1 To 2 Step 0.5` | 31/12/1899, then 12:00 on it, then 01/01/1900 | 01/01/1900 12:00:00 |

An Integer, Long or Byte counter rounds a fractional bound into its type as it is converted (#839): `Step 1.5` is 2, and
a Long's `Step 0.5` is 0, so `For x = 1 To 4 Step 0.5` runs for ever at 1.

**A Single counter: the one place native code and p-code part.** Both convert the step and the limit to Singles. Native
code adds in Single, so each sum is rounded before it is tested. P-code adds in Double and tests the sum against the
limit before rounding it into the counter. Where the sum lands just over the limit, p-code stops a pass early:

| Single counter, header | Native | P-code (and HexIDE) |
|---|---|---|
| `For x = 0 To 1 Step 0.1` | 10 passes, the ninth and tenth printing 0.8000001 and 0.9000001 | the same |
| `For x = 0.1 To 0.5 Step 0.1` | 5 passes, after 0.6 | **4 passes**, after 0.5 |
| `For x = 0 To 0.9 Step 0.15` | 7 passes, after 1.05 | **6 passes**, after 0.9 |
| `For x = 0.1 To 0.9 Step 0.2` | 5 passes, after 1.1 | **4 passes**, after 0.9 |
| `For x = 0 To 0.3 Step 0.1`, `0 To 0.7 Step 0.7`, `0.1 To 0.7 Step 0.3` | 4, 2, 2 passes | the same |

The last row is what pins the model: a Double limit or a Double step would each give a different count there.

**A Variant counter takes its type from the start, the limit and the step together, from the first pass,** by the
result table of `+` ("`+` and `-` — identical tables" above):

| Variant counter, header | Type |
|---|---|
| `0 To 1 Step 0.1`, `1 To 2.5`, `0.5 To 3`, `1# To 3`, `"1" To "2" Step "0.5"` | Double |
| `0& To 1 Step 0.25!`, `0 To 40000 Step 10000.5!` | Double: a Long beside a Single |
| `0 To 1 Step 0.25!`, `0! To 0.3! Step 0.1!` | Single, which steps as a Single counter does |
| `0@ To 1 Step 0.25`, `0 To 1@ Step 0.25`, `1@ To 3@` | Currency |
| `0 To 1 Step CDec(0.1)`, `0 To CDec(2)`, `CDec(0.5) To 2 Step 1` | Decimal, exact: 11 passes to 1, after 1.1 |
| `#1/1/2000# To #1/2/2000# Step 0.5`, `36526 To #1/3/2000#`, `… Step 0.5@` | Date |
| `1 To 3`, `True To 1`, `CInt(1) To CInt(3) Step CInt(1)` | Integer |
| `49998 To 50000`, `1 To 50000 Step 24999`, `1& To 3&` | Long, from the first pass, 1 included |

- **A Variant counter widens as Variant arithmetic does** when a step takes it past its type: `32766 To 32767` ends at
  the Long 32768.
- **With no Step clause, p-code takes the type from the start and the limit alone.** `CByte(1) To CByte(3)` counts in
  Bytes, and `CByte(254) To CByte(255)` ends at the Integer 256. Native code counts both in Integers, as though the
  implied step were an Integer 1. With a Byte step, `Step CByte(2)`, both count in Bytes.

**What resisted explanation, and is not followed.** A Variant counter whose start is a Decimal and whose For has no
Step clause counts in Dates in p-code:
- `For x = CDec(0) To 2` gives 00:00:00, 31/12/1899, 01/01/1900, and after 02/01/1900.
- `For x = CDec(0.5) To 2` gives 12:00:00 and then the same days at noon.

Native code counts both in Decimals. So does p-code once there is a Step clause, or when only the limit is a Decimal. No
reading of the type table produces a Date there, and nothing else in VB6 turns a Decimal into a Date. HexIDE counts in
Decimals, as VB6 plainly meant: the Fidelity Principle, not the p-code rule.

**After a header that raised** (the fourth batch), a fractional counter steps exactly as above, from the counter as the
body left it, by the step the statement last had, against the limit it last had (#839's model). One example is a Double
`For x = 0.1 To Lim() Step 0.25`, the second time round with its limit raising: 0.1, 0.35, 0.6, 0.85, then 1.1. A Single
counter there splits native from p-code as in the table above.

### In HexIDE (#983)

- **A Single, Double, Currency or Date counter, and a Variant one, steps in its loop's type.**
  - Each pass adds the step with Variant arithmetic, `VbNumeric.Add`, and compares with `VbNumeric.CompareNumbers`.
  - A Single sums in Double and is tested before it is rounded.
  - A Variant counter's type comes from the start, the limit and the step through the `+` table, a Date beside any number
    being a Date. A Date beside a Decimal wasn't measured, and is refused.
- **An Integer, Long or Byte counter** keeps its whole-number loop. A Boolean or String counter still takes whole
  numbers only, as before: nothing was measured for one.
- **`FractionalForTests`** runs each case program as it ran on vb6.exe, and must print what its p-code build logged, line
  for line. The two Decimal cases pin native code's Decimals.
- **Waiting on #966:** a Currency counter whose step is a Double tie, `Step 0.00005`, is 0 here until a Double becomes a
  Currency by its binary value. Until then that loop runs for ever at its start where vb6.exe counts in steps of 0.0001.
  It isn't pinned yet.

