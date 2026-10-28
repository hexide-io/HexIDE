## A name read with a type character (2026-10-27, #742)

**The question.** In HexIDE a read of `s$`, `i%` or `arr%(1)` stops the run, "Type hint is not supported". What does
vb6.exe do with the character on a read, whatever the name is: a local, a module or Public variable, a constant, an
array, a parameter or a Function? What if the character doesn't match the declared type, the name is a Variant or an
object, or the name was never declared?

**The probe.** One program and eight one-construct projects, each built native and p-code with the guest's vb6.exe and
run there.
- The program's Module2 is `Option Explicit` and declares one name of each kind. It reads each through its character
  and logs `TypeName` and `CStr`.
- Module3 has no `Option Explicit` and reads names it never declares.
- The form reads Module2's Public variables through their characters.

Native and p-code logged the same 21 lines, and each compile probe failed the same way both ways.

**Result: the character names the declared type, and the read is the name's ordinary read.**

| Declared | Read as | vb6.exe |
|---|---|---|
| `Dim lS As String`, `lI As Integer`, `lL As Long`, in the procedure | `lS$`, `lI%`, `lL&` | String "ls", Integer 9, Long 8 |
| `Public pS As String, pI As Integer` in a module | `pS$`, `pI%`, in the module and from the form | String "ps", Integer 4 |
| `Private mL As Long, mD As Double, mC As Currency, mG As Single` | `mL&`, `mD#`, `mC@`, `mG!` | Long 70000, Double 2.5, Currency 1.25, Single 1.5 |
| the same `lI` and `lS` | `lI% + 1`, `lS$ & "x"` | Integer 10, String "lsx" |
| `Public Const KS$ = "konst"`, `KI% = 3` | `KS$`, `KS`, `KI%` | String "konst" twice, Integer 3 |
| `Const KL As Long = 7` | `KL&` | Long 7 |
| `Dim arrI(2) As Integer`, and `arrS() As String` after `ReDim arrS(1)` | `arrI%(1)`, `arrS$(1)`, `UBound(arrI%)` | Integer 5, String "e", 2 |
| `Public Function Twice%(ByVal n As Integer)` | `Twice%(4)`, `Twice(4)` | Integer 8 each |
| the parameters of `Sub ReadParam(n%, s As String)` | `n%`, `s$` | Integer 6, String "q" |
| `pS` as above | `Module2.pS$` | String "ps" |

**Undeclared, in a module without `Option Explicit`.** The character on a name's first appearance declares it, just as
it does on an assignment:
- `u$` read first is the String "", not Empty.
- After `w% = 3`, both `w%` and `w` read the Integer 3.
- `x&` read first is the Long 0, and so is the bare `x` after it.

**Refused, each a compile error on the line of the read, native and p-code alike:**

| Construct | vb6.exe |
|---|---|
| `Dim v As Long`, then `v%` | **"Type-declaration character does not match declared data type"** |
| `Dim s As String`, then `s%` | the same |
| `Dim v`, a Variant, then `v$` | the same: a Variant takes no character |
| `Dim o As Object`, then `TypeName(o$)` | the same |
| `Const K As Long = 1`, then `K%` | the same |
| `Dim a(2) As Long`, then `a%(1)` | the same |
| `Function F%()`, then `F$()` | the same |
| `u%` never declared, under `Option Explicit` | **"Variable not defined"** |

So a character on a read is a check, not a conversion. The name is looked up without it. The character must then be the
type the name was declared with, and a Variant, an object or a mismatched type is a compile error. A name the module has
never seen, outside `Option Explicit`, is declared with that type by the read itself.

**The rest, measured the same day.** One construct per project, native and p-code alike:

| Declared | Read as | vb6.exe |
|---|---|---|
| `Const K = 5`, no As clause | `K%` | Integer 5 |
| the same | `K&`, `K$` | compile error: the character must be the value's type |
| `Const S = "a"`; `Const D = 1.5`; `Const K = 70000` | `S$`; `D#`; `K&` | String "a"; Double 1.5; Long 70000 |
| `Enum E: E1 = 3` | `E1&` | Long 3 |
| the same | `E1%` | compile error |
| `Sub P(ByVal n%)` | `n`, `n%` | Integer 3, each |
| `Sub P(v)`, a Variant parameter | `v$` | compile error |
| `Dim b As Byte`; `Dim bo As Boolean`; `Dim d As Date` | `b%`; `bo%`; `d#` | compile error, each: those types have no character |
| `Dim va(2)`, a Variant array | `va$(1)` | compile error |
| `Dim a(2) As Integer` | `a%` passed to `TypeName` and `UBound` | "Integer()", 2 |
| `Dim a() As Integer`, never allocated | `UBound(a%)` | compiles; fails only at run time, as `UBound(a)` does |
| under `DefInt A-Z`, `Dim y` | `y%`; `y&` | Integer 4; compile error |
| under `DefInt A-Z`, `x$` never declared, no `Option Explicit` | `x$`, then `x` | String "" each: the character wins over the DefType |
| never declared, no `Option Explicit` | `z@`, then `z` | Currency 0 each |
| `Dim q`, then `q = q$ & "x"` | | compile error |

All of this is one rule. The name is looked up without the character, and its type must be the character's. That type
is the declared one, or for a Const without an As clause its value's, and an Enum member's is Long. A name never
declared takes the character's type, ahead of a DefType.

**In HexIDE, when measured:** every one of the reads above stops the run, except the program's own Function (#740) and
the library's `$` functions. `ExpressionExecutor.ResolveImplicitCall` and the call path still throw for any other name.
