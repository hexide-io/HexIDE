## Erl: the line number an error reports (2026-10-28, #2528)

**Measured on vb6.exe SP6**, built native and as p-code, which logged the same lines. One procedure per case, each
logging `Erl` from its handler.

**The rule the rows give:**
- `Erl` is a Long, 0 until an error.
- **When an error is raised, `Erl` becomes the line number at or above the place it was raised, in source order, in the
  procedure that handles it, whether or not that numbered line ran.** An error on an unnumbered line after line 20 is 20.
  One with no number above it in the procedure is 0. A named label doesn't count. (Corrected the same day: this said "the
  last line number passed", which the rows below the follow-up overturn.)
- An error in a callee, handled by the caller, is the **caller's** line: the call's, 40, whether or not the callee has
  numbers of its own (its line 6 is not reported).
- **Every `On Error` statement and every `Resume` set it to 0**: `On Error GoTo`, `Resume Next` and `GoTo 0` alike, and
  `Resume Next` or `Resume label`. **`Err.Clear` does not.** Leaving a procedure does not either: a Function that read `Erl`
  50 under `On Error Resume Next` leaves `Erl` 50 for its caller.
- Under `On Error Resume Next` each error sets it again; a line passed with no error does not change it.
- Line numbers wrap at 65536: line 65529 reads 65529, and **line 70000 reads 4464**.
- An error inside an active handler is not caught by an `On Error GoTo` the handler sets. It is fatal, as VB6 has always
  done (the first probe ran into it).

| Case | Logged |
|---|---|
| `no error yet` | `Erl 0 Long` |
| `E1 numbered, error on 20` | `Erl 20 Long err 11` |
| `E2 error on an unnumbered line after 20` | `Erl 20` |
| `E3 no line numbers` | `Erl 0` |
| `E4 error in an unnumbered callee, called on 40` | `Erl 40` |
| `E5 error on the callee's 6, called on 40` | `Erl 40` |
| `E6 before Clear` | `Erl 10` |
| `E6 after Err.Clear` | `Erl 10` |
| `E7 Resume Next, error on 100` | `Erl 100 err 11` |
| `E7 after 200 ran, no new error` | `Erl 100` |
| `E7 a second error, unnumbered, after 200` | `Erl 200` |
| `E8 line 65529` | `Erl 65529 Long` |
| `E9 line 70000` | `Erl 4464 Long` |
| `E10 error after a named label, after 10` | `Erl 10` |
| `E11 Err.Raise on 10` | `Erl 10 err 1234` |
| `E12 single-line If on 20` | `Erl 20` |
| `E13 in a loop, error on 20` | `Erl 20 i 2` |
| `E14 handler, before On Error GoTo h2` | `Erl 10` |
| `E14 after On Error GoTo h2 in the handler` | `Erl 0 err 0` |
| `E15 after Resume Next, on 20` | `Erl 0` |
| `E15 after On Error Resume Next` | `Erl 0` |
| `E15 after On Error GoTo 0` | `Erl 0` |
| `E16 handler` | `Erl 10` |
| `E16 after Resume to a label` | `Erl 0` |
| `back in Main after E16` | `Erl 0` |
| `F17 returns Erl from inside` | `50, Main then reads Erl 50` |
| `Main's own error on 70` | `Erl 70` |
| `E3 no line numbers` | `Erl 0` |
| `Main after calling E3 (handled inside)` | `Erl 0` |

**A follow-up probe, native and p-code agreeing:** `Erl` is cleared exactly where VB6 clears `Err` itself, and not by the program's own `Err.Clear`.

| Case | `Erl` | `Err.Number` |
|---|---|---|
| G1's handler, error on 10 | 10 | |
| Main, after G1 left its handler by `Exit Sub` | **0** | 0 |
| Main, after G2 left its handler by `End Sub` (error on 20) | **0** | 0 |
| Main, after G3, an error on 30 under `On Error Resume Next` | 30 | 11 |
| Main, after G5, which has no `On Error` and raises nothing | 30 | 11 |
| Main, after G4, whose only statement is `On Error Resume Next` | **0** | 0 |
| Main's own error on 70, then after G6, whose only statement is `On Error GoTo 0` | 70, then **0** | 0 |
| Main's error on 80, then `Err.Raise 5` on an unnumbered line after it | 80, then 80 | |

