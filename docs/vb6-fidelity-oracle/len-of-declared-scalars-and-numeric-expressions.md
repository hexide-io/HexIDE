## Len of declared scalars and numeric expressions (#780)

Provenance: issue #780's body records module-level probes on vb6.exe; its later comment records a repeat during
#987 with native and p-code agreeing. These are supplied measurements, not new runs for this fix.

| Operand | Len |
|---|---|
| Byte variable holding 5 | 1 |
| Integer variable holding 5 | 2 |
| Long or Single variable holding 5 | 4 |
| Double or Currency variable holding 5 | 8 |
| Date variable holding #1/1/2000# | 8 |
| Boolean variable holding True | 2 |
| String variable holding "12345" | 5 |
| Empty Variant | 0 |
| Variant holding Integer 5; holding 12345 | 1; 5 |

The body's zero-initialised Single, Double, Currency, Integer and Date variables returned 4, 8, 8, 2 and 8.
`Len(12345)`, `Len(i + 0)`, `Len(1 / 3)` and `Len(CSng(1) / 3)` fail compilation with
"Variable required - can't assign to this expression". HexIDE raises that compile error when the call executes.
The comment also measured `LenB(i)` for Integer as 2 and `LenB(s)` for String "12345" as 10; LenB remains unimplemented.
No inference about fields, array elements, qualified or parenthesised variables is made from these probes.

### Procedure declarations reading a later Const (#894)

Provenance: hexide-io/HexIDE#894 records two independent vb6.exe SP6 `/make` measurements with identical
results. In `Sub Main`, `Const A = B + 1` before `Const B = 2` fails at A: "Variable not defined" with
Option Explicit, "Constant expression required" without it. `Dim a(N) As Long` before `Const N = 3`
fails at the Dim with the same respective messages.

With module `Const B = 10`, Option Explicit and procedure `Const A = B + 1` before `Const B = 2`,
VB6 instead reports "Duplicate declaration in current scope" at the local B. A local `Const B = 2`
with no earlier use builds and reads 2. These are compile refusals; HexIDE checks the expressions at
procedure entry. Other procedure expression rules are unchanged by #894.

Unmeasured combinations left unchanged: a later local shadowing a module Const without Option Explicit,
and a Dim bound reading a module Const before the local shadow. Qualified reads and other procedure
constant-expression restrictions are outside these measurements.

