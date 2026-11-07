## Enums (2026-09-03)

Thirty-odd probes. Enums are everywhere in VB6's object model, and almost nothing about them was written
down.

### A member's value is a constant EXPRESSION, not a literal

| probe | value |
|---|---|
| `&HFF` | 255 |
| **`&H80000005`** | **−2147483643** — the high bit makes it a negative Long |
| the member after it, with no value | −2147483642 |
| `&O17` | 15 |
| `-3`, then the member after it | −3, then −2 |
| `0` explicitly, then the member after it | 0, then 1 |
| `xFirst + 1` where `xFirst = 5` | 6 |
| `KBase` where `Public Const KBase As Long = 100` | 100 |
| `2 ^ 3` | 8 |
| first member with no value | 0 |

So hex, octal, negatives, references to earlier members, references to a `Const`, and arbitrary arithmetic
are all ordinary. Every member is a **`Long`** — `TypeName` says `Long` and `VarType` says 3, never the
enum's own name.

**Issue #176 understates its own bug.** It says members "must be decimal literals"; that is the symptom.
The rule is that a member takes a constant expression, and a wider literal parser would not have been the
fix.

### Evaluation is a single forward walk — measured, not assumed

| probe | verdict |
|---|---|
| a member referencing a **later** member | **illegal** — *Constant expression required* |
| a member referencing a **later** `Const` | **illegal** — same |
| a member referencing an **earlier** enum, qualified | legal |
| a member referencing an earlier member of its **own** enum, qualified | legal |

