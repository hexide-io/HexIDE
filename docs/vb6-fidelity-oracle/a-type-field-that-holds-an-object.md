## A Type field that holds an object (2026-11-14, #3924)

**The question.** HexIDE refuses a `Type` field declared `As Object`, `As Collection` or as one of the project's classes
(#3924). What does vb6.exe do with one: how it starts, `Set` into it, copying the UDT that holds it, a `Set` of the wrong
class, and a `Let` without `Set`?

**The probe.** VB6 SP6, `/make`, native code and p-code, which logged the same lines. Two classes, `Leaf` and `Other`,
each with `Public Value As Long`. A standard module declares:
- `Private Type InnerO` with `Obj As Object`;
- `Private Type T2` with `C As Collection`;
- `Private Type T3` with `L As Leaf`;
- and `Dim u As InnerO`.

`NewLeaf(n)` returns a new `Leaf` whose `Value` is `n`. Each row is a function, with `On Error Resume Next` where it
reads `Err`.

| Row | Code | vb6.exe |
|---|---|---|
| E1 | `Set u.Obj = NewLeaf(7)`, then `u.Obj.Value` | `7` |
| E2 | a fresh `InnerO`, `T3` and `T2`: `x.Obj Is Nothing`, `y.L Is Nothing`, `z.C Is Nothing` | `True`, `True`, `True` |
| E3 | `Set y.C = New Collection`, `y.C.Add 5`, then `y.C.Count` and `TypeName(y.C)` | `1`, `Collection` |
| E4 | `Set a.L = NewLeaf(1)`, `b = a`, `a.L.Value = 2`, then `b.L.Value`; then `Set a.L = NewLeaf(3)`, and `b.L.Value`, `a.L.Value`, `a.L Is b.L` | `2`; then `2`, `3`, `False` |
| E5 | `Set o = New Other`, with `o As Object`, then `Set t.L = o` for a fresh `T3` | err 13, and `t.L Is Nothing` stays `True` |
| E6 | `t.L.Value = 4` for a fresh `T3` | err 91 |
| E7 | `x.Obj = NewLeaf(9)`, a `Let` without `Set`, for a fresh `InnerO` | err 91, and `x.Obj Is Nothing` stays `True` |

A class field two UDTs deep is rows B3 and B4 of
[Chained writes](chained-writes-qualified-set-and-udt-returning-members.md) (#173).

**Readings:**
- **A field of an object type is an object slot,** as a variable of that type is. It's `Nothing` until `Set`, and member
  access reads through it. That holds for `Object`, `Collection` and a class.
- **Copying the UDT copies the reference, not the object.** After `b = a`, a change made through `a.L` shows through
  `b.L`. A later `Set a.L` leaves `b.L` on the old object.
- **A field typed as a class checks its class at run time.** An `Other` reaching it through an `Object` variable is 13,
  and the field keeps what it held.
- **A `Let` into an unset object field is 91,** and leaves the field `Nothing`.

**Not measured:** when `Class_Terminate` runs for an object a field holds; an array field of an object type; a
`Let` into a field that holds an object with a default member.

### Fields of other object types, and Set through an indexed Type, an array field or a Variant field (2026-11-20, #3924)

vb6.exe SP6, `/make`, built as native code and as p-code, which logged the same lines in all 22 run-time cases and 6
compile-time ones. **The setup:** each case was a Function under `On Error Resume Next`, logging the error number and
what it read back. The form held `Text1` and `Text2`, and the project's class `Leaf` has `Public Value As Long`. Each
compile-time case was a project of its own.

**A. A field typed as another object type** (`f As StdFont`, `t As TextBox`, `c As Control`, `fm As Form`, `p As StdPicture`) behaves as a variable of that type:
- **Each starts as Nothing.** `Set` takes an object of the type:
  - `Set u.f = New StdFont`, then `u.f.Size = 12`, reads back 12, with `TypeName` `Font`.
  - `Set u.t = Form1.Text1` works, and `u.t.Text = "x"` sets Text1's Text.
  - `Set u.c = Form1.Text2` gives `TypeName` `TextBox`.
  - `Set u.fm = Form1` makes `u.fm Is Form1` True, with `TypeName` `Form1`.
  - `Set u.p = LoadPicture()` gives `TypeName` `Picture`. `Set u.p = Form1.Icon` gives `.Type` 3.
- **Any other type is 13, and the field keeps its object.**
  - `StdFont`: a `Collection`, or `Form1`.
  - `TextBox`: `Form1`, or `New StdFont`.
  - `Control`: `Form1`, or `New StdFont`.
  - `Form`: `Form1.Text1`.
  - `StdPicture`: a `Collection`.
- **`Set … = Nothing`** clears it.
- **Copying the record shares the object:** `w = u` gives `w.f Is u.f` True, and `w.f.Size = 20` shows in `u.f.Size`, as 20.25.
- **A Let without `Set` assigns the default member:**
  - `u.t = "y"` sets Text1's Text to "y".
  - `u.f = "Arial"` on a held font sets its Name to "Arial".
  - Either one while the field holds Nothing is **91**.
- **`Put` of a Type holding a `StdFont` field** is the compile error "Can't Get or Put an object reference variable or a variable of user-defined type containing an object reference", as for any object field.
- **A field of an undeclared type** (`f As Foo`) is "User-defined type not defined".
- **`ADODB` types weren't probed:** the probe had no reference to ADO.

**B. `Set` on a field of an element of a Type array.** Here `T` is `Obj As Object`, and `Outer` is `In1 As T`:
- `Dim ua(1 To 2) As T`, then `Set ua(1).Obj = New Collection`, works:
  - `TypeName(ua(1).Obj)` is `Collection`, and `ua(2).Obj` is Nothing;
  - `ua(1).Obj.Add 5` gives `Count` 1.
- `Set ua(1).Obj = New Leaf` works, with `ua(1).Obj.Value = 4` reading back 4. Then `Set ua(1).Obj = Nothing` gives Nothing.
- Nested, `Dim ub(1 To 2) As Outer` and then `Set ub(2).In1.Obj = New Leaf` works. `ub(1).In1.Obj` stays Nothing.
- Dynamic, `ReDim ud(1)` and then `Set ud(0).Obj = New Collection` works. `ReDim Preserve ud(2)` keeps it, and `ud(2).Obj` is Nothing.
- `Set ua(3).Obj = …`, out of range, is **9**.

**C. `Set` into an element of an array field** (`a(1 To 2) As Leaf`, `c(1) As Collection`, `v(1) As Variant`, `o(1) As Object`, `w As Variant`):

| Statement | vb6.exe |
|---|---|
| `Set u.a(1) = New Leaf`, then `u.a(1).Value = 3` | works: 3, `TypeName` `Leaf`. `u.a(1)` was Nothing before |
| `Set u.a(2) = New Collection` | **13** |
| `Set u.c(0) = New Collection` | works: `Collection` |
| `Set u.c(1) = New Leaf` | **13** |
| `Set u.v(0) = New Collection` | works: `Collection`. A later `u.v(1) = 5` holds an Integer |
| `Set u.o(0) = New Leaf` | works: `Leaf` |
| `Set u.o(2) = …`, out of range | **9** |
| `u.w = Array(1, 2)`, then `Set u.w(1) = New Collection` | works: the element holds the Collection |
| `Set u.w(1) = New Collection` while `w` is Empty | **13** |

**D. `Set` on a field that isn't an object field:**
- `Set u.w = New Collection`, with `w As Variant`, works: the Variant holds the Collection. `Set u.w = Nothing` then gives Nothing.
- `Set` on an `Integer`, `String` or nested-Type field, or on an element of an `Integer` array field, is the compile error **"Object required"**.

### In HexIDE (2026-11-20, #3924)

On `upstream/main` `d1fb96f26`, every case in this section refused, citing #3924, except that a Type with an object
field already gave the "Can't Get or Put…" compile error. The work is briefed on #3924.
