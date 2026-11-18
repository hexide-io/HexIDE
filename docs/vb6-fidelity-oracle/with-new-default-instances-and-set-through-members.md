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

What a ByRef interface parameter does when its argument isn't a variable, and a parameterized default member reached
through a Property Get that takes arguments of its own, are unmeasured.
