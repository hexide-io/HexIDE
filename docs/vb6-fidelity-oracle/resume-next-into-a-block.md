## Resume Next into a block (2026-10-12, #839)

**The question.** Under `On Error Resume Next`, what does VB6 do when the condition or header of a block statement
raises? This covers If, ElseIf, Select Case, Do, While, For, For Each and With. HexIDE skipped the whole statement, so a
program relying on Resume Next carried on down a path VB6 would not take, with no error.

**The answer, in one line:** VB6 goes on at the next statement, and for a block statement that is the first statement
inside it. So the block runs.

### A new axis: native code and p-code

Every probe in this file before this one was built as **native code**, and several were also run in the IDE (the App
section, #174's and #840's, #847's, #874's, #882's and the Join and Filter rounds). What is new here is the p-code
build. `/make` honours the project's `CompilationType`,
and a `.vbp` with no `CompilationType` line builds native code with VB6's default optimisations: overflow checks on.
Five probes here were each built several ways, and for the first time the two compiled forms give different answers.

- **Native code, the harness's default.** No `CompilationType` line, or `CompilationType=0`.
- **Native code with integer overflow checks removed.** `CompilationType=0` plus `OverflowCheck=-1`.
- **P-code.** `CompilationType=-1`.

Each probe starts with a tell that proves which build ran: `Dim ov As Integer: ov = 32767: ov = ov + 1` under Resume
Next.
- Native code without overflow checks gives -32768 and error 0.
- Both other builds give 32767 and error 6.

The probes told the two native builds apart with this tell, and the two compiled forms apart with the For evaluation
order below.

**And the IDE.** `VB6.EXE /runexit probe.vbp` runs a project in the IDE, as F5 does, and closes VB when the program
ends. It works from PowerShell Direct's session 0, with no window or agent, because the probe writes its own results
file. Two points about running it that way:
- VB6.EXE exits with 0xC000041D (`STATUS_FATAL_USER_CALLBACK_EXCEPTION`) as it closes there, after the program has
  finished.
- A probe must not raise an unhandled error, which would leave the IDE waiting on a modal no one can see.

**Four of these probes were run in the IDE, and it gave p-code's answer in every row**, including every row where
native code differs. So in the tables below, the p-code column is also the IDE's, which is what HexIDE emulates. Where
the compiled forms differ, HexIDE follows p-code.

### The probes

All of them are `Sub Main` programs, built with `vb6.exe /make` on the Win10 guest and run. Each result is written to a
file.
- `Boom()` is a Function `As Long` whose body is `Boom = 1 / 0`, so calling it raises 11.
- `T(label, v)` appends `label` to a trail and returns `v`, which shows what was evaluated and in what order.
- `s` is a **String** holding `"abc"`, `z` is 0, and `k` is an instance of a class with no default member.

`s` must be a String. A Variant holding `"abc"` does not raise in `s > 1`: VB6 compares it as greater than any number,
without converting it (recorded on #211).

Every statement ran under `On Error Resume Next` unless a row says otherwise.

Unless a table has a column for each mode, native code, p-code and the IDE all gave the same answer.

**Conditions:**

| Statement | VB6 runs | Err after |
|---|---|---|
| `If Boom() > 0 Then ... Else ...` | the Then branch | 11 |
| `If False Then ... ElseIf Boom() > 0 Then ... Else ...` | the ElseIf branch | 11 |
| the same, then a later `ElseIf T("e2", True)` | the first ElseIf's branch; `e2` is never evaluated | 11 |
| `If Boom() > 0 Then c = "Then" Else c = "Else"`, one line | the Then part | 11 |
| `If Boom() > 0 Then c = "Then": c = c & "+second"` | both statements of the Then part | 11 |
| `If Boom() > 0 Then GoTo X Else ...` | the jump | 11 |
| `If s > 1 Then`, a comparison that raises | the Then branch | 13 |
| `If 1 \ z > 0 Then`, an operator that raises | the Then branch | 11 |
| `If T("a", 1) = Boom() Then` | the Then branch; trail `a Boom` | 11 |
| `If k Then`, from #174's probe | the Then branch | 438 |

**Select Case.** A test that raises on a Case line:

| Statement | VB6 runs | Trail |
|---|---|---|
| `Select Case 5`, `Case Boom()`, `Case T("next", 5)`, `Case Else` | `Case Boom()`'s body | `Boom`: nothing later is tested |
| `Select Case 5`, `Case T("a", 1), Boom(), T("b", 5)` | that Case's body | `a Boom`: the rest of the list is not tested |
| `Select Case 5`, `Case 1 To Boom()` | that Case's body | |
| `Select Case 5`, `Case Is > Boom()` | that Case's body | |
| `Select Case 5`, `Case k` | that Case's body, 438 | |
| `Select Case s`, `Case 1`, `Case T("cabc", "abc")` | `Case "abc"`, error 0 | `Case 1` does not raise: 1 becomes `"1"` (see *Select Case matching*) |

**Select Case.** The selector itself raises. The compiled forms differ here, and native code also depends on the shape
of the Cases:

| Statement | Native code | P-code and the IDE |
|---|---|---|
| `Select Case Boom()`, `Case 1`, `Case 0`, `Case Else`, constant Cases | nothing runs | nothing runs |
| `Select Case Boom()`, `Case Else` only | nothing runs | nothing runs |
| `Select Case Boom()`, `Case T("c1", 1)`, `Case T("c0", 0)`, `Case Else` | **`Case 0`**, trail `Boom c1 c0` | nothing runs |
| the same with a Variant Function, and with `1 \ z` | **`Case 0`** | nothing runs |
| `Select Case k`, `Case 1` or `Case T("c1", 1)`, `Case Else`; also through a Variant and `CLng(k)` | **`Case Else`** | nothing runs |

Native code with non-constant Cases goes on to test them against a selector it never computed: 0 for a numeric
selector, Empty for an object's. That is uninitialised state, not a rule.

**Do and While:**

| Statement | VB6 runs |
|---|---|
| `Do While Boom() > 0`, `Do Until Boom() > 0`, `While Boom() > 0` | the body, and the condition is asked again each time round: three passes, three calls, until the probe's own `Exit` |
| `Do ... Loop While Boom() > 0`, `Loop Until` | the body once, then the statement after `Loop`: one call |
| `Do While s > n`, a comparison that raises | the body each time round, error 13 |

**For.** First, with nothing raising, the three expressions are evaluated in different orders:

| `For i = T("start", 1) To T("limit", 3) Step T("step", 1)` | Native code | P-code and the IDE |
|---|---|---|
| trail | **`limit step start`** | `start limit step` |

**That is observable without any error at all:** a For whose bounds have side effects behaves differently built native
and built as p-code. HexIDE evaluates start, limit, step, which is p-code's order.

**For.** When the start, limit or step raises, the body runs once with the counter never assigned. Then `Next`
carries on with loop state that was never set. Each case below began with the counter at the stated value, and the
probe's guard ended the loop after 4 passes:

| Header, counter before | Native code | P-code and the IDE |
|---|---|---|
| `1 To Boom()`, i 0 | **for ever**: 0 0 0 0 | 1 pass (0); after, 1 |
| `1 To Boom()`, i -5 | **for ever**: -5 -5 -5 -5 | -5 -4 -3 -2, counting up to 0 |
| `1 To Boom()`, i 7 | 1 pass (7); after, 7 | 1 pass (7); after, **8** |
| `Boom() To 3`, i 7 | 1 pass; after, 8 | 1 pass; after, 8 |
| `Boom() To 3`, i 0 | 0 1 2 3, counting to 3 | 1 pass (0); after, 1 |
| `Boom() To 3`, i 2 | 2 3; after, 4 | 1 pass (2); after, 3 |
| `1 To 3 Step Boom()`, i 7 | 1 pass; after, 7 | 1 pass; after, 7 |
| `1 To 3 Step Boom()`, i 0 | **for ever** at 0 | **for ever** at 0 |
| `Boom() To 1 Step -1`, i 7 | 7 6 5 4, counting down to 1 | 1 pass; after, 7 |
| `Boom() To 1 Step -1`, i 0 | 1 pass; after, -1 | **for ever** at 0 |
| `T("start", 1) To Boom() Step T("step", 2)`, i 7 | 1 pass; after, 7; trail `Boom` | 1 pass; after, 7; trail `start Boom` |

**The model that reproduces every p-code and IDE row:**
1. The three are evaluated in order, and one that raises stops the rest.
2. At `Next`, the counter is read back from the variable and the step added.
3. The result is tested against the limit: to stay at or below it when the step is 0 or more, at or above it otherwise.
4. A limit or step that was not evaluated is the value the statement last had in this call, or 0 if it has not run
   before. So a raised step is a `Step 0`, which loops for ever while the counter is within the limit.
5. Without a Step clause the step is 1 for an Integer or Long counter. Any other counter's implied step is kept as an
   evaluated one is, so before the statement has run it is 0 (review round one, below).

Native code fits the same model with two differences. It evaluates the limit and step before the start, and it has no
implied step: an unevaluated step is 0, with or without a Step clause, for every counter type. So `1 To Boom()` with the
counter at 0 loops for ever natively, and runs once in p-code.

"Last had in this call" is measured. In each case below, one For statement ran twice in one procedure; the first run
was normal and the second raised. All three modes agreed:

| Second run | VB6 |
|---|---|
| limit raises, i 3, after a run with limit 5 | 3 4 5; after, 6: the limit was **5** |
| step raises, i 0, after a run with `Step 2` | 0 2; after, 4: the step was **2** |
| start raises, i 3, after a run `To 5` | 3 4 5; after, 6 |
| a For in a Sub called twice, limit raising on the second call, i 3 | native: after, 3; p-code and the IDE: after, 4. Each call starts from 0 |

**A Variant counter** does not carry on. `Next` raises **92**, "For loop not initialized", and the loop ends. The
counter is left as it was, in all builds: `For vi = 1 To Boom()` with `vi` 7 gives one pass, then 7, and error 92.

**Measured with it:** VB6's `Next` reads the counter back, so a body that changes the counter changes the loop. This
matches the model above, and holds in both builds. HexIDE reads an integral numeric counter back at `Next` since #841:
- `For i = 1 To 10` with `i = i + 1` in the body runs 5 passes: 1 3 5 7 9, ending at 11.
  This was measured for both a Long and a Variant counter (#839, fifth probe, counter rows M1–M3; also recorded in #841).
- `If i = 3 Then i = 0` in `For i = 1 To 3` starts the loop again, for ever.

**For Each.** In each case `x` started as `"pre"`, and the errors were logged in the order raised, under an
`On Error GoTo` handler that did `Resume Next`:

| Collection | Errors | `x` in the body | `x` after |
|---|---|---|---|
| an unallocated `dyn() As Long` | 92, then 92 | `"pre"` | `"pre"` |
| a Collection variable that is Nothing | **424**, then 92 | `"pre"` | Empty |
| a Variant holding Nothing | 424, then 92 | `"pre"` | Empty |
| a Variant that is Empty | 13, then 92 | `"pre"` | Empty |
| a Variant holding 5 | 13, then 92 | `"pre"` | Empty |
| Null | 13, then 92 | `"pre"` | Empty |
| a Variant Function that raises 11 | 11, then 92 | `"pre"` | Empty |
| a Function `As Long()` that raises 11 | 11, then 92 | `"pre"` | `"pre"` |
| control: a two-item Collection | none | `a`, `b` | Empty |

The body runs once with the element as it was, then `Next` raises 92 and the loop ends. Whether `Next` empties the
element depends on whether the collection is an array. A declared array leaves the element alone, and so does a Variant
holding one (`v = dyn`, then `For Each x In v`, measured by #971's review in both modes). A Variant holding anything
else, or an object, empties it. Where the collection itself raised, nothing arrived, and its declared type decides: a
Function `As Long()` keeps the element and a Variant Function empties it.

**Nothing raises 424, "Object required", not 91.** 91 was our expectation.

**With:**

| Statement | VB6 |
|---|---|
| `With BoomObj()`, a Function returning a class that raises 11 | the body runs; `Err.Number` there is 11; `.Tag = 5` raises **91** |
| `With np`, a class variable that is Nothing | the `With` raises nothing; the body runs; `.Tag = 5` raises 91 |

**On Error GoTo, then `Resume Next` in the handler,** gives the same answers as `On Error Resume Next` in every case
tried:
- block If: Then;
- a raising Case test: its body;
- a raising selector: native `Case 0`, p-code and the IDE nothing;
- `Do While Boom()`: three passes;
- `For i = 1 To Boom()`: native after 7, p-code and the IDE after 8.

**What a condition is.** No error handling here: the value was assigned to a Variant `v` and tested by `If v Then` and
`Do While v`.

| `v` | If | Do While |
|---|---|---|
| 5, 0.5, `#1/1/2000#`, -1@ | Then | runs |
| 0 | Else | does not run |
| `"True"`, `"1"` | Then | runs |
| `"abc"`, `""` | error 13 | error 13 |
| Null, Empty | Else | does not run |

So a condition is read as `CBool` reads it, except that Null is False rather than error 94.

### The tests' own programs, run through vb6.exe

`ResumeNextIntoABlockTests` pins these rows, so its programs were also turned into one probe and run as written: 57
programs. **In the IDE, all 57 printed exactly what the tests expect.** As native code, exactly the 9 that the tests
name as p-code's answer differed, each as its name says.

The first such run found three tests wrong. They had declared `s` as a Variant, and so expected an error 13 that VB6
does not raise. HexIDE raised it anyway (#211), so the tests passed while pinning HexIDE's bug.

### What this overturned

- **That a raised condition skips its block.** HexIDE did exactly that, and #174's probe was the first to show
  otherwise.
- **#174's probe said `Select Case k` reaches Case Else.** That is native code's answer only. Its explanation, that
  each Case raised in turn, was wrong: no Case was tested against `k` at all. P-code and the IDE run nothing.
- **This file's first #839 probe said a raised selector runs nothing.** True in p-code and the IDE always, and in
  native code only for a numeric selector with constant Cases: an object selector reaches Case Else there even with
  constant Cases.
- **That one oracle build speaks for VB6.** The For evaluation order differs between the compiled forms with no error
  anywhere. The harness's default, native code, is the one that differs from the IDE.
- **For Each over Nothing raises 424, not 91.**

### Resisted explanation

Nothing in the p-code columns. The native columns follow from the model above with the native order and a kept step,
but that was checked row by row, not proved.

### Review round one (2026-10-13)

#971's review measured four more axes, and each was then measured here with `/make`, one project built twice, as native
code and as p-code (each with the tells above); the p-code projects were then run in the IDE too, which matched p-code on
every line. The counter ran from 7, or 0, with the trail and a guard of four passes as above. Unless a column says
otherwise, native code and p-code agree.

**The counter's type decides the implied step.** `For c = 1 To Boom()`, no Step clause:

| Counter | from 7 | from 0 |
|---|---|---|
| Integer, Long | p-code `1: 7:8`; native `1: 7:7` | p-code `1: 0:1`; native for ever at 0 |
| Byte, Single, Double, Date | `1: 7:7` | for ever at 0 |
| Currency | p-code `1: 7:7`; native **for ever at 7** | for ever at 0 |

`For c = Boom() To 3` gives the same split: Integer and Long step by 1 in p-code, and the other types by 0, so a Byte
from 0 loops for ever in p-code where native code counts 0 to 3. After a normal run of the same statement in the same
call, every type keeps that run's limit and step, the implied 1 included: `For c = 1 To Lim(k)`, raising on the second
run with `c` at 0, counts 0 1 2 3 for a Byte as for a Long, and `For c = 1 To 5 Step St(k)` counts 0 2 4.

**A typed parameter is a typed counter.** `Function P(ByVal i As Long)` with `For i = 1 To Boom()`: `P(7)` is p-code
`1: 7:8`, native `1: 7:7`; `P(-5)` counts -5 -4 -3 -2 up in p-code and stays at -5 natively. The same for a ByRef `i As
Long` given a literal and for `Optional i As Long = 7`; a `ByVal b As Byte` is a Byte (`P(0)` loops for ever). The
parameter keeps its type for a store too: `Optional i As Long = 7` is `Long 7`, `i = 2.5` into `ByVal i As Long` is
`Long 2`, and `b = 300` into a `ByVal b As Byte` raises 6 and keeps 7.

**The bounds convert into the counter's type, in the header.** Through variables, so nothing is folded:

| Counter, header | VB6 |
|---|---|
| Byte, `s0 To l0` with 300 To 400; `1 To l0` with 300; `1 To 3 Step st` with 300 | 6, then the raised header: `1: 7:7`, and from 0 for ever |
| Byte, `3 To 1 Step st` with -1; `0 To 0 Step st` with -1; `3 To 1 Step -1`, a literal | 6, the same |
| Integer, `s0 To l0` with 40000; `1 To l0` with 40000 | 6; p-code `1: 7:8` and `1: 0:1`, native `1: 7:7` and for ever |
| Integer, `1 To 3 Step st` with 40000 | 6; `1: 7:7`, and from 0 for ever |
| Integer, `1 To 3 Step st` with 0.5 | 1 for ever: 0.5 is 0 |
| Integer, `Step st` with 1.5; with 2.5 | 1 3 5; 1 3 5 7: 1.5 is 2, and 2.5 is 2 too |
| Integer, `s0 To 4` with 1.5; with 2.5 | 2 3 4, both |
| Integer, `1 To l0` with 2.5; with 3.5 | 1 2; 1 2 3 4 |
| Long, `Step st` with 1.5 | 1 3 5 |
| Single and Currency, `1 To 2 Step st` with 0.5 | 1 1.5 2, then 2.5 |
| Integer, `1 To l0` with `"3"`; with `"x"` | 1 2 3; 13, then the raised header |

So each of start, limit and step is converted as a store into the counter would convert it (banker's rounding, 6 on
overflow, 13 for text that is not a number) as it is evaluated, and a conversion that raises is a raised header. A
Byte counter cannot step down at all.

**A Case Is or To value converts into the selector's type**, as an equality Case's does (see *Select Case matching*).
**Corrected in review round two:** into a TYPED selector's type. Each selector below is a literal or a declared variable
except the Variant in the third row, whose answer the two rules share; a Variant selector converts nothing, and the
section *A typed selector and a Variant one* has its rules.

| Selector | Case | VB6 |
|---|---|---|
| 5 | `Is > "abc"`; `"a" To "z"`; `1 To "x"`; `Is = "abc"`; `Is > v`, `v` a Variant holding `"abc"` | that Case, Err 13 |
| 5 | `Is > "3"` | that Case |
| `"abc"`, a String variable or a Variant holding it | `Is > 5` | that Case: 5 becomes `"5"` against the two typed ones, and the Variant is above any number (round two) |
| `"10"`; a String `"9"` | `Is > 9`; `Is < 10` | Case Else: text against text |
| 2 | `Is < 2.4`; `Is < 2.6`; `1.6 To 1.9`; `2.6 To 3` | Else; that Case; that Case; Else |
| a Currency 2 | `Is < 2.00004` | Case Else |
| 2 | `Is > 40000` | that Case, Err 6 |
| Null | `Is > "abc"` | Case Else |
| Empty | `Is > "abc"`; `Is < 1` | Case Else; that Case |

**An empty branch.** `If Boom() > 0 Then` with an empty Then and an Else, and the same for an empty ElseIf after
`If False`: neither branch runs, Err 11.

**Resisted explanation.** Natively, a Currency counter's `1 To Boom()` from 7 loops for ever at 7, where every other type
stops after one pass. An unevaluated limit of 0 explains the others; this one behaves as if the limit were at least 7.
P-code does not do it, and HexIDE follows p-code.

### HexIDE, after #839

**Conditions.** If, ElseIf, one-line If, Do and While read a condition through `ConditionMet`, which wraps main's
`ConditionHolds` (#847, #840) and adds only what Resume Next needs.
- A condition that raises under Resume Next is recorded in `Err` and enters its block: the branch, or a pre-tested
  loop's body, asked again next time round. A post-tested loop ends. An empty branch runs nothing (review round one);
  it crashed, visiting a block the grammar does not give it.
- The reading is `ConditionHolds`'s: `CBool`'s rule, Null and Empty False, an object read as an operand is. This branch
  first wrote the same reading itself, and #847 landed it on main first, so the merge kept main's. An untrappable
  refusal, such as a class's default member (#174), still stops the run under Resume Next, because it is not a run-time
  error.

**Select Case.**
- A Case test that raises runs its own Case's body.
- A selector that raises skips the statement, which is the IDE's and p-code's answer, and native code's for a numeric
  selector with constant Cases.
- How a Case value is compared is main's, unchanged. Review round one converted a `Case Is` or `To` value into the
  selector's type, round two limited that to a typed selector and compared a Variant one as VB6 compares Variants, and
  round three took both out of #839, because each round's version regressed programs it did not measure. The
  measurements are in *A typed selector and a Variant one*, and the work is #987.

**For.**
- A header that raises runs the body once, then carries on by the p-code model above. A Variant counter raises 92 at
  `Next` instead.
- Each call keeps the limit and step each For statement last evaluated, a missing Step clause's 1 included.
- The infinite loops are reproduced: a raised step is a `Step 0`.
- Review round one: without a Step clause a raised header steps by 1 only for an Integer or Long counter, and by the
  kept step for a Byte, Single, Double, Currency or Date one. A String or Boolean counter is refused, since none was
  measured.
- Review round one: each bound converts into a numeric counter's type as it is evaluated, through the conversion a
  store uses, so an overflow or a text that is not a number is a raised header, and a fractional bound rounds into an
  Integer or Long counter. A typed parameter's slot now records its numeric type, as a Dim's does, so it is a typed
  counter and a store into it converts. It is also a ceiling in arithmetic, as a
  declared variable is (#122), where it widened; review round four measured that, below.
- A start, limit or step still fractional after its conversion into the counter's type is refused as not implemented,
  and untrappably. That is any counter that keeps a fraction: a Single, Double, Currency or Date one, and a Variant or
  undeclared one, which is the common `For x = 0 To 1 Step 0.25`. Main raised a trappable 13 for every fractional For,
  which Resume Next stepped over, skipping the loop without a word. Neither is VB6's answer, and this one says so (#983).
- A raised header with a String or Boolean counter is refused, since VB6 was not measured there (#984).

**For Each.**
- An unallocated array raises 92, Nothing 424, and any other non-object value 13, each at the statement. The first used
  to run no passes and say nothing. The others used to raise 13 with a note that Collections were not implemented.
- Under Resume Next, the body runs once and `Next` raises 92, emptying the element unless the collection is an array.
- Where the collection itself raised, #969 reads its declaration from existing runtime slots and member tables:
  parentheses unwrap, an array element is not declared as an array, and direct class methods/Property Gets and
  module-qualified Functions use their return declarations. A variable shadows a same-named Function.
  This applies the measured declared-array rule above; it adds no new VB6 measurement.
  Unavailable declarations still refuse, naming the expression and #969; the remaining shapes are recorded in
  interpreter-gaps.md under Resume granularity.
- For Each over an object is refused as not implemented, where it used to raise a trappable 13.

**With.** A target that raises leaves the body with no object, so each leading-dot member raises 91.

**Not changed: On Error GoTo, then `Resume Next` in the handler.** HexIDE resumes after the whole statement at the
procedure's top level. That is the tree-walk limit for nested-granular `Resume`, recorded in `interpreter-gaps.md`.


