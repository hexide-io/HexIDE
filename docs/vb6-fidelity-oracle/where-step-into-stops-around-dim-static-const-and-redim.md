## Where Step Into stops around Dim, Static, Const and ReDim (2026-11-21, #4326, IDE, SP6)

**The question.** HexIDE's Step Into stops on a `Dim` line as though it were a statement (#4326). It's commonly said
that VB6's F8 never does, because a `Dim` isn't executable. Where does VB6's F8 stop when a procedure starts with a
`Dim`, a `Static`, a `Const` or a `ReDim`, and when one of them sits between two statements?

**The probe:**
- **The IDE:** VB6.EXE 6.00.9782 (SP6), in the console session of a clone of the oracle guest. It was driven through
  the console agent, as "Reaching the VB6 *IDE*, not just the compiler" in the README describes.
- **The keys:** F8 from design mode, then F8 again, 32 presses in all, until the IDE was back in design mode. Each
  went to the IDE's window through `SendKeys`.
- **What was recorded after each key:**
  - the IDE's title;
  - a screenshot;
  - the caret's line, copied with Home, Shift+End and Ctrl+C.
- **Why the caret line is the stop:** a pause puts the caret on the line it stops at. In three screenshots, after the
  5th, 17th and 23rd F8, the yellow current-statement bar and its arrow were on the copied line.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.
- **The program:** a Standard EXE whose startup is `Sub Main`, with this module and nothing else:

```vb
Option Explicit
Dim n As Integer

Sub Main()
    n = 1
    ProcDim
    ProcStatic
    ProcConst
    ProcReDim
    ProcMid
    ProcOnlyDim
    n = 99
End Sub

Private Sub ProcDim()
    Dim dimA As Integer
    dimA = 11
End Sub

Private Sub ProcStatic()
    Static stB As Integer
    stB = 22
End Sub

Private Sub ProcConst()
    Const kC = 33
    n = kC
End Sub

Private Sub ProcReDim()
    ReDim rD(2) As Integer
    rD(1) = 44
End Sub

Private Sub ProcMid()
    n = 55
    Dim midE As Integer
    midE = 66
    Static midF As Integer
    midF = 77
    Const midG = 88
    n = midG
End Sub

Private Sub ProcOnlyDim()
    Dim onlyZ As Integer
End Sub
```

**Where each F8 stopped.** `Sub Main` stopped at `Sub Main()`, `n = 1`, then at each call line before stepping into
the call. Inside each procedure:

| Procedure | VB6's stops, in order |
|---|---|
| `ProcDim` | `Private Sub ProcDim()`, `dimA = 11`, `End Sub` |
| `ProcStatic` | `Private Sub ProcStatic()`, `stB = 22`, `End Sub` |
| `ProcConst` | `Private Sub ProcConst()`, `n = kC`, `End Sub` |
| `ProcReDim` | `Private Sub ProcReDim()`, **`ReDim rD(2) As Integer`**, `rD(1) = 44`, `End Sub` |
| `ProcMid` | `Private Sub ProcMid()`, `n = 55`, `midE = 66`, `midF = 77`, `n = midG`, `End Sub` |
| `ProcOnlyDim` | `Private Sub ProcOnlyDim()`, `End Sub` |

After `ProcOnlyDim`'s `End Sub`, F8 stopped at `n = 99`, then at `Sub Main`'s `End Sub`, and the 32nd F8 left the IDE
in design mode.

**Readings:**
- **F8 never stopped on a `Dim`, `Static` or `Const` line.** That held for one as a procedure's first line and for
  one between two statements. The step went from the line before it straight to the next executable statement.
- **`ReDim` is a statement, and F8 stopped on it.**
- **A procedure whose body is only a `Dim`** stopped at its declaration and then at its `End Sub`.
- This agrees with F9: VB6 refuses a breakpoint on a `Dim` line ("Breakpoint not allowed on this line", in
  [A breakpoint on a procedure's End line or its header](a-breakpoint-on-a-procedures-end-line-or-its-header.md)).

### In HexIDE

#4326 records `step_into` stopping on `Dim a As Integer` on a Debug build of `fix/4261-codex` (e96b240b2), on
2026-11-16. That wasn't checked again for this page.

### Not measured

- `Dim x As New Class1`, and a `Dim` of a fixed-length string or an array with bounds;
- F9 on a `Static` or a `Const` line;
- Step Over (Shift+F8) and Step Out across these lines. Only F8 was pressed.
