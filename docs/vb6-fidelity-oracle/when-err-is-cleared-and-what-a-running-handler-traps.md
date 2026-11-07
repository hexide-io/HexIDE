## When Err is cleared, and what a running handler traps (2026-10-23, #877)

Measured with `/make` on vb6.exe 6.00.8176 (the release build) with the MSVBVM60 6.00.9848 (SP6) runtime. These are run-time rows, and native and p-code agree on every one. The rows add to #877's first three and to the two its review rounds measured: `/make` and the IDE agreed then. Each program logged `Err.Number`, with `Err.Description` in brackets where it matters, at the points named. `ErrLifetimeTests` runs the same programs and must print the same lines.

**Every `On Error` statement clears Err**, Description and Source included:

| After `Err.Raise 5` under Resume Next | `Err` |
|---|---|
| `On Error Resume Next` | 0, Description and Source empty |
| `On Error GoTo 0` | 0 |
| `On Error GoTo H` | 0 |
| `On Error GoTo -1` | 0. **VB6 compiles it**, although only VBA documents it |
| `On Error Resume Next` inside a loop, again on its second pass | 6 on the first, 0 on the second |

**Every `Resume` clears it, and so does leaving a procedure from its handler.** Each callee raised 11 into `On Error GoTo H`:

| How the callee ended | `Err` seen after |
|---|---|
| `Exit Sub` from the handler | 0, though the handler itself read 11 |
| `Exit Function` from the handler, which set the return value | 0, and the value is returned |
| the handler reaching `End Function` or `End Sub` | 0, even when the caller had its own 5 set before the call |
| the handler set `Err.Number = 7`, then `End Sub` | 0 |
| the handler said `On Error Resume Next` or `On Error GoTo 0`, set `Err.Number = 7`, and ended or `Exit Sub`'d | 0: leaving the handler clears it, whatever mode the handler set |
| the handler's `Resume Next`, or `Resume Back` to a label | 0 at the statement resumed, and 0 for the caller |
| the handler raised 13 | 13 reaches the caller |
| a handler for a callee's error 9, ending normally | the handler read 9, and the caller 0 |
| `On Error Resume Next`, raise 11, then `Exit Sub` or `End Sub` (no handler) | **11**: leaving under Resume Next keeps it |

**While a handler runs, nothing in its procedure traps**, whatever `On Error` it says:

| Inside a running handler for 11 | Then |
|---|---|
| `On Error Resume Next`, then `Err.Raise 13` | not trapped: the statements after it never ran, and **13 reached the caller**. HexIDE trapped it and ran on |
| `On Error GoTo H` (itself), then `Err.Raise 13` | 13 reached the caller |
| `On Error Resume Next`, then `Resume Next` | resumed. A later `Err.Raise 13` **is** trapped and continued past: the mode the handler set applies once the handler has ended |
| `On Error GoTo H2`, then `Resume Next` | the next error went to `H2` |
| `On Error GoTo -1`, then `On Error Resume Next`, then `Err.Raise 13` | trapped: `-1` ends the handler |
| `On Error GoTo -1`, then `On Error GoTo H`, then `Err.Raise 13` | the same handler ran again, for 13 |
| a helper whose first line is `On Error Resume Next`, called from the handler | the handler's `Err` is 0 after the call: Err is global |

`On Error GoTo -1` under `On Error Resume Next`, outside any handler, leaves Resume Next in force: a later `Err.Raise 6` is trapped.

**`Err.Raise` with no Description keeps the one already set**, until `Err.Clear`. The other fields behave as follows:

| Sequence, from `Err.Clear` | `Err.Number`, `Err.Description` |
|---|---|
| `Err.Raise 11`, `Err.Raise 13` | 13, **"Division by zero"** |
| `Err.Raise 1000, "S1", "custom"`, `Err.Raise 5` | 5, "custom", and Source stays "S1" |
| `Err.Raise 1000, , "custom"`, `Err.Raise 1000` | 1000, "custom" |
| `Err.Description = "manual"`, `Err.Raise 13` | 13, "manual" |
| `Err.Raise 11`, `Err.Description = ""`, `Err.Raise 13` | 13, **""**: an empty Description, once set, is kept too |
| `Err.Number = 5`, `Err.Raise 6` | 6, "Overflow": setting Number sets no Description |
| `Err.Raise 11`, `Err.Raise 13, , ""` | 13, "" |
| `Err.Raise 11`, then `x = 1 / 0` | 11, "Division by zero": a run-time error always sets its own |
| `Err.Raise 5` alone | Source is "Probe", the project's name |

**Measured, and not followed:** `Resume` in a handler for an `Err.Raise` inside a one-line `If`, `If tries = 0 Then Err.Raise 11`, retried the `Err.Raise` itself, not the `If`. So it raised again although the handler had made `tries` 1. It looped until `tries` overflowed, and 6 reached the caller. HexIDE's `Resume` retries the top-level statement, the `If`, which is the nested-granular `Resume` limit in `interpreter-gaps.md`, so there it runs on.

### In HexIDE (#877)

- `VisitOnErrorStmt` clears Err first, for every form. `GoTo -1` sets `inHandler` false and leaves the mode alone.
- Every `Resume` clears Err.
- `LeaveHandler` clears it when the procedure is left with its handler running.
- `Traps` and the Resume Next catches read `!inHandler`, so a running handler traps nothing.
- `VbErr` records whether a Description has been set, and a `Raise` with none keeps it.
- Source is still not filled with the project's name.


## Err.Source after a run-time error and after Err.Raise (#1483)

Provenance: hexide-int's measurement reported in #1483, committed to PR #2279 as
4552d7cf; confirmed as this run's scope by the supervisor on 2026-10-27. vb6.exe SP6,
two clients built both native and p-code; all four logs agreed. Both clients used
`Name="Probe"`, one also used `Title="Titled App"`. A late-bound ActiveX DLL used
`Name="ProbeDll"`.

| Probe | Source logged |
|---|---|
| Run-time error, `Err.Raise 1000`, or `Error 5` in a standard module or form | `Probe` |
| Same in a project class, trapped inside it or by its caller | `Probe` |
| Same inside the DLL class, trapped inside it or by its caller | `ProbeDll` |
| Late-bound call of a member absent from the DLL class (438) | `Probe` |
| `Err.Raise 1001, "MySrc"`, in either project | `MySrc` |
| `Err.Clear` | empty string |
| `Err.Source = "Set"`, followed by `Err.Raise 7` without Source | `Set` |

The project name has no class suffix, and Title does not affect it. A sourceless
Raise fills an empty Source; the measured non-empty `Set` case keeps it. Other
unmeasured Source combinations are not established by these probes.
