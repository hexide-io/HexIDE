## Infinity, NaN, and division by zero (2026-08-22)

VB6 has **no Infinity and no NaN**. IEEE-754 produces them; VB6 raises instead — so a result that is not a
VB6 number is always an error, never a value.

| Probe | Result | Rule |
|---|---|---|
| `1E+308 * 10` | **Err 6** | infinite magnitude → Overflow |
| `1E+308 / 0.0001` | **Err 6** | same, through `/` |
| `1E+308 ^ 2` | **Err 6** | same, through `^` |
| `Exp(1000)` | **Err 6** | same, through an intrinsic |
| `CSng(3E+38) * CSng(10)` | **Err 6** | Single overflows at its own range |
| `(-2) ^ 0.5` | **Err 5** | NaN → Invalid procedure call |
| `0 ^ -1` | **Err 5** | infinite, but a DOMAIN error, so 5 and not 6 |
| `Sqr(-1)`, `Log(0)`, `Log(-1)` | **Err 5** | domain |
| `2 ^ 10` | `1024` Double | `^` is always Double |

`0 ^ -1` is the row worth keeping: the result is infinite, so the obvious rule says Overflow, and vb6.exe
says Invalid procedure call. No inspection of the result can get it right — the distinction is in the cause.

### Division by zero depends on whether the operands were DECLARED

| Probe | Result |
|---|---|
| `a / b` with **Variant** operands (`a = 1`, `b = 0`) | **Err 6** |
| `a / b` with **declared** `Integer` or `Double` operands | **Err 11** |
| `1 / 0` (literals) | **Err 11** |
| `a \ b` by zero, declared or Variant | **Err 11** |
| `a Mod b` by zero | **Err 11** |

Real division by zero is *Overflow* on the Variant path and *Division by zero* everywhere else — the OLE
Automation `VarDiv` quirk, which maps a zero divisor to `DISP_E_OVERFLOW`. `\` and `Mod` are Err 11 either
way. HexIDE cannot express the Variant row yet: it has no notion of which operands were declared (see
*Declared variables do not retain their declared type* below), so it raises 11 for all of them.

