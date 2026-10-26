## Another module's Public names, bare (2026-10-14, #880)

Can one standard module name another's Public variable or Const without its module's name in front, and where does
that stop? Each row below is its own project, built with `/make` and run, native and p-code agreeing on every one; a
row that does not compile is its own project because `/make` stops at the first error. Sub Main in Module1 is the
startup unless the row says otherwise, and Module1 holds the logger `L`. Module2 holds `Public G As Long`,
`Public Const M = 3` and `Global G2 As Integer` unless the row gives it something else.

| Module1 does | VB6 |
|---|---|
| `Init2` (Module2 sets `G = 5`, `G2 = 6`), then `TypeName(G) & " " & G & " " & TypeName(M) & " " & M & " " & G2` | `Long 5 Integer 3 6` |
| `G = 7`, then `Module2.G` | `7` |
| `Bump G`, Module1's `Sub Bump(n As Long)` adding 1, then Module2's `Show2()` returning `CStr(G)` | `8` |
| `Option Explicit` in Module1, then `M & " " & G & " " & G2` | `3 5 6` |
| Module2's `Private P As Long`, `Dim D As Long` and `Private Const PC = 9`, set by Module2: `TypeName` of each, bare | `Empty Empty Empty` |
| `Option Explicit`, then `L P` with Module2's `Private P` | does not compile: "Variable not defined" |
| `L X`, with `Public X As Long` in both Module2 and Module3 | does not compile: "Ambiguous name detected: X" |
| the same two `Public X`, set qualified and read `Module2.X & " " & Module3.X`; then Module2's own `L "own " & X` | `1 2`, then `own 1` |
| Module1's own `Dim X As Long` set to 1, Module2's `Public X` to 2: `X & " " & Module2.X` in Module1 | `1 2` |
| `L X`, with Module2's `Public X As Long` and Module3's `Public Sub X()` | does not compile: "Ambiguous name detected: X" |
| `L F`, with Module2's `Public F As Long` and Module3's `Public Function F() As Long` | does not compile: "Ambiguous name detected: F" |
| `vbCancel & " " & TypeName(vbCancel)`, with Module2's `Public Const vbCancel = 42` | `42 Integer` |
| Class1's `GetG = G`, then its `G = 11`, then `Module2.G` from Module1 | `5`, then `11` |
| Form1 as the startup: Load's `L G & " " & M`, then `G = 4` and `L Module2.G` | `0 3`, then `4` |
| Module1's module-level `Const A = M + 1` and `Dim d(M) As Long`, Module1 listed first: `A & " " & UBound(d)` | `4 3` |
| Module1's own `Function V() As String` returning "own fn", with Module2's `Public V As String`: `L V` | `own fn` |
| Module2's `Public A() As Long`: `ReDim A(4)`, `A(2) = 9`, then `UBound(Module2.A) & " " & Module2.A(2) & " " & A(2)` | `4 9 9` |
| Module2's `Public C As Collection`: `Set C = New Collection`, `C.Add 1`, then `Module2.C.Count & " " & C.Count` | `1 1` |
| Module2's `Public P As PT`, a Public Type with `X As Long`: `P.X = 3`, then `Module2.P.X & " " & P.X` | `3 3` |
| `M = 5` with Module2's `Public Const M = 3` | does not compile: "Assignment to constant not permitted" |
| a procedure's `Dim G As String` set to "local", with Module2's `Public G`: `G & " " & Module2.G` | `local 0` |

What these show:
- **A standard module's Public names are the project's.** A Public variable, a Public Const and a `Global` are read,
  written and passed ByRef bare from another module, with or without Option Explicit, and from a class's code and a
  form's. The obvious alternative, that only procedures cross modules bare, is what HexIDE did; it read each as Empty.
- **Private stays private.** A `Private` variable, a module-level `Dim` and a `Private Const` are each a new variable
  bare from another module, and "Variable not defined" under Option Explicit.
- **The module's own names come first:** its own module-level variable, its own Function, and a procedure's local.
  In a form, the form's own members come before them too (L2, *A form as an object*).
- **A clash is an error only where it is used.** Two modules exporting `X` compile, each reads its own, and qualified
  reads work; only the bare use from a third module is "Ambiguous name detected". A Public variable beside another
  module's Public Sub or Function of the same name is ambiguous in the same way, so variables and procedures share one
  namespace here.
- **The project comes before the library** at run time as in a bound: `vbCancel` is Module2's 42.
- **A store into another module's Public Const** is refused as one into the module's own is.

### HexIDE

Before #880, a name a module did not declare itself was looked for only among other modules' Public procedures, so
every row that reads or writes another module's variable or Const bare read Empty, or was "Variable not defined" under
Option Explicit; the Const store made a new variable; a bound or Const naming one was refused naming #880.

Since #880, each module sees the other standard modules' Public variables and Consts bare, behind its own names. The
table is collected from the pre-pass's lists of each module's Public names (`ProjectNames`), and a name is resolved to
its slot only when the walk reads it. Every row above matches, and is pinned in `PublicNamesAcrossModulesTests`,
`FormInstanceTests`, `ModuleLevelArrayBoundTests` and `ConstStoreTests`, with three rows changed in shape because of
gaps elsewhere: the probes that stored through a qualifier (`Module2.X = 1`) have each module store its own, because a
qualified store raises 424 (#1006); `Module2.A(2)` is read through a Function of Module2's, because it reads the whole
array (#965); and the Collection row uses a class, because HexIDE cannot create a Collection with `New` yet.

Since #893, a module-level Const or bound initialises a referenced Const's module on demand, in either list order,
and refuses module cycles with VB6's message. Another module's Public Enum member, bare, is read since #889 (*Another module's Enum members, bare*).

