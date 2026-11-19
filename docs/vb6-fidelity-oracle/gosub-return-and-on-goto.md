## GoSub, Return, and On … GoTo and On … GoSub (2026-11-18, #4726)

**The question.** HexIDE refused all four statements, and the reason given was that a tree-walk has nothing to
save and return to. Before lifting that, what exactly does VB6 do?
- How does `On x` pick its label?
- What does `Return` do with no GoSub?
- How do nesting, recursion, and GoSub from inside a block behave?
- What happens to error handling inside a routine?

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code.
- **The programs:** Sub Main programs, each row a Sub of its own so its labels are its own. Every row logged the same
  in native code and p-code but one, G40, which is shown separately.
- **`On x GoTo A, B, C`:** each label appends its letter and goes to the end; the line after the `On` statement appends
  `fell`, then falls into `A`.
- **`On x GoSub A, B, C`:** each routine appends its letter and returns; the line after the `On` statement appends
  `back`.
- **Errors:** a row with `On Error GoTo E` logs `err <number>` and what it had built.

### On … GoTo and On … GoSub

| Row | Selector | VB6 |
|---|---|---|
| G1 | `On 2 GoTo A, B, C` | `B` |
| G2 | `0` | `fell A`: it falls through |
| G3 | `4`, past the three labels | `fell A` |
| G13 | `255` | `fell A` |
| G4 | `-1` | **5** |
| G5 | `256` | **5** |
| G6 | `2.5` | `B` |
| G7 | `1.5` | `B` |
| G8 | `2.6` | `C` |
| G9 | `"2"` | `B` |
| G10 | `Null` | **94** |
| G11 | an Empty Variant | `fell A` |
| G12 | `Pick()`, a Function counting its calls and returning 2 | `B`, and one call |
| G14 | `On 2 GoSub A, B, C` | `B back` |
| G15 | `0` | `back` |
| G16 | `5` | `back` |
| G17 | `-1` | **5** |

### GoSub and Return

| Row | Program | VB6 |
|---|---|---|
| G18 | `GoSub A`, where `A` returns | `A back` |
| G19 | `Return` with no GoSub | **3** "Return without GoSub" |
| G25 | the main flow falls into a routine and reaches its `Return` | `x A`, then **3** |
| G20 | `A` does `GoSub B` | `A1 B A2 back` |
| G32 | `A` does `GoSub A` until a counter reaches 4 | `r4 r4 r4 r4 back 4` |
| G21 | `GoSub A` inside `For i = 1 To 3` | `1 2 3 end`: each Return comes back into the loop |
| G22 | `GoSub A` inside an `If` inside a `Case` inside a `Do While` | `A2 in end` |
| G24 | `A` does `GoTo B`, and `B` returns | `A B back` |
| G23 | `A` does `Exit Sub` | the procedure ends there |
| G30 | `A` has no Return, and runs to `End Sub` | the procedure ends, with no error |
| G31 | `A` has no Return, and does `GoTo Done` | it runs on from `Done`, with no error |
| G29 | `GoSub 100` and `On 2 GoTo 200, 300`, with numbered lines | `100 back 300` |

### Errors inside a routine

| Row | Program | VB6 |
|---|---|---|
| G26 | `On Error GoTo H`; `A` raises 5; `H` does `Resume Next` | `A1 H5 A2 back`: it resumes inside `A`, and A's Return still returns |
| G27 | `On Error Resume Next`; `A` raises 11 | `A1 A2 11 back 0` |
| G28 | `A` raises 5; `H` does `Resume L`, a label after the routine, whose line returns | `H5 L back`: the GoSub is still pending after the Resume |
| G33 | `A` does `On Error GoTo H`, then returns; the main flow raises 6 | `A back H6 after`: the handler set in the routine stays |
| G34 | the main flow raises 5; `H` does `GoSub A`, then `Resume Next` | `H5 A H2 after` |
| G35 | `A` raises 5; `H` does `Return` with no Resume | `H5`, then `back 5`: Return leaves the handler, and Err is still 5 |
| G40 | `A` does `GoSub A` with no end, under `On Error GoTo E` | see below |

