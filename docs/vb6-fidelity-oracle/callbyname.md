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

## A Collection's methods through CallByName (2026-11-18, #4688)

**The question.** The section above measured `Add` with an item and a key, and `Item` with an index. That leaves the
rest of a Collection's surface through `CallByName`, and whether its errors are `CallByName`'s own or the
Collection's.

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged all 26 rows identically.
- **The setup:**
  - `k` is a module-level `Collection`, and `Fill` makes it hold `"x"` keyed `"kx"` and `"y"` keyed `"ky"`;
  - `C1` is a class with one Public field and no default member;
  - `v` is a Variant and `o` an Object.
- **The read:** each row stops at its first error and logs the error number.

| Setup | Statement | VB6 |
|---|---|---|
| empty `k` | `v = CallByName(k, "Count", VbMethod)` | `TypeName(v)` Long, 0 |
| `Fill` | `CallByName(k, "Count", VbMethod)` | 2 |
| `Fill`, `Set vk = k` (a Variant) | `CallByName(vk, "Count", VbMethod)` | 2 |
| `Fill` | `v = CallByName(k, "Add", VbMethod, "a")` | `TypeName(v)` Empty; `k.Count` 3, `k(3)` "a" |
| `Fill` | `CallByName k, "Add", VbMethod, "b", "kb", 1` | `k.Count` 3, `k(1)` "b", `k("kb")` "b" |
| `Fill` | `CallByName k, "Add", VbMethod, "c", "kc", "kx"` | `k.Count` 3, `k(1)` "c" |
| `Fill` | `CallByName k, "Add", VbMethod, "d", "kx"` | 457 |
| `Fill` | `CallByName k, "Remove", VbMethod, 1` | `k.Count` 1, `k(1)` "y" |
| `Fill` | `CallByName k, "Remove", VbMethod, "ky"` | `k.Count` 1, `k(1)` "x" |
| `Fill` | `v = CallByName(k, "Remove", VbMethod, 1)` | `TypeName(v)` Empty, `k.Count` 1 |
| `Fill` | `CallByName k, "Remove", VbMethod, 5` | 9 |
| `Fill` | `CallByName k, "Remove", VbMethod, "nokey"` | 5 |
| `Fill` | `v = CallByName(k, "Item", VbMethod, 5)` | 9 |
| `Fill` | `v = CallByName(k, "Count", VbMethod, 1)` | **450** |
| `Fill` | `v = CallByName(k, "Item", VbMethod)` | 450 |
| `Fill` | `v = CallByName(k, "Nope", VbMethod)` | 438 |
| `Fill` | `v = CallByName(k, "Item", VbLet, "a")` | 438 |
| `k` holds a `New C1` | `TypeName(CallByName(k, "Item", VbMethod, 1))` | C1 |
| `k` holds a `New C1` | `Set o = CallByName(k, "Item", VbMethod, 1)` | `TypeName(o)` C1, `o Is k(1)` True |
| `k` holds a `New C1` | `Set v = CallByName(k, "Item", VbMethod, 1)` | C1, True |
| `k` holds a `New C1` | `v = CallByName(k, "Item", VbMethod, 1)`, without `Set` | 438 |
| `k` holds a `New Collection` | `TypeName(CallByName(k, "Item", VbMethod, 1))` | Collection |
| `Fill` | `k.Remove 5` (direct) | 9 |
| `Fill` | `k.Remove "nokey"` (direct) | 5 |
| `Fill` | `v = k(5)` (direct) | 9 |
| `k` holds a `New C1` | `v = k(1)` (direct), without `Set` | 438 |

**Readings:**
- **`VbMethod` reaches all four members:**
  - `Count` gives a Long;
  - `Add` takes one to three arguments, including a Before index or key;
  - `Remove` takes an index or a key;
  - `Add` and `Remove` used as values give Empty.
- **The Collection's own errors pass through unchanged,** as the direct calls in the last four rows show: 9 for an
  index out of range, 5 for an unknown key, 457 for a key already used.
- **The arity errors are `CallByName`'s own:** `Count` given an argument and `Item` given none are both 450. A
  late-bound call of `Count` with an argument is 451 ([`collection.md`](collection.md), `o.Count(1)`), so this isn't the late-bound path either.
- **An object item comes back as an object.** It needs `Set`. Without it, the assignment reads the object's default
  member, so a class with none is 438, as the direct `v = k(1)` is, and a Collection, whose `Item` needs an argument,
  is 450, as the section above found for a property.
- `VbLet` on `Item` and an unknown name with `VbMethod` are both 438.

**Not measured:** `Add` with four arguments, or with none; `Remove` with none or two; `VbSet`; omitted arguments.

## A control's and a form's members through CallByName (2026-11-18, #4520)

**The question.** #4508 measured one control member through `CallByName`, a CommonDialog's Color. Does `CallByName` reach a built-in control's and a form's properties and methods generally, and with which errors?

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged all 56 rows identically.
- **The form:** a ListBox `List1`, a ComboBox `Combo1`, a TextBox `Text1` and a PictureBox `Picture1`. Every row ran in `Form_Load`, before the form showed, under `On Error Resume Next`.
- **The helper:** `G what, o, member, kind, args…` ran `v = CallByName(o, member, kind, args…)` into a Variant `v`, through a `ByVal o As Object` parameter, and logged `TypeName(v)` and its value or the error. A statement row is `CallByName …` on its own.

