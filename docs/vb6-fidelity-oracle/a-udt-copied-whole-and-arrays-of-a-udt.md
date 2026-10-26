## A UDT copied whole, and arrays of a UDT (#178)

**Provenance.** The later measurement comment on hexide-io/HexIDE#178 reports vb6.exe SP6 `/make`
probes, built both native and p-code, with identical results on every line. The comment identifies
its full tables as the section of this name on branch `docs/oracle-2172-2181-1631`. This entry records
only the findings supplied in that comment; it does not claim a new measurement or reconstruct the
unavailable tables.

The probe's Type contained a String, `fx(1 To 3) As Long`, `dy() As Long`, a Long, and a nested Type
containing a String.

- `arr(1) = u` copies every field independently. Subsequent writes to the source's scalar, fixed-array,
  dynamic-array and nested fields, including ReDim and Erase of its dynamic array, leave the element alone.
- `w = arr(1)` and `arr(2) = arr(1)` copy the same way. Changing the element, including ReDim Preserve of
  its dynamic-array field, leaves the copies alone. `arr(0) = MakeT()`, returning the Type, and module-level
  `gU = u` also copy independently.
- A fresh element has empty Strings (the outer String's StrPtr is 0), zero fixed-array elements, an
  unallocated dynamic array (UBound raises 9), and an empty nested String.
- Growing `arr(0 To 3)` with `ReDim Preserve arr(0 To 5)` retains all existing fields and gives new elements
  those defaults. Shrinking to `0 To 1` keeps the surviving elements. ReDim without Preserve resets all
  elements; Erase resets a fixed array and deallocates a dynamic array.
- `ModByRef arr(1)` passes the element itself. Field writes, including `ReDim x.dy`, show through during
  and after the call. ReDim, ReDim Preserve and Erase of the containing array inside the call each raise
  10, "This array is fixed or temporarily locked", leaving the array unchanged.

Three forms did not compile in either build:

| Form | Compiler message |
|---|---|
| `Sub BV(ByVal x As T)` | User-defined type may not be passed ByVal |
| `For Each v In a` with an array of the Type | For Each may not be used on array of user-defined type or fixed-length strings |
| `v = u` into a Variant, with a Private Type in a standard module | Only user-defined types defined in public object modules can be coerced to or from a variant or passed to late-bound functions |

### In HexIDE

`UdtArrayTests` exercises the measured allocation, copy, resize, reset, ByRef and For Each results.
Array fields use [the measured array-variable store rule](a-let-into-an-array-variable.md), including
String-to-Byte() conversion. Fixed-length strings, nonliteral or inverted Type-field bounds, and array
fields declared As New retain their refusal. This change does not alter the pre-existing acceptance of
ByVal UDT parameters or UDT-to-Variant assignments; those compile-time restrictions remain divergences.

Get/Put of UDT arrays and of Types containing array fields remain explicitly refused: their record-file
layout is not established by these copy probes. Four Linux-runnable guard cases in `UdtArrayTests` keep
that serialization boundary from disappearing as runtime allocation is enabled.
