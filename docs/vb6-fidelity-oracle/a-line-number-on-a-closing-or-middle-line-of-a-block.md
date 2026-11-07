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

**What a `GoTo` to each such line does,** measured native and p-code (one probe per row, each `Sub Main` with
`On Error GoTo h` and a handler logging `err`, `erl`, `s` and `i`; `ok` is the line after the block):

- **`Next`, `Loop` (with or without a condition) and `Wend`**, from inside the loop: the loop goes round, as a `Continue`
  would. The condition or counter is tested as usual.
- **`Loop While` and `Wend`, from before the loop starts:** the condition is tested, and the loop runs if it holds.
- **`Next`, from before its `For`:** native code **hung** (the run timed out), and p-code ran on with `i` 1. Neither raised
  an error.
- **`Else`, `ElseIf`, a later `Case` and `Case Else`**, from inside the branch before it or from before the block: control
  goes to the **`End If` or `End Select`**. The `Else` part does not run, and an `ElseIf` or `Case` is not tested.
- **`End If`, `End Select` and `End With`:** control goes on after the block.
- **`End Sub`:** the procedure returns.

| Probe | Lines | Logged, native | Logged, p-code |
|---|---|---|---|
| g-case-inside | `10 Select Case i` / `Case 0` / `30 GoTo 40` / `35 s = "zero"` / `40 Case 5` / `50 s = s & "five"` / `60 End Select` | ok [] i 0 | same |
| g-caseelse-inside | `10 Select Case i` / `Case 0` / `30 GoTo 40` / `35 s = "zero"` / `40 Case Else` / `50 s = s & "other"` / `60 End Select` | ok [] i 0 | same |
| g-caseelse-outside | `GoTo 40` / `10 Select Case i` / `Case 0` / `30 s = "zero"` / `40 Case Else` / `50 s = s & "other"` / `60 End Select` | ok [] i 0 | same |
| g-else-from-then | `i = 1` / `10 If i = 1 Then` / `20 GoTo 30` / `25 s = "then"` / `30 Else` / `40 s = "else"` / `50 End If` | ok [] i 1 | same |
| g-else-outside | `i = 1` / `GoTo 30` / `10 If i = 1 Then` / `20 s = "then"` / `30 Else` / `40 s = "else"` / `50 End If` | ok [] i 1 | same |
| g-elseif-from-then | `i = 1` / `10 If i = 1 Then` / `20 GoTo 30` / `25 s = "then"` / `30 ElseIf i = 1 Then` / `40 s = "elseif"` / `50 End If` | ok [] i 1 | same |
| g-endif-inside | `10 If i = 0 Then` / `20 GoTo 30` / `25 s = "skipped"` / `30 End If` / `35 s = s & "after"` | ok [after] i 0 | same |
| g-endif-outside | `GoTo 30` / `10 If i = 0 Then` / `25 s = "inside"` / `30 End If` / `35 s = s & "after"` | ok [after] i 0 | same |
| g-endselect-inside | `10 Select Case i` / `Case 0` / `30 GoTo 60` / `35 s = "zero"` / `40 Case Else` / `50 s = s & "other"` / `60 End Select` / `65 s = s & "after"` | ok [after] i 0 | same |
| g-endsub | `Sub T()` / `10 L "T starts"` / `20 GoTo 90` / `30 L "not reached"` / `90 End Sub` / `Sub Main()` / `T` / `L "g-endsub: back in Main"` | T starts · back in Main | same |
| g-endwith-inside | `10 With c` / `20 GoTo 30` / `25 .Add 2` / `30 End With` / `35 s = CStr(c.Count)` | ok [0] i 0 | same |
| g-loop-inside | `10 Do` / `20 i = i + 1` / `25 If i < 3 Then GoTo 30` / `27 s = s & i` / `30 Loop While i < 5` | ok [345] i 5 | same |
| g-loop-outside | `GoTo 30` / `10 Do` / `20 i = i + 1` / `30 Loop While i < 3` | ok [] i 3 | same |
| g-loopbare-inside | `10 Do While i < 4` / `20 i = i + 1` / `25 If i = 2 Then GoTo 30` / `27 s = s & i` / `30 Loop` | ok [134] i 4 | same |
| g-next-inside | `10 For i = 1 To 3` / `20 If i = 2 Then GoTo 30` / `25 s = s & i` / `30 Next` | ok [13] i 4 | same |
| g-next-outside | `GoTo 30` / `10 For i = 1 To 3` / `25 s = s & i` / `30 Next` | run exit: timeout · NO LOG | ok [] i 1 |
| g-wend-inside | `10 While i < 3` / `20 i = i + 1` / `25 If i = 1 Then GoTo 30` / `27 s = s & i` / `30 Wend` | ok [23] i 3 | same |
| g-wend-outside | `GoTo 30` / `10 While i < 3` / `20 i = i + 1` / `30 Wend` | ok [] i 3 | same |

**`Erl` for an error raised on one of these lines** reads that line's number: a `Loop While` condition on 30 is 30, an
`ElseIf` condition on 30 is 30, a `Case` expression on 40 is 40, and a `Next` that overflows its counter on 30 is 30. A
`Do While` or `While` condition asked again after `30 Loop` or `30 Wend` reads 10, the line it is written on (see the
`Erl` page).

| Probe | Lines | Logged, native | Logged, p-code |
|---|---|---|---|
| e-case | `10 Select Case i` / `Case 1` / `20 s = "one"` / `40 Case 1 / i` / `50 s = "b"` / `60 End Select` | err 11 erl 40 [] i 0 | same |
| e-elseif | `10 If i = 1 Then` / `20 s = "a"` / `30 ElseIf 1 / i > 0 Then` / `40 s = "b"` / `50 End If` | err 11 erl 30 [] i 0 | same |
| e-loopbare | `10 Do While 1 / (2 - i) > -5` / `20 i = i + 1` / `30 Loop` | err 11 erl 10 [] i 2 | same |
| e-loopcond | `10 Do` / `20 i = i + 1` / `30 Loop While 1 / (2 - i) > -5` | err 11 erl 30 [] i 2 | same |
| e-next | `10 For b = 32766 To 32767` / `20 s = s & "x"` / `30 Next` | err 6 erl 30 [xx] i 0 | same |
| e-next-step | `10 For b = 1 To 3 Step 1 / i` / `20 s = s & "x"` / `30 Next` | err 11 erl 10 [] i 0 | same |
| e-wend | `10 While 1 / (2 - i) > -5` / `20 i = i + 1` / `30 Wend` | err 11 erl 10 [] i 2 | same |
