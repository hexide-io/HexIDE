## In-box constants: the library qualifier selects (2026-09-03)

The full inventory of VB6's in-box constants — 728 across 77 enums and 2 constant modules in the four
libraries a Standard EXE references by default — is in
[`vb6-inbox-constants.md`](../vb6-inbox-constants.md), read out of the real type libraries by
`scripts/dump-typelib-constants.ps1`. The behavioural facts that inventory established are here.

### `VBRUN.vbCancel` is 0 and `VBA.vbCancel` is 2

Exactly one name of the 728 is declared twice with two different values, and each qualified form resolves
to its own library's:

| written | VB6 |
|---|---|
| `vbCancel` | **2** |
| `VBA.vbCancel` | **2** |
| `VBRUN.vbCancel` | **0** |
| `VBRUN.DragConstants.vbCancel` | **0** |

`vbCancel` is `2` in `VBA.VbMsgBoxResult` and `0` in `VBRUN.DragConstants`; the bare form takes VBA's, so
default reference order gives VBA precedence over VBRUN.

### The reference order, and what of it is actually measured

VB6 lists the four in Project → References as **VBA, VBRUN, VB, stdole**, and an unqualified name resolves
through them in that order, first match winning. `VBA`, `VBRUN` and `VB` are implicit, irremovable and
fixed in that sequence — they never appear as `Reference=` lines in a `.vbp`, which is corroborated by the
shipped VB6 template projects, where the only `Reference=` entries are removable ones such as stdole.
`stdole` is an ordinary listed reference: it can be removed or reordered, but not moved ahead of the fixed
three, so it is always last of the four.

**Only the VBA-before-VBRUN step is observable.** It comes from `vbCancel`, the one ambiguous name. `VB`
declares no constants at all and `stdole` shares no name with the other three, so the tail of the order
has no measurable consequence today.

Recorded as a limit rather than tidied away, because the first version of this got it wrong in a way the
oracle's own rules warn about: the order was hand-written into the generator as *VBA, VBRUN, stdole, VB*
and then asserted in a test and stated here as though it had been measured — an invented rule laundered
into a fact. No behaviour changed when it was corrected, which is exactly why nothing caught it. **Supplied
by the user from the References dialog**; the implementation now reads the order from an explicit
`referenceOrder` field in the inventory instead of inferring it from key order, so it is data that can be
checked rather than a sequence someone typed.

**This is what makes a flat name→value table wrong rather than merely incomplete**, and it is why the flat
table was replaced by `VB6InBoxLibraries`. Before that, HexIDE treated both the library and the enum
qualifier as *transparent* — looked past them and resolved the bare name — so it answered `2` for all four
rows and was wrong on two of them. A wrong value, which CLAUDE.md ranks as never acceptable, and invisible
because nothing exercised `VBRUN.vbCancel`: every corpus case covering library-qualified addressing was
module-scope, so all of them wrap in a `Sub Main` the interpreter cannot run.

The control: `vbNormal` is the only other duplicated name, and it is `0` in both `VBA.VbFileAttribute` and
`VBRUN.FormWindowStateConstants`. A *consistent* duplicate is harmless, which is what attributes the
`vbCancel` result to the disagreement rather than to duplication as such.

### Both qualifier levels are real scopes, and a mismatch is REFUSED

The other half of "the qualifier selects": a qualifier that does not declare the name is an error, not a
level to step over. All three of these are **illegal** — "Method or data member not found":

| written | why |
|---|---|
| `VBRUN.VbMsgBoxResult.vbCancel` | `VbMsgBoxResult` is VBA's enum, not VBRUN's |
| `stdole.vbCancel` | stdole declares neither |
| `DragConstants.vbYes` | `vbYes` belongs to `VbMsgBoxResult` |
| `VBA.vbKeyA` | `vbKeyA` is declared by `VBRUN.KeyCodeConstants` and by nothing else |
| `VB.vbKeyA` | VB declares nothing at all |

`VBRUN.vbKeyA` is 65, and so is a bare `vbKeyA`.

**`VBA.vbKeyA` being illegal overturned a passing interpreter test.** `MultiModuleTests
.LibraryQualifier_Constant` asserted `VBA.vbKeyA` = 65 and had always passed — because the library
qualifier was resolved transparently, so it was pinning a false acceptance rather than a fact. That is the
third test in this line of work found to encode the implementation instead of the compiler; a unit test
cannot catch a false acceptance it was written from.

### The container qualifier selects on its own

`VbMsgBoxResult.vbCancel` is 2 and `DragConstants.vbCancel` is 0 — no library needed. All 79 container
names happen to be unique across the four libraries, which is what makes the unqualified form
unambiguous, and is asserted by a test rather than assumed so a regenerated inventory cannot break it
silently.

A constant **module** qualifies its members exactly as an enum does: `Constants.vbCrLf` and
`VBA.Constants.vbCrLf` are both legal.

### The project's own declarations win

| written | VB6 |
|---|---|
| `Private Enum VbMsgBoxResult` with `vbCancel = 42`, then bare `vbCancel` | **42** |
| `Private Const vbCancel = 7`, then bare `vbCancel` | **7**, and as an **Integer** |

