# With New, a default instance in its own Class_Initialize, and Set through members (2026-11-18, #4599 to #4607)

Provenance: `vb6.exe` SP6 (VB6.EXE 6.00.9782, MSVBVM60 6.00.9848) in the `Win10` guest. Every row was built with
`/make` twice, native (`CompilationType=0`) and p-code (`CompilationType=-1`), and run. **Native and p-code agreed on
every row.** These were measured for the gaps a real VBA program met on HexIDE's runtime (#4610), so each row says
which issue it pins. The enum, Type-bound, object-array-field and `LoadPicture` rows from the same night are on
hexide-int's pages.

## `With New`: when the object is made and released (#4599)

A class `Class1` whose `Class_Initialize` and `Class_Terminate` each log a line, and a `Public Function Self()`
returning `Me`. Each block is followed by a log line, so the order shows where `Terminate` falls.

| Program | Logged, in order |
|---|---|
| `With New Class1` / `.X = 5` / `Log "inside, X " & .X` / `End With` / `Log "after End With"` | initialize, inside, X 5, **terminate**, after End With |
| `With New Class1` / `Set keep = .Self` / `End With` / `Log keep.X` / `Set keep = Nothing` / `Log` | initialize, (after the block) 0, **terminate**, after `Set keep = Nothing` |
| `With New Collection` / `.Add 1` / `Log .Count` / `End With` | 1 |

So the object is made when the `With` line runs and held for the block. It is released at `End With`, and
terminates there unless the block stored another reference.

## A class's default instance named in its own `Class_Initialize` (#4605)

`Class1` with `Attribute VB_PredeclaredId = True`, whose initializer is:

```vb
Private Sub Class_Initialize()
    Module1.Log "init " & Module1.Inits
    Module1.Inits = Module1.Inits + 1
    If Module1.Inits > 5 Then Exit Sub        ' a guard, in case it recursed
    If Me Is Class1 Then IsDefault = True
    Module1.Log "init end, IsDefault " & IsDefault
End Sub
```

| Step | Logged | Then |
|---|---|---|
| `d = Class1.IsDefault` (the first use of `Class1`) | init 0; init end, IsDefault True | `d` True, `Err.Number` 0 |
| `Set o = New Class1` | init 1; init end, IsDefault False | `o.IsDefault` False, `Err.Number` 0 |
| at the end | `Inits` is 2 | |

