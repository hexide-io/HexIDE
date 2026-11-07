## Debug.Assert (2026-10-23, #1467)

Measured with vb6.exe in two places. The IDE was driven with `vb6.exe /runexit`, in session 0 as the `/make` probes are,
and the compiled EXE was built both native and p-code, the two agreeing. Each program the tests pin was run as written.

**In the IDE, release build**, `Debug.Assert X` with `L` logging each side of it. These rows ran on the oracle's IDE,
VB6.EXE 6.00.8176 with VBA6.DLL 6.0.8169, which is VB6's original release and not SP6 (#1524). On SP6 (2026-10-24, #1663)
every one of them ran the same. The compiled-EXE rows below ran on MSVBVM60 6.00.9848, SP6's runtime, and stand.

| X | result |
|---|---|
| `True`, `5`, `-1`, `"True"` | goes on |
| `F()`, returning True and logging "F called" | goes on; F is called |
| `False`, `0`, `Empty`, `Null`, `"False"` | break mode at the statement |
| `G()`, returning False and logging "G called" | G is called, then break mode |
| `"abc"` | 13, which On Error Resume Next traps |
| `1 / 0` | 11, trapped |
| `False`, or `G()`, under On Error Resume Next | still break mode |

So the condition is read as `If` reads it (#847): Null and Empty do not hold, where `CBool(Null)` raises 94.

**Telling break mode from an error.** A project that breaks leaves the IDE waiting, which `/runexit` cannot end, so the
probe's timeout reports it, with nothing logged after the assertion. An error dialog would wait the same way. So each
waiting case was also run under On Error Resume Next: there an error is trapped and the next line logs it, as "abc" and
`1 / 0` show. Null, Empty, 0 and "False" still waited, so they break rather than raise.

**Not measured:** what Continue (F5) does after the break, since session 0 has no one to press it. HexIDE goes on with
the statement after the assertion, as it does after `Stop`.

**In a compiled EXE**, native and p-code:

| statement | result |
|---|---|
| `Debug.Assert False` | skipped |
| `Debug.Assert G()` | skipped, and **G is never called** |
| `Debug.Assert "abc"`, `Debug.Assert 1 / 0` | skipped: nothing is raised |
| `Debug.Print F()` | **F is called**, though nothing is printed |

The two `Debug` statements differ here. The compiler removes `Debug.Assert` with its argument, but evaluates
`Debug.Print`'s arguments and discards only the printing. "Debug statements are removed at compile time", as the issue
had it, is true of the first and not of the second.

**Compile errors:** `Debug.Assert` with no condition, and `Debug.Assert True, False`, are each "Syntax error". A compile
error is the compiler's answer, so these came from the release build's compiler even in a `/make` (#1524).

### In HexIDE (#1467)

`StatementExecutor` takes `Debug.Assert` as a statement beside `Debug.Print`:
- **With a debugger** (`DebugController`, which every IDE run has), it evaluates the condition with `ConditionHolds`, the
  same reading `If` uses. When the condition does not hold it suspends through
  `IDebugController.EnterBreakFromAssertionAsync`.
- **Headless** (the Make EXE runner and the test fixture), it returns before evaluating anything, as `Stop` ends the
  program there.

The break carries a new `StopReason.Assertion`, which `get_debug_state` and `run_project`'s note report.
`Call Debug.Assert(x)` reaches `DebugProxy.Call` with no statement to break at, and is refused, naming the statement
form.

**A reusable harness.** `vb6.exe /runexit project.vbp`, run in the guest with a timeout, measures what the VB6 IDE does
rather than the compiler: the release build's IDE until 2026-10-23, SP6's from 2026-10-24 (#1524). A project that ends
logs to its end. One that waits is in break mode or showing a dialog, and
running it again under On Error Resume Next tells the two apart.

