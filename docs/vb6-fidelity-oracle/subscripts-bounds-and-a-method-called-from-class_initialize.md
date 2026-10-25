## Subscripts, bounds, and a method called from Class_Initialize (2026-10-12, #760, #849, #850)

**The questions.**
- What VB6 does with a subscript or an array bound that is not an Integer. HexIDE accepted only an Integer, a
  Byte or an integral string, and stopped the run on anything else with an untrappable "Type mismatch", so `a(i)`
  with `i` a Long, and `a(UBound(a))`, both died.
- Whether a call from `Class_Initialize` to one of the object's own methods can terminate it. In HexIDE it did.

**Probe.** A Standard EXE with a form, a module and two classes, built with `vb6.exe /make` on the Win10 guest
(session 0) and run, logging each value with `TypeName` and `Err.Number` under `On Error Resume Next`. `a` is
`Dim a(5) As Integer` holding `a(j) = j * 10`.

Each probe was built as native code, the harness's default, and also run in the IDE. The IDE runs used
`VB6.EXE /runexit probe.vbp`, which runs a project as F5 does and closes VB when the program ends. It was started in
the guest's session 0 over PowerShell Direct, and the program logged with `Print #` to a file beside the project,
because `Debug.Print` output is gone once VB closes. #842 tracks making both harnesses part of the tree. Every line
read in both modes was identical in the two, except where a row says otherwise. A compile error can only be read
from `/make`, so those rows are native only.

The review's later probes are reproduced at the end of the section. `p22` is the form-startup project behind the
control-array, Missing, ReDim and `UBound` rows. `p21a` to `p21h` are eight one-module projects, one per answer,
because a compile error stops the build.

### Subscripts

| Expression | Value | Type | Err |
|---|---|---|---|
| `a(CLng(2))`, `a(2&)` | 20 | Integer | |
| sum of `a(i)` over `i` a Long, 0 to `UBound(a)` | 150 | Integer | |
| `a(UBound(a))` | 50 | Integer | |
| `a(1.5)`, `a(2.5)`, `a(2.4)` | 20 | Integer | |
| `a(2.6)` | 30 | Integer | |
| `a("3")`, `a(" 3 ")` | 30 | Integer | |
| `a("2.5")` | 20 | Integer | |
| `a(Empty)`, `a(False)` | 0 | Integer | |
| `a(CCur(2.5))`, `a(CDate(2))` | 20 | Integer | |
| `a(CSng(3.5))` | 40 | Integer | |
| `a("x")` | | | 13 |
| `a(Null)` | | | 94 |
| `a(True)`, `a(-1)`, `a(6)`, `a(40000)` | | | 9 |
| `a(3000000000#)` | | | 6 |
| `a(x)`, `x` an omitted `Optional x As Variant` (`p22`) | | | 13 |
| `a(x) = 99`, the same `x` (`p22`) | `a(0)` unchanged | | 13 |
| `a(CVErr(5))` (`p22`) | | | 13 |
| `a(2.5) = 99`, then `a(2)` | 99 | Integer | |
| `a(3.5) = 77`, then `a(4)` | 77 | Integer | |
| `m(1.5, 0.5)` and `m(CLng(2), CLng(1))` of a `Dim m(2, 2)` | `m(2, 0)` and `m(2, 1)` | | |

**A subscript is a Long, coerced as `CLng` coerces it**, and only then checked against the bounds:
- banker's rounding (2.5 and 1.5 are both 2, 3.5 is 4), also through Currency, Single, a Date and a numeric
  string;
- `True` is -1, so out of bounds; `Empty` and `False` are 0;
- `Null` is 94 and a non-numeric string 13;
- 40000 is out of bounds (9) rather than an overflow, because it is a Long, not an Integer;
- beyond a Long, 6;
- **the one exception to CLng:** an omitted `Optional` Variant, and an Error value, are 13. `CLng` reads the omitted
  argument as 448, the number of the error value VB6 passes for it, and HexIDE's subscript did the same, so `a(x)`
  was `a(448)`: that element where one exists, and 9 where not. HexIDE has no Error values yet (`CVErr`), so that
  row is not tested.

A write finds its element the same way, and so does every subscript of a two-dimensional array. So does a `Set` of an
element (measured later, both modes): with `wa(3) As Widget`, `Set wa(i) = w` with `i` a Long 2, `Set wa(2.5) = Nothing`
and `Set wa(0.6) = w` reach `wa(2)`, `wa(2)` and `wa(1)`.

### Bounds

| Statement | Then | Err |
|---|---|---|
| `ReDim b(CLng(3))` | `UBound(b)` 3 | |
| `ReDim b(2.5)` | `UBound(b)` 2 | |
| `ReDim b(3.5)` | `UBound(b)` 4 | |
| `ReDim b(1.5 To 3.5)` | `LBound(b)` 2, `UBound(b)` 4 | |
| `ReDim b("4")` | `UBound(b)` 4 | |
| `ReDim b(i)`, `i` a Long 4 | `UBound(b)` 4 | |
| `ReDim b(1)`, then `ReDim b("x")` | `UBound(b)` still 1 | 13 |
| `ReDim b(1)`, then `ReDim b(Null)` | `UBound(b)` still 1 | 94 |
| `ReDim b(1)`, then `ReDim b(3000000000#)` | `UBound(b)` still 1 | 6 |
| `ReDim b(1)`, then `ReDim b(-1)`, `ReDim b(5 To 2)` or `ReDim b(0 To -1)` | `UBound(b)` raises 9 | 9 |
| `Dim d(N)`, with `Const N As Long = 3` | `UBound(d)` 3 | |
| `Dim d2(-2& To 2&)` | `LBound(d2)` -2 | |

Every bound goes through the same coercion. The two kinds of failure leave the array differently:
- **A bound that cannot be coerced** raises before anything changes, and `b` keeps its old bounds.
- **An upper bound below its lower** raises 9 and leaves `b` **erased**, so `UBound(b)` raises 9 too.

**A Variant is not an array variable, and ReDim treats it differently** (`p22`, native and in the IDE). `v` is a
Variant given `ReDim v(2)` first; `b` is `Dim b() As Long` given `ReDim b(2)` first:

| Statement | Err | Afterwards |
|---|---|---|
| `ReDim v(5 To 4)`, `ReDim v(-1)`: the upper bound one below the lower | 9 | `v` still 0 To 2 |
| `ReDim v(5 To 2)`: further below | 7 | `v` Empty, VarType 0 |
| `ReDim b(5 To 4)`, `ReDim b(5 To 2)` | 9 | `b` erased |

So the "9 and erased" rule belongs to a variable declared an array. HexIDE erased a Variant too. It also could not
`ReDim` a Variant that had never held an array: it took the Variant's own type, Empty, which is not an array type,
and stopped with an internal error.

