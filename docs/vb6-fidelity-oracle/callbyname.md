## CallByName (2026-10-28, #2519)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`. The probe's class:

```vb
Public x As Integer                                    ' a field
Public Property Get P() As Variant / Let P(ByVal v)    ' Let logs "Let P <v>"
Public Property Get O() As Object / Set O(ByVal v)     ' Set logs "Set O"
Public Property Get Item(ByVal i As Integer) As String ' "item " & i
Public Function F(ByVal a As Integer, ByVal b As Integer) As Long   ' a + b
Public Function G(Optional ByVal a As Variant, Optional ByVal b As Integer = 7) As String
Public Function H(ParamArray rest() As Variant) As String           ' "count " & n
Public Sub S(ByVal a As Variant)                       ' logs "S <a>"
Public Sub Twice(ByRef n As Integer)                   ' n = n * 2
```

**What the rows say:**
- `VbGet` 2, `VbLet` 4, `VbMethod` 1, `VbSet` 8. The name is found in any case. **3 (Get and Method together) reads the
  field; 0 and 16 are 438.** An unknown or empty name is 438.
- Arguments are converted as a direct call's are (`"2"` and 3.6 give 6), Optional and ParamArray parameters work, and
  **a ByRef argument is written back** (`n` 4 becomes 8).
- Too few arguments is 449, and too many 450. `VbGet` on a method is 450.
- An object result needs `Set`: without it, a Collection result is 450.
- **The object Nothing is 5, not 91**, and a non-object, a Variant holding 5, is 424. A Variant holding Null as the name
  is 94. (A literal `Null` as the name is VB6's compile-time "Type mismatch"; here it may be refused as the statement
  runs, as `interpreter-core` allows.)
- **A Collection's `Count` is a method:** `CallByName(k, "Count", VbGet)` is 438 and `Item` needs `VbMethod`. Its type
  library declares both as methods, and `CallByName` checks the call type against that.
- A Sub called as a value gives Empty, with no error. `VBA.CallByName` is the same function.

| Expression | Result | Type | Err |
|---|---|---|---|
| `vbGet, vbLet, vbMethod, vbSet` | `2 4 1 8` | String | 0 |
| `CallByName c, "x", VbLet, 5` | `x 5` | | 0 |
| `CallByName(c, "x", VbGet)` | `5` | Integer | 0 |
| `CallByName(c, "X", VbGet) (case)` | `5` | Integer | 0 |
| `CallByName c, "P", VbLet, "hello"` | `P hello` | | 0 |
| `CallByName(c, "P", VbGet)` | `hello` | String | 0 |
| `CallByName c, "O", VbSet, k` | `O Is k True` | | 0 |
| `CallByName(c, "O", VbGet) (object, with Set)` | object | Collection | 0 |
| `CallByName(c, "O", VbGet) (object, without Set)` |  |  | 450 |
| `CallByName(c, "Item", VbGet, 3)` | `item 3` | String | 0 |
| `CallByName(c, "F", VbMethod, 2, 3)` | `5` | Long | 0 |
| `CallByName(c, "F", VbMethod, "2", 3.6)` | `6` | Long | 0 |
| `CallByName(c, "F", VbMethod, 2)` |  |  | 449 |
| `CallByName(c, "F", VbMethod, 2, 3, 4)` |  |  | 450 |
| `CallByName(c, "F", VbGet, 2, 3)` |  |  | 450 |
| `CallByName(c, "G", VbMethod)` | `IsMissing(a) True, b 7` | String | 0 |
| `CallByName(c, "G", VbMethod, 1, 2)` | `IsMissing(a) False, b 2` | String | 0 |
| `CallByName(c, "H", VbMethod, 1, 2, 3)` | `count 3` | String | 0 |
| `CallByName(c, "H", VbMethod)` | `count 0` | String | 0 |
| `CallByName c, "S", VbMethod, "s1"` | `ok` | | 0 |
| `CallByName(c, "S", VbMethod, "s2") (a Sub as a value)` |  | Empty | 0 |
| `CallByName c, "Twice", VbMethod, n (ByRef)` | `n 8` | | 0 |
| `CallByName(c, "Nope", VbGet)` |  |  | 438 |
| `CallByName(c, "Nope", VbMethod)` |  |  | 438 |
| `CallByName(c, "x", 3)` | `5` | Integer | 0 |
| `CallByName(c, "x", 0)` |  |  | 438 |
| `CallByName(c, "x", 16)` |  |  | 438 |
| `CallByName(c, "", VbGet)` |  |  | 438 |
| `CallByName(c, nv (a Variant holding Null), VbGet)` |  |  | 94 |
| `CallByName(o (Nothing), "x", VbGet)` |  |  | 5 |
| `CallByName(five (a Variant holding 5), "x", VbGet)` |  |  | 424 |
| `CallByName k, "Add", VbMethod, "a", "ka"` | `Count 1` | | 0 |
| `CallByName(k, "Count", VbGet)` |  |  | 438 |
| `CallByName(k, "Item", VbMethod, "ka")` | `a` | String | 0 |
| `CallByName(k, "Item", VbGet, 1)` |  |  | 438 |
| `CallByName(Err, "Number", VbGet)` | `0` | Long | 0 |
| `VBA.CallByName(c, "x", VbGet)` | `5` | Integer | 0 |
| `TypeName(CallByName(c, "F", VbMethod, 1, 1))` | `Long` | String | 0 |
