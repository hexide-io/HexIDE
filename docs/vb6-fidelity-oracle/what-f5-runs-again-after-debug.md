## What F5 runs again after Debug: an ElseIf, a Loop Until, a single-line If's tail and a Case (2026-11-21, #1079, IDE, SP6)

**The question.** After an unhandled run-time error, Debug and then F5 retry the faulting statement.
[An unhandled run-time error](an-unhandled-run-time-error.md) measured one case: for `x = F() + 1 \ 0`, the whole
statement runs again, and `F` is called again. HexIDE chose a granularity for four more places without measuring them
(#1079). What does VB6 run again when the fault is in an `ElseIf`'s condition, a `Loop Until`'s condition, a
single-line `If`'s tail, or a `Case`'s expression?

**The probe:**
- **The IDE:** VB6.EXE 6.00.9782 (SP6), in the console session of a clone of the oracle guest. It was driven through
  the console agent, as "Reaching the VB6 *IDE*, not just the compiler" in the README describes.
- **The keys:** F5 from design mode. At each run-time error dialog, its Debug button was pressed, then F5.
- **What was recorded** at each dialog, at each break, and at the end:
  - the IDE's title;
  - the log;
  - a screenshot;
  - the caret's line at the break, and in the screenshot, the statement highlighted as the current one.
- **No Immediate window.** The divisor comes from `Dz`, which counts its calls and returns 0 on the first and 1 after.
  So the retry succeeds without editing anything, and every call is in the log. Each function logs its own name.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.
- **The program:** a Standard EXE whose startup is `Sub Main`, in one module with Error Trapping at its default, *Break
  in Class Module*. `L` appends a line to a log, and `nd`, `n` are module-level `Integer`s.

```vb
Function Dz() As Long
    nd = nd + 1
    L "Dz " & nd
    If nd = 1 Then Dz = 0 Else Dz = 1
End Function

Function Fx() As Boolean
    L "Fx"
    Fx = False
End Function

Function Tx() As Integer
    L "Tx"
    Tx = 5
End Function

Function E1() As Integer
    L "E1"
    E1 = 0
End Function

Sub Main()
    L "case1"
    nd = 0
    If Fx() Then
        L "then1"
    ElseIf 1 \ Dz() > 0 Then
        L "elseif1"
    End If
    L "case2"
    nd = 0
    n = 0
    Do
        n = n + 1
        L "body " & n
    Loop Until 1 \ Dz() > 0
    L "case3"
    nd = 0
    n = 0
    If True Then n = n + 1: L "tail " & (1 \ Dz())
    L "n3 " & n
    L "case4"
    nd = 0
    Select Case Tx()
        Case Is < E1()
            L "case4 first"
        Case Is > 1 \ Dz()
            L "case4 second"
    End Select
    L "end"
End Sub
```

**What happened.** Each fault raised "Run-time error '11': Division by zero". Debug broke with the highlight shown:

| Case | The break, on Debug | Logged before the fault | Logged after F5, before the next case |
|---|---|---|---|
| `ElseIf` | `ElseIf 1 \ Dz() > 0 Then` | `case1`, `Fx`, `Dz 1` | `Dz 2`, `elseif1` |
| `Loop Until` | `Loop Until 1 \ Dz() > 0` | `case2`, `body 1`, `Dz 1` | `Dz 2` |
| Single-line `If`'s tail | **`L "tail " & (1 \ Dz())` alone**, highlighted from its colon. The rest of the line wasn't | `case3`, `Dz 1` | `Dz 2`, `tail 1`, `n3 1` |
| `Case` | `Case Is > 1 \ Dz()` | `case4`, `Tx`, `E1`, `Dz 1` | `Dz 2`, `case4 second` |

The run then logged `end` and returned to design mode.

**Readings:**
- **`ElseIf`:** F5 ran the `ElseIf`'s condition again, alone. `Fx`, the `If`'s own condition, wasn't called again.
- **`Loop Until`:** F5 ran the condition again, alone. The body didn't run a second time: there's no `body 2`.
- **A single-line `If`'s tail:** F5 ran the faulting tail statement again, alone. `n = n + 1` didn't run again, so
  `n3 1`. The break's highlight agrees: it covers the tail statement only.
- **`Case`:** F5 ran the faulting `Case`'s expression again, alone. `Tx`, the test expression, and `E1`, the earlier
  `Case`'s expression, weren't called again.
- **Across all four,** nothing that ran before the fault in the same construct ran again.

### In HexIDE

#1079 records HexIDE's choices, pinned by `HaltOnErrorTests` and not checked again for this page:
- the `ElseIf`'s condition alone, the `Loop Until`'s condition alone, and the tail statement alone. All three are
  what VB6 does;
- **the whole `Select Case` again**, test expression and earlier `Case`s included. VB6 runs only the faulting `Case`'s
  expression again, so this one differs.

### Not measured

- a fault in a block `If`'s own condition, or in `Select Case`'s test expression;
- a `Case` with a list, such as `Case 1, 1 \ d`, faulting in its second item;
- a `For` loop's bounds or step, a `Do While` condition at the top, and a `With`'s object;
- Step Into (F8) from the break, in place of F5.
