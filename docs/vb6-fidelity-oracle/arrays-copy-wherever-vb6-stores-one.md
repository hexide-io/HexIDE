## Arrays copy wherever VB6 stores one (2026-10-14, #970)

**The question.** #873's review found that HexIDE's array Let shares the array where VB6 copies it, and #873's fifth
round proved out a copy, then took it back out because it broke four things (round six, above). This measures the rest
of where VB6 copies, and what a copy does to the objects it holds.

**The probe.** Three projects, each built as native code and as p-code and run in the IDE, the three agreeing on every
row. A module with `Dim modArr(3) As Long` holding 7 at `modArr(1)`, under `On Error Resume Next`:

| Program | VB6 | HexIDE before #970 |
|---|---|---|
| `d = RetFixed()`, a Function `As Long()` returning `modArr`, then `d(1) = 99` | `modArr(1)` 7, `d(1)` 99 | "Expected array": the Function returned a Long (#975, since fixed) |
| `v = RetVariant()`, an untyped Function returning it, `v(1) = 98` | `modArr(1)` 7 | 98 |
| `Sub ByValV(ByVal v)`: `v(1) = 55`, given `modArr` | `modArr(1)` 7 | 55 |
| `Sub ByRefV(v As Variant)`: `v(1) = 66` | 66 | 66 |
| `outer(0) = inner`, then `inner(0) = 50` | `outer(0)(0)` 1 | 50 |
| `outer(0)(0) = 40` | `inner(0)` 50, `outer(0)(0)` 40 | refused: a store through two subscripts is not implemented (read from the code) |
| the row buffer: `For i = 0 To 2: row(0) = i: rows(i) = row: Next` | `0\|1\|2` | `2\|2\|2` |
| `For Each e In nest`, `e(0) = 99`, with `nest(0)` `Array(1, 2)` and `nest(1)` `Array(3, 4)` | `nest(0)(0)` 1, `nest(1)(0)` 3 | 99 |
| `v = Array(1, 2)`, `v(1) = v`, `w = v` | no error; `UBound(w(1))` 1, `w(1)(1)` 2 | `w(1)` was `v` itself: the element Let stored the array, not a copy |
| `d = fx`, `fx` a `Dim fx(3) As Long` holding 7, then `Erase d` | `fx(1)` 7, `fx` 0 To 3; `UBound(d)` 9 | `fx(1)` 0: the shared fixed array was reset |
| `Sub SB(b() As Long)`: `b = gO`, a dynamic 0 To 5, given a fixed `f3(3)` holding 9 | 13; `f3` 0 To 3, `f3(1)` 9 | no error; `f3` became 0 To 5 |
| `t = a`, `t` a fixed `Dim t(3) As Long` | does not compile: "Can't assign to array" | ran |
| `w = v`, both Variants, `w(0) = 77` | `v(0)` 1 | 77 |

**Objects in a copied array.** A class `Foo` whose `Class_Terminate` logs its `Name`, each scope a Sub of its own:
- **After `b = a`:** `b(0) Is a(0)` is True, so a copy shares its objects. `Set b(0) = New Foo` terminates nothing, and A
  and B both terminate at `End Sub`, A first.
- **After `v = a`:** `Set a(0) = Nothing` terminates nothing, and C terminates at `End Sub`.
- **Through `Sub Hold(ByVal v)`:** `Set v(0) = Nothing` terminates nothing, and D terminates at the end of the caller.
- **HexIDE before #970** terminated A as soon as `b(0)` was set, because there was only one array; and a local object
  array's objects were never terminated at all, which `interpreter-gaps.md` recorded as a divergence.

**What it says.** VB6 copies an array wherever it stores one: a Let into a dynamic array, a Variant, a Function's own
name, an element, a ByVal parameter and a For Each variable. A ByRef parameter, `As Variant` included, shares the
caller's array instead. The copy shares the objects the original holds, and each holder keeps them alive. With the rows
#873's rounds measured (the copy is dynamic and can be ReDimmed; it copies every dimension; `GA(v As Variant)` reaches
the caller's array and its fixedness; an untyped parameter ReDims by the Variant rule), that is the whole of #970.

**HexIDE, after #970.** Every row above matches, except the store through two subscripts, which is still refused.
- **The copy.** `BasicInterpreter.CopyForStore` copies an array level by level, each element as a store copies it, and
  makes the copy dynamic. It's applied at a Let, a field write, an element Let, a ByVal argument and a For Each
  variable; a Function's return is a Let into its name. An element Let copying is also what keeps `v(1) = v` from
  making an array hold itself; a copy nested more than 1000 deep is refused rather than risk the stack.
- **The objects.** The copy AddRefs each object it holds, so every array has exactly one holder, and `ReleaseRef`
  descends into an array. A local object array's objects now terminate at `End Sub`, an overwritten element releases
  what it held, a For Each variable holds its element and is emptied when the loop runs out, and an array a Function
  returns is released at the end of the caller's statement, after whatever stored it has taken its copy. At `End Sub`
  the scalar locals go before the arrays, as below.
- **Where a reference is left uncounted**, the error is towards a late Terminate, never an early one: `Erase` and a
  `ReDim` without `Preserve` don't release the objects in the array they discard, and a module-level array's objects
  wait for the end of the program, as before.
- **A ByRef `v As Variant` given an array** shares the caller's slot, as an untyped `v` does. Given a typed scalar it
  still gets a copy, because that case is not measured.
- **An untyped or Variant parameter's ReDim** takes the Variant rule, read from the parameter rather than the shared
  slot, whose type and fixedness stay the caller's.
- **A fixed array given an array** is "Can't assign to array" named directly, raised as the statement runs, and 13
  through a parameter, with the array kept.

### Measured in #1000's review

Four more projects, each built as native code and as p-code, the two agreeing on every line. A class `Foo` whose
`Class_Terminate` logs its `Name`, each scope a Sub of its own:

| Program | VB6 |
|---|---|
| `x = MakeArr("M1")`, a Function returning a local `Dim a(0) As Foo` holding M1, then `x(0).Name` | "M1", then M1 terminates at `End Sub` |
| `MakeArr "M2"`, called as a statement | M2 terminates before the next statement |
| `For Each o In a` over F0 and F1, then `TypeName(o)` | "Empty": the variable lets go when the loop runs out |
| then `Set a(0) = Nothing`; `Set a(1) = Nothing` | F0 terminates at the first, F1 at the second |
| the same loop left by `Exit For` at X0 | `o` is still X0, so `Set a(0) = Nothing` terminates nothing |
| `For Each e In Array(1, 2)`; over a `Long()`; over an empty `Split("")` with `e` holding 9 | `TypeName(e)` "Empty" after each |
| `Set v(0) = New Foo`, `v(0) = 5`, `Dim v(0)`; `Set w = New Foo`, `w = 5`, `Dim w` | the object terminates at the Let, Err 0, and `TypeName` is "Integer": with no default member, the Let replaces it |

**The order of `End Sub`.** Two objects in each scope, the first set first:

| Locals | VB6 terminates |
|---|---|
| `Dim a(0) As Foo, o`; `Dim o, a(0) As Foo` | `o`'s, then the array's, both ways round |
| `Dim p As Foo, a(0) As Foo`; `Dim a(0) As Foo, p As Foo` | `p`'s, then the array's, both ways round |
| `Dim p As Foo, q As Foo`; `Dim a(0) As Foo, b(0) As Foo` | in declaration order |

So a procedure releases its scalar locals, Variants and object references alike, before its arrays, each group in
declaration order. Not run: a Variant local holding an array, which HexIDE puts with the scalars, by its slot.

**A ByRef `As Variant` given a scalar is written back.** `Sub S(v As Variant): v = 5` gives 5 to a caller's Variant
holding 1, and to a caller's `Dim n As Integer` too, which compiles; `Sub U(v): v = 6` gives 6 and leaves `n` an
Integer. HexIDE copied the scalar into a `v As Variant`, so the caller kept 1: #1001.

**And the parameter is still a Variant** (#1001, measured native and p-code): given a `Dim n As Integer` holding 5,
`v * 7000` is a Long 35000 in `Sub S(v As Variant)` and in `Sub U(v)` alike, `Select Case v` takes `Case Is < 5.4`, and
`TypeName(v)` is "Integer". So sharing the caller's typed variable does not make the parameter typed.

**HexIDE, after #1001.** A ByRef `v As Variant` shares its argument's slot whatever the argument holds, as an untyped
`v` does, and a read of an untyped or Variant parameter is never typed, though the slot it shares may be. Before,
`U(v)` given a typed Integer read as typed, so `v * 7000` overflowed where VB6 widens; that was older than #970, and
the copy into `S(v As Variant)` had hidden it there. Every row is pinned.

**A constant passed ByRef goes by value** (#1002's review, measured native and p-code). A module `Const MK = 1`, a local
`Const LK = 1` and an Enum member `Red = 1`, each passed to `Sub S(v As Variant): v = 5`, are still 1 afterwards, and
nothing is raised; so is a `Const MKI As Integer = 1` passed to `Sub X(x As Integer): x = 7`, and `MK` passed to an
untyped `v`. VB6 passes a constant through a temporary. HexIDE aliased its slot, so the write overwrote a Const (main
did it already for an untyped or matching typed parameter, and #1001 would have opened `As Variant` to it), and an
Enum member, being read-only, raised "Assignment to constant not permitted". Now an argument naming a Const or an Enum
member has no location to share. A store into a Const named directly, `MK = 2`, is VB6's compile error "Assignment to
constant not permitted" (measured), and is #1003.

**HexIDE, after the review.** Every row above is pinned. A For Each that runs out empties its variable and releases
what it held; `End Sub` releases scalar locals before arrays. The ByRef `As Variant` scalar is #1001's, apart from #970.

