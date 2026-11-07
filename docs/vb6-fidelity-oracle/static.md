## Static (2026-10-23, #1491)

**The question.** `Static n As Long` stopped the program: "non dim variables not supported". The `Static` keyword on a
procedure was parsed and then ignored, so a `Static Sub`'s locals started again at zero on every call: silently wrong.
#1491 asked what a Static keeps and for how long:
- its first value;
- recursion;
- a `Static Sub` or `Function`;
- arrays;
- a class's and a form's Statics, and when they end.

**The probe.**
- Two Sub Main programs, the second with four classes and a form, made native and p-code under en-GB.
- 34 one-statement compile projects.
- Two two-form projects started from Form1, the second without `New Form2`.
- `StaticTests`' 4 transcript programs and 2 parse refusals, and the form project `StaticInFormTests` runs, made the same
  way. They logged exactly what the tests pin, and gave the compile errors the tests expect.

Native code and p-code agreed on every line.

### What a Static keeps

| Program | VB6 |
|---|---|
| a Sub with `Static n As Long: n = n + 1`, called three times | 1, 2, 3 |
| two Subs, each with its own `Static n` | separate: 10, 20 in the second while the first counts on |
| a recursive Sub, `Static n`, three levels deep, then called again | 1, 2, 3 on the way down; 3 at every level on the way back; 4 on the next call. **One slot for every frame** |
| `Private Static Sub` with `Dim a As Long` and `Dim s As String` | 1, then 2; "z", then "zz" |
| `Private Static Function F() As Long` with `Dim m`, and `F = F + 1` | `m` counts 1, 2; **`F` is 1 on every call**: a Static Function's own return value is not Static |
| `Bump n`, passing the Static ByRef to a Sub that adds 100 | 100, then 200 |
| `Static q` inside a `For` run three times, `q = q + 1` | 3, then 6: the statement re-run does nothing |
| `Static u` inside an `If` that is never taken | `u` exists and counts 1, 2 |

### First values, and what it holds

- Each type starts at its own zero:
  - Integer, Long, Byte: 0, with TypeName Integer, Long, Byte;
  - Boolean: False;
  - Single, Double, Currency: TypeName Single, Double, Currency;
  - Date: 0;
  - String: "";
  - Variant and untyped: Empty;
  - `As Object`, a class and `As Collection`: Nothing;
  - `fa(2) As Long`: `Long()`, UBound 2;
  - `da() As Long`: `Long()`;
  - a Type: its fields' zeros.
- Every kind keeps what it was given:
  - a fixed array's element;
  - a dynamic array grown by `ReDim Preserve`;
  - a Type's fields;
  - a String;
  - a Variant;
  - an object, and a Collection with its items.
- `Erase` of a Static fixed array zeros it, and of a dynamic one deallocates it. The next call finds `fa(0)` 0 again,
  and the array as it was ReDimmed last.

### A class's, and a form's

| Program | VB6 |
|---|---|
| two instances of a class, each calling a method with `Static n` | each counts from 1: **a class's Statics are its instance's** |
| `Static times` in Class_Initialize | 1 for each instance |
| a `Public Static Sub` in a class | per instance |
| a Static in a Private method reached through a Public one | per instance, as if called directly |
| `Set k = Nothing` on an instance holding a field object and three methods' Static objects, made in the order Hold, B, A | Class_Terminate, then the field's object, then the Statics' in the module's order, **Hold, A, B**, not the order they were made |
| a form's `Public Sub Tick()` with `Static n`, after `Load Form2` | 1, 2 |
| `Unload Form2`, then `Form2.Tick` | 3: **a Static survives Unload** |
| `Set Form2 = Nothing` (Form_Terminate), then `Form2.Tick` | 1: a new instance starts again |
| `New Form2`, loaded, `Tick` twice | 1, 2: its own |
| a Sub's `Static t As C` still holding its object when Sub Main returns | the object terminates as the program ends (#1490) |

### Static As New

`As New` works on a Static as it does on a Dim (#435), and the object it makes is kept from call to call:

| Program | VB6 |
|---|---|
| `Static s As New Made`, then `s.Name = s.Name & "x"`, three calls | made once, at the first call's use; "x", "xx", "xxx" |
| the third call ending `Set s = Nothing` | it terminates there |
| `Static c As New Made`, with `Set c = Nothing` at the end of the first call | the second call makes a new one, its Name "" |
| a class method with `Static c As New Collection: c.Add 1`, twice on one instance and once on a second | 1, 2, then 1 |

### What VB6 refuses to compile

| Declaration | VB6's compile error |
|---|---|
| `Static n As Long` at a standard module's level, or a class module's | Invalid outside procedure |
| `Dim n` then `Static n` in one procedure; `Static n` twice | Duplicate declaration in current scope |
| `Private Sub S(Static x As Long)` | Syntax error |
| `Sub S(Static x As Long)`, at the top of the module | Expected: identifier |
| `Static Const C = 1` | Syntax error |
| `Dim L As Long` in a Sub, then a call of the module's Sub `L` | Expected Sub, Function, or Property: the local hides the procedure, so the call no longer names one |

Every `Static` form compiles: a fixed or dynamic array, a Type, a Date, an untyped Static, `As Object`, `As Collection`,
a class, and two names on one line.

### What this overturned

- **That a Static Function's own name is Static too.** Its other locals are, and its return value starts at its zero on
  every call.
- **That a class's Statics are released as they were made.** They are released in the order the module declares them,
  after the fields.

### What resisted explanation

- Why `Sub S(Static x As Long)` is "Expected: identifier" at the top of a module, and "Syntax error" when the Sub is
  Private further down.

### In HexIDE (#1491)

**What it does:**
- A Static local's slot is made the first time its procedure declares it, and found again by the declaration's own parse
  node at every later call, a recursive one included. The call never releases it.
- A standard module's procedures keep their Statics for the run.
- A class's or a form's are kept per instance. When the instance terminates, they are released after its fields, in
  source order.
- A `Static Sub`, `Function` or `Property` makes every Dim local Static. Its parameters and a Function's return value are
  not.
- `Static` outside a procedure is "Invalid outside procedure". A Static parameter or a `Static Const` does not parse.

**Known divergences:**
- #1490: a standard module's Static, like a module variable, keeps its object past the end of the program.
- A duplicate `Dim` or `Static` in one procedure was not refused (#1499), and the first declaration won. It is refused
  as the module loads since #1499: see "A name declared twice".
- `New Form2` is #822, so the form test leaves out the second instance.

**What pins it:**
- `StaticTests` pins four programs and three refusals.
- `StaticInFormTests` pins the form program.

