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
fields declared As New retain their refusal. [#4673](https://github.com/hexide-io/HexIDE/issues/4673) now refuses
ByVal UDT parameters when a call binds them; vb6.exe refuses the build, so a procedure never called is not caught. UDT-to-Variant assignments
remain a divergence from the measured compile-time restriction.

Get/Put of UDT arrays and of Types containing array fields remain explicitly refused: their record-file
layout is not established by these copy probes. Four Linux-runnable guard cases in `UdtArrayTests` keep
that serialization boundary from disappearing as runtime allocation is enabled.

### Get and Put of Type arrays and array fields, As New and object array fields, and arguments on a field (2026-11-19, #4925)

vb6.exe SP6, `/make`, every row built as native code and as p-code, which logged the same lines in every row. **The
setup:** a Sub Main with a form holding two TextBoxes, every row under `On Error Resume Next`, logging the error number
and description, the file's bytes read back, and the values a `Get` left. Each file row wrote at position 1 of a fresh
file. The compile-time rows were each a project of their own. This settles what the section above left open: the
record-file layout of arrays of a Type and of Types holding arrays.

- **The probe's Types**, all `Private` in one module:
  - `T1` is `i As Integer`, `s As String * 2`.
  - `T2` is `i As Integer`, `s As String`.
  - `T3` is `v As Variant`.
  - `U1` is `i As Integer`, `a(1) As Integer`, and `U2` is `i As Integer`, `a() As Integer`.
  - `U3` is `a(1) As String`, `U4` is `a(1) As Variant`, `U5` is `a(1) As T1`, and `U6` is `a(1 To 2, 0 To 1) As Byte`.
  - `U7` is `a() As String`, and `U8` is `a(1) As Long`, `s As String`.
- **The values:** in `T1` and `T2` arrays, element 0 is `i = 1`, `s = "ab"`, and element 1 is `i = 2`, `s = "cd"` (`T1`) or `""` (`T2`).
- **Bytes** are hex, and each row writes at position 1.

**Case 1: `Put` of an array of a Type.** The elements are written in order (the first index fastest), each packed as a lone Type is written now:

| Array, file | Bytes |
|---|---|
| `T1` `(1)`, static or dynamic, Binary | `01 00 61 62 02 00 63 64` |
| `T1` `(1)` static, Random `Len = 32` or `Len = 8` | the same 8 bytes |
| `T1` `(1)` **dynamic**, Random `Len = 32` | `01 00`, `02 00 00 00`, `00 00 00 00`, then the same 8 bytes: the descriptor every dynamic array gets in a Random file |
| `T1` static, Random `Len = 4`; `T1` dynamic, `Len = 8`; `T1` dynamic `(0 To 1, 1 To 2)`, `Len = 32` | **59**, and nothing written |
| `T1` `(0 To 1, 1 To 2)` static, Binary, `i` 1, 2, 3, 4 at `(0,1)`, `(1,1)`, `(0,2)`, `(1,2)`, `s` unset | `01 00 00 00 02 00 00 00 03 00 00 00 04 00 00 00` |
| `T2` `(1)` static, Binary or Random 32 | `01 00 02 00 61 62 02 00 00 00`: the String with its 2-byte length |
| `T2` `(1)` dynamic, Random 32 | the descriptor, then those 10 bytes |
| `T3` `(1)`, `v` `CInt(5)` then `"x"`, Binary or Random 32 | `02 00 05 00 08 00 01 00 78`: each Variant as a Variant is written now |
| `T1` dynamic, never allocated | Binary: nothing written. Random 32: `00 00` |

**Case 1, `Get` of an array of a Type**, each reading back what the row above wrote:
- **Binary, into a static `(1)`:** both elements as written.
- **Binary, into a dynamic array allocated `(2)`:** it keeps `(0 To 2)`. Elements 0 and 1 come back, and element 2 reads past the end as zeros (`0`, two NULs). `EOF` is True.
- **Binary, into a dynamic array never allocated:** no error. It reads nothing and stays unallocated (`LBound` is 9), with `EOF` False.
- **Binary, 3 bytes `01 00 61`, into a static `(1)` holding 9 and "zz":** element 0 reads `1`, "a" then NUL, and element 1 `0` and two NULs. Reading past the end gives zeros, not the old values.
- **Random 32, static into static:** both elements.
- **`T2`, `T3`:** round-trip, the Variant field keeping `Integer` 5 and `String` "x".

**Case 3: a Random `Get` into a dynamic array of a Type** takes its bounds from the descriptor, as a dynamic array of anything else does:
- **Never allocated, or allocated `(4)`:** written from a dynamic `(1)`, it becomes `(0 To 1)`, holding both elements.
- **A `T2` array** behaves the same.
- **Two dimensions:** a 2-D dynamic `T1` array can't be written at `Len = 32` (59), so there's no row for reading one.
- **A static target:** given the dynamic record, a static `(1)` reads the descriptor bytes as data. Element 0 reads `i` 1 and `s` = `{2}{0}`; element 1 reads `0` and two NULs.
- **A record written from a static array has no descriptor.** Read into a never-allocated dynamic array, it reads the data as one: dimensions `01 00`, count `61 62 02 00`, lower bound `63 64` then the end. The array is left allocated as `(25699 To 181955)`, and **then 59** is raised, since the elements can't fit.

**Case 2: a Type holding an array field.** The field is written in place, its elements in order (the first index fastest), each as a field of its type is now. **A dynamic array field is preceded by its descriptor, in Binary as well as Random files.**

| Type, Binary and Random `Len = 32` alike | Bytes |
|---|---|
| `U1`, `i = 1`, `a` 2, 3 | `01 00 02 00 03 00` |
| `U2`, `i = 1`, `a` `ReDim (1)` 2, 3 | `01 00`, then `01 00` `02 00 00 00` `00 00 00 00`, then `02 00 03 00` |
| `U2`, `i = 1`, `a` never allocated | `01 00 00 00`: the descriptor is a dimension count of 0 |
| `U3`, `a` "ab", "" | `02 00 61 62 00 00` |
| `U4`, `a` `CInt(5)`, "x" | `02 00 05 00 08 00 01 00 78` |
| `U5`, `a(0)` 1 "ab", `a(1)` 2 "cd" | `01 00 61 62 02 00 63 64` |
| `U6`, `a(1,0)` 1, `a(2,0)` 2, `a(1,1)` 3, `a(2,1)` 4 | `01 02 03 04` |
| `U7`, `a` `ReDim (1)` "ab", "c" | `01 00 02 00 00 00 00 00 00 00`, then `02 00 61 62 01 00 63` |
| `U7`, `a` never allocated | `00 00` |
| `U8`, `a` 1, 2, `s` "z" | `01 00 00 00 02 00 00 00 01 00 7A` |
| An array `(1)` of `U1`: `a(0).i` 1, `a(0).a(1)` 2, `a(1).i` 3, `a(1).a(0)` 4, Binary | `01 00 00 00 02 00 03 00 04 00 00 00` |
| An array `(1)` of `U2`: `a(0).i` 1 with `a` `ReDim (0)` 2, `a(1).i` 3 with `a` never allocated, Binary | `01 00 01 00 01 00 00 00 00 00 00 00 02 00`, `03 00 00 00` |
| `U1` at Random `Len = 4`; `U2` at `Len = 8` | **59** |

#179's measured row, `Type` `i As Integer`, `a(1) As String * 3`, with `i = 1`, `a(0) = "ab"`, is `01 00 61 62 20 00 00 00` in Binary.

**Case 2, `Get`:** every Type above round-trips in Binary and Random 32. For a dynamic field, **the descriptor sets the bounds.**
- `U2`'s `a`, never allocated or allocated `(3)` holding 7s, reads back `(0 To 1)` holding 2, 3.
- A record with `00 00` there leaves `a` unallocated, even if it was `ReDim (2)` before the `Get`.
- `U7` reads back `(0 To 1)` "ab", "c".

Binary, 3 bytes `01 00 02` into `U1` holding 9s, gives `1`, `2`, `0`: zeros past the end.

**Case 4: an inverted bound on a Type's array field** is VB6's compile error **"Range has no values"**. That holds for a literal bound (`a(5 To 1) As Integer`) and for one resolved from Consts (`a(L To H)`, with `L = 5` and `H = 1`), the same as for a plain `Dim x(5 To 1)`. `a(3 To 3)` is fine.

**Case 5: an array field `As New` compiles and runs as `As New` does.** The rows are for `a(1) As New Collection` and `a() As New Collection`. A scalar `c As New Collection` field was checked for two of them: it isn't Nothing, and a copy after use shares it.
- `u.a(0) Is Nothing` is False. `u.a(0).Add 7` works and gives `Count` 1, and `TypeName` is `Collection`. `u.a(1).Count` is 0.
- After `Set u.a(0) = Nothing`, `u.a(0) Is Nothing` is still False, and `Count` is 0: a new instance.
- **Copying the record shares instances already created.** `w = u` after `u.a(0).Add 7` gives `w.a(0) Is u.a(0)` True, and an `Add` on `w`'s shows in `u`'s.
- **An instance not yet created isn't shared.** `w = u` before any use gives `w.a(0) Is u.a(0)` False. A later `u.a(0).Add 1` leaves `w.a(0).Count` 0.
- **Dynamic:**
  - `ReDim u.a(1)` makes elements that aren't Nothing.
  - `ReDim Preserve u.a(2)` keeps element 1's contents, with element 2 new.
  - `Erase u.a` leaves it unallocated, so `LBound` is 9.

**Case 6: array fields of other object types.** `a(1) As StdFont`, `a() As StdFont`, `a(1) As TextBox` and `a(1) As Control` all compile in a `Private Type`:
- Each element starts as Nothing. `LBound` and `UBound` read `(0 To 1)`, or for a dynamic field never allocated, 9. **An oddity of the probe, recorded:** passed to a `Variant` parameter, that unallocated object array field reads `(0 To -1)` there, with no error, while an unallocated `Integer` array field passed the same way raises 9.
- **What `Set` takes:**
  - an element of the declared type, so `Set u.a(0) = Form1.Text1` and then `u.a(0).Text = "x"` sets the TextBox's Text;
  - `New StdFont`, and then `u.a(0).Size = 12` reads back 12, with `TypeName` `Font`;
  - for `As Control`, any control, so `TypeName` gives `TextBox`.
- **What `Set` refuses: a mismatched type is 13.** A `Collection` into `As StdFont`, `New StdFont` or `Form1` into `As TextBox`, and `Form1` or `New StdFont` into `As Control`.
- **Copying the record shares the objects:** `w = u` gives `w.a(0) Is u.a(0)` True.
- **Dynamic:** `ReDim` makes elements of Nothing, and after `Erase` the field reads as unallocated.
- **A field of an undeclared type** (`a(1) As Foo`) is the compile error **"User-defined type not defined"**.
- **`Put` of a Type with an object array field** is the compile error **"Can't Get or Put an object reference variable or a variable of user-defined type containing an object reference"**.
- `ADODB` types weren't probed: the probe had no reference to ADO.

**Case 7: a Let or read with arguments on a Type field:**

| Statement | vb6.exe |
|---|---|
| `u.i(1) = 5`, `i As Integer`; `u.s(1) = "x"`, `s As String` | compile error **"Expected array"** |
| `u.v(1) = 9`, `v As Variant` holding `Array(1, 2, 3)` | sets the element: `u.v(1)` reads 9 |
| `u.v(1) = 9`, and `x = u.v(1)`, `v` holding Empty | **13** |
| `u.v(1) = 9`, `v` holding "abc" | **13** |
| `u.v(1) = 9`, `v` holding a `Collection` holding 5 | **438**; the item stays 5 |
| `u.o(1) = 9`, and `x = u.o(1)`, `o As Object` holding Nothing | **91** |
| `u.o(1) = 9`, `o` holding a `Collection`, or a `StdFont` | **451** |
| `u.o(1) = 9`, `o` holding `Form1.Text1` | **344**, "Must specify index for object array"; Text unchanged |
| `u.a(0)(1) = 9`, `a(1) As Variant` with `u.a(0) = Array(1, 2)`; and `u.v(0)(1) = 9` with `v = Array(Array(1, 2), 3)` | **runs:** the inner element becomes 9. Reading `u.a(0)(1)` gives 2 before |

**What was overturned, ours:** writing the probe, we expected an array field `As New` might not compile, as an inverted
bound doesn't, so it went in the compile-time set. It compiles, and every element is auto-created like any `As New` variable. And `u.o(1) = 9` on a field
holding a TextBox wasn't expected to say anything about arrays: it's 344, "Must specify index for object array".

### In HexIDE (#4925)

On `upstream/main` `5a2b04635`, all seven cases refused, citing #4925, except that a Type with an object field already
gave the "Can't Get or Put an object reference…" compile error. The work is briefed on #4925.
