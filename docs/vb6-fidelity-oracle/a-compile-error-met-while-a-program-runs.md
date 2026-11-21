## A compile error met while a program runs, with Compile On Demand on and off (2026-11-21, #833, IDE, SP6)

**The question.** A program can reach code that doesn't compile while it runs, such as a call to a Sub that doesn't
exist. HexIDE shows that in its run-time error dialog and lets the run carry on (divergence D22, #833). What does VB6's
IDE do at F5, with Compile On Demand on and off? Which box appears, with which buttons? Does the IDE reset or break,
and where? Does any code after the failing call run? And what of a syntax error?

**The probe:**
- **The IDE:** VB6.EXE 6.00.9782 (SP6), in the console session of a clone of the oracle guest. It was driven through
  the console agent, as "Reaching the VB6 *IDE*, not just the compiler" in the README describes.
- **Compile On Demand:** set in the registry value `CompileOnDemand` (HKCU, `Software\Microsoft\VBA\Microsoft Visual
  Basic`) before each IDE started, and set back to the guest's 1 afterwards. Background Compile stayed on throughout.
- **Each row was a fresh IDE:** F5, a wait of 4 s, then Enter on whatever box was up.
- **What was recorded** after F5, after Enter, and 3 s later:
  - the IDE's title;
  - every window of the VB6 process, with its texts;
  - the program's log;
  - a screenshot.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.

**The three programs.** Each is a Standard EXE with `Form1` as its startup. `Form1` has a Timer at 1500 ms, and `L`, a
Public Sub in `Module1`, appends a line to a log.

| Program | Where the code that doesn't compile is |
|---|---|
| **A** | `Module2`: `Public Sub CallsMissing()` runs `L "in callsmissing"`, then `NoSuchSub`, then `L "after nosuchsub"`. The Timer's handler runs `Timer1.Enabled = False`, `L "tick before"`, `CallsMissing`, `L "tick after"` |
| **B** | `Form1` itself: the Timer's handler calls `NoSuchSubInForm` in place of `CallsMissing` |
| **C** | `Module2`, as in A, but with the syntax error `synX = (1 +` in place of `NoSuchSub` |

`Form_Load` runs `L "load"` in every program.

**The box** is the same in every row, apart from its second line. It is titled *Microsoft Visual Basic*, reads
"Compile error:" over "Sub or Function not defined" (A and B) or "Syntax error" (C), and offers **OK and Help only**.
There is no Debug or End button.

| Program | Compile On Demand | When the box came | Log by then | After OK |
|---|---|---|---|---|
| A | on | at the Timer's call to `CallsMissing`, with the IDE in *[run]* and the box owned by `Form1` | `load`, `tick before` | **[break].** The execution point is on `Public Sub CallsMissing()`, and `NoSuchSub` is selected |
| A | off | at once, with the IDE in *[design]* | nothing | **[design].** `NoSuchSub` is selected, with no execution point |
| B | on | at the tick, before the handler's first statement | `load` | **[break].** The execution point is on `Private Sub Timer1_Timer()`, and `NoSuchSubInForm` is selected |
| B | off | at once, in *[design]* | nothing | [design], with `NoSuchSubInForm` selected |
| C | on | at the call to `CallsMissing` | `load`, `tick before` | [break] at `Public Sub CallsMissing()`, with `synX = (1 +` selected |
| C | off | at once, in *[design]* | nothing | [design], with `synX = (1 +` selected |

In every row, 3 s after OK nothing more had been logged. `tick after`, `in callsmissing` and `after nosuchsub` never
ran.

**From that break (program A, Compile On Demand on):**

| Key | Result |
|---|---|
| F5 | The same box at once, with the IDE in *[run]*. Nothing more was logged |
| OK, then F8 | The same box again |

So the run can't get past the procedure. Its first statement never runs, and only End or Reset leaves the break.

**Readings:**
- **With Compile On Demand on, the error comes when the procedure that holds it is first called,** before any of its
  statements run.
  - Code that compiles runs first. In program B, `Form_Load` ran, and its sibling in the same module failed only when
    the Timer called it.
  - This is consistent with compiling each procedure on its first call. Whether the rest of `Module2` compiles at the
    same moment isn't told apart here, since `CallsMissing` is its only procedure.
- **With Compile On Demand off, F5 compiles everything first.** The box comes before any code runs, and the IDE stays
  in design mode.
- **The box offers OK and Help only.**
  - With Compile On Demand on, the IDE is in break mode after OK. The execution point is on the failing procedure's
    declaration line, and the offending code is selected.
  - With it off, the IDE is in design mode after OK, with the same selection.
- **A syntax error behaves the same way.** With Compile On Demand on, it doesn't stop F5. It's reported when its
  procedure is first called.

### In HexIDE

D22 in `docs/debugger-vb6-divergences.md` and #833 record what HexIDE does. It shows the error in its run-time error
dialog, with End enabled and Continue, Debug and Help not, and closing the dialog any other way leaves the run going.
That wasn't checked again for this page.

### Not measured

- Background Compile off, with Compile On Demand on;
- Ctrl+F5, Start With Full Compile;
- a module with several procedures, one of which doesn't compile, to tell compiling a procedure from compiling its
  module;
- a call reached from a click rather than a Timer.
