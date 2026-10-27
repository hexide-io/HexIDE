## Operator precedence (2026-10-15, #1074)

**How it was found.** The Battleship demo (#1071) gives a 5 x 5 board `gSize * gSize \ 2` shots, which VB6 makes 12.
HexIDE made it 10, from `5 * (5 \ 2)`.

**The probe.** [The scripted harness](README.md#the-scripted-harness--scriptsvb6-oracleps1), VB6.EXE 6.00.8176 (the original release) in the
oracle guest. Each expression was assigned to a Variant and logged with its `TypeName`. There were three `/make` runs:
- 22 expressions;
- 21 more, one per pair of adjacent levels the first run left out;
- 10 comparing a Boolean with a number.

**Within a level, left to right:**

| Expression | VB6 | HexIDE before #1074 |
|---|---|---|
| `10 - 2 + 3` | 11, Integer | **5** |
| `1 - -1 + 1` | 3, Integer | **1** |
| `8 - 3 - 2` | 3, Integer | 3 |
| `6 / 2 * 3` | 9, Double | 9 |
| `2 * 3 / 4` | 1.5, Double | 1.5 |
| `100 / 10 / 2` | 5, Double | 5 |
| `12 / 2 / 3` | 2, Double | |
| `20 \ 3 \ 2` | 3, Integer | 3 |
| `9 Mod 5 Mod 3` | 1, Integer | 1 |
| `2 ^ 3 ^ 2` | 64, Double | 64 |
| `1 < 2 = True` | True | **False** |
| `3 = 3 < 4` | True | **False** |
| `3 > 2 > 1` | False | |
| `Nothing Is Nothing = True` | True | |
| `False Imp False Imp False` | False | |

**Each pair of adjacent levels:**

| Pair | Expression | VB6 | HexIDE before #1074 |
|---|---|---|---|
| `^`, negation | `-(2) ^ 2` | -4, Double | |
| `^`, `*` | `2 * 3 ^ 2`, `2 ^ 3 * 2` | 18, 16, Double | |
| `*`, `\` | `5 * 5 \ 2` | 12, Integer | **10** |
| `*`, `\` | `10 \ 2 * 3` | 1, Integer | **15** |
| `*`, `\` | `2 * 6 \ 4 * 2` | 1, Integer | **4** |
| `/`, `\` | `7 / 2 \ 1`, `6 \ 4 / 2` | 4, 3, Long | |
| `\`, `Mod` | `7 Mod 4 \ 2`, `8 \ 3 Mod 2` | 1, 0, Integer | 1 |
| `*`, `Mod` | `7 Mod 3 * 2`, `2 * 3 Mod 4` | 1, 2, Integer | 2 |
| `Mod`, `+ -` | `2 + 3 Mod 2`, `5 - 7 Mod 4`, `9 Mod 4 + 1` | 3, 2, 2, Integer | 3 |
| `+ -`, `&` | `1 + 2 & 3`, `5 - 3 & 1`, `"a" & 1 + 2` | `"33"`, `"21"`, `"a3"` | `"33"`, `"21"` |
| `&`, comparisons | `"a" & "b" = "ab"` | True | |
| comparisons, `And` | `1 + 2 = 3 And 4` | 4, Integer | |
| comparisons, `Not` | `Not 1 = 2`, `Not 0 = 1` | True, True | True |
| `Not`, `And` | `Not False And False` | False | **True** |
| `And`, `Or` | `True Or True And False` | True | |
| `Or`, `Xor` | `True Xor True Or True` | False | |
| `Eqv`, `Imp` | `False Imp False Eqv False` | True | |

A blank HexIDE cell is a row measured after the fix.

Two pairs give the same answer whichever way they group, so these rows can't order them:
- **negation and `*`,** since `-(a * b)` is `(-a) * b`;
- **`Xor` and `Eqv`,** since `Eqv` is `Not Xor`, and `a Xor b Eqv c` is `a Xor (b Eqv c)` for any operands.

`1 = 1 Is Nothing` doesn't compile either way, with "Type mismatch".

**A Boolean beside a number** compares as that number's type:

| Expression | VB6 |
|---|---|
| `True = -1`, `True < 0`, `False = 0`, `True > -2`, `True <> 0` | True |
| `True = 1` | False |
| `True = -1#`, `CDbl(-1) >= True` | True |
| `False < CLng(1)` | True |
| `CByte(1) > True` | **False** |

True is -1, and beside a Byte it is 255, its bits, as `CByte` converts it. HexIDE refused every one of these with
Type mismatch, which a chained comparison such as `3 = 3 < 4` reaches.

**What VB6 does,** from all of these:
- **The ladder:** `^`, then negation, then `*` and `/` together, then `\`, then `Mod`, then `+` and `-` together, then
  `&`, then every comparison together, `Like` and `Is` included. Then `Not`, `And`, `Or`, `Xor`, `Eqv` and `Imp`.
- **Operators of one level are taken left to right,** `^` and `Imp` included: `2 ^ 3 ^ 2` is 64, not 512.
- **`\` is below `*` and `/`:** `5 * 5 \ 2` is `25 \ 2`. The obvious guess, from languages where integer division sits
  with `*`, is overturned.

**Why HexIDE went wrong.** ANTLR ranks the alternatives of a left-recursive rule in the order they are written. The
interpreter's grammar gave each operator an alternative of its own, so each was ranked above the next: `/` and `\`
(one token) above `*`, `+` above `-`, and `=` above `<`. A row whose answer didn't depend on the order came out right,
which is why the rest lasted.

`Not` went wrong a different way. Its alternative wrapped the operand in a sub-rule,
`NOT (WS valueStmt | LPAREN valueStmt RPAREN)`, and ANTLR treats an alternative as a prefix operator only when its
operand ends it. So `Not` had no level at all, and its operand was a whole expression: `Not False And False` was
`Not (False And False)`, True. That is the shape of `If Not found And retry Then`.

The LSP server's grammar (`VisualBasic6Parser.g4`) already grouped them as VB6 does, in `vsMultDiv`, `vsIDiv`,
`vsAddSub` and one `vsComp`. The interpreter's now does the same.

**In HexIDE (#1074):**
- **The grammar:** the operators of a level share one alternative, and `\` has a token of its own. `-2 ^ 2` was still
  4 after #1074, because the lexer read the sign as part of the number (fixed by #1073, below).
- **The comparisons** read a Boolean beside a number as above.
- **The evaluators that share the change:** the Immediate window, watches and the editor's data tips all evaluate
  through `BasicInterpreter.ParseValueStmt`, so they take the same tree. Two places fold an expression without the
  executor, and both were changed with it: the pre-pass's Enum folder (`PrePass.TryFoldToDouble`) and the bounds of a
  module-level array (`ModuleLevelBounds`).


### A minus sign before a literal is negation (#1073)

Provenance: the measurement comment on #1073, carried from PR #2279 (e0f825d3),
confirmed by the supervisor on 2026-10-27. vb6.exe SP6 produced the same 56 log lines
in native and p-code builds except for the Integer-minimum negation below.

| Probe | Value and type |
|---|---|
| `-2 ^ 2`, `- 2 ^ 2`, `-(2) ^ 2`, `Const C = -2 ^ 2` | Double -4 |
| `(-2) ^ 2` | Double 4 |
| `-x ^ 2`, Integer x = 5 | Double -25 |
| `32767`, `-32767` | Integer 32767, -32767 |
| `-32768`, `Const C = -32768` | Long -32768 |
| `-2147483647`, `-2147483648` | Long -2147483647, Double -2147483648 |
| `-1.5`, `-1.5!`, `-1@`, `-1&`, `-1%` | Double -1.5, Single -1.5, Currency -1, Long -1, Integer -1 |
| `Const C As Integer = -32768`, assignment of `-32768` to Integer | Integer -32768 |
| `x-1`, `x - -1`, `x*-1` with Integer x = 5 | Integer 4, 6, -5 |
| `3 - -2`, `- -2` | Integer 5, 2 |
| `-&H7FFF`, `-&HFFFF`, `-&O10`, `-&H8000&` | Integer -32767, Integer 1, Integer -8, Long -32768 |
| `Const C = -&H8000`, `Const C As Integer = -32769` | compile error: Overflow |
| `v = -&H8000`, `-h` with Integer h = &H8000 | p-code: error 6; native: Integer -32768 without error |

The digits determine a literal's type before negation. In particular, the tempting
Integer interpretation of `-32768` is wrong: 32768 is a Long. HexIDE follows p-code
for the overflow split. Removing leading signs from the literal tokens fixes the
power grouping and allows subtraction without spaces; exponent signs remain within
the token.
