## What `/make` says for a syntax error or an unbalanced block (2026-11-20, #4415, SP6)

**The probe.** vb6.exe SP6, `/make`, native code and p-code giving the same message on the same line in all 36 rows.
Each row was a project of its own, so only its one mistake was reported.
- **Inside a procedure,** each row was one bad line in `Private Function PF() As Variant`, after
  `Dim x As Variant, i As Long, c As New Collection`.
- **"Line"** below means the line VB6 reports in the `/make` log.

**What was guessed, and overturned.** The card that put #4415 to the maintainer expected "Expected: …" for many of
these. That came from the oracle's other pages, which record "Expected: end of statement" and the like 17 times. In a
loaded module built with `/make`, every malformed statement inside a procedure said plain `Syntax error`. "Expected: …"
appeared only for two broken declarations at module level. The other pages' "Expected:" rows are worth re-reading with
that in mind. Some come from lines typed in the IDE, whose line-by-line check is a different path (reasoned, not
measured).

**1. A malformed statement inside a procedure is `Syntax error`, at its own line.** That held in all 19 rows:

| Statement | | Statement | |
|---|---|---|---|
| `x = ` | Syntax error | `If x 1 Then x = 2` | Syntax error |
| `x = = 1` | Syntax error | `For i = 1 10` | Syntax error |
| `x = 1 2` | Syntax error | `For i 1 To 10` | Syntax error |
| `x = 1 +` | Syntax error | `Dim` | Syntax error |
| `x = (1 + 2` | Syntax error | `Dim y As` | Syntax error |
| `x = 1)` | Syntax error | `Exit` | Syntax error |
| `x = Mid$("abc", 1` | Syntax error | `On Error` | Syntax error |
| `If x = 1` (no `Then`), then a block and `End If` | Syntax error, at the `If` | `GoTo` | Syntax error |
| `ReDim` | Syntax error | `Const K 5` | Syntax error |
| `Do` / `Loop Until` (no condition) | Syntax error, at the `Loop Until` | | |

**2. An unbalanced block has VB6's own message.**

| Mistake | Message | Line |
|---|---|---|
| `If x = 1 Then` with no `End If` | **Block If without End If** | the procedure's `End Function` |
| `For i = 1 To 3` with no `Next` | **For without Next** | `End Function` |
| `Do` with no `Loop` | **Do without Loop** | `End Function` |
| `Select Case x` / `Case 1` with no `End Select` | **Select Case without End Select** | `End Function` |
| `With c` with no `End With` | **Expected End With** (no colon) | `End Function` |
| a stray `Next` | **Next without For** | its own |
| a stray `Loop` | **Loop without Do** | its own |
| a stray `End If` | **End If without block If** | its own |
| a stray `Case 1` | **Case without Select Case** | its own |
| a stray `Else` | **Else without If** | its own |
| a stray `Wend` | **Wend without While** | its own |
| a stray `End With` | **End With without With** | its own |

**3. At module level:**

| Module text | Message | Line |
|---|---|---|
| `Private Sub Foo()` and a statement, with no `End Sub` | **Expected End Sub** | after the module's last line |
| `x = 1` | **Invalid outside procedure** | its own |
| a stray `End Sub` | **Invalid outside procedure** | its own |
| `Private Sub Foo(` then `End Sub` | **Expected: identifier** | the `Sub` line |
| `Private Function G() As` then `End Function` | **Expected: type name** | the `Function` line |

**An unterminated string isn't on this list:** `x = "abc` compiled. That's measured on
[the line-continuation page](line-continuations-and-statement-separators.md).

**Not measured:**
- which message wins when one module has both a malformed statement and an unbalanced block;
- nesting, such as an `If` left open inside a `For` that's closed;
- the same mistakes typed into the IDE's code window, which checks each line as it's entered.
