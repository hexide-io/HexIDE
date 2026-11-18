## Object array fields in a Type (2026-11-18, #4596, SP6)

**The question.** A `Type` may hold an array whose elements are objects: `o(1 To 2) As Object`, `c() As Collection`,
`k(1 To 2) As Class1`. What does each element start as, what may be `Set` into it, what do `Erase` and copying the Type
do, and when do its objects terminate?

**The probe.** One project per row, each a `Sub Main` in `Module1` that writes its results to a file, under
`On Error Resume Next`. The rows from TO5 on share one Type, `k(1 To 2) As Class1`, `o(1 To 2) As Object` and
`c() As Collection`. `Class1` holds `Public V As Long`, and its `Class_Terminate` writes `term` and its `V` to the same
file, so the file shows when each instance ends. Every project was built with `VB6.EXE /make` as native code and as
p-code, on the Win10 guest. **Native and p-code agreed on every row.**

| Row | Statements, on `Dim x As T` | Result |
|---|---|---|
| TO1 | read `x.o(1) Is Nothing`; `Set x.o(1) = New Collection`; `x.o(1).Add 5` | True; then `Count` 1, `TypeName` Collection, no error |
| TO2 | `ReDim x.c(1)`; read `x.c(0) Is Nothing`; `Set x.c(1) = New Collection` | True; then `Count` 0, `UBound` 1 |
| TO3 | read `x.k(1) Is Nothing`; `Set x.k(1) = New Class1`; `x.k(1).V = 3` | True; then `V` 3, `TypeName` Class1 |
| TO4 | `Set x.o(1) = New Collection`; `y = x`; `y.o(1).Add 9`; `Set y.o(1) = Nothing` | `x.o(1).Count` 1, `y.o(1) Is Nothing` True |
| TO5 | `Set x.k(1) = New Collection` | **error 13**, and `x.k(1)` stays Nothing |
| TO9 | `Dim o As Object: Set o = New Collection`, then `Set x.k(1) = o` | error 13, and `x.k(1)` stays Nothing |
| TO6 | `Set x.k(1) = New Class1` with `V` 1, `Set x.o(2) = New Collection`, then `Erase x.k`, `Erase x.o` | `term 1` at the `Erase`; both elements Nothing after; `UBound(x.k)` still 2 |
| TO7 | `ReDim x.c(2)`, `Set x.c(0) = New Collection`, `Erase x.c`, then `UBound(x.c)` | error 9 |
| TO8 | see below | see below |

**TO8, when the objects end.** `Sub Inner` declares a local `Dim x As T`, sets `x.k(1)` to a new `Class1` with `V` 1,
writes `leaving Inner` and returns. Then `Main` sets `y.k(2)` to a new `Class1` with `V` 2, copies `z = y`, and clears
first `y.k(2)` and then `z.k(2)`. The file read, in order:

```
leaving Inner
term 1
after Inner
y cleared
term 2
z cleared
```

**What it shows:**
- **Each element is an object variable of the declared type.** It starts as Nothing, takes `Set`, and refuses an object
  of another class with error 13, whether the object came from `New` or through an `Object` variable.
- **Copying the Type copies the array and shares the objects,** as copying object variables does. In TO4 the copy's
  `Add` shows through the original, but clearing the copy's element leaves the original's.
- **`Erase` on a fixed-size object array sets each element to Nothing and keeps the bounds.** On a dynamic one it frees
  the array, so `UBound` is error 9.
- **An object terminates when its last reference goes:** as the local Type goes out of scope at `End Sub`, at `Erase`,
  and when the last of two copies is cleared, not the first.

### In HexIDE

At `86dcc6c50`, in `HexIDE.Runtime.Tests` probes, every row stops before its first statement. Allocating the Type
refuses: "Array field 'o' has an unsupported element type (#178)" (`BasicInterpreter.NewUdt`, #4596). A `Set` through
a Type field reached with arguments is refused separately, as "Set through this Type field is not implemented (#3924)".
