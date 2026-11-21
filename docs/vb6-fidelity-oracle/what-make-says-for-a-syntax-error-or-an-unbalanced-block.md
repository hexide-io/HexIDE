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
- the same mistakes typed into the IDE's code window, which checks each line as it's entered. Since measured: see the last section.

### The same mistakes at F5, and typed into the code window (2026-11-21, #830, SP6)

**The question.** #830 asked what the IDE says for a syntax error found when a program starts. "Not measured" above asked the same for a line typed into the code window.

**The probe:**
- **The IDE:** vb6.exe SP6, on the guest's console, driven by keys. Each case was a fresh IDE and a project of one form, read back from the dialog's own controls and the window titles.
- **At F5:** the bad line was in the saved `Form_Load`, after `Debug.Print "load"`. F5, then OK.
- **Typed:** under Auto Syntax Check, the default, a new `Private Sub T()` was typed into the form's code. Then the bad line was typed into it and Enter pressed.

| Line | At F5 | Typed, on Enter |
|---|---|---|
| `If Then` | Compile error: **Syntax error** | Compile error: **Expected: expression** |
| `x = 1 +` | Syntax error | Expected: expression |
| `If x = 1`, no `Then` | Syntax error | **Expected: Then or GoTo** |
| `Dim y As` | Syntax error | not typed |

**The dialog is the same in both:**
- its title is "Microsoft Visual Basic";
- its text is "Compile error:", a blank line, and then the message;
- its buttons are OK and Help;
- it names no module and no line.

**At F5:**
- **The IDE's title read `[run]` while the dialog showed,** and `[break]` after OK.
- **The code window's current line was the bad line,** in all four. In the screenshot of the `If x = 1` case it was selected from its first column to its end, indentation included.
- **Nothing in the procedure had run.** The `Debug.Print "load"` before the bad line printed nothing: the Immediate window was empty in the screenshot of the `If x = 1` case. That's one case.

**Typed:**
- **The dialog came as the line was left,** and the IDE stayed in `[design]`.
- **After OK, the line stayed as typed.**

**What it shows:** **the IDE at F5 says what `/make` says,** plain "Syntax error" for every malformed statement. That's the same as the table at the top of this page, `Dim y As` included. **The "Expected: …" wording came from the line-by-line check of a line as it's typed:** in all three typed rows, and in none of the four at F5. The remark above, that some "Expected:" rows on other pages come from lines typed in the IDE, now has a measurement behind it.

**Not measured:**
- an unbalanced block at F5;
- a line typed with Auto Syntax Check turned off;
- the line's colour in the code window.
