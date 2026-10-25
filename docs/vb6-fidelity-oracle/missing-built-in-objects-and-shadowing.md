## Missing built-in objects and shadowing (#871, 2026-10-17)

Measured with `scripts/vb6-oracle.ps1`, native and p-code (`CompilationType=-1`), with identical results:

| Expression | Value | TypeName of result |
|---|---|---|
| `TypeName(Screen)` | `Screen` | `String` |
| `TypeName(Clipboard)` | `Clipboard` | `String` |
| `TypeName(Printer)` | `Printer` | `String` |
| `TypeName(Printers)` | `Object` | `String` |
| `TypeName(Forms)` | `Object` | `String` |

For each of the five names, this declaration and expression `Shadowed()` returned `local`, type `String`,
under both compilation modes (substitute each name for `Screen`):

```vb
Option Explicit
Function Shadowed() As String
    Dim Screen As String
    Screen = "local"
    Shadowed = Screen
End Function
```

A user function also shadows each global. Supplying the following declaration and evaluating bare `Screen`
returned `function`, type `String`, for all five substituted names, native and p-code:

```vb
Function Screen() As String
    Screen = "function"
End Function
```

HexIDE still does not implement these objects. Its unresolved bare and member reads now throw
`NotImplementedException` before the implicit Empty fallback or the Option Explicit undeclared-name error.
This engine limitation cannot be swallowed by `On Error`. Normal name resolution runs first so declared
variables and user functions keep their measured precedence. Indexed object access and assignment behavior
are outside this change.