**G40, runaway recursion.** Native code and p-code disagree here. Neither printed the line `E` logs, and the error
reached Main's handler instead.

| Build | Main's handler logged |
|---|---|
| p-code | **28** "Out of stack space" |
| native code | **14** "Out of string space" |

HexIDE follows p-code. Why native code reports 14 resisted explanation.

**Overturned: "`E` never runs."** This page first read G40 as runaway GoSub skipping the procedure's own handler and
going straight to the caller's. That was inferred from `E`'s line never printing. But `E` printed by calling `Log`, a
procedure, and a procedure call is just what a stack that runaway GoSub has exhausted can't make. G41 and G42 tell
the two apart: `E` sets a module-level Long, which needs no call.

```vb
Sub G41()
    On Error GoTo E
    GoSub A
    Exit Sub
A:
    Depth = Depth + 1
    GoSub A
    Return
E:
    EHit = Err.Number
    Exit Sub
End Sub

' G42 is G41 with `EHit = EHit + 1000` after the first GoSub, and `Resume Next` in place of E's Exit Sub.
' EHit and Depth are module-level Longs. Main calls each row, logs "<row> back EHit=... depth=...", and resets both.
```

| Row | p-code | native code |
|---|---|---|
| G41 | `G41 back EHit=28 depth=252154` | no output; still running at the 90-second timeout, and stopped |
| G42 | `G42 back EHit=1028 depth=252154` | (the same run) |

- **In p-code, the procedure's own handler traps runaway GoSub's 28.** `E` runs, and with `Exit Sub` the procedure
  returns normally. G40's line was lost because `E`'s first statement made a procedure call with no stack left. That
  raised 28 inside the handler, which went to the caller.
- **G42's `Resume Next` resumes after the faulting GoSub.** The pending Returns then unwind until the statement after
  the first GoSub runs, which is the `+ 1000`.
- **The native build neither logged nor ended this time,** where the first run reported 14. Why resisted explanation.

### Readings

- **`On x` rounds the selector as `CInt` does.** 2.5 and 1.5 both give 2, and 2.6 gives 3. A String converts, and
  Null is 94. The selector is evaluated once.
- **0, and anything past the list up to 255, falls through** to the next statement. Below 0 or over 255 is 5.
- **GoSub keeps its place on a stack of its own, not in the statement tree.**
  - A routine can GoSub again, itself included, and each Return comes back to the innermost pending GoSub, wherever
    in a block that GoSub stood.
  - A GoSub left pending is not an error. A routine may end the procedure, by `Exit Sub` or by reaching `End Sub`,
    or carry on somewhere else by `GoTo`.
- **Error handling is the procedure's, not the routine's.** A handler set inside a routine stays set after its
  Return, and a handler can GoSub.
- **A fault inside a routine keeps the routine pending.** `Resume Next` resumes inside the routine, and `Resume
  <label>` keeps the GoSub pending for a later Return. `Return` from the handler leaves the handler and keeps Err.
- **Runaway GoSub raises 28 that the procedure's own handler traps** (G41, G42), at a depth of 252,154 in p-code.
  This page first read G40 as the handler being skipped; see the overturn above.

**Not measured:**
- a selector beyond Integer's range, measured since, below (#4880);
- `Resume` inside a routine reached from a handler, for the error that handler is handling, measured since, below;
- the depth native code reaches, and why it neither logged nor ended;
- labels inside a block. These are already GoTo's own limit in HexIDE.

## A selector beyond Integer, and Resume from a routine a handler called (2026-11-19, #4880)

**The probes.** Two projects with `Sub Main` in a module, built with `/make` native and p-code and run in session 0.
Native and p-code agreed line for line. The first ran each selector through `On v GoTo A, B` and `On v GoSub A, B`, in
a Sub with `On Error GoTo E` that logged `Err.Number`, then four of them again under `On Error Resume Next`. The second
ran the Resume rows. Each Resume row is a Sub with `On Error GoTo H`, called under `On Error Resume Next`, so an error
it doesn't handle reaches the caller and is logged.

**A selector beyond Integer's range is 6, Overflow,** for GoTo and GoSub alike:

