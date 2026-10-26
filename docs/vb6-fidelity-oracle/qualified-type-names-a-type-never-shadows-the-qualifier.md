## Qualified type names: a `Type` never shadows the qualifier (2026-09-19)

**The question.** One name that is both the project and a user-defined `Type` inside it. When that name
qualifies a class, as in `New verify.Class`, which does VB6 bind it to? A literal reading of MS-VBAL §5.6.4
says the type binding context includes user-defined types, so the `Type` would win and `.Class` would fail
to resolve. Behaviour reported from MS-VBA is that it compiles and builds the class.

**The probe.** `scripts/vb6-oracle.ps1` always names its project `verify`, so a `Type` of that name recreates
the collision with nothing else changed. Every run adds one class module, literally named `Class` (not a
reserved word), and each question is a separate compile, because one compile failure fails the whole
program:

```powershell
$classes = @{ Class = 'Public Value As Long' }
$udt     = 'Option Explicit', 'Public Type verify', '    x As Long', 'End Type'

# R2
.\scripts\vb6-oracle.ps1 -Declarations $udt -Classes $classes 'TypeName(New verify.Class)'
# R3: the same collision after As, through a helper function in the declarations
#   Public Function AsQualified() As String
#       Dim c As verify.Class: Set c = New Class: AsQualified = TypeName(c)
#   End Function
# R4: the Type is still a type
#   Public Function UdtUnqualified() As String
#       Dim u As verify: u.x = 5: UdtUnqualified = TypeName(u.x) & "=" & CStr(u.x)
#   End Function
```

VB6 6.00.8176 (the original release), in the oracle guest. The harness sanity pair (`CByte(100) + CByte(100)` and
`CByte(200) + CByte(100)`) reproduced `200 | Byte` and `ERR6` in the same session.

| Run | Code | `Type verify` in scope | Result |
|---|---|---|---|
| R1 | `TypeName(New verify.Class)` | no | compiles, `Class` |
| R1 | `TypeName(New Class)` | no | compiles, `Class` |
| **R2** | `TypeName(New verify.Class)` | **yes** | **compiles, `Class`** |
| **R3** | `Dim c As verify.Class` | **yes** | **compiles, `Class`** |
| R4 | `Dim u As verify` | yes | compiles, the `Type`: `Long=5` |

R2 was reproduced in a second, separate run.

**The rule these fit.** In a qualified type name `A.B`, VB6 resolves `A` as a **container**: something that
can hold a type, such as the project. A `Type` or an `Enum` cannot hold a type, so it never competes for
that position, whatever its name. The final part `B` resolves normally. The rule holds after `New` (R2)
and after `As` (R3) alike, and it leaves an unqualified `As verify` finding the `Type` (R4). So "`New`
excludes user-defined types" is too narrow, since it misses R3, and "type contexts exclude user-defined
types" is too broad, since it breaks R4. The position of the name, qualifier or final part, is what
decides.

**Not measured, and worth doing before relying on the rule's edges:**

- An `Enum` named like the project, in place of the `Type`. Expected to behave the same; not yet shown.
- A bare `New verify` where `verify` is only a `Type`. Presumably a compile error; which one is not
  recorded.
- Three-part names, and a module name rather than the project name as the qualifier.
- MS-VBA (VBA7). This is VB6 only. The two share a language-engine lineage, but VB6 and VBA7 are known to
  diverge at the edges, and that is exactly where this sits.
- **HexIDE's own interpreter.** It resolves `New` at run time rather than binding ahead of time, and it has
  not been checked against this collision.


