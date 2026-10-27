## A watch and its context, in the IDE's Watches window (2026-10-27, #2290)

**The question.** A watch is added with Procedure set to (All Procedures), on a local that belongs to one procedure.
What does the Watches window show while that procedure is paused, while another one is, and after Step Out?

**The probe:**
- **The software:** VB6's IDE, SP6 (6.00.9782), on Windows 10, in the en-GB user locale.
- **The project:** a Standard EXE whose only form is Form1:

  ```vb
  Option Explicit
  Private modvar As Integer

  Private Function Square(ByVal n As Integer) As Integer
      Dim inner As Integer
      inner = n * n
      Stop
      Square = inner
  End Function

  Private Sub Form_Load()
      Dim total As Integer
      modvar = 5
      total = Square(7)
      Stop
      Debug.Print total
  End Sub
  ```

- **The watches:** six, added at design time through Debug, Add Watch, each in module Form1 with Watch Type set to Watch
  Expression.
  - `inner`, `total`, `nosuch` and `modvar` kept Procedure at (All Procedures). With the caret in the declarations,
    the dialog offers that. The Context column reads `Form1`.
  - A second `inner` had Procedure set to Square, and its Context column reads `Form1.Square`.
  - A second `total` had Procedure set to Form_Load, and its Context column reads `Form1.Form_Load`.
- **The run:**
  1. F5, which stops at the `Stop` in Square.
  2. Step Out, which stops at the `Stop` in Form_Load, with `total = Square(7)` complete.
  3. F5, which runs Form_Load to its end and shows the form.
  4. Run, End.

  The window was screenshotted at design time, and after steps 1, 2 and 4.

**What the window showed**, with the Type column in brackets:

| Watch | Context | Design time | Paused in Square | After Step Out, in Form_Load | After End |
|---|---|---|---|---|---|
| `inner` | Form1 | `<Expression not defined in context>` (Empty) | the same | the same | the same |
| `total` | Form1 | `<Expression not defined in context>` (Empty) | the same | the same | the same |
| `nosuch` | Form1 | `<Expression not defined in context>` (Empty) | the same | the same | the same |
| `modvar` | Form1 | `<Out of context>` (Empty) | 5 (Integer) | 5 (Integer) | `<Out of context>` (Empty) |
| `inner` | Form1.Square | `<Out of context>` (Empty) | 49 (Integer) | `<Out of context>` (Empty) | `<Out of context>` (Empty) |
| `total` | Form1.Form_Load | `<Out of context>` (Empty) | 0 (Integer) | 49 (Integer) | `<Out of context>` (Empty) |

**What it shows:**
- **There are two texts, and they mean different things.**
  - `<Expression not defined in context>`: the expression's names don't resolve in the watch's context. A
    procedure's local in a module-wide watch reads this, and so does a name that exists nowhere.
  - `<Out of context>`: the names resolve, but the context isn't live. Either nothing is running, or the watch's
    procedure isn't on the call stack.
- **A module-wide watch never sees a local, even inside the procedure that declares it.** Paused in Square, the (All
  Procedures) watch on `inner` read `<Expression not defined in context>`, not 49. The guess this overturns is the
  one #2290 started from: that (All Procedures) evaluates in whichever procedure is paused.
- **A procedure's watch reads that procedure's frame from anywhere on the call stack.** Paused in Square, the
  Form_Load watch on `total` read 0, its value at that moment, because Form_Load was Square's caller. Once Square had
  returned, its own watch read `<Out of context>`.
- **The Type column** is Empty beside either text, and the value's type when the watch evaluates.

**What resisted explanation:**
- **Both `inner` watches' Expression column read `Inner`, capitalised.** They were typed as `inner`, the code declares
  `Dim inner`, and the code window still showed `inner`. The other three expressions kept the case they were typed in.
- **While the program ran after step 3, the window wasn't re-evaluated.** `modvar` still read 5, and the row that held
  the selection painted blank. Don't build on this.

**Unmeasured:**
- what the Immediate window prints for `?inner` paused in Form_Load, which the probe's keystrokes didn't reach;
- the same procedure twice on the call stack;
- the two Break When watch types;
- a watch whose context is another module.