More of a Variant, in a later probe (`p23`, a Sub Main project, native and in the IDE):

| `v` before | Statement | Err | `v` afterwards |
|---|---|---|---|
| Empty, or 5 | `ReDim v(2)` | | `Variant()`, 0 To 2 |
| a `Long` array | `ReDim v(2)` | | `Long()`, 0 To 2 |
| Empty, or 5 | `ReDim v(5 To 2)` | 7 | Empty |
| Empty | `ReDim v(5 To 4)` | 9 | Empty, as it was |
| `Variant()`, 0 To 2 | `ReDim v(0 To 1, 5 To 2)` | 7 | Empty |
| `Variant()`, 0 To 2 | `ReDim v(0 To 1, 5 To 4)` | 9 | 0 To 2, as it was |
| `Variant()`, 0 To 2, or Empty | `ReDim v(2147483647)` | 7 | Empty |

So a Variant's new array keeps the element type of the array it held, and is a Variant array otherwise.

**With several dimensions,** a dimension exactly one below its lower bound wins, wherever it is. Measured in `p30`,
native and in the IDE, each from `ReDim v(2)`:

| Statement | Err | `v` afterwards |
|---|---|---|
| `ReDim v(5 To 4, 5 To 2)`, `v(5 To 2, 5 To 4)`, `v(5 To 2, 0 To 1, 5 To 4)`, `v(0 To 1, 5 To 4, 5 To 2)`, `v(5 To 4, 0 To 1)` | 9 | 0 To 2, as it was |
| `ReDim v(5 To 2, 5 To 1)`, `v(0 To 1, 5 To 2)` | 7 | Empty |

This section first said "any one dimension decides", and HexIDE checked for one further below first, so the five 9
rows raised 7 and emptied `v`.

**A parameter is ReDimmed as what it was declared** (`p30`, both modes):

| Parameter | `ReDim b(5 To 2)` | Afterwards |
|---|---|---|
| `b() As Long`, passed a `Long()` | 9 | an erased `Long()`; after `ReDim b(1)`, `b(0) = "x"` is 13 |
| `b()`, passed a `Variant()` | 9 | an erased `Variant()` |
| `v As Variant`, passed a Variant holding `Variant()` | 7 | Empty |

HexIDE took the `b() As Long` parameter for a Variant, so 7 and Empty, and the next `ReDim` built a `Variant()`
that took the String with Err 0 (found in review round two). A parameter's slot did not carry its declared type: a
`b() As Long` parameter was recorded as a Long, so the array was copied rather than shared, then narrowed as if it
were a scalar.

**The edge of a Long** (`p22`, both modes):

| Statement | Err | Afterwards |
|---|---|---|
| `ReDim b(2147483647)`, from undimensioned or from 0 To 2 | 7 | `b` erased |
| `ReDim b(1 To 2147483647)` | 7 | `b` erased |
| `ReDim b(-2147483648# To 2147483647)` | none | `LBound` -2147483648, `UBound` 2147483647 |

The first two rows are 7, Out of memory, and that is all they say: `1 To 2147483647` is exactly a Long's maximum
count, and 8 GB fails to allocate in a 32-bit process anyway, so they do not separate "more than a Long counts" from
"more than memory holds". The last row is VB6's own count wrapping: 2^32 elements is 0 in 32 bits, so it allocates
nothing and reports bounds it cannot hold. **That is a defect, not a
behaviour, and HexIDE does not reproduce it** (the Fidelity Principle): it raises 7 there too. HexIDE counted in an
int, so 0 To 2147483647 wrapped negative and raised 9, and the full range wrapped to the same zero as VB6's.

**`UBound` and `LBound`'s dimension** coerces as `CInt` does (`p22` and `p30`, both modes), with `b` 1 To 3:

| `UBound(b, d)`, `d` | Err | Value |
|---|---|---|
| `"1"` | | 3 |
| `Null` | 94 | |
| `"x"`, `CVErr(5)` | 13 | |
| `x`, an omitted `Optional x As Variant`; `LBound(b, x)` too | 13 | |
| `3000000000#`, `40000`, `32768`, `70000&`, `-32769`, `"40000"` | 6 | |
| `32767`, `True` (-1), `Empty` (0), `-1`, `1.5` and `2.5` (each 2) | 9 | |

**This section first said "as a subscript does", that is as `CLng`,** from the `p22` rows alone, every one of which
fits both. The review's `40000` row separates them: a Long, so 9 as a subscript, but 6 here. An omitted `Optional`
is 13, not dimension 1: it is passed, not left out. HexIDE read the dimension with its integer helper, which
disagreed on several of these; after round two it read it as `CLng`, and read an omitted one as dimension 1.

`ReDim b(0 To -1)`, an empty range, is refused. HexIDE keeps such an array internally for `Split` and friends, so
the refusal belongs to `ReDim` itself, not to the array.

`UBound` and `LBound` are Longs throughout.

**A bound that is a constant expression** is coerced the same way. A third probe declared these on a form, under
`Private Const N = 2.5`, `M = 3.5` and `GRID_MAX = 9`, and in `Form_Load` under `Const P = 2.5`:

| Declaration | Bounds |
|---|---|
| module `Private mA(N)` | 0 To 2 |
| module `Private mB(M)` | 0 To 4 |
| module `Private mC(&HA)` | 0 To 10 |
| module `Private mD(8 + 1)` | 0 To 9 |
| module `Private mH(N - 1)`, which is 1.5 | 0 To 2 |
| module `Private mF(0 To GRID_MAX, 0 To GRID_MAX)` | 0 To 9, 0 To 9 |
| module `Private mG(1.5 To M)` | 2 To 4 |
| procedure `Dim pA(P)` | 0 To 2 |
| procedure `Dim pB(P + 1)`, which is 3.5 | 0 To 4 |

Banker's rounding again: 2.5 is 2, and 3.5 and 1.5 round up to 4 and 2.

### What a module-level array's bound may name

The review asked what VB6 allows in a module-level bound beyond literals and same-module Consts above it. `p21a` to
`p21h`, each built with `/make` and run where it built:

| Module-level declarations | VB6 |
|---|---|
| `Dim d(N) As Long`, then `Const N = 3` | compile error, "Constant expression required" |
| `Dim n As Long`, then `Dim d(n) As Long` | compile error, "Constant expression required" |
| `Const A = B + 1`, then `Const B = 2`, then `Dim d(A) As Long` | compile error, "Constant expression required", at `Const A` |
| `Dim d(M)`, `Dim e(Module2.M)`, `Dim g(1 To M + 1)`, with `Public Const M = 3` in Module2 | 0 To 3, 0 To 3, 1 To 4 |
| `Dim d(eMax)`, with `eMax = 4` in an Enum | 0 To 4 |
| `Dim b(0 To GRID - 1, 0 To GRID - 1)`, `Dim h(GRID \ 3)`, with `Const GRID = 10` | 0 To 9 each; 0 To 3 |
| `Dim p(2.5)` | 0 To 2 |
| `Dim d(Len("abc"))` | 0 To 3 |
| `Dim d(vbKeyA)` | 0 To 65 |

