## A line number on a closing or middle line of a block (2026-10-28, #2529)

The column for HexIDE is what `upstream/main` did on 2026-10-28, when this was measured.

**Measured on vb6.exe SP6.** Each form was its own probe, `Sub Main` with the numbers below, built as native code with `/make` and run (not as p-code):

| Numbered line | vb6.exe | HexIDE (interpreter, `upstream/main`) |
|---|---|---|
| `30 Next` and `30 Next i`, closing a `For` | compiles and runs | Compile error (parse) |
| `30 Next`, closing a `For Each` | compiles and runs | Compile error (parse) |
| `30 End If` | compiles and runs | Compile error (parse) |
| `30 Else` and `30 ElseIf i = 0 Then` | compiles and runs | Compile error (parse) |
| `30 Loop While i < 3` and `30 Loop` | compiles and runs | Compile error (parse) |
| `30 Wend` | compiles and runs | Compile error (parse) |
| `40 Case Else`, `40 Case 0` after a first `Case`, `60 End Select` | compiles and runs | Compile error (parse) |
| `30 End With` | compiles and runs | Compile error (parse) |
| `90 End Sub` | compiles and runs | Compile error (parse) |
| `20 Case 0`, **the first `Case`** after `Select Case` | **refused**: "Statements and labels invalid between Select Case and first Case" | Compile error (parse) |
| control: `10 s = "x"`, `20 i = 1` | compiles and runs | runs |

In every accepted case the program logged the same result it gives unnumbered (the loops counted to 3, the `If` branches took the expected arm).

**Not measured yet:** what a `GoTo` to a number on a closing line does (#2529 measures it).
