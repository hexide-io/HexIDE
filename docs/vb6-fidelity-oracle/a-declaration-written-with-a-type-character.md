## A declaration written with a type character (2026-10-27, #2409)

**The question.** `Dim s$` stopped its module loading in HexIDE, a local one stopped the run, `Property Get P$()`
stopped its module loading too, and the character on `Sub F(n%)` was ignored, so `n` took a Variant. In vb6.exe, is a
declaration's character the As clause it stands for? And how does it meet arrays, `ReDim`, `Static`, `Optional`, and
an As clause beside it?

**The probe.** One program and seventeen one-construct projects, each built native and p-code with the guest's vb6.exe
and run there. The program's Module2 declares at module level, locally and in procedure headers. It then logs
`TypeName` and `CStr` of each, or an array's `TypeName` with its bounds. Native and p-code logged the same 34 lines,
and each project the same result.

**Result: the character is the As clause.**

| Declaration | vb6.exe |
|---|---|
| Module-level `Dim mI%, mL&, mG!, mD#, mC@, mS$`, never assigned | Integer 0, Long 0, Single 0, Double 0, Currency 0, String "" |
| `Private privS$`, `Public qI%` | String "", Integer 0 |
| `Dim mixA%, mixB As Long, mixC$` | Integer 0, Long 0, String "" |
| Locals `Dim lI%, lL&, lG!, lD#, lC@, lS$` | the same six |
| `Dim mA%(3)` | `Integer()`, 0 To 3, an element Integer 0 |
| `Dim mB$()`, then `ReDim mB(2)`, then `ReDim mB$(4)` | `String()` 0 To 2, an element "", then `String()` 0 To 4 |
| A local `Dim lA&(2)`; `Dim lB#()`, then `ReDim lB(1)` | `Long()` 0 To 2; `Double()` 0 To 1 |
| `Dim x%()`, then `ReDim x(2)`, then `ReDim x%(3)` | `Integer()`, then `Integer()` with UBound 3 |
| `Static sI%, sS$`, each call adding 1 and "x" | Integer 1 and "x", then Integer 2 and "xx" |

**A store converts as into a variable of that type:**
- `lI = 2.5` gives Integer 2, and `lL = 3.5` Long 4, rounding to even.
- `lS = 12` gives String "12", and a module's `mI = 7.5` Integer 8.
- `lI = 40000` raises 6, and `lI = "x"` raises 13.
- From the form, another module's `Public qI%` given 2.6 holds Integer 3, and `qS$` given 1.5 holds String "1.5".

**Parameters:**

| Procedure and call | Inside it |
|---|---|
| `Sub TakeI(n%)`, `TakeI 2.6` | Integer 3 |
| `Sub TakeIV(ByVal n%)`, `TakeIV 2.6` and `TakeIV "7"` | Integer 3, Integer 7 |
| `Sub TakeS(ByVal s$)`, `TakeS 5` | String "5" |
| `Sub TakeOpt(Optional n% = 3, Optional s$ = "d")`, with no arguments, then `TakeOpt 9, "e"` | Integer 3 and "d", then Integer 9 and "e" |
| `Sub TakeArr(a%())`, given `Dim arr%(1 To 2)` | `Integer()` 1 To 2 |
| `Sub Bump(n%)` with `n = n + 1`, given an Integer `k` holding 4 | `k` is Integer 5 afterwards |

**Property procedures:**
- `Property Get PS$()` returns a String, and after `Property Let PL(v$)` is given 12 it reads "got 12".
- `Property Get PN%()`, with `PN = 7.6`, returns Integer 8.
- **Unlike a Function (#740),** `Property Get Q$() As String` compiles, and `Property Get Q$() As Long` returns a Long:
  the As clause wins over the character.
- `Property Get Q$()` beside `Property Let Q(v$)` compiles. Read as `Q$`, `Q` is String "q", and its `TypeName` reads
  String either way.

**Refused, each a compile error:**

| Construct | vb6.exe |
|---|---|
| `Dim x% As Integer` | **"Expected: end of statement"** |
| `Dim s$ * 10` | **"Expected: end of statement"** |
| `Sub P(n% As Integer)` | **"Expected: list separator or )"** |
| `Dim x%` and then `Dim x$` in one module | **"Ambiguous name detected: x"** |
| `Dim x() As Long` and `ReDim x%(2)`, both at module level or both in one procedure | **"Type-declaration character does not match declared data type"** |
| `a%` as a member of a `Type` | **"Statement invalid inside Type block"** |
| `Sub P(n%)` given a Long variable, or a Variant one | **"ByRef argument type mismatch"** |

`ReDim x%(2)` in the form, with `x` a module's Private `Dim x() As Long` there, compiled and ran: the form cannot see
that `x`, so the ReDim declared a new local `Integer()`, as a ReDim of an undeclared name does.

**In HexIDE (#2409):**
- `Dim`, `Private`, `Public` and `Static` take the character's type, at module level and locally, arrays included.
  With an As clause as well, the declaration is VB6's compile error.
- A parameter's character is its type. With an As clause as well, it is VB6's compile error.
- `Property Get P$()` returns the character's type, and the As clause's when there is one.
- `ReDim x%(…)` on a declared array checks its element type.
- A ByRef argument of another type is copied rather than refused, as for a parameter declared with an As clause.
- `Dim s$ * 10` and a Type member's character fail to parse, where vb6.exe refuses to compile them.
- A standard module's Property Get is still not reached by name (#977), so its character is reached only in a class.