So `Erl` is program-wide, as `Err` is: a callee's `On Error` statement clears the caller's `Erl` with its `Err`.

**Corrected 2026-10-28: source order, not the order lines ran.** The rule first written here was "the last line number
passed", the obvious reading of the rows above, and every one of them fits it. Two cases separate it from the right rule:

- After a `Then` part ran, an error past an unnumbered `End If` reads **40**, the number of a line in the `Else` part, which
  never ran.
- A loop's second pass reads the **`For` or `Do` line's** number, 10, not 20, the body's last line, which ran last. A
  `Do While` condition with no number above it reads 0 when it is asked again, though line 30 ran just before.

So `Erl` is the line number at or above the place the error was raised, in source order, in the procedure that trapped
it. The call stands on a line of its own procedure, which is why an error in a callee reads the call's line.

Native and p-code, one probe per row, each `Sub Main` with `On Error GoTo h` and a handler logging `err`, `erl`, `s` and `i`:

| Probe | Lines | Logged, native | Logged, p-code |
|---|---|---|---|
| p-case-after-case | `10 Select Case i` / `Case 0` / `20 s = "zero"` / `40 Case 5` / `50 s = "five"` / `End Select` / `x = 1 / 0` | err 11 erl 50 [zero] i 0 | same |
| p-caseelse-after-case | `10 Select Case i` / `Case 0` / `20 s = "zero"` / `40 Case Else` / `50 s = "other"` / `End Select` / `x = 1 / 0` | err 11 erl 50 [zero] i 0 | same |
| p-do-body-top | `10 Do` / `If i = 1 Then x = 1 / 0` / `20 i = i + 1` / `Loop While i < 3` | err 11 erl 10 [] i 1 | same |
| p-dowhile-body-top | `10 Do While i < 3` / `If i = 1 Then x = 1 / 0` / `20 i = i + 1` / `Loop` | err 11 erl 10 [] i 1 | same |
| p-dowhile-loop-unnumbered | `10 Do While 1 / (2 - i) > -5` / `20 i = i + 1` / `Loop` | err 11 erl 10 [] i 2 | same |
| p-dowhile-top-unnumbered | `Do While 1 / (2 - i) > -5` / `20 i = i + 1` / `30 Loop` | err 11 erl 0 [] i 2 | same |
| p-else-after-then | `10 If i = 0 Then` / `20 s = "a"` / `30 Else` / `40 s = "b"` / `End If` / `x = 1 / 0` | err 11 erl 40 [a] i 0 | same |
| p-elseif-after-then | `10 If i = 0 Then` / `20 s = "a"` / `30 ElseIf i = 5 Then` / `40 s = "b"` / `End If` / `x = 1 / 0` | err 11 erl 40 [a] i 0 | same |
| p-endif | `10 If i = 0 Then` / `20 s = "a"` / `30 End If` / `x = 1 / 0` | err 11 erl 30 [a] i 0 | same |
| p-endselect | `10 Select Case i` / `Case 0` / `20 s = "zero"` / `60 End Select` / `x = 1 / 0` | err 11 erl 60 [zero] i 0 | same |
| p-endwith | `10 With c` / `20 .Add 1` / `30 End With` / `x = 1 / 0` | err 11 erl 30 [] i 0 | same |
| p-for-body-top | `10 For i = 1 To 2` / `If i = 2 Then x = 1 / 0` / `20 s = s & i` / `Next` | err 11 erl 10 [1] i 2 | same |
| p-for-next-unnumbered | `10 For b = 32766 To 32767` / `20 s = s & "x"` / `Next` | err 6 erl 20 [xx] i 0 | same |
| p-foreach-body-top | `c.Add 1` / `c.Add 2` / `10 For Each v In c` / `If v = 2 Then x = 1 / 0` / `20 s = s & v` / `Next` | err 11 erl 10 [1] i 0 | same |
| p-while-wend-unnumbered | `10 While 1 / (2 - i) > -5` / `20 i = i + 1` / `Wend` | err 11 erl 10 [] i 2 | same |