In a procedure, `Dim a(N)` before `Const N = 3` is a compile error too, "Variable not defined" (`p21f`, under
`Option Explicit`). HexIDE used to run it because a procedure's Consts are declared at entry ahead of its Dims
(#760, below). #894 now checks these forward reads before hoisting; a Const forward reference without
`Option Explicit` used to give a wrong value (`A` was 1).

**VB6 refuses a forward reference between module Consts.** `Const A = B + 1` above `Const B = 2` does not compile.
That is narrower than CLAUDE.md's pre-pass paragraph implies: it reasons from order-independent Const evaluation, as
though `Const A = B + 1` before `Const B` were valid VB6. Within one module it is not.

**Across modules, list order does not matter, but the modules' Consts must not form a cycle.** Measured for #873's
review with `vb6.exe /make` (2026-10-13), by two measurers working independently, identical in every row, except the
two rows marked as measured once. Each row is one project: the program column is all of each module's declarations,
Sub Main sits in Module1 and prints each value, and a project that built was run:

| Program | VB6 |
|---|---|
| Module1 `Public Const A = Module2.B + 1`, Module2 `Public Const B = 2`, either list order | A = 3 |
| the same, unqualified, `Public Const A = B + 1`, either list order | A = 3 |
| Module1 `Public Const A = B + 1`, Module2 `Public Const B = A + 1`, either order | "Circular dependencies between modules", at Module2's Const |
| Module1 `Public Const A = 1` and `Public Const C = B + 1`, Module2 `Public Const B = A + 1`: no Const depends on itself | "Circular dependencies between modules" |
| as above, with a module-level `Public arr(A) As Long` in place of a Const | "Circular dependencies between modules": a bound is an edge too |
| a Const read inside a procedure body, a procedure-level Const in the other module, or a procedure call, as the back edge | builds: procedure bodies are not edges |
| Module1 `Const A = 2`, `Const A2 = Module1.A + 1` | "Circular dependencies between modules": the module naming itself |
| `Const A = Module1.B + 1` above `Const B = 2`, in Module1 | "Circular dependencies between modules", not "Constant expression required" |
| in Module1, `Const C = Module1.n + 1` after `Private n As Long`; `Private d(Module1.n) As Long` after `Public n As Long`; `Const C = Module1.Twice(2)`; `Const C = Module1.Nope + 1`, with nothing named Nope | "Circular dependencies between modules", each: whatever the module's own name qualifies (measured once, for #873, `/make`, one project each) |
| a class's `Private Const C = Module1.M + 1` and `Private Const D = M + 2`, Module1's `Public Const M = 3` | C = 4, an Integer, and D = 5 (measured once, for #873, native and in the IDE) |
| `Const A = B + 1` above `Const B = 2`, module level, with or without `Option Explicit` | "Constant expression required" |
| the same in a class, in a form never loaded, and `Const A = eLater + 1` above the Enum declaring `eLater` | "Constant expression required"; the form's fires because `/make` compiles every module |
| `Const A = B + 1`, Module2's `Private Const B = 2` | "Variable not defined": invisible |
| in a procedure, `Const A = B + 1` above `Const B = 2` | "Variable not defined" with `Option Explicit`, "Constant expression required" without |
| in a procedure, `Dim a(N) As Long` above `Const N = 3`, no `Option Explicit` | "Constant expression required" |

So the cycle check is per module, not per Const. Module-level Consts and array bounds make edges between modules;
procedure bodies do not. Within a module, every scope probed evaluates strictly in source order: module, class,
form, procedure and Enum member. And at module level `Option Explicit` still says "Constant expression required",
where at procedure level it says "Variable not defined": module-level names are known before their values are.

**HexIDE, after #893.** A module-level Const or array bound reading another standard module's Public Const,
qualified or bare, runs that module's declarations on demand first, once. Re-entering a module whose declarations
are still running raises "Circular dependencies between modules" as a compile error during execution. The check
is per module, even when the individual Consts do not depend on themselves. Procedure bodies make no dependency
edges. The test harness's `Execute` completes the startup module's declarations before its executable top-level
statements, so a class created there reads the completed startup Consts too (the class row above, measured native
and in the IDE, and issue #893's follow-up comment). No new VB6 measurement is needed for this fix.

**Review round three** asked what lies one level down: what a Const above the Dim may itself read, which built-ins
are allowed, and what a qualified name does. Each case below is its own project, built with `/make` (`p27`, `p28` and
`p32`), because `/make` stops at the first error; each that built was run. Every module has `Option Explicit` unless
the row says otherwise.

| Module-level declarations | VB6 |
|---|---|
| `Const A = 2`, `Const B = A * 2`, `Dim d(B)` | 0 To 4 |
| an Enum with `eMax = 4`, `Const C = eMax + 1`, `Dim d(C)`; the same with `Const C = Sizes.eMax + 1` | 0 To 5 |
| `Const C = vbKeyA - 60`, `Dim d(C)` | 0 To 5 |
| `Const C = M + 1`, `Dim d(C)`, with Module2's `Public Const M = 3` | 0 To 4 |
| `Const C = Module2.M + 1`, `Dim d(C)`, the same Module2 | 0 To 4 |
| `Const C = Twice(2)`, `Dim d(C)`, `Twice` a user function | "Constant expression required" |
| `Dim d(Twice(2))` | "Constant expression required" |
| `Dim n As Long`, `Const C = n + 1`, `Dim d(C)` | "Constant expression required" |
| `Const A = A + 1`, `Dim d(A)` | "Constant expression required" |
| `Const C = Len("abcd")`, `Const C = Asc("A") - 60`, `Const C = Int(2.5)`, each then `Dim d(C)` | "Constant expression required" |
| `Dim d(colMax)`, Module2 holding `Public Enum Cols` with `colMax` its third member | 0 To 2 |
| `Dim d(Cols.colMax)`, the same Module2 | 0 To 2 |
| `Dim d(colMax)`, Module2's Enum `Private` | "Variable not defined" |
| an Enum `Sizes` with `eMax = 4`, `Dim d(Sizes.eMax)` | 0 To 4 |
| `Const N = 3`, `Dim d(Module1.N)`, in Module1 | "Circular dependencies between modules" |
| the Enum `Sizes`, `Dim d(Module1.eMax)`, in Module1 | "Circular dependencies between modules" |
| `Dim d(Module2.M)`, Module2's `Public Const M = 3` | 0 To 3 |
| `Dim d(M)`, Module2's `Private Const M = 3`, or its `Const M = 3` | "Variable not defined" |
| `Dim d(M)`, Module2's `Public Const M = 3`, no `Option Explicit` | 0 To 3 |
| `Dim d(n)`, Module2's `Public n As Long` | "Constant expression required" |
| `Dim d(zz)`, nothing declaring `zz` | "Variable not defined"; without `Option Explicit`, "Constant expression required" |
| `Dim s As String`, `Dim d(Len(s))`; `Dim n As Long`, `Dim d(Abs(n))` | "Constant expression required" |
| `Private Const K = "abcd"`, `Dim d(Len(K))` | 0 To 4 |
| `Dim d(Abs(Int(-2.5)) + Len("ab"))` | 0 To 5 |

So a Const with no visibility is Private, and invisible to another module, and a Const initialiser refuses every
function call, `Len` and `Int` included, where a bound allows some. **Built-in functions in a bound,** each of
constants:

| Allowed (the bound built, with the value VB6 sizes) | Refused, "Constant expression required" |
|---|---|
| `Abs(-3)` 3, `CBool(1)` (as `-CBool(1) + 1`) 2, `CByte(3)` 3, `CCur(3)` 3, `CDbl(3)` 3, `CInt(2.5)` 2, `CLng(3.5)` 4, `CSng(3)` 3, `Len(CStr(123))` 3, `CVar(3)` 3, `Fix(2.5)` 2, `Int(2.5)` 2, `Len("abc")` 3, `LenB("abc")` 6, `Sgn(5) + 1` 2 | `Asc`, `AscW`, `Atn`, `CDec`, `Choose`, `Chr$`, `Exp`, `Hex`, `IIf`, `InStr`, `LBound`, `Left$`, `Log`, `Mid$`, `Rnd`, `Round`, `Sin`, `Space$`, `Sqr`, `Timer`, `UCase$`, `Val` |

No rule I would defend separates the two columns. The allowed set is the conversions and the arithmetic functions
VB6 can fold at compile time, but `Round`, `Sqr` and `CDec` are refused while `CCur` and `Sgn` are allowed.

**HexIDE, after round two.** The bound was classified one level deep: a same-module Const above the Dim was accepted
without looking at what its own initialiser read, any module's Enum member was accepted, and every registered
built-in was. So `Const C = M + 1`, `Const C = n + 1`, `Const C = Twice(2)` and `Const A = B + 1` above `Const B`
each sized `d` from what HexIDE's evaluator made of the Const (0 To 1, or 4), another module's Enum member read as
Empty and sized 0 To 0, and `Dim d(Int(Rnd() * 10))` sized at random.

**HexIDE, after round three.** The pre-pass collects each Const's declaration and visibility, and each Public
variable. Each module-level Const is checked as it runs, before it is evaluated, and the bound at the Dim, both by
the rules above. The Consts above ran first, so a Const a bound or another Const names has been checked already, and
the check is one level deep. So `Const A = B + 1` above `Const B` is refused with no array in sight, as VB6 refuses it.
Procedure forward-local-Const reads are now checked by #894; other procedure rules remain unchanged. At module level, what is refused:
- a Const below, the Const itself, a variable, a user function, a function call inside a Const, a built-in VB6
  refuses, and another module's Public variable are VB6's compile error;
- a name the module cannot see is "Variable not defined" under `Option Explicit`, and "Constant expression
  required" without it;
- the module's own name as a qualifier is "Circular dependencies between modules";
- another module's Public Const, bare (since #880) or qualified, initialises that module on demand first (#893); another module's Public Enum member, bare or qualified by its Enum's name, is
  read, a class's included (#889);
- a built-in no probe has asked about is refused, rather than guessed either way.

`LenB` and `CVar` are allowed in a bound but not implemented in HexIDE, so a bound using them stops there as any use
of them does.

**Review round four** found that the check refused qualified constants main had read, and asked what else a bound or
a Const may read. Measured with `/make`, one project each, on the en-GB guest; each project that built was run, and
the types (added in round five) are each value's `TypeName` from the same run:

| Declarations | VB6 |
|---|---|
| `Private Const C = VBA.vbCrLf`, then `Len(C)` | 2, Long |
| `Const C = KeyCodeConstants.vbKeyA`; `Const C = VBRUN.KeyCodeConstants.vbKeyA` | 65, Long; 65 |
| `Dim d(VBRUN.vbKeyA)` | 0 To 65 |
| `Const C = Module2.colMax + 1`, Module2's `Public Enum Cols` with `colMax = 4` | 5, Long |
| `Dim d(Module2.Cols.colMax)`, the same Module2 | 0 To 4 |
| `Const C = Probe.Module2.Cols.colMax`; `Const C = Probe.Module2.M + 1`, the project being `Probe` | 4, Long; 4, Integer |
| `Const C = Module2.pMax`, Module2's `Private Enum P` | "Method or data member not found" |
| `Const C = KeyCodeConstants.vbCancel` | "Method or data member not found": another container's member |
| `Const C = Module1.Sizes.eMax`, in Module1 | "Circular dependencies between modules" |
| `Const C = Sizes.eMax` above `Enum Sizes` | "Constant expression required" |
| `Dim d(VBA.Len("ab"))` | "Constant expression required", though `Dim d(Len("ab"))` sizes |
| `Const S = "abc"`, `Dim d(Len(S))`; `Const S As String = "abcd"`, `Dim d(Len(S))` | 0 To 3; 0 To 4 |
| `Dim d(LenB("ab"))`; `Const S = "abc"`, `Dim d(LenB(S))`; `Const S = "ab"`, `Dim d(Len(S & "c"))` | 0 To 4; 0 To 6; 0 To 3 |
| `Dim d(LenB(12345))` | "Variable required - can't assign to this expression" |
| `Dim d(-1)`; `Const N = 0`, `Dim d(1 To N)`; `Dim d(5, 2 To 1)`, each at module level | "Range has no values" |
| `Dim d(-1)`; `Const N = 0`, `Dim d(1 To N)`, each in a procedure | "Range has no values" |
| `Dim d(0 To 0)` | 0 To 0 |
| `Dim d(n)`, Module2's `Global n As Long`, with and without `Option Explicit` | "Constant expression required" |

The review measured the rest itself, two verifiers each: `Len(12345)`, `Len` of a numeric Const and of a typed,
fractional or Date Const are "Variable required"; Module2's `Public Const vbKeyA = 3` sizes `Dim d(vbKeyA)` 0 To 3,
the project beating the library; and `Const A = eLater + 1`, `Dim d(eLater)` and `Dim d(E.eLater)` above the Enum
declaring `eLater` are "Constant expression required".

So a qualified constant resolves level by level as the evaluator resolves it: the project, a module, an Enum, a
library, an in-box container. A call through a qualifier is no constant, even of a function a bound allows bare. `Len`
and `LenB` size only from a String: of a number VB6 asks for a variable. An Enum, like a Const, must be declared above
what reads it. And a dimension with no values does not compile, at either level.

**HexIDE, after round four.**
- Every qualified form above is read as VB6 reads it, or refused with VB6's compile error. A shape the evaluator's
  own levels do not resolve is refused loudly, rather than guessed. The project name in front is stepped over, and
  what follows it is resolved by the same rules: measured for `Probe.Module2.Cols.colMax` and `Probe.Module2.M + 1`,
  and an inference for any other shape. (Round five narrowed this. It said a qualified shape no probe had asked about
  was refused, which the project-name step contradicts.)
- Another module's Public declaration is looked for before a built-in constant of the same name; its Const is then
  read, bare as qualified, since #880.
- `Len` and `LenB` size from a String constant only: a literal, a String Const, a concatenation, or `CStr` of a
  constant. Of a number or a non-String Const they are "Variable required". Of anything else, such as another
  function's result, they are refused loudly. HexIDE has no `LenB` to size from yet.
- The pre-pass records where each Enum is declared, which is collection; an Enum below a Const or bound is "Constant
  expression required".
- A `Global` variable counts as Public.
- A dimension with no values is "Range has no values" at a Dim, at module and procedure level, raised as the Dim runs.
  It built an array with no elements, so `UBound` read -1 and loops ran no passes. (Only where upper is lower - 1, or a
  bound is not a literal: a literal range inverted by more was still a run-time 9 until round five, below.)

### A Const's As type

A Const's `As` clause was ignored: the Const held whatever its initialiser gave, and a module array's bounds trusted
it. Measured with `/make`, one project each, each that built run and its value and `TypeName` written:

| Declaration | VB6 |
|---|---|
| `Const C As Integer = 2.5`; at procedure level too | 2, Integer |
| `Const C As Long = 2.5`; `= 3.5` | 2; 4, Long |
| `Const C As Single = 1.23456789` | 1.234568, Single |
| `Const C As Currency = 1.23456`; `= 1.00005` | 1.2346; 1.0001, Currency |
| `Const C As String = 5`; `= 1.5`; `= #1/2/2003#` | "5"; "1.5"; "02/01/2003", String |
| `Const C As Date = 1`; `= #1/2/2003#`; `= "1/2/2003"` | 31/12/1899; 02/01/2003; 01/02/2003, Date |
| `Const C As Integer = "5"`; `= True` | 5; -1, Integer |
| `Const C As Boolean = "True"` | True, Boolean |
| `Const C As Variant = 2.5` | 2.5, Double |
| `Const C As Byte = 300`; `= -1`; `Const C As Integer = 32768`; `Const C As Currency = 922337203685478#`; `Const C As Date = 3000000`; a procedure's `Const P As Byte = 300` | "Overflow" |
| `Const C As Integer = "x"`; `Const C As Date = "abc"` | "Type mismatch" |
| `Const C As Integer = 2.5`, `Dim d(C + C)` | 0 To 4 |

The review measured three more: `Const CL As Long = 7 / 2` is 4 and sizes `Dim d(CL * 2)` 0 To 8, and `Const B As
Boolean = 5` sizes `Dim d(B To 1)` -1 To 1.

So a Const converts into its type as a store into a variable of that type converts, and where that store would raise 6
or 13 the Const is the compile error of the same name. `1.00005` is not a tie as a Double, so it rounds up, which
HexIDE's Currency conversion does not do yet (#966): it gives 1, for this Const and for every store and `CCur` of that
value. The same probe found a store of a date string into a Date raised 13 in HexIDE (`s01`, below), where VB6 reads
it as `CDate` does.

| Statement (`s01`, both modes) | VB6 |
|---|---|
| `c = 1.00005`, `c` a Currency; `CCur(1.00005)` | 1.0001; 1.0001 (HexIDE gives 1 for both, #966) |
| `CCur(2.00005)`; `CCur(0.00015)` | 2; 0.0001: the Doubles nearest those lie below the midpoint (HexIDE gives 2 and 0.0002, #966) |
| `dd = "1/2/2003"`, `dd` a Date | 01/02/2003, no error (HexIDE raised 13 until #967) |
| `dd = "abc"` | 13 |

**HexIDE, after this.** A module-level or procedure Const converts into its `As` type through the same conversion a
store uses, and a conversion that would raise 6 or 13 is "Overflow" or "Type mismatch", raised as the Const runs. A
store of a String into a Date read as `CDate` reads it, in the program's locale, where every date string was 13;
round five reverted that, below, because HexIDE's `CDate` had no numeric fallback (#967, since fixed: *A String read as
a Date*). A Const of an object or
fixed-length String type is refused loudly. Currency from a Double still rounds the decimal form
(#966).

### ReDim of a fixed array through a parameter

Making a `b() As Long` parameter share the caller's array (round three) let a callee's ReDim replace a caller's fixed
array. Measured, native and in the IDE where marked:

| Program | VB6 |
|---|---|
| `Dim buf(3) As Byte` in Sub Main, passed to `Sub Grow(b() As Byte)`, which does `ReDim b(10)` (both modes) | 10, "This array is fixed or temporarily locked"; `buf` still 0 To 3 |
| the same with a module-level `Private mbuf(3) As Byte` | 10; still 0 To 3 |
| `ReDim Preserve b(10)` (both modes) | 10 |
| `Dim buf(3)` passed to an untyped `Sub Grow(b())` (both modes) | 10 |
| `ReDim b(3)`, the same bounds again (both modes) | 10, and `buf(1)` keeps its value |
| `ReDim b("x")` | 13: the bound converts first |
| `ReDim b(5 To 2)` (both modes) | 10, before an inverted range's 9 |
| `Dim buf() As Byte`, `ReDim buf(3)`, then `Grow` | no error, 0 To 10 in the caller too |
| `Erase b` in the callee, of the fixed `buf` (both modes) | no error: 0 To 3, and element 1, which held 9, reads 0 |
| `Dim buf(3) As Byte`, `ReDim buf(10)`, by name, in Sub Main; the same of a module-level one | does not compile: "Array already dimensioned" |

**HexIDE, after this.** ReDim of a fixed array through an array parameter raises 10 and leaves it as it was, after
converting the bounds and before the inverted-range check; named directly it is "Array already dimensioned", raised
as the ReDim runs. Each call records its array parameters by name, because a ByRef one shares the caller's slot and
the slot cannot say so (every parameter, since round five, below). `Erase` already reset a fixed array's elements and kept its bounds. `ReDim Preserve` is not
implemented at all. (Round six, below, moved the decision from the array to the declaring slot. This round read the
array object, which HexIDE's Let shares, so `d = f` then `ReDim d(10)` was refused, where main and VB6 run it.)

**Another module's array, qualified, found on the way** (`g01`, both modes): `x = Module2.g(1)` is the element,
5, a Long, and `Module2.g(2) = 7` writes it. HexIDE reads the whole array, and the write is a trappable 424 (#965).

### Review round five

Round four's guards read the array object, and HexIDE's array Let shared that object, so a dynamic array holding a
copy of a fixed one could not be ReDimmed; on main, which checked nothing, that ReDim worked. The review measured that
VB6 copies, and the rest below was measured afterwards with `/make`, one project each, natively. Each project that
builds was then run in the IDE too, in round six, and every line matched. `fx` is `Dim fx(3) As Long` with
`fx(1) = 7`:

| Program | VB6 |
|---|---|
| `Dim d() As Long`, `d = fx`, `d(1) = 99` | `fx(1)` 7, `d(1)` 99: a copy (the review measured the same program) |
| then `ReDim d(10)` | 0 To 10, `fx` still 0 To 3 with `fx(1)` 7 |
| `Dim v`, `v = fx`, `v(1) = 55` | `fx(1)` 7, `v(1)` 55, `TypeName(v)` "Long()" |
| then `ReDim v(5)` | 0 To 5, `fx` still 0 To 3 |
| `Dim fx(1, 2) As Long`, `fx(1, 2) = 7`, `d = fx`, `d(1, 2) = 99`; the same into a Variant | `fx(1, 2)` 7 both times |
| `Dim buf(3) As Byte`, `buf(1) = 9`, `G buf`, `Sub G(v)`, `ReDim v(10)`, the caller under `On Error Resume Next` | 10, "This array is fixed or temporarily locked"; `buf` 0 To 3, `buf(1)` 9 |
| the same with `ReDim Preserve v(10)` | an error that ends `G`, its number not logged here; `buf` kept. Round six logged it: 10 |
| `Sub GV(ByVal v)`, `ReDim v(10)` | no error, `v` 0 To 10 inside, `buf` 0 To 3 with `buf(1)` 9 |
| module-level `Private d(5 To 2)`; `Private d(0 To -5)`; `Private d(1, 5 To 2)` | "Range has no values", each |
| a class's `Private d(5 To 2)`; a procedure's `Dim d(5 To 2)` | "Range has no values", both |
| `Private d(Len("ab" + "c"))`; `Const S = "ab" + "c"`, `Private d(Len(S))` | 0 To 3; 0 To 3, `S` a String |
| `Private d(Len("ab" + 1))` | "Variable required - can't assign to this expression" |
| `Const C As Integer = Null`; `Const C As String = Null`; a procedure's `Const P As Integer = Null` | "Invalid use of Null", each |
| `Const C As Variant = Null`; `Const C = Null` | Null, `TypeName` "Null", both |
| `Dim a(5) As Long`, `a(2) = 7`, `x = a(k)`, `k` a class with no default member | 438, `x` unchanged |
| `x = a(w)`, `w` a class whose default member returns 2; `a(w) = 5` | 7; writes `a(2)` |
| `x = a(n)`, `n` a Nothing | 91 |

The review also measured, before this: `d = fx` then `ReDim d(10)` works at module level, from a Variant-returning
Function's fixed local, and after passing `d` to `Sub Grow(b() As Long)`; and, for #967, `CDate("36526")` is 01/01/2000,
`"36526.5"` noon that day, `" 2 "` 01/01/1900 and `"-1"` 29/12/1899; and `d = "36526"` into a Date gives the same.

So an assignment copies an array, every dimension, and the copy is dynamic, since only a dynamic array or a Variant
can be assigned one. A fixed array can then be held only by its own declaration, or reached through a ByRef
parameter, and a parameter's ReDim of one is 10 whether the parameter is an array or untyped, `Sub G(v)`: VB6 cannot
know at compile time what a Variant holds. Round six measured the same through `v As Variant`: 10, below. A literal range with no values does not compile wherever it is. `+` of two
Strings is a String, and with a number it is not. A typed Const cannot hold Null, where a Variant one can.

**HexIDE, after round five.**
- An array Let copied the array, every level, as a dynamic array (`VBArray.CopyForLet`, through `CopyIfValueType`),
  where it shared it. Round six took this back out (below), and it is #970's.
- ReDim through an untyped parameter of a fixed array is 10, where main silently replaced the caller's array and round
  four gave "Array already dimensioned". Each call records all its parameters by name.
- A literal range with no values is "Range has no values" from the pre-pass, which built it and raised 9, at module
  level and in a class.
- `Len` and `LenB` size from `"ab" + "c"`, and of `"ab" + 1` are "Variable required".
- A typed Const of Null is "Invalid use of Null", raised as the Const runs.
- `a(k)` is 438 and `a(Nothing)` 91, as before; `a(w)` is refused naming #174, read or written.
- **Reverted from round four:** a store of a String into a Date goes through `ToNum` again, as on main. Round four
  sent it through `CDate`, which has no numeric fallback, so `d = "36526"` became 13, and which accepts a dotted
  date, so `d = "1.2.2003"` became silently 1 February (#800). So `d = "1/2/2003"` is 13 again, where VB6 gives a
  date, and a `Const … As Date` given a String is refused loudly. Both are #967, fixed since: *A String read as a Date*.
- HexIDE's `CCur(0.00015)` is 0.0002, where VB6 gives 0.0001: #966 is wrong in both directions.

**Round six: the copy comes out, and fixedness is the declaration's.** Copying on Let is VB6's rule, but the change
reaches every Let, argument, element store, For Each and object reference count, and the review found it broke four
things: a ByRef `v As Variant` parameter given an array got a private copy (so `Sub BubbleSort(arr As Variant)` left
its array unsorted), the copy did not count the objects it held, `v(1) = v` then a Let overflowed the stack, and an
element Let and a For Each still shared. All of that, with every row above, is #970. Here:
- An array Let shares the array again, as on main. The copy rows above are #970's to reproduce.
- Fixedness is the declaring slot's: `Dim a(N)` marks its slot (`ExecutionState.MarkFixedArray`), at module and
  procedure level; a class's fields are declared through the procedure path, `DeclareLocal`, for each instance. So
  `d = fx` then `ReDim d(10)` works again, giving `d` an array of its own, and a ByRef parameter that shares the
  caller's slot reaches the caller's mark: 10, through `b() As Long` or an untyped `v`.
- A `v As Variant` parameter has a slot of its own, so its ReDim of a fixed array is not refused yet (#970).
- `Sub S3(v As Variant): v(0) = 33` writes the caller's array again (33 for each of the review's four shapes), and is
  pinned, so #970's copy cannot break it unseen.

(**Since #970, 2026-10-14:** the copy is back, with the four things round six found missing, and the HexIDE columns
below are history. See *Arrays copy wherever VB6 stores one*.)

Round six measured three more programs, five rows here, native and in the IDE alike, with `buf` a `Dim buf(3) As Byte` holding 9 at
`buf(1)` and the caller under `On Error Resume Next`:

| Program | VB6 | HexIDE |
|---|---|---|
| `Sub GP(v)`: `ReDim Preserve v(10)`; `Sub GPA(b() As Byte)`: the same | 10 each, `buf` kept | refused: `ReDim Preserve` is not implemented |
| `Sub GA(v As Variant)`: `ReDim v(10)`, given `buf` | 10, `buf` kept | no error, `buf` kept: the ReDim replaces the parameter's own slot (#970) |
| `Sub GW(v As Variant)`: `v(1) = 5`, given `buf` | `buf(1)` 5 | `buf(1)` 5 |
| `GA dyn`, `dyn` a `Dim dyn() As Byte` ReDimmed to 3 | no error, `dyn` 0 To 10 | no error, `dyn` 0 To 3: silently wrong, by a path this PR does not change (#970) |
| module-level `Private mdyn() As Byte`: `ReDim mdyn(10)` in a procedure, then `Grow mdyn` with `ReDim b(12)`; `Private mvar`, `ReDim mvar(4)` | 10, then 12; 4 | the same, and pinned: only `Dim a(N)` marks a slot fixed |

So a `v As Variant` parameter shares the caller's array as an untyped one does, for a ReDim too, and HexIDE does not
share it, for a ReDim. That is #970's alias item.

**And an untyped parameter ReDims by the Variant rule even when it shares a declared array** (measured for #873's
seventh review, native; the review measured the same, T1 to T7). The caller has `Dim a() As Long`, `ReDim a(3)`,
`a(1) = 9`, and passes `a` to `Sub G(v)`, which does the ReDim; the caller is under `On Error Resume Next`:

| `ReDim v(…)` in `G` | VB6 | HexIDE, at round six |
|---|---|---|
| `-1`; `5 To 4`; `5 To 2, 5 To 4` | 9, and `a` keeps 0 To 3, a `Long()` | 9, and `a` erased |
| `5 To 2`; `0 To -5`; `0 To 1, 5 To 2` | 7, and `a` erased, still a `Long()` | 9, and `a` erased |
| `2` | no error, `a` 0 To 2 | the same |

So VB6 applies the rule above for a Variant (9 and kept when any dimension is exactly one below its lower bound, 7 and
emptied otherwise) to an untyped parameter whatever it shares, emptying a declared array by erasing it. HexIDE's
parameter shares the caller's slot and its declared-array mark, so it applies the declared-array rule: data VB6 keeps
is lost in the first row, and the second raises 9 where VB6 raises 7. Main does the same. It is #970's, which owns
parameter aliasing.

Because declarations run in source order and VB6 refuses a forward reference, a same-module Const above the Dim has
already run and holds its final value, so reading it then is all the laziness the rule needs.

A form's run does not run declarations at all (#256), so an array whose bounds name a constant is refused there,
naming the module and the array. Sizing it would not be enough: on that run the module's own Consts read Empty too.

### A control-array index

On a form with `Cmd(0)` and `Cmd(1)`:
- `Cmd(CLng(1))`, `Cmd(0.6)` and `Cmd("1")` are all `Cmd(1)`;
- `Cmd(1.5)` raises 340, "Control array element doesn't exist", because 1.5 rounds to 2.

`Load` and `Unload` read theirs the same way, measured later on a form with only `Cmd(0)`: `Load Cmd(i)` with `i` a
Long 2 loads element 2, `Load Cmd(3.5)` loads element 4, and `Unload Cmd(4.4)` unloads it again, each with no error,
native and in the IDE.

**But an index is coerced as `CInt`, not as a subscript** (`p22`, on a form with only `Cmd(0)`, native and in the
IDE). This section first said "exactly as a subscript is", from rows that could not tell the two apart:

| Statement | Err |
|---|---|
| `Load Cmd(32768)`, `Load Cmd(40000)`, `Load Cmd(70000&)`, `Load Cmd(3000000000#)`, `Load Cmd("40000")`, `Load Cmd(-32769)` | 6 |
| `Load Cmd(32767.6)`: it rounds to 32768 first | 6 |
| `Load Cmd(-1)`, `Load Cmd(True)`, `Load Cmd(-32768)` | 341, "Invalid control array index" |
| `c = Cmd(40000).Caption`; `c = Cmd(-1).Caption` | 6; 341 |
| `Unload Cmd(40000)`; `Unload Cmd(-1)` | 6; 341 |
| `c = Cmd(1).Caption`, with no element 1 | 340, after those checks |
| `Load Cmd(x)`, `c = Cmd(x).Caption` and `Unload Cmd(x)`, `x` an omitted `Optional x As Variant` (`p31`) | 13, and nothing loads |
| `Load Cmd(CVErr(5))` (`p31`) | 13 |
| `Load Cmd(32766.5)` | none: element 32766, banker's rounding |

Coerced as a Long, HexIDE loaded elements 40000, -1 and `True` with no error (found in review). Before #873 each of
those stopped the run untrappably, because the index had to be an Integer already.

### A method called from Class_Initialize

`Class1`'s `Class_Initialize` calls a `Private Sub` and a `Public Sub` of its own. `Class2`'s stores `Me` in a module
variable `gKeep`, then calls a method of its own. Logged in order:

```
-- New Class1
C1 Initialize: start
C1 Reset (private sibling)
C1 Bump (public sibling) 1
C1 Initialize: end, Count 1
after New Class1, Count 1
C1 Terminate, Count 1          <- at Set k = Nothing
after Set k = Nothing
-- New Class2
C2 Initialize: stores Me in gKeep
C2 Touch (sibling)
after New Class2
after Set k2 = Nothing         <- no Terminate: gKeep still holds it
C2 Terminate                   <- at Set gKeep = Nothing
after Set gKeep = Nothing
-- New Class1 inside a Sub, held only by a local
C1 Initialize: start ... C1 Initialize: end, Count 1
inside NewAndDrop, Count 1
C1 Terminate, Count 1          <- at End Sub
```

Nothing called from `Class_Initialize` terminates the object. It terminates when its last reference goes, and a
reference `Initialize` makes itself counts.

**HexIDE got this wrong.** The object stood at a count of 0 while `Class_Initialize` ran. A method call counts `Me`
for its duration, so the first call took the object to 1 and back to 0, and fired `Class_Terminate` before
construction finished. The object was then marked terminated and never counted again. `New` now holds the object
while `Class_Initialize` runs, and hands it back at whatever count `Initialize` left.

**Two by-products.**
- **A Sub can be named `Reset`.** The probe's private sibling is called `Reset`, which is also a VB6 statement.
  vb6.exe compiled it and called the Sub, measured native and in the IDE in the #850 probe (also recorded in
  #851). HexIDE still parses the bare call as a `Reset` statement, but now dispatches it to the module's own
  Sub when one exists (#851). With no such Sub, the file-closing statement remains unimplemented.
- **`mE` is `Me`.** The constant-bound probe first declared `Private mE(N - 1)`, and vb6.exe refused it: "Expected:
  identifier". Identifiers are matched without regard to case (#811), and so are reserved words, so `mE` is the
  reserved word `Me`. The row was renamed `mH`.

**HexIDE, after this.**
- Every subscript and `Dim`/`ReDim` bound is coerced as `CLng` does, except that an omitted `Optional` is 13. A
  control-array index is coerced as `CInt` does, an omitted `Optional` is 13 there too, and a negative one is 341.
  `UBound`'s and `LBound`'s dimension is coerced as `CInt` does, and an omitted `Optional` is 13.
- `ReDim` with an upper bound below its lower raises 9 and erases a variable declared an array, or an array
  parameter, keeping its type. A Variant keeps its array if any dimension's upper bound is one below its lower, and
  is emptied with 7 otherwise. Only a slot known to be a Variant is treated as one. A Variant can be `ReDim`med at
  all. The measured Out of memory rows are 7, and erase the array.
- A typed array parameter, `b() As Long`, is a `Long()`: passed ByRef, it shares its caller's array. It was taken for
  a Long, so the array was copied and then narrowed as if it were a scalar.
- A module-level `Dim` bounded by anything but a plain integer, such as `Dim d(N)` or `Dim d(2&)`, loads. Its bounds
  were parsed as integers before the module ran, so either one threw and the module failed to load. They are now
  read where the array is allocated, under the rules in "What a module-level array's bound may name", and a form's
  run refuses such an array (#256) rather than leave it unallocated.
- A procedure's `Const`s are declared at its entry, ahead of its `Dim`s. HexIDE declares a procedure's `Dim`s when
  the procedure is entered, so `Dim pA(P)` under `Const P = 2.5` was sized while `P` was still Empty, not as VB6's
  `pA(0 To 2)`. #760's module-level fix needed this too: without it, the same bound inside a procedure was sized
  from Empty rather than refused.
- `New` holds the object while `Class_Initialize` runs.

**The review's probes.** `p22`'s `Form1.frm` holds `Cmd(0)` and runs everything from `Form_Load`, logging each line
with `W`, which appends to `log.txt`. Abridged to one of each kind of row:

```vb
Private Sub Missing(Optional x As Variant)
    Dim a(3) As Long, b() As Long, c As Variant
    On Error Resume Next
    Err.Clear: c = a(x)
    W "a(x), x an omitted Optional Variant" & vbTab & "err " & Err.Number
    Err.Clear: ReDim b(x)
    W "ReDim b(x)" & vbTab & "err " & Err.Number
End Sub

Private Sub Form_Load()
    Dim c As Variant, v As Variant, b() As Long
    On Error Resume Next
    Err.Clear: Load Cmd(40000)
    W "Load Cmd(40000)" & vbTab & "err " & Err.Number & vbTab & "Count " & Cmd.Count
    Missing
    v = Empty: ReDim v(2): v(0) = "kept"
    Err.Clear: ReDim v(5 To 4)
    W "ReDim v(2) then ReDim v(5 To 4)" & vbTab & "err " & Err.Number & vbTab & Shape(v)
    ReDim b(2)
    Err.Clear: ReDim b(2147483647)
    W "ReDim b(2) then ReDim b(2147483647)" & vbTab & "err " & Err.Number & vbTab & Shape(b)
    ReDim b(1 To 3)
    Err.Clear: c = UBound(b, Null)
    W "UBound(b, Null)" & vbTab & "err " & Err.Number
    End
End Sub
```

`p23` is a Sub Main project of the same shape, logging with `Print #`:

```vb
Sub Main()
    Dim f As Integer, v As Variant, l(1) As Long, b() As Long
    f = FreeFile
    Open App.Path & "\log.txt" For Output As #f
    On Error Resume Next
    Err.Clear
    v = l: ReDim v(2)
    Print #f, "v = a Long array, ReDim v(2)" & vbTab & "err " & Err.Number & vbTab & Shape(v)
    Err.Clear
    v = Empty: ReDim v(5 To 2)
    Print #f, "Empty v, ReDim v(5 To 2)" & vbTab & "err " & Err.Number & vbTab & Shape(v)
    Close #f
End Sub
```

`p30` adds the parameters, the mixed dimensions and the dimension rows:

```vb
Private Sub TypedParam(b() As Long)
    On Error Resume Next
    Err.Clear: ReDim b(5 To 2)
    W "b() As Long param, ReDim b(5 To 2)" & vbTab & "err " & Err.Number & vbTab & Shape(b)
    Err.Clear: ReDim b(1)
    Err.Clear: b(0) = "x"
    W "  then ReDim b(1), b(0) = ""x""" & vbTab & "err " & Err.Number & vbTab & Shape(b)
End Sub

Sub Main()
    Dim v As Variant, b() As Long
    '   ...
    ReDim b(2): TypedParam b
    ReDim v(2)
    Err.Clear: ReDim v(5 To 4, 5 To 2)
    W "v 0..2, ReDim v(5 To 4, 5 To 2)" & vbTab & "err " & Err.Number & vbTab & Shape(v)
    ReDim b(1 To 3)
    Err.Clear: c = Empty: c = UBound(b, 40000)
    W "UBound(b, 40000)" & vbTab & "err " & Err.Number & vbTab & "[" & c & "]"
End Sub
```

`p31` is `p22`'s form, with only `Cmd(0)`, calling `Load Cmd(x)`, `Cmd(x).Caption` and `Unload Cmd(x)` from a Sub
whose `Optional x As Variant` is omitted. Each `p27`, `p28` and `p32` project is a `Module1.bas`, and a `Module2.bas`
where the row names one, with the declarations in the table and a `Sub Main` that writes `UBound(d)` as `p21a`'s
below does.

`Shape` reports `LBound..UBound`, or the 9 that reading them raises. Each `p21` project is a `Module1.bas` with
`Option Explicit`, the declarations in the table, and a `Sub Main` that writes the bounds. `p21a`, for example:

```vb
Option Explicit
Dim d(N) As Long
Const N = 3

Sub Main()
    Dim f As Integer
    f = FreeFile
    Open App.Path & "\log.txt" For Output As #f
    On Error Resume Next
    Print #f, "forward ref" & vbTab & "UBound " & UBound(d) & vbTab & "err " & Err.Number
    Close #f
End Sub
```

