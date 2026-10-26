## Command, and Left$ in a form's code (2026-10-17, #742)

**The probe.** Found by the Notepad demo, whose `Form_Load` reads `Trim$(Command$)` and then takes `Left$(path, 1)`, and
which stopped on both. A form whose `Form_Load` logs to a file under `On Error Resume Next`, in a project with
`Command32="hello world"`, built native and p-code and run with no arguments. Both builds agreed.

| Read, in the form's own code | Logged |
|---|---|
| `TypeName(Left$("abcdef", 2))`, `Left$("abcdef", 2)` | `String`, `ab` |
| `TypeName(Left("abcdef", 2))`, `Left("abcdef", 2)` | `String`, `ab` |
| `Left$(Null, 2)` | raises: the line was skipped, as 94 is for any `$` form given Null |
| `TypeName(Left(Null, 2))` | `Null` |
| `TypeName(Me.Left)` | `Single` |
| `Command$`, and `TypeName` of `Command$`, `Command` and `Command()` | `""`, and `String` for each |

So with arguments `Left$` is VBA's function in a form's code, exactly as `Left` is, and the form's Left property, which
takes none, is only reached without them. And a made program ignores the project's `Command32`: run with no arguments,
its `Command$` is empty.

**In HexIDE (#742):** `Left$` with arguments in a form's code runs as VBA's `Left$`. `Command`, `Command()` and
`Command$` give the project's `Command32`, empty where there is none. That half is chosen rather than measured: VB6
gives `Command32` under F5, and HexIDE always runs as F5 does. A made program's own process arguments are not passed.