| Selector, as passed in a Variant | VB6 |
|---|---|
| `2`, and `CByte(2)` | `B` |
| `255` | falls through |
| `256`, `32767`, `-32768` | **5** |
| `32768`, `-32769`, `65537`, `65538`, `-65534`, `131074`, `2147483647` (Long) | **6** |
| `2147483648#`, `1E+20` (Double); `CSng(32768)`; `CCur(32768)`; `CDec(32768)`; `"32768"` | **6** |
| `32767.5`, `32768.4` | **6** |
| `-32768.5` | **5** |

- **The selector converts as `CInt` does, and only then is checked against 0 to 255.**
  - 65537 and 65538 overflow rather than wrapping to 1 and 2.
  - 32767.5 rounds to the even 32768, which overflows.
  - -32768.5 rounds to the even -32768, which converts and is then out of range: 5.
- **Under `On Error Resume Next` the next statement runs.** `On 32768 GoTo` and `On 32768 GoSub` each went on to the
  following line with `Err.Number` 6. `On 256 GoTo` did the same with 5.

**`Resume` in a routine the handler reached by GoSub resumes the handler's error, and leaves the GoSub pending.**
Each row raises 5 in the main flow, and its handler `H` does `GoSub A`, unless the row says otherwise:

| Row | What `A` does | VB6 |
|---|---|---|
| R1 | `Resume Next` | the main flow goes on after the raise, with `Err.Number` 0; `H`'s own lines after the GoSub don't run |
| R2 | `Resume`; the fault was `v = Boom()`, which raises only on its first call | `Boom` runs again: `v=2`, `Err.Number` 0 |
| R3 | `Resume L1`, a label in the main flow | at `L1`, with `Err.Number` 0 |
| R4 | `Resume Next`; the main flow then raises 6 | `H` traps the 6, so the handler is armed again |
| R5 | `GoSub B`, and `B` does `Resume Next` | the main flow goes on after the raise |
| R6 | `Resume Next`; `H` reached it by `On 1 GoSub A` | the main flow goes on after the raise |
| R7 | `Resume Next`; the main flow then does `Return` | the Return comes back into `H`, after its `GoSub A`, with `Err.Number` 0 |
| R8 | as R7; `H`'s code after the GoSub then raises 7 | the line after the raise runs, and nothing reaches the caller: `H` took the 7 and its `A` resumed after it, as R1b logs `H` doing |
| R1b | as R7; `H`'s code after the GoSub then does `Resume Next` | error **20**, trapped by `H`, and so on; the probe stopped it after three passes |

What these show:
- **A routine's `Resume`, `Resume Next` or `Resume <label>` does what the handler's own would.** It clears Err, arms
  the handler again, and goes on at the resume point. It does that from a routine the handler reached directly, by
  `On … GoSub`, or by a GoSub nested inside that routine.
- **The handler's GoSub stays pending** (R7). A `Return` reached later comes back to the line after the handler's
  `GoSub`, and that code then runs as ordinary code, not as the handler:
  - an error there is trapped by the handler, as anywhere (R8);
  - a `Resume` there has no error to resume, so it is 20, "Resume without error", which the handler traps (R1b).
- **R1b's loop follows from those two.** Each 20 sends `H` round its `GoSub A` again, and `A`'s `Resume Next`
  resumes after the statement that raised the 20.
  - The first time, that statement is `H`'s `Resume Next`, and the line after it is `A`'s own `Resume Next`. So a
    second 20 comes at once.
  - The second time, it resumes after `A`'s `Resume Next`, and `A` returns into `H`, whose `Resume Next` starts the
    next pass.
  - The first run of this probe, without the stop, looped until the 60-second timeout, in both builds.

**Not measured:** a selector held in a typed variable rather than a Variant; and stepping over a GoSub in VB6's
debugger (D37 in `docs/debugger-vb6-divergences.md`).

**In HexIDE (#4880),** read from the code on main at d5c6dc78d:
- **A selector that overflows Integer** is refused as not implemented, even under `On Error Resume Next`
  (`StatementExecutor.cs:3676`).
- **A `Resume` in a routine reached from an active handler,** with no error in the routine's own pass, is refused as
  not implemented (`StatementExecutor.cs:6189`).