**Inside the default instance's own `Class_Initialize`, the class's name already refers to the instance being
built.** It isn't made a second time, so there is no recursion. `If Me Is ClassName Then` is how a program tells
its default instance apart from the start. The obvious guess, that the name isn't bound until `Initialize`
returns, would recurse, and that is what HexIDE did: error 28 (#4605).

## Set through members (#4600, #4601, #4602, #4606, #4607)

Measured with `scripts/vb6-oracle.ps1 -Classes`, plus the same script with `CompilationType=-1` added. The classes:
- **`IHas`:** an interface with `Property Get/Set P As Object` and `Function Q() As String`.
- **`Impl`:** `Implements IHas`. Its `IHas_Q` returns `"q"`.
- **`Box`:** `Property Get/Set O As Object`.
- **`Holder`:** `Private hv As Box`, `Public Property Get Inner() As Box`.
- **`Bag`:** a default `Property Get/Let Item(ByVal k As Variant)` (`Attribute Item.VB_UserMemId = 0`) over a Collection, and `Property Get Self() As Bag`.

| Probe | Value |
|---|---|
| `Dim c As New Box` / `With c` / `Set .O = New Collection` / `End With` / `TypeName(c.O)` (#4600) | `Collection` |
| `Function Ask(ByVal x As IHas)`: `Set x.P = New Collection` / `Ask = x.Q & " " & TypeName(x.P)`, called with an `IHas` variable (#4601) | `q Collection` |
| the same `Ask`, called with a `Dim c As New Impl` (#4601) | `q Collection` |
| `Function GetIt() As IHas` returning `New Impl`; `GetIt.Q` (#4601) | `q` |
| inside `Holder`: `Set hv = New Box` / `Set Inner.O = v`, then `TypeName(hv.O)` (#4602) | `Collection` |
| `Dim b As New Bag` / `b("x") = "v"` / `b("x") & " " & b.Self("x")` (#4606) | `v v` |
| `Dim arr(1 To 2) As Box` / `Set arr(1) = New Box` / `Set arr(1).O = New Collection` / `TypeName(arr(1).O)` (#4607) | `Collection` |

Every row raised nothing. These are ordinary VB6, so none was in doubt. They are recorded because HexIDE failed
each one when this page was written (450, 461, 91, 13, and a refusal), and the issues pin these values.

## Measured by hexide-int: interface views, a bare Function, and LSet on a member (#4601, #4624, #4626)

Measured by hexide-int the same day, on the same `vb6.exe` with `/make`, native and p-code agreeing on every row. The
classes:
- **`IHas`:** `Property Get/Set P As Object` and `Q As String`.
- **`Impl`:** `Implements IHas`, its `Q` returning `"q"`.
- **`Holder`:** `Property Get Thing() As IHas`, returning an `Impl`.
- **`Box`:** `Property Get/Set O As Object` and `Public S As String`.
- **In the module:**
  - `Function GetIt() As IHas` returns `New Impl`;
  - `AskRef(x As IHas)`, ByRef, and `AskOpt(Optional ByVal x As IHas)` each return `x.Q`;
  - `Function GetC() As Collection`.
- **The variables:** `i` is `As IHas`, and `c` is `As Impl`; each holds an `Impl`. `b` is a `Box`.

| Program | VB6 |
|---|---|
| `GetIt().Q` | `q` |
| `GetIt.Q`, the Function called bare at the head of the chain | `q` |
| `h.Thing.Q`, through the Property Get | `q` |
| `TypeName(GetIt)` | `Impl` |
| `AskOpt(i)` | `q` |
| `AskRef((i))`, a parenthesised argument to the ByRef parameter | **438** "Object doesn't support this property or method" |
| `AskRef((c))` | **438** |
| `GetC.Count`, and `GetC().Count` | 1, and 1 |
| `With b` / `Set .O = New Collection`, then `TypeName(b.O)`; then `Set .O = Nothing` | `Collection`; then `Nothing` |
| `With b` / `LSet .S = "ab"`; `LSet b.S = "ab"`; `With b` / `RSet .S = "ab"` | each the compile error "Variable required - can't assign to this expression" |

**What this overturned.** #4601 suggested that an argument which isn't a variable, so is copied, should take the
parameter's interface view, as a ByVal parameter does. It doesn't. A parenthesised argument to a ByRef interface
parameter loses the view, and `x.Q` is 438. Giving that copy the view would answer `q`, a wrong value.

**In HexIDE,** at `23a38af17`:
- `GetIt().Q` and `h.Thing.Q` were the compile error "Method or data member not found (Q)" (#4601).
- `GetIt.Q` and `GetC.Count` were 424 "Object required": a Function called bare at the head of a chain wasn't
  called (#4624).
- `LSet .S` and `RSet .S` parsed as a Let on a variable named `LSet` (#4626).

## Measured by hexide-int: a default member that takes arguments (#4606)

Measured the same way, native and p-code agreeing on every row. The classes, each in its own module:
- **`Bag`:** over a `Private store As New Collection`.
  - Its default member is `Property Get Item(ByVal k As Variant) As Variant` (`Attribute Item.VB_UserMemId = 0`),
    with a `Property Let Item(ByVal k, ByVal v)` beside it.
  - It also has `Property Get Self() As Bag`.
- **`FBag`:** a default `Function Item(ByVal k As Variant) As Variant`, returning `"f" & k`.
- **`TBag`:** a default `Property Get Item(ByVal a As Long, ByVal b As Long) As Long`, returning `a * 10 + b`.
- **`Holder`:** a `Private names As Bag` field.
  - `Init` sets it to a new `Bag`.
  - `PutIt` does `names(k) = v`, and `GetIt` returns `names(k)`.
  - `Property Get State() As Bag` returns `names`. It takes no parameters, and has no Let.
- **In the module:** `Function MakeBag() As Bag` returns a new `Bag` after `MakeBag.Item("x") = "m"`.
- **`b`** is a `Dim b As New Bag`, as in the table above.

| Program | VB6 |
|---|---|
| `Dim v As Variant` / `Set v = New Bag` / `v("x") = "v"` / `v("x")` | `v` |
| `Dim o As Object` / `Set o = New Bag` / `o("x") = "o"` / `o("x")` | `o` |
| `Dim n As Bag`, Nothing: `n("x")` | **91** |
| `Dim n As Bag`, Nothing: `n("x") = "n"` | **91** |
| `h.Init` / `h.PutIt "k", "hv"` / `h.GetIt("k")`: a class field, Let and read | `hv` |
| then `h.State("k")`, through the parameterless Get | `hv` |
| `h.Init` / `h.State("k2") = "w"` / `h.GetIt("k2")`: a Let through the Get-only `State` | `w` |
| `Dim f As New FBag` / `f("q")`, a Function default member | `fq` |
| `Dim t As New TBag` / `t(1, 2)`, two arguments | `12` |
| `b("x") = "bv"` / `s = b("x") & "!"`, with `s As String` | `bv!` |
| `MakeBag()("x")` | `m` |
| `MakeBag("x")`: a Function with no parameters and a class result, given an argument | `m` |
| `TypeName(v("x")) & " " & TypeName(b("x"))` | `String String` |
| `Dim arr(1 To 2) As Bag` / `Set arr(1) = New Bag` / `arr(1)("x") = "a"` / `arr(1)("x")` | `a` |
| then `arr(1).Item("x")` | `a` |

So arguments given to an object go to its default member, read or Let. That holds whatever holds the object: a
variable of the class, a Variant or `As Object`, a class field, a Function's result, an array element, or a
parameterless Property Get. A Let through that Get works even though it has no Property Let, because the Let goes to
the object the Get returns. Nothing raises 91 either way.

**In HexIDE,** at `23a38af17`, every row above failed (#4606). For example, `v("x") = "v"` was 13, `f("q")` the
compile error "Sub or Function not defined", and `MakeBag()("x")` a refusal naming #174.

**Still unmeasured:**
- a Variant or `As Object` holding Nothing, given arguments;
- the order in which a Let's arguments and value are evaluated, for an object;
- a default member reached through a Property Get that takes arguments of its own.

### When the default member can't take the arguments (2026-11-18, #4747)

**The question.** The table above gives arguments to a default member that takes them. What does VB6 do when the
default member can't: when there's no default member, or it's a Sub, a variable, or a Get or Function with no Let?

**The probe.** VB6 SP6, `/make`, native code and p-code, which logged every row identically. Each early-bound row
(`Dim e As <class>`) is a project of its own, because a compile error stops the whole build. The late-bound rows
(`Dim o As Object`) share one project. Each row runs under `On Error Resume Next` and logs `Err.Number` after every
statement. The classes, each in its own module:
- **`CN`:** no default member, only `Function F(ByVal i As Long)`;
- **`CS`:** a default `Sub Value(ByVal i As Long)`, which logs `Sub <i>`;
- **`CV`:** a default `Public Value As Variant` (`VB_VarUserMemId = 0`), set to `Array(10, 20, 30)` in
  `Class_Initialize`;
- **`CVS`:** the same, holding 7;
- **`CG`:** a default `Property Get Value(ByVal i As Long)`, returning `i * 10`, with no Let;
- **`CF`:** a default `Function Value(ByVal i As Long)`, returning `i * 10`;
- **`CG2`** and **`CF2`:** as `CG` and `CF`, but each logs `Get <i>` or `Function <i>` first;
- **`CVE`:** a default `Public Value As Variant` that is never set;
- **`CVT`:** a default `Public Value As Long`, set to 7.

| Class | Statement | Early-bound | Late-bound |
|---|---|---|---|
| `CN` | `x = e(1)` | 438 | 438 |
| `CN` | `e(1) = 5` | 438 | 438 |
| `CS` | `x = e(1)` | compile error "Expected Function or variable" | logs `Sub 1`; `x` is Empty, no error |
| `CS` | `e(1) = 5` | compile error "Expected Function or variable" | logs `Sub 1`, then 451 |
| `CV` | `x = e(1)` | `x` is 20, an Integer | 451 |
| `CV` | `e(1) = 5` | no error, and the array is still 10, 20, 30 | 451 |
| `CV` | `x = e(5)` | 9 | not measured |
| `CV` | `e(5) = 1` | 9 | not measured |
| `CV` | `x = e.Value(1)` | 20 (see below) | 451 |
| `CVS` | `x = e(1)` | 13 | 451 |
| `CVS` | `e(1) = 5` | 13 | 451 |
| `CVE` | `x = e(1)` | 13 | 451 |
| `CVE` | `e(1) = 5` | 13 | not measured |
| `CVT` | `x = e(1)` | compile error "Wrong number of arguments or invalid property assignment" | 451 |
| `CVT` | `e(1) = 5` | compile error "Can't assign to read-only property" | 451 |
| `CG` | `e(1) = 5` | 424 | 451 |
| `CF` | `e(1) = 5` | 424 | 451 |
| `CG2` | `e(1) = 5` | logs `Get 1`, then 424 | logs `Get 1`, then 451 |
| `CF2` | `e(1) = 5` | logs `Function 1`, then 424 | logs `Function 1`, then 451 |

451 is "Property let procedure not defined and property get procedure did not return an object". The early-bound
`CV` read through `.Value(1)` comes from the `e(1) = 5` row's log line, which concatenated `e.Value(0)` to
`e.Value(2)`, so its type was not recorded.

**Readings:**
- **No default member is 438 both ways,** read or assigned.
- **Early-bound, a default Variant is indexed as its value.** An array gives its element, and an index past its
  bounds is 9. A scalar or Empty is 13. An assignment indexes a copy: no error, and the array is unchanged.
- **Early-bound, a typed default field doesn't compile with arguments,** and the two compile errors differ: a read
  is "Wrong number of arguments or invalid property assignment", an assignment "Can't assign to read-only
  property".
- **A Let through a Get or Function with no Let calls it with the arguments first,** then needs an object from it. A
  number is 424 early-bound and 451 late-bound. That is the same route as `h.State("k2") = "w"` above, where the Get
  did return an object.
- **Late-bound, every case but no default member and a Sub's read is 451.** Arguments to a default variable are 451
  even through its name, `o.Value(1)`. A default Sub is run for its arguments, and gives Empty to a read or 451 to an
  assignment.

**In HexIDE,** at `7929177e6`, probes of every row but `e(5) = 1` and the `CG2` and `CF2` rows were refused as
unmeasured, citing #4606 (#4747 since then). The two `.Value(1)` reads are the exception: the late-bound one was
already 451, and the early-bound one 20.

**Still unmeasured:** an index out of range through a late-bound reference; and a Let with arguments through a
parameterless Property Get whose value isn't an object.
