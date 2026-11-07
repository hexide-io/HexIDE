## Another module's Enum members, bare (2026-10-22, #889)

**The question.** A member of another module's Public Enum, read unqualified, read as Empty in HexIDE, with no error
without `Option Explicit`. What does VB6 see, and where does a name clash?

**The probes.** Module2 has `Public Enum Cols` (`colA`, `colB`, `colMax`) and `Private Enum Hidden` (`hidA = 7`). Class1
has `Public Enum ClsE` (`clsA = 40`) and a Function returning `colMax`, and so does Form1. Sub Main in Module1, under
`Option Explicit`, logs each read. Probes that VB6 refuses to build are a project each, read from `/make`'s answer. Made
native and p-code; every log and every refusal was identical.

| In Module1 | VB6 |
|---|---|
| `colMax` / `TypeName(colMax)` | `2` / `Long` |
| `colA + colMax` | `2` |
| `For i = 0 To colMax` | runs 3 times |
| `Private Const K = colMax + 1` | `3`, a Long |
| `Private d1(colMax) As Long` | 0 To 2 |
| `Module2.colMax` / `Cols.colB` | `2` / `1` |
| `clsA`, Class1's Public Enum member | `40`, a Long |
| `Private Const KC = clsA + 1` / `Private d2(clsA) As Long` | `41`, a Long / 0 To 40 |
| a Function in Class1, and one in Form1, returning `colMax` | `2`, each |
| `Dim colB As Integer: colB = 9`, then `colB` / `colA` | `9`: the local comes first / `0` |
| `hidA`, Module2's Private Enum member | compile error, "Variable not defined" |
| `colA = 5` | compile error, "Assignment to constant not permitted" |
| `colB`, with Module3's `Public Enum E3` also declaring `colB = 20` | compile error, "Ambiguous name detected: colB" |
| `Cols.colB`, `E3.colB`, `Module3.colB`, the same project | `1`, `20`, `20` |
| `Private Const KB = colB`, the same project | compile error, "Ambiguous name detected: colB" |
| `colB`, with Module3's `Public colB As Long` | compile error, "Ambiguous name detected: colB" |

**What it shows:**
- **A Public Enum's members are project-wide names,** as a standard module's Public variables and Consts are (*Another
  module's Public names, bare*), and **a class module's are too**. Each is a Long constant, so it sizes an array and
  folds into a Const.
- **They clash as those do.** Two modules' members of one name, or a member beside another module's Public variable,
  are ambiguous where used bare, and only there: qualified, each reads its own.
- **A local, and the module's own names, come first.**

### In HexIDE (#889)

- `ProjectNames` lists each other standard or class module's Public Enum members beside the Public variables and Consts,
  so a bare read finds the read-only slot that module's pre-pass hoisted the member into. Assigning it is refused as a
  constant, as VB6 refuses it.
- A module-level Const or bound reads such a member as the constant it is, where #873 refused it, and two of them are
  ambiguous there too.
- **Not measured:** an Enum declared in a form, which HexIDE leaves out of the table, and a member beside another
  module's Public Sub or Function of the name, which HexIDE treats as it treats a Public variable there: ambiguous.

