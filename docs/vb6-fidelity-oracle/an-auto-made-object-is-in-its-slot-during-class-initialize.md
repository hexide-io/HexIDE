## An auto-made object is in its slot while its Class_Initialize runs (2026-11-18, #4605, SP6)

**The question.** A predeclared class's default instance and an `As New` slot are both made on first use. Can that
object's own `Class_Initialize` already find it under the slot's name? And what happens when that Initialize raises?

**The probe:** one Standard EXE, built with `vb6.exe` SP6 `/make` as native code and as p-code, which logged the same
lines.
- **Logging:** `Sub Main` runs under `On Error Resume Next`. Each step's log line gives `Err.Number`.
- **Guarding against recursion:** each class's `Class_Initialize` counts itself in a module variable and stops reading
  after its third run. A recursion would then show as extra runs rather than a crash.
- **The log file:** it is opened for append on every line, so a `Class_Terminate` at the program's end can still log.

```vb
' Class4: VB_PredeclaredId = True
Public IsDefault As Boolean
Private Sub Class_Initialize()
    Module1.Inits4 = Module1.Inits4 + 1
    Dim n As Integer: n = Module1.Inits4
    Module1.Log "C4 init " & n & " start"
    If n > 5 Then Exit Sub
    If Me Is Class4 Then IsDefault = True
    Module1.Log "C4 init " & n & " end, IsDefault " & IsDefault
End Sub

' Class2: VB_PredeclaredId = True, with a Class_Terminate that logs "C2 terminate"
Private Sub Class_Initialize()
    Module1.Inits2 = Module1.Inits2 + 1
    Module1.Log "C2 init " & Module1.Inits2 & ", Me Is Class2 " & (Me Is Class2)
    If Module1.Fail2 Then Err.Raise 5
End Sub

' Class3, Class5 and Class6 are not predeclared. Each one's Initialize counts itself as Class4's does, then logs
' "Is Nothing" and "Is Me" for its own slot: Module1.g, Module1.ga(1) and Module1.t.m.

' Module1
Public Type TT
    m As New Class6
End Type
Public g As New Class3
Public ga(1) As New Class5
Public t As TT
```

### The rows

| # | `Sub Main` | Logged |
|---|---|---|
| 1 | `Dim o As Class4` / `Set o = New Class4`, before the default instance exists | `C4 init 1 start`, `C4 init 2 start`, `C4 init 2 end, IsDefault True`, `C4 init 1 end, IsDefault False`. Then `o.IsDefault` False, `Class4.IsDefault` True, two Initializes, err 0 |
| 2 | `Fail2 = True` / `v = Class2.Value` | `C2 init 1, Me Is Class2 True`, then err 5 |
| 3 | `v = Class2.Value` again | `C2 init 2, Me Is Class2 True`, err 5 |
| 4 | `b = True` / `b = (Class2 Is Nothing)` | `C2 init 3, Me Is Class2 True`, err 5; `b` still True, because the assignment never happened |
| 5 | `Fail2 = False` / `v = Class2.Value` | `C2 init 4, Me Is Class2 True`, err 0, four Initializes |
| 6 | `v = g.Tag` | one Initialize: `g Is Nothing` False, `g Is Me` True; err 0 |
| 7 | `v = ga(1).Tag` | one Initialize: `ga(1) Is Nothing` False, `ga(1) Is Me` True; err 0 |
| 8 | `v = t.m.Tag` | one Initialize: `t.m Is Nothing` False, `t.m Is Me` True; err 0 |
| 9 | after `end of Main` | `C2 terminate`, once |

An earlier probe the same day, with a predeclared `Class1` whose Initialize does `If Me Is Class1 Then IsDefault =
True`, read `Class1.IsDefault` first and then made `New Class1`. It logged one Initialize with `Me Is Class1` True,
then one with it False, and two in all.

### What the rows say

- **The object is in its slot before its Initialize runs.** That holds for a predeclared class's default instance
  (rows 1 to 5) and for every kind of `As New` slot: a module variable (row 6), an array element (row 7) and a Type's
  field (row 8). So `Me Is Class4` is the usual way to tell the default instance apart in its own Initialize, and it
  doesn't recurse.
- **The order of first uses doesn't matter** (row 1). The default can first be made inside another instance's
  Initialize, by that Initialize naming the class. Its own Initialize then runs nested, and sees itself as the
  default.
- **A raising Initialize leaves the slot Nothing.**
  - Each use runs Initialize again and raises again (rows 2 to 4), as [As New](as-new.md#an-initialize-that-raises)
    found for a variable. Inside each of those runs the object was already in the slot: `Me Is Class2` is True every
    time.
  - **No `Class_Terminate` runs for an object whose Initialize raised.** Three such objects were made, and the only
    Terminate logged (row 9) is the fourth one's, the instance that succeeded, at the program's end.

### What this overturned

The issue's guess was that the slot is filled when `New` returns, as an explicit `Set x = New C` fills `x`. That is
how HexIDE built it. It holds for an explicit `New` and for none of the auto-made slots.

### In HexIDE, at `23a38af17`

These rows were run on HexIDE's runtime with the same guard, and #4605 is the fix. The slot is filled only after
`NewObject` returns, so each read inside Initialize makes another object:
- **Rows 1 and 6 to 8:** four Initializes each, with `Is Me` False at every level.
- **Without the guard:** error 28, "Out of stack space", as #4605 found running NavalBattle's `Ship.cls` at
  `125ff7682`.
- **Rows 2 to 5:** the first use gives 5, but the innermost nested object finished and was stored, so the second use
  gives 0 where VB6 gives 5.

### A form's As New instance is in its slot during Form_Initialize (2026-11-18, #4640, SP6)

**The probe.** A Sub Main project held Form2 and Module1. Module1 declared `Public g As New Form2`. Form2's
Form_Initialize counted itself in a module variable and gave itself that number. It stopped after the third run,
guarding against recursion as the classes above do, then logged `g Is Me`. Its Form_Terminate logged too. Sub Main ran
under `On Error Resume Next`, and nothing showed or loaded the form. p-code and native code logged the same lines.

| `Sub Main` | Logged |
|---|---|
| `g.Id`, a Public Function of Form2 | `Initialize #1`, and inside it `g Is Me` True with one Initialize so far. Then `g.Id` read 1; `Forms.Count` 0 |
| `Set g = Nothing` | `Terminate #1` |
| `TypeName(g)` | `Initialize #2`, and inside it `g Is Me` True with two so far. Then `TypeName(g)` read "Form2" |
| after the end of Main | `Terminate #2` |

- **A form's `As New` instance is in its variable before its Form_Initialize runs,** as a class's is (row 6 above).
  `g Is Me` there is True, and it doesn't make a second instance.
- **The same holds for the next instance** after `Set g = Nothing`.

**Not measured:** a Form_Initialize that raises, and an `As New` form in an array element or a Type's field.
