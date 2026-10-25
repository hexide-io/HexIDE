## ReDim Preserve (2026-10-22, #1459)

**The question.** HexIDE refused every `ReDim Preserve` as not implemented, so a program that grows an array as it goes,
reading lines or collecting matches, stopped at the first one. The mechanism is well known; the edges were not, and #1459
listed them: which bounds may change, shrinking, a fixed array, an `As` clause, Variants and parameters, the lock #1455 added,
and Type and object elements.

**The probe.** Three Sub Main programs under `On Error Resume Next`, each statement's `Err.Number` taken before anything else
is read, and each array's bounds printed by a helper that walks `UBound(x, n)` until it raises. Four further projects, one per
expected compile error. Made native and p-code under en-GB; every line was identical. The three programs `RedimPreserveTests`
runs were then made the same way, and logged exactly what the tests pin.

| Statement, on what | VB6 |
|---|---|
| `ReDim Preserve a(4)`, `a` a Long array `(0 To 2)` with `a(2)` 7 | `(0 To 4)`, `a(2)` 7, `a(4)` a Long 0 |
| `ReDim Preserve a(2)`, the same bounds | no error, nothing lost |
| `ReDim Preserve c(2)`, `c` `(0 To 4)` | `(0 To 2)`, `c(1)` kept; `c(4)` is then 9 |
| `ReDim Preserve c(0)` | `(0 To 0)`, `c(0)` kept |
| `ReDim Preserve c(-1)`, and `c(5 To 2)` | 9, and `c` is still `(0 To 4)` |
| `ReDim Preserve m(1, 4)`, `m` `(0 To 1, 0 To 2)` | `(0 To 1, 0 To 4)`, every element kept |
| `ReDim Preserve m(1, 1)` | `(0 To 1, 0 To 1)`, `m(1, 1)` kept |
| `ReDim Preserve m(0 To 1, 0 To 3)`, the first dimension written out unchanged | `(0 To 1, 0 To 3)` |
| `ReDim Preserve m(2, 2)`: the first dimension changes | 9, `m` unchanged |
| `ReDim Preserve m(1, 1 To 2)`: the last dimension's lower bound changes | 9, `m` unchanged |
| `ReDim Preserve m(4)`, two dimensions to one; `ReDim Preserve a(2, 3)`, one to two | 9 each, unchanged |
| `ReDim Preserve bb(2 To 5)` and `bb(0 To 5)`, `bb` `(1 To 3)` | 9 each, `bb` unchanged |
| `ReDim Preserve bb(1 To 1)`, and `bb(1 To 6)` | `(1 To 1)`, and `(1 To 6)` with every element kept |
| `ReDim Preserve u(3)`, `u` a `Dim u() As Long` never dimensioned | `(0 To 3)` |
| `ReDim Preserve s(3)`, a String array; `w(3)`, a Variant array | the new elements are `""` and `Empty`; `w(1)` stays an Integer |
| `ReDim Preserve a(2147483647)` | 7, and `a` is still `(0 To 2)` with its values |
| `ReDim Preserve a(1 To 2147483647)` | 9: the bounds are checked before the size |
| `ReDim Preserve a("x")`, and `a(4.5)` | 13 and unchanged; `(0 To 4)` |
| `ReDim Preserve v(4)`, `v = Array(1, 2, 3)` | `Variant()` `(0 To 4)`, `v(2)` 3, `v(4)` Empty |
| `ReDim Preserve tv(5)`, `tv` a Variant holding a copy of a Long array | `Long()` `(0 To 5)`, `tv(5)` a Long |
| `ReDim Preserve ev(2)`, `ev` an Empty Variant; `tv(2)`, `tv` holding 5 | 13 each, and each is left as it was |
| `ReDim Preserve v(2, 2)`, `v` holding a copy of `m` | 9, unchanged |
| `ReDim Preserve a(4) As Long`, the array's own type | no error |
| `ReDim Preserve a(4) As Integer`, and `As Variant`, on a declared Long array | compile error: "Can't change data types of array elements" |
| `ReDim Preserve v(4) As Long`, `v` holding `Array(...)`; `tv(4) As Integer`, `tv` holding a `Long()` | 13 each, unchanged |
| `ReDim Preserve v(4) As Variant`, `v` holding `Array(...)` | no error |
| `ReDim Preserve f(5)`, `f` a `Dim f(3) As Long` | compile error: "Array already dimensioned" |
| `ReDim Preserve x(6)` in `GF(x() As Long)`, given that `f`; and `x("x")`, `x(5 To 2)` there | 10, `f` unchanged; 13; 10 |
| `ReDim Preserve x(6)` in `GP(x() As Long)`, `GV(x)` and `GVV(x As Variant)`, given a dynamic Long array | the caller's array is `(0 To 6)`, its values kept |
| `ReDim Preserve ta(3)`, `ta` an array of a Type | every field of the kept elements kept, the new ones empty |
| `ReDim Preserve ov(3)`, then `ov(0)`, `ov` a Variant array holding two `Class1` objects | grows with both kept; the shrink terminates the object held only in `ov(1)` during the statement |
| inside `For Each x In a`: `ReDim Preserve a(5)` | 10 (#1460) |

**What it shows:**
- **Only the last dimension's upper bound may change.** Any other bound changing, the number of dimensions included, is 9 and
  leaves the array as it was. The lower bound of a one-dimensional array may not move either way.
- **A shrink drops the elements past the new bound**, and an object held only there is released by the statement itself. One
  element is the least; fewer is 9.
- **Too big is 7 and leaves the array as it was**, unlike a plain ReDim, which erases it (*ReDim*, #849).
- **A Variant is preserved in place and keeps its type**, but a Variant holding no array is 13, where a plain ReDim gives it
  one.
- **An `As` clause may only repeat the type.** On a declared array anything else does not compile; on a Variant it is 13.
- **The error order** is a plain ReDim's: the bounds are converted (13), then a fixed or locked array is refused (10), then
  the shape is checked (9), then the size (7).

### In HexIDE (#1459)

ReDim Preserve takes ReDim's own path, so the bounds conversion and the fixed-array and lock checks come first, in that
order. Then `StatementExecutor.TryPreserve` refuses what VB6 refuses and calls `VBArray.Preserve`, which builds the resized
storage before it lets go of the old (so a 7 leaves the array as it was) and resizes in place, so a parameter sharing the
caller's slot sees it. The elements dropped by a shrink are released after the resize. An undimensioned dynamic array goes
on to a plain ReDim.

Run through HexIDE, the probes gave VB6's answer on every row above except the array of a Type, whose elements HexIDE
does not reach yet (#178). The `For Each` row is now pinned by #1460 (see its entry beside #1455). `RedimPreserveTests`
pins the rows its three programs cover, and the compile errors. One row is worth noting: `GVV(x As Variant)` gives VB6's answer although a plain ReDim through
a `v As Variant` parameter resizes only the parameter (#970), because Preserve resizes the shared array in place.