So the search order is: the project's Enums and Consts, then the libraries in reference order. Note the
`Const` case also changes the *type* — the user's constant is typed by its own literal, not by the library
member it shadows.

Assignment is refused as it is for a user enum member: `vbCancel = 5` is "Assignment to constant not
permitted".

### An in-box enum is a type

`Dim x As VbMsgBoxResult` compiles and runs (measured: assigning `vbCancel` to it and printing gives `2`),
and so does `Dim x As VBA.VbMsgBoxResult` — **the library may qualify a type name**. That is an asymmetry
worth noting against user enums, where `Dim p As Module2.MyEnum` is *illegal* because an Enum's identity
is project-scoped: a library is not a module.

`Dim x As Constants` is illegal — "Automation type not supported in Visual Basic". An enum name is a type
*and* a qualifier; a constant module's name is only a qualifier.

An in-box-enum-typed variable reports `TypeName` "Long" and accepts a value outside the enum (`x = 999`
gives 999), both exactly as a user enum does — so the two share one representation.

### `vbUseCompareOption` does not exist — the documentation is wrong

`vbUseCompareOption` is widely documented as a member of `VbCompareMethod` with value `-1`. The type
library declares **three** members and not that one: `vbBinaryCompare` 0, `vbTextCompare` 1,
`vbDatabaseCompare` 2. And the compiler agrees with the type library:

| written | VB6 |
|---|---|
| `VbCompareMethod.vbUseCompareOption` | **compile error** — "Method or data member not found" |
| `VbCompareMethod.vbBinaryCompare` / `vbTextCompare` / `vbDatabaseCompare` | 0 / 1 / 2 |
| `Debug.Print vbUseCompareOption` | "compiles", prints **Empty** |

That last row is a trap, and the reason the qualified probe is the decisive one: with no `Option
Explicit` the bare name is simply an undeclared Variant, so it evaluates to `Empty` and the program
builds. Read alone it looks like confirmation that the constant exists with an empty value. This is the
same harness artefact that earlier produced a confident wrong fact about `Private` enum visibility —
an unresolvable name and a resolvable one are indistinguishable without `Option Explicit`.

Consequence for the coverage inventory: refusing `vbUseCompareOption` is **faithful**, not a gap. It had
been recorded in `MISSING_LANGUAGE.md` as **Dies** on the reasoning that the name was missing from
`builtInConstants` — true, and irrelevant, because it is missing from VB6 too. (Raised by the user from a
Stack Overflow thread reporting the same discrepancy, then settled here against the compiler.)

### The stdole constants are bare-resolvable despite their generic names

stdole contributes seven members that are not `vb`-prefixed: `Default`, `Color`, `Gray`, `Checked`,
`Unchecked`, `Monochrome`, `VgaColor`. Measured, they resolve **bare** — `Default` is 0, `Color` is 4,
`Checked` is 1 — and still do under `Option Explicit`. So including them is faithful rather than reckless,
and a user declaration of the same name shadows them by the rule above. This was worth measuring before
implementing: adding `Default` and `Color` to the bare namespace on a guess would have been a plausible
way to break ordinary code.

### Where the constants actually live, which the obvious guess gets wrong

- **`VB` — "Visual Basic objects and procedures" — declares NO constants at all.** The control constants
  (`AlignConstants`, `BorderStyleConstants`, `ScaleModeConstants`, `MousePointerConstants`) are **not**
  there. All 590 of them are in **`VBRUN`**, which holds 81% of the total. I asserted the opposite before
  measuring, against a correct comment already in `ExpressionExecutor.cs`.
- **`VBRUN` is not a file.** It shares `MSVBVM60.DLL` with `VBA` and is reachable only as typelib resource
  `MSVBVM60.DLL\3` (`VBA` is index 1). A tool that enumerates library *files* finds `VBA` and silently
  misses VBRUN entirely.

### `VBA.Constants` — the string-valued constants

`vbTab` = `Chr(9)`, `vbCr` = `Chr(13)`, `vbLf` = `Chr(10)`, `vbCrLf` = `vbNewLine` = CRLF, `vbBack` =
`Chr(8)`, `vbVerticalTab` = `Chr(11)`, `vbFormFeed` = `Chr(12)`, `vbNullChar` = `Chr(0)`, `vbNullString`
= zero-length, `vbObjectError` = `-2147221504`.

Confirmed twice by independent means: read from the type library, and separately from a compiled program
reporting `Asc`/`Len` of each. The cross-check earned its keep — the first generator run passed these
through a tab-separated file and reported `vbTab` as the **empty string**, with identical row counts and
every numeric constant unaffected. A measurement that has been through a lossy transport looks exactly
like a correct one.

Seven of these — `vbTab`, `vbBack`, `vbFormFeed`, `vbNewLine`, `vbNullChar`, `vbNullString`,
`vbVerticalTab` — were absent from HexIDE altogether when this was measured; `vbTab` and `vbNullString` were
already recorded as **Dies** in `MISSING_LANGUAGE.md`. #215 resolved all eleven through the type library the
same day, but the inventory went on rating seven of them Dies until #1633 probed them on main: each is its
type library value, bare, as `VBA.x`, as `VBA.Constants.x` and in a module-level `Const`, under `Option
Explicit`. A status that a fix does not update outlives the fix.

