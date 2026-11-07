## A label on a closing or middle line of a block (2026-10-28, #2529)

**Measured on vb6.exe SP6, native and p-code, which logged the same lines.** A named label on the line that closes a block,
or opens its next part, behaves exactly as a line number there does. `Skip: Next` is the usual way to go round a loop early;
a `GoTo` to it from inside the loop goes round, and one to `Skip: Else`, `Skip: Case Else`, `Skip: End If`,
`Skip: End Select` or `Skip: End With` from inside ends the block it is in. A number and a label may share the head,
`30 Skip: Next`. A label before the first `Case` is refused, "Statements and labels invalid between Select Case and first
Case", as a number there is.

HexIDE read `Skip` in `Skip: Next` as a call to a procedure named Skip, so the loop died with "Sub or Function not defined"
the first time it reached the line. The corpus recorded that divergence as the tree-walking limit; it was not one, since the
label marks the end of the block before it (#2529).

Each probe is a `Sub Main` with `On Error GoTo h` and a handler logging `err`, `erl`, `s` and `i`; `ok` is the line after the
block:

| Probe | Lines | Logged, native | Logged, p-code |
|---|---|---|---|
| l-caseelse-inside | `Select Case i` / `Case 0` / `GoTo Skip` / `s = "zero"` / `Skip: Case Else` / `s = s & "other"` / `End Select` | ok [] i 0 | same |
| l-else-from-then | `i = 1` / `If i = 1 Then` / `GoTo Skip` / `s = "then"` / `Skip: Else` / `s = "else"` / `End If` | ok [] i 1 | same |
| l-endif-inside | `If i = 0 Then` / `GoTo Skip` / `s = "skipped"` / `Skip: End If` / `s = s & "after"` | ok [after] i 0 | same |
| l-endselect-inside | `Select Case i` / `Case 0` / `GoTo Skip` / `s = "zero"` / `Skip: End Select` / `s = s & "after"` | ok [after] i 0 | same |
| l-endsub | `Sub T()` / `L "T starts"` / `GoTo Done` / `L "not reached"` / `Done: End Sub` / `Sub Main()` / `T` / `L "l-endsub: back in Main"` | T starts · back in Main | same |
| l-endwith-inside | `With c` / `GoTo Skip` / `.Add 2` / `Skip: End With` / `s = CStr(c.Count)` | ok [0] i 0 | same |
| l-firstcase | `Select Case i` / `Skip: Case 0` / `s = "zero"` / `End Select` | NO EXE | same |
| l-foreach-inside | `c.Add 1` / `c.Add 2` / `c.Add 3` / `For Each v In c` / `If v = 2 Then GoTo Skip` / `s = s & v` / `Skip: Next` | ok [13] i 0 | same |
| l-loop-inside | `Do` / `i = i + 1` / `If i < 3 Then GoTo Skip` / `s = s & i` / `Skip: Loop While i < 5` | ok [345] i 5 | same |
| l-next-inside | `For i = 1 To 3` / `If i = 2 Then GoTo Skip` / `s = s & i` / `Skip: Next` | ok [13] i 4 | same |
| l-nexti-inside | `For i = 1 To 3` / `If i = 2 Then GoTo Skip` / `s = s & i` / `Skip: Next i` | ok [13] i 4 | same |
| l-numlabel-next | `For i = 1 To 3` / `If i = 2 Then GoTo Skip` / `s = s & i` / `30 Skip: Next` | ok [13] i 4 | same |
| l-plain-next | `For i = 1 To 3` / `s = s & i` / `Skip: Next` | ok [123] i 4 | same |
| l-wend-inside | `While i < 3` / `i = i + 1` / `If i = 1 Then GoTo Skip` / `s = s & i` / `Skip: Wend` | ok [23] i 3 | same |

**A head on `End Function` and `End Property`, and two edge shapes** (#2561), native and p-code alike. A `GoTo 90` to
`90 End Function`, or to `Done: End Function`, returns the value the Function had (5 and 6, not 7 and 8); `End Property`
the same (3 and 4). `90: End Sub`, a number with a colon, is a head as well. `L "T starts": End Sub`, an `End Sub` after a
statement and a colon on one line, compiles and returns: there is no head there.

| Probe | Lines | Logged, native | Logged, p-code |
|---|---|---|---|
| f-colon-number | `Sub T()` / `L "T starts"` / `GoTo 90` / `L "not reached"` / `90: End Sub` / `Sub Main()` / `T` / `L "f-colon-number: back in Main"` / `End Sub` | T starts · back in Main | same |
| f-endfunction | `Function F() As Long` / `10  F = 5` / `20  GoTo 90` / `30  F = 7` / `90 End Function` / `Function G() As Long` / `G = 6` / `GoTo Done` / `G = 8` / `Done: End Function` / `Sub Main()` / `L "f-endfunction: F " & F() & " G " & G()` / `End Sub` | F 5 G 6 | same |
| f-endproperty | `Property Get P() As Long` / `P = 3` / `GoTo Done` / `P = 9` / `Done: End Property` / `Property Get Q() As Long` / `10  Q = 4` / `20  GoTo 90` / `90 End Property` / `Sub Main()` / `L "f-endproperty: P " & P & " Q " & Q` / `End Sub` | P 3 Q 4 | same |
| f-statement-then-end | `Sub T()` / `L "T starts": End Sub` / `Sub Main()` / `T` / `L "f-statement-then-end: back in Main"` / `End Sub` | T starts · back in Main | same |