This matters more than it looks. CLAUDE.md prescribes lazy-memoised evaluation for `Const` on the premise
that `Const` is order-independent, and the obvious move was to reach for the same pattern here. VB6 says
no: source order **is** the rule, so a plain forward walk is not a shortcut, it is the specification. **The premise
is wrong for `Const` too, within a module** (measured later, #873): `Const A = B + 1` above `Const B = 2` is refused
at every scope probed, as "Constant expression required", except in a procedure under `Option Explicit`, where it is
"Variable not defined". Only across modules is the order free, and there VB6 refuses a
cycle (the section "Subscripts, bounds, and a method called from Class_Initialize"). The
pre-pass stays pure collection and a forward reference simply is not found — the right answer for the right
reason.

### `/` is real division, then rounding half to EVEN

The one that bites, and the one that caught this session's own implementation.

| probe | value |
|---|---|
| `10 / 5` | 2 |
| `7 / 2` | **4** |
| `5 / 2` | **2** |
| `-7 / 2` | **−4** |
| `10 / 3` | 3 |
| `10 \ 4` | 2 |
| `-7 \ 2` | −3 |

A member is a Long, so the expression is evaluated and then **coerced**, and VB6 coerces by rounding half
to even. Folding in integers instead gives 3 for `7 / 2` and −3 for `-7 / 2` — both plausible, both wrong,
and neither announces itself. HexIDE shipped exactly that for two hours; it was caught because one lexer
token covers `/` and `\` and the runtime tells them apart by the token's text, which the constant folder
did not.

### An Enum and its member may share a name (2026-10-24, #1637)

Provenance: #1637's comment "Measured: the rows this issue owed", vb6.exe SP6
(VB6.EXE 6.00.9782), `/make`, en-GB guest; native and p-code agreed. Each compile
error used a separate project. Unless stated otherwise, `Enum E` contains `E` and `F = 5`.

| Probe | Measured result |
|---|---|
| `E.E`, `E.F` | Long 0, Long 5 |
| bare `E` in an expression | Compile error: "Expected variable or procedure, not enum type" |
| `Dim x As E`, read before storing | Long 0 |
| `x As E`: store `E.E`, then `E.F + 1` | Long 0, then Long 6 |
| `x As E`: `x = E` | "Expected variable or procedure, not enum type" |
| `Twice(E.F)`, with `Function Twice(ByVal v As E) As Long` doubling v | 10 |
| Class with `Private Enum E`: read `E.E`, `E.F`, then x As E given `E.F` | 0, 5, 5 |
| Same class, bare `E` | "Expected variable or procedure, not enum type" |
| Module2's Public Enum, read from Module1: `E.E`, `Module2.E.E`, x As E given `E.F` | Long 0, Long 0, Long 5 |
| Same project: bare `E` or `Module2.E` as a value | "Expected variable or procedure, not enum type" |
| Control: Enum G contains `E = 7`; bare `E`, `G.E` | Long 7, Long 7 |
| `E.E = 1` in an uncalled Sub | "Assignment to constant not permitted" |
| Module2 exports Enum E with E = 3; Module3 exports Enum G with E = 4; Module1 reads bare E | "Ambiguous name detected: E" |

The issue body's earlier probe also measured `E.E` as 0 with VB6.EXE 6.00.8176
and MSVBVM60 6.00.9848, native and p-code. **The SP6 comment overturned the issue's
own guess that bare E would select the member:** the Enum wins that shared name;
qualification reaches its member. A differently named Enum's member remains readable bare.

`EnumOwnNameTests` pins these rows. HexIDE looks up names as the walk reaches them
and raises `VBCompileErrorException` with the compile diagnostic's text. This existing
exception has no error number; these are compile diagnostics, not numbered runtime errors.
VB6 rejects the constant assignment even if its Sub is never called; HexIDE only sees
executed statements, so the test calls that Sub (the CST boundary's second-order consequence).

### Addressing: `[[Lib.][Enum.]]Value`, both qualifiers independently optional

| probe | verdict |
|---|---|
| `vbAlignBottom` | legal |
| `AlignConstants.vbAlignBottom` | legal |
| `VBRUN.AlignConstants.vbAlignBottom` | legal |
| **`VBRUN.vbAlignBottom`** — skipping the enum level | **legal** |
| `VBA.VbMsgBoxStyle.vbOKOnly` | legal — holds for the VBA library too |
| `Module1.pTwo` — a module qualifying a bare member | legal |
| `Module1.EPlain.pTwo` — module, enum, member | legal |
| `EPlain.EPlain.pTwo` | **illegal** — *Method or data member not found* |
| `VBRUN.EPlain.pTwo` — a user enum via a library qualifier | **illegal** — same |

All the legal forms resolve to the same value. The first slot takes a **module or a type library**, and the
middle slot is optional in both cases — so the notation is really `[[Lib.][Enum.]]Value`. It is not a
free-form namespace: a fourth level, and a user enum reached through a library, are both refused.

### A member is a CONSTANT, and an enum-typed variable is an open Long

| probe | verdict |
|---|---|
| `pTwo = 5` | **illegal** — *Assignment to constant not permitted* |
| `Dim x As EPlain` then `TypeName(x)` | `Long` — the enum name is not retained |
| `Dim x As EPlain` then `x = 999` | legal, prints 999 |
| `TypeName(EPlain)` | **illegal** — *Expected variable or procedure, not enum type* |
| an `Enum` declared inside a procedure | **illegal** — *Invalid inside procedure* |

So enums are named Longs, not closed sets: a value no member declares is accepted without complaint.
HexIDE was hoisting members as ordinary variables, so `pTwo = 5` **succeeded** — a program could overwrite
`vbRed` and nothing anywhere would say so. That row belonged in the *silently wrong* tier that
`MISSING_LANGUAGE.md` reports as "0 names", and which the file itself flags as a floor rather than a
ceiling.

### Two enums may share a member name — until you use it

| probe | verdict |
|---|---|
| two enums both declaring `shared_` | legal to DECLARE |
| `EOne.shared_` / `ETwo.shared_` | 11 / 22 |
| bare `shared_` | **illegal** — *Ambiguous name detected* |

The declaration is fine and the *use* is the error, which is a shape worth remembering: the collision is
diagnosed where it is unresolvable, not where it is created. HexIDE silently returns the last one loaded.

### Which keywords may name a label — measured word by word, because it is not derivable

Thirty-three probes, prompted by a line-label defect and by a maintainer's recollection about `New`.

**Usable as a name:** `Beep` `Reset` `Error` `Name` `Width` `Load` `Kill` `Time` `Mid`
**Reserved:** `End` `Close` `Randomize` `Resume` `Loop` `Next` `Wend` `Case` `Print` `Get` `Put` `Write`
`Input` `Open` `Seek` `Lock` `Unlock` `Set` `Let` `Call` `Date` `Stop` `Return` `Else` `New` `Nothing`

`Reset:` is a label and `Randomize:` is the Randomize statement. `Beep:` is a label and `Stop:` is a syntax
error. Each pair is a keyword whose statement form is complete on its own, so **no structural property
separates them** — which is why the grammar now carries a list and not a rule.

`New` and `Nothing` came from a recollection that `New` is illegal as a `Sub` name, recorded as a
hypothesis and then measured: both refused with *Expected: identifier*. Worth pinning rather than filing as
incidental, because the illegality is load-bearing for anyone extending the language — a dialect that wants
`New` to name a constructor can only do so cleanly because VB6 leaves the name unavailable.