**Properties:**

| Statement | VB6 |
|---|---|
| `CallByName(Text1, "Text", VbGet)`; and `"text"` | String, the text: names are case-insensitive |
| `CallByName Text1, "Text", VbLet, "abc"` | ok; `Text1.Text` is abc |
| `CallByName(Text1, "Left", VbGet)` | **Single** 2400 |
| `CallByName Text1, "Left", VbLet, 300`, then `"400"` | ok; 300, then 400: a String converts |
| `CallByName(Text1, "Name", VbGet)` | String "Text1" |
| `CallByName(Text1, "Visible", VbGet)` | Boolean False (the form hadn't shown yet) |
| `CallByName(Text1, "", VbGet)` | String "abc": an empty name reads the default member, `Text` |
| `CallByName(List1, "ListCount", VbGet)`; `Combo1`'s | **Integer** 2; Integer 0 |
| `CallByName List1, "ListCount", VbLet, 5` | **383** "Set not supported (read-only property)" |
| `CallByName(List1, "List", VbGet, 1)` | String "a" |
| `CallByName List1, "List", VbLet, 0, "zz"` | ok: the index comes first, the value last |
| `CallByName(List1, "List", VbGet)`, no index | **385** "Need property array index" |
| `CallByName(Me, "Caption", VbGet)`; `VbLet, "NewCap"` | String; ok |
| `CallByName(Text1, "Font", VbGet)` into a Variant | String "MS Sans Serif": **the Font's default member**, its Name |
| `Set o = CallByName(Text1, "Font", VbGet)`, `o As Object` | ok; `TypeName(o)` Font |
| `CallByName Text1, "Font", VbSet, f`, `f` a `New StdFont` named Courier New | ok; `Text1.Font.Name` Courier New |
| `CallByName Text1, "Font", VbLet, f` | ok |
| `CallByName(Text1, "Container", VbGet)` into a Variant | **450**: the form's default member needs an argument |
| `CallByName(Me, "Text1", VbGet)` into a Variant | String "abc": the control's default member |
| `Set o = CallByName(Me, "Text1", VbGet)` | ok; `TypeName(o)` TextBox |
| `CallByName(v, "Text", VbGet)`, `v` a Variant holding Text1 | String "abc" |

**Methods:**

| Statement | VB6 |
|---|---|
| `CallByName Text1, "Move", VbMethod, 500, 2500` | ok; at 500, 2500 |
| `CallByName Text1, "SetFocus", VbMethod` | **5**, as `Text1.SetFocus` is before the form shows |
| `CallByName List1, "AddItem", VbMethod, "a"`; `"b", 0` | ok; `[b][a]` |
| `CallByName List1, "AddItem", VbMethod`, no argument | **450** |
| `CallByName List1, "AddItem", VbMethod, "c", 0, 1`, one too many | **450**, and nothing is added |
| `CallByName List1, "RemoveItem", VbMethod, 0` | ok |
| `CallByName List1, "Clear", VbMethod` | ok; `ListCount` 0 |
| `CallByName(Picture1, "ScaleX", VbMethod, 1440, vbTwips, vbPixels)` | Single 96 |
| `CallByName(Me, "TextWidth", VbMethod, "abc")` | Single 270 |
| `CallByName Me, "Move", VbMethod, 800, 900` | ok; the form at 800, 900 |

**The wrong call type, and a missing member:**

| Statement | VB6 |
|---|---|
| `CallByName(Text1, "Foo", VbGet)`, `VbLet, 1`, `VbMethod` | **438** each |
| `CallByName(Text1, "Text", VbMethod)`: a property as a method | **438** |
| `CallByName(Text1, "Move", VbGet, 1, 2)`; `CallByName(List1, "Clear", VbGet)`: a method as a property | **438** |
| `CallByName Text1, "Text", VbSet, "x"`: Set on a String property | **438** |
| `CallByName(Text1, "Text", 3)`: VbGet Or VbMethod | String "abc": read as a property |

**Readings:**
- **`CallByName` on a control or a form reaches its own properties and methods** with the types the control gives them: Single for Left, Integer for ListCount, String for Text.
- **The call type must fit the member.**
  - A method through `VbGet`, a property through `VbMethod`, and `VbSet` on a property that isn't an object are all 438, as an unknown name is.
  - Call type 3 reads a property.
- **A property's own errors pass through:** 383 for a read-only one, 385 for a property array read with no index.
- **Arity is 450, `CallByName`'s own,** in both directions, as #4688 found for a Collection.
- **An object property, read into a Variant without `Set`, gives its default member.** The Font's is its Name, the form's (through `Container`) needs an argument, so 450, and a control's is its value property. With `Set`, it gives the object.
- **A Variant holding the control works as the control does.**

**Not measured:**
- other controls' members;
- `VbSet` with a non-object;
- omitted arguments;
- named arguments to `CallByName` itself.
