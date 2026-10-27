## A breakpoint on a procedure's End line or its header (2026-10-26, #2172, IDE)

**The question.** In HexIDE, a breakpoint on a procedure's `End Sub` line was accepted and never paused the run. Does a
breakpoint there pause VB6's IDE, and where does a step from a procedure's last statement stop?

**The probe.** VB6.EXE 6.00.8176 SP6, the IDE, in the guest's console session through the agent ("Reaching the VB6
*IDE*, not just the compiler" in the README). The project is a Standard EXE whose Form1 holds the code below. Each
procedure appends a line to `log.txt` through `L`, so the log shows how far the program got at each pause. Lines 1 to 10 are `Option Explicit` and `L`.
Breakpoints were set with F9 on lines counted from `Option Explicit` as line 1, as the numbers in the comments below
give them. The run was started with F5 and continued with F5 at each pause. At each pause the
IDE's title, the log and a screenshot were recorded. The IDE's toolbar shows the caret's line as "Ln n, Col 1", and a
pause puts the caret on the line it stops at. This is IDE behaviour, so there is no native or p-code build to compare.

```vb
Private Sub Form_Load()           ' 11
    L "before"
    Helper                        ' 13
    L "after helper"              ' 14
    Dim x As Long                 ' 15
    x = F()                       ' 16
    L "after f " & x              ' 17
    x = P
    L "after p " & x
    Helper2
    L "after helper2"
End Sub                           ' 22

Private Sub Helper()              ' 24
    L "in helper"                 ' 25
End Sub                           ' 26

Private Function F() As Long      ' 28
    L "in f"
    F = 1                         ' 30
End Function                      ' 31

Private Property Get P() As Long  ' 33
    L "in p"
    P = 2
End Property                      ' 36

Private Sub Helper2()             ' 38
    Dim y As Long                 ' 39
    y = 3
    L "in helper2 " & y
End Sub
```

**Breakpoints on lines 26, 31, 36, 22, 38 and 39.**

| Pause | Highlighted line | The log so far |
|---|---|---|
| 1 | 26, Helper's `End Sub` | `before`, `in helper` |
| 2 | 31, `End Function` | … `after helper`, `in f` |
| 3 | 36, `End Property` | … `after f 1`, `in p` |
| 4 | 38, `Private Sub Helper2()` | … `after p 2` |
| 5 | 22, Form_Load's `End Sub` | … `in helper2 3`, `after helper2` |
| then | | the run carried on, title `[run]` |

- **A breakpoint on `End Sub`, `End Function` or `End Property` pauses the run** after the procedure's last statement and
  before it returns. The line is highlighted as the next statement, and the caller's next line hasn't run.
- **A breakpoint on a procedure's declaration line pauses on entry,** with the declaration highlighted, before the
  first statement of the body runs.
- **F9 on a `Dim` line is refused** with a message box captioned "Microsoft Visual Basic": "Breakpoint not allowed on
  this line", with OK and Help. No breakpoint is set.

**Stepping onto the End line.** A second run had breakpoints on line 25, Helper's only statement, and line 30, `F = 1`.
At each pause it pressed Step Over (Shift+F8) twice:

| From | First Shift+F8 | Second Shift+F8 |
|---|---|---|
| line 25, `L "in helper"` | line 26, `End Sub` | line 14, the caller's next line |
| line 30, `F = 1` | line 31, `End Function` | line 17, the caller's next line |

So a step from a procedure's last statement stops on its `End` line, and the step after that returns to the caller and
stops on the caller's next statement. It doesn't stop on the calling line again, even when the call is part of an
assignment, as in `x = F()`.

### In HexIDE

The debugger's gate runs before each statement of a procedure's body, and a procedure's declaration line and its End line
aren't statements. So neither can pause:
- The `End Sub` breakpoint is #2172, found on a live run.
- A breakpoint on a declaration line is accepted and, going by the code, never hit either.
- A step from the last statement goes straight to the caller (debugger divergence D20).
