## Indexed Property Get, Let and Set on a class (2026-11-16, #177)

**The question.** A class's `Property Get Item(ByVal i As Long)` with matching `Let` and `Set` is how a VB6 program
builds its own collection. #177 asked what such accessors return and store, and what VB6 says when the `Get`, `Let` and
`Set` don't match.

**The probe.** VB6 SP6, `/make`, native code and p-code, which logged the same lines. Class `CItem` keeps a
`Private m(5) As Variant` and has three accessors:
- `Property Get Item(ByVal i As Long) As Variant`, returning `m(i)` with `Set` when it holds an object;
- `Property Let Item(ByVal i As Long, ByVal v As Variant)`, storing `m(i) = v`;
- `Property Set Item(ByVal i As Long, ByVal v As Object)`, storing `Set m(i) = v`.

The mismatched classes were each built on their own.

| Code | vb6.exe |
|---|---|
| `Dim o As New CItem, o2 As New CItem`, `o.Item(1) = 42`, then read `o.Item(1)` | 42, `TypeName` Integer |
| `Set o.Item(2) = o2`, then `TypeName(o.Item(2))` and `o.Item(2) Is o2` | `CItem`, True |
| late bound: `Dim o As Object`, `Set o = New CItem`, `o.Item(3) = "late"`, then read `o.Item(3)` | "late" |

**Compile errors.** Each of these reports "Definitions of property procedures for the same property are inconsistent,
or property procedure has an optional parameter, a ParamArray, or an invalid Set final parameter":
- `Get Item(ByVal i As Long) As Long` with `Let Item(ByVal i As Long, ByVal v As String)`, a value type unlike the
  `Get`'s;
- `Get Item(ByVal i As Long)` with `Let Item(ByVal i As Integer, ByVal v As Long)`, an index of another type;
- `Get Item(ByVal i As Long, ByVal j As Long)` with `Let Item(ByVal i As Long, ByVal v As Long)`, a different number
  of indexes.

**Readings:**
- **An indexed `Let` or `Set` passes the indexes first and the assigned value last.** An indexed `Get` passes the
  indexes, early bound or late.
- **The accessors must agree.** The same index count and types, and a `Let` value of the `Get`'s type, or VB6 refuses
  the class at compile time.

**Not measured:** `Optional` and `ParamArray` indexes, and an indexed `Let` with no `Get`.
