## An array element passed ByRef (2026-10-22, #1455)

**The question.** HexIDE copied an array element given to a ByRef parameter, so `PL a(1)`, with `PL(x As Long)` storing 9,
left `a(1)` as it was, and `Swap s(0), s(1)` swapped nothing. Does VB6 pass the element itself, and what may the procedure do
to the array while it holds one?

**The probe.** Three Sub Main programs under `On Error Resume Next`, logging after each call. Made native and p-code under
en-GB; every line was identical. The three programs `ByRefElementTests` runs were then made the same way, and logged exactly
what the tests pin.

| Call | The procedure | VB6 | HexIDE before |
|---|---|---|---|
| `PL a(1)`, `a` a Long array, `a(1)` 7 | `PL(x As Long)`: `x = "9"` | Long 9 | Long 7 |
| `PV w(1)`, `w` a Variant array | `PV(x As Variant)`: `x = "v"` | String "v" | Integer 7 |
| `PV a(1)`, the Long array | the same | 13, and `a(1)` keeps 7 | Long 7, no error |
| `PV v(1)`, `v` a Variant holding `Array(1, 2, 3)` | the same | String "v" | Integer 2 |
| `PLI al(gi)`, `gi` 1 | `PLI(x As Long)`: `gi = 2`, then `x = 9` | `al(1)` 9, `al(2)` 0 | both 0 |
| `PL m(1, 2)`, a two-dimensional Long array | `PL` | 9 | 0 |
| `Swap s(0), s(1)`, holding 2 and 1 | `t = a: a = b: b = t` | 1 2 | 2 1 |
| `PL al(0 + 0)` | `PL` | 9 | 4 |
| `PL (al(0))` | `PL`, the argument in parentheses | 4 | 4 |
| `PL2 al(1)` | `PL2(y As Long)`: `PL y` | 9 | not probed |
| `PG g(1)`, `g` a module's Long array, `g(1)` 7 | `x = 9`, reads `g(1)`; `g(1) = 4`, reads `x` | reads 9, then `x` is 4; `g(1)` 4 | not probed |
| `PL ua(1).l`, `ua` an array of a Type | `PL` | 9 | not reached (#178) |
| `PO oc(1)`, `oc` an array of Collection | `PO(c As Collection)`: `Set c = New Collection` | Collection | not reached |

A parameter reads as the element does, as it reads a field (*A UDT field passed ByRef*): given `a(1)` of an Integer array
holding 5, `x * 7000` is 6 in `PI(x As Integer)` and 35000 in `PV(x As Variant)` and `PU(x)`; given `w(1)` of a Variant
array, 35000 in either.

**While the call runs, the array is locked.** `PRD d(1)`, with `d` a module-level dynamic Long array `ReDim d(2)`:

| Inside the procedure | VB6 |
|---|---|
| `ReDim d(5)` | 10, "This array is fixed or temporarily locked", and `UBound(d)` stays 2 |
| `ReDim Preserve d(5)` | 10, and `UBound(d)` stays 2 |
| `ReDim d("x")` or `ReDim Preserve d("x")` | 13: the bounds are converted first |
| `ReDim d(5 To 2)` or `ReDim Preserve d(5 To 2)` | 10, ahead of the inverted range's 9 |
| `Erase d` | 10 |
| `d = e`, `e` a Long array `ReDim e(3)` | 10, and `UBound(d)` stays 2 |
| `v = 5`, inside `PVV v(1)` with `v` a Variant holding `Array(1, 2, 3)` | 10, and `v` is still `Variant()` |
| a store through the parameter | lands: `d(1)` is 9 after the call |
| `ReDim d(4)` after the call returns | works: `UBound(d)` 4 |

**What it shows:**
- **VB6 passes the element itself,** the same in every shape it was given: one or two dimensions, a typed, Variant or object
  array, an array held in a Variant, a field of an element, and an element passed on through a second ByRef parameter.
- **The subscripts are evaluated once, at the call.** `PLI al(gi)` writes `al(1)` although the procedure has made `gi` 2.
- **A store through the parameter converts into the array's element type,** and one that won't, raises: `PV a(1)` storing
  "v" into the Long array is 13.
- **The array is locked until the call returns.** Anything that would replace or free its storage raises 10, and leaves it
  as it was.
- **Parentheses pass a copy,** as they do for a variable.

### In HexIDE (#1455)

An element argument carries its array and its subscripts, evaluated once at the call, and a ByRef parameter of the element's
type, or an untyped or Variant one, gets a slot that reads and writes the element for as long as the call runs, as #1451's
does for a field. The slot is declared as the element reads. The call locks the array (`VBArray.LockCount`), and ReDim,
ReDim Preserve, Erase, and a store into a variable holding the array (`ModuleExecutionContext.TryUpdateVariable`) raise 10
while it is locked. ReDim Preserve (#1459) checks the lock before it resizes anything, so none can resize an array whose
element a parameter is writing. Every row above that HexIDE reaches is pinned in `ByRefElementTests`. Not reached: an array of a Type
(#178), and an array of Collection, whose declaration HexIDE refuses.

