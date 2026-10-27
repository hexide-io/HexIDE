## A UDT copied whole, and arrays of a UDT (2026-10-26, #178)

**The question.** HexIDE stored `arr(i) = u` without copying `u`, so once arrays of a UDT work, the element and the
variable would share one value. What does VB6 copy when a whole UDT is assigned to an array element, from one, or
between two, and what happens to an array of a UDT under `ReDim Preserve`, `Erase` and a ByRef call?

**The probe.** One Sub Main in a standard module, built with `/make` as native code and as p-code. The two builds logged
the same lines. The Type is:

```vb
Private Type Inner
    t As String
End Type

Private Type T
    s As String
    fx(1 To 3) As Long
    dy() As Long
    n As Long
    sub1 As Inner
End Type

Dim arr() As T              ' ReDim arr(0 To 3) at the start
Dim fixedArr(1 To 3) As T
```

`Fill x, tag, k` sets `s` to the tag, `fx` to k, k+1, k+2, `dy` to `ReDim (0 To 1)` holding k×10 and k×10+1, `n` to k
and `sub1.t` to the tag plus `-in`. Each row below logs every field.

### A whole-UDT assignment is a deep copy, every way round

| Program | Logged |
|---|---|
| `Fill u, "u", 1`, `arr(1) = u`, then change `u.s`, `u.fx(1)`, `u.dy(0)`, `u.n` and `u.sub1.t` | `arr(1)` keeps `u`, 1,2,3, 10,11, 1, `u-in` |
| then `ReDim u.dy(0 To 4)`, and then `Erase u.dy` | `arr(1).dy` is still `(0 To 1)` holding 10,11 |
| `w = arr(1)`, then change every field of `arr(1)` | `w` keeps the old values |
| `ReDim Preserve arr(1).dy(0 To 2)` and `arr(1).dy(2) = 2002` | `arr(1).dy` is 10,2001,2002; `w.dy` still 10,11 |
| `arr(2) = arr(1)`, then `arr(1).s = "a3"` and `arr(1).dy(0) = 3000` | `arr(2)` keeps `a2` and 10 |
| `arr(0) = MakeT("made")`, a Function returning T | `arr(0)` holds the function's value |
| `arr(1) = w`, `ReDim Preserve arr(0 To 3)`, then `w.s = "w-changed"` | `arr(1).s` is still `w` |
| A module-level `gU = u`, then `u.dy(1) = 11111` | `gU.dy(1)` is still 111 |

The String, the fixed array, the dynamic array and the nested Type are all copied. No field is shared between the two
sides afterwards, the dynamic array included.

### A new element, ReDim Preserve, ReDim and Erase

| Program | Logged |
|---|---|
| `arr(3)`, never assigned | `s` empty with `StrPtr` 0, `fx` 0,0,0, `dy` unallocated (`UBound` raises 9), `n` 0, `sub1.t` empty |
| `ReDim Preserve arr(0 To 5)` | `arr(0)` to `arr(2)` keep every field, `dy` included; `arr(3)` to `arr(5)` are new elements as above |
| `ReDim Preserve arr(0 To 1)`, shrinking | `arr(0)` and `arr(1)` keep every field; `UBound(arr)` 1 |
| `Fill arr(1), …`, then `ReDim arr(0 To 2)` with no Preserve | `arr(1)` is a new element |
| `Erase arr` on the dynamic array | `UBound(arr)` raises 9 |
| `fixedArr(2) = u`, then change `u` | `fixedArr(2)` keeps its copy; `fixedArr(1)` is a new element |
| `Erase fixedArr` on the fixed array | every element is a new element again; the bounds stay 1 To 3 |

### An element passed ByRef

`ModByRef x As T` sets each field of `x` and logs `arr(1)` from inside.

| Program | Logged |
|---|---|
| `arr(1) = u`, then `ModByRef arr(1)` | inside, `arr(1)` already shows the new values; after, `arr(1)` keeps them; `u` is unchanged |
| `ReDimField arr(1)`, which does `ReDim x.dy(0 To 3)` and `x.dy(3) = 333` | `arr(1).dy` is `(0 To 3)` holding 0,0,0,333 |
| Inside a call given `arr(1)` ByRef: `ReDim arr(0 To 1)`, `ReDim Preserve arr(0 To 9)`, `Erase arr` | each is error 10, "This array is fixed or temporarily locked", and `arr` is left as it was |
| `ModByRef u`, a plain variable | `u` takes the new values; `arr(1)` is unchanged |

So an element passed ByRef is the element itself, as for any other element type ([An array element passed
ByRef](an-array-element-passed-byref.md)), and the array is locked for the call.

### What doesn't compile

Each is a compile error from `/make`, the same in both builds:

| Code | VB6 |
|---|---|
| `Private Sub BV(ByVal x As T)` | "User-defined type may not be passed ByVal" |
| `For Each v In a`, with `Dim a(1) As T` and `v As Variant` | "For Each may not be used on array of user-defined type or fixed-length strings" |
| `v = u`, with `v As Variant` and `T` a Private Type in a standard module | "Only user-defined types defined in public object modules can be coerced to or from a variant or passed to late-bound functions" |

So passing an element ByVal can't arise: a UDT parameter is always ByRef.

### What HexIDE did before #178

`Dim arr(3) As T` was accepted and the array held the wrong thing, failing at the first element access. The
whole-element store, `arr(1) = u`, puts the value into the array without copying it, so once arrays of a UDT work,
`arr(1)` and `u` would be one value, where VB6 keeps two. Fixing both is #178.

### What HexIDE does now

`UdtArrayTests` exercises the measured allocation, copy, resize, reset, ByRef and For Each results.
Array fields use [the measured array-variable store rule](a-let-into-an-array-variable.md), including
String-to-Byte() conversion. Fixed-length strings, nonliteral or inverted Type-field bounds, and array
fields declared As New retain their refusal. This change does not alter the pre-existing acceptance of
ByVal UDT parameters or UDT-to-Variant assignments; those compile-time restrictions remain divergences.

Get/Put of UDT arrays and of Types containing array fields remain explicitly refused: their record-file
layout is not established by these copy probes. Four Linux-runnable guard cases in `UdtArrayTests` keep
that serialization boundary from disappearing as runtime allocation is enabled.
