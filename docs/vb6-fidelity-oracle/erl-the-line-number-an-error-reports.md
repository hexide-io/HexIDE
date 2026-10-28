## Erl: the line number an error reports (2026-10-28, #2528)

**Measured on vb6.exe SP6**, built native and as p-code, which logged the same lines. One procedure per case, each
logging `Erl` from its handler.

**The rule the rows give:**
- `Erl` is a Long, 0 until an error.
- **When an error is raised, `Erl` becomes the last line number passed in the procedure that handles it.** An error on an
  unnumbered line after line 20 is 20. One with no line numbers at all is 0. A named label doesn't count.
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
