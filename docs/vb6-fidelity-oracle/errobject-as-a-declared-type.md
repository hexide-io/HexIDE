## ErrObject as a declared type (2026-10-28, #2627)

**The question.** Is `ErrObject` a type a variable can be declared as, and what does such a variable take?

**The probe.** A Standard EXE `Sub Main` under `On Error Resume Next`: `Dim e As ErrObject, v As Variant`, then the
statements below. `New ErrObject` is a project of its own, in case it didn't compile; it does.

| Statement | vb6.exe |
|---|---|
| `Set e = Err` | err 0; `TypeName(e)` ErrObject, `TypeName(Err)` ErrObject |
| `e Is Err` | True |
| `Err.Raise 7`, then `e.Number` | 7 |
| `Set v = Err` | `TypeName(v)` ErrObject, `VarType(v)` 3 |
| `Set e = Nothing` | err 0, `e Is Nothing` True |
| `Set e = New Collection` | err 13 |
| `Set e = New ErrObject` | err -2147221164, "Class not registered" followed by a line break; `e Is Nothing` True |

So `ErrObject` is `Err`'s type: a variable of it takes `Err` and `Nothing`, and is `Err` by `Is`. A Variant holding `Err`
reads `VarType` 3, its default member's. Any other object is 13 at the `Set`. `New ErrObject` compiles and raises
-2147221164, "Class not registered", at run time, and `e` stays Nothing.
