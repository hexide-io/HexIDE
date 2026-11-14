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
