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
- a watch whose context is another module (measured in the last section, #4481).

## Add Watch's Module and Procedure lists, for a property (2026-11-17, #4401)

**The question.** What does Add Watch's Procedure list offer for a class with a property: one entry per property, or
one per Property procedure? And what does the Context column then read?

**The probe:**
- **The software:** VB6's IDE, SP6, on the oracle VM (Windows 10). Measured by the hexide-int session, at
  hexide-mcp's request.
- **The project:** a Standard EXE holding Form1 and Class1. Class1:

  ```vb
  Private m As Long

  Public Property Get X() As Long
      X = m
  End Property

  Public Property Let X(ByVal v As Long)
      m = v
  End Property

  Public Sub Bump()
      m = m + 1
  End Sub
  ```

- **The steps:** in design mode, Debug, Add Watch, with no code window open. Module set to Class1, then the Procedure
  list read. Expression `X`, Procedure set to each property entry in turn, then OK, and the Watches window read.
- **How it was read and driven:** the list items by window message (`CB_GETLBTEXT`) and from a screenshot of the open
  list, which agree. The choices were made by window message (`CB_SETCURSEL`, with `CBN_SELCHANGE` sent to the dialog)
  and OK by `BM_CLICK`; a screenshot of the filled dialog shows the state a person's choice leaves.

**What it showed:**

| What | Measured |
|---|---|
| Module list | `(All Modules)`, `Class1`, `Form1` |
| Module, with no code window open | `(All Modules)` |
| Procedure list, Module `(All Modules)` | `(All Procedures)` only |
| Procedure list, Module `Class1` | `(All Procedures)`, `Bump`, `X(Get)`, `X(Let)`, in that order |
| Procedure, by default | `(All Procedures)` |
| Context column, Procedure `X(Get)` | `Class1.X(Get)` |
| Context column, Procedure `X(Let)` | `Class1.X(Let)` |
| Value and Type, in design mode | `<Out of context>`, `Empty` |

**What it shows:**
- **A property's procedures are separate entries,** each the name with its kind in parentheses and no space:
  `X(Get)`, `X(Let)`. A Sub is its plain name. The Context column carries the same text after the module.
- **The guesses this overturns.** #4401's brief assumed a property is listed once, by its name. The bundled language
  server names the same procedures `X (Get)` and `X (Let)`, with a space, and HexIDE's Add Watch lists those names
  (2026-11-17, #4401).
- **Add Watch can watch every module.** The Module list starts with `(All Modules)`, which is the default with no code
  window open; with the caret in a module's declarations it is that module (the section above).

**Unmeasured:**
- what the Context column reads for a watch added with Module `(All Modules)` (measured in the next section);
- a `Property Set`;
- whether a watch scoped to `X(Get)` evaluates while paused in `X(Let)` (measured in the next section);
- Procedure's default with the caret inside a procedure.


## A watch scoped to one accessor, and a watch on (All Modules) (2026-11-17, #4401, #4452)

**The questions.**
- Does a watch scoped to `X(Get)` evaluate while paused in `X(Let)`?
- What does the Context column read for a watch added with Module `(All Modules)`, and which names can that watch see?

**The probe:**
- **The software:** VB6's IDE, SP6, on the oracle VM (Windows 10). Measured by the hexide-int session, at hexide-mcp's
  request, in three runs.
- **The project:** the one in the section above, changed so each accessor stops, plus a third stop outside the class:

  ```vb
  ' Class1
  Private m As Long

  Public Property Get X() As Long
      Stop
      X = m
  End Property

  Public Property Let X(ByVal v As Long)
      Stop
      m = v
  End Property

  ' Form1
  Private Sub Form_Load()
      Dim c As New Class1
      c.X = 5
      Debug.Print c.X
      Stop
  End Sub
  ```

- **The first run:** four watches, read in design mode and then at each stop in turn:
  - `m` at `Class1.X(Get)`;
  - `m` at `Class1.X(Let)`;
  - `v` at `Class1.X(Let)`;
  - `m` at `Class1`, `(All Procedures)`.
- **The second run:** watches added with Module `(All Modules)`. The project gained `Module1` with `Public g As Long`, and
  Form_Load gained `g = 7`.
- **The third run:** more `(All Modules)` watches, to find which names project scope includes.
  - Module1 gained `Private p As Long` and `Public Sub SetP()`, which sets `p = 3`.
  - Form_Load gained two locals, `loc` and `zq`.
  - Form_Load then ran `SetP`, `g = 7`, `c.X = 5`, `Debug.Print c.X`, `loc = 9` and `zq = 8`, then `Stop`.
  - The watches were added in the order `p`, `loc`, `zq`, `g`, `1 + 1`.
- **How it was read:** from the Watches window, in design mode and at each stop, with screenshots kept by hexide-int.

**What the first run showed** (Value / Type):

| Watch, and its Context column | Stop in the Let (`m` 0, `v` 5) | Stop in the Get (`m` 5) | Stop in Form_Load |
|---|---|---|---|
| `m`, `Class1.X(Get)` | `<Out of context>` / Empty | 5 / Long | `<Out of context>` / Empty |
| `m`, `Class1.X(Let)` | 0 / Long | `<Out of context>` / Empty | `<Out of context>` / Empty |
| `v`, `Class1.X(Let)` | 5 / Long | `<Out of context>` / Empty | `<Out of context>` / Empty |
| `m`, `Class1` | 0 / Long | 5 / Long | `<Out of context>` / Empty |

In design mode all four read `<Out of context>` / Empty.

**What the second run showed**, for watches added with Module `(All Modules)`:

| Watch | Context column | Value / Type |
|---|---|---|
| `m`, Class1's Private | blank | `<Expression not defined in context>` / Empty at all three stops, the one inside Class1's own Let included |
| `g`, Module1's Public | blank | 0 / Long in design mode; 7 / Long at the stop in the Let |
| `1 + 1` | blank | 2 / Integer in design mode and at the stop in the Let |

**What the third run showed**, for watches added with Module `(All Modules)`, Procedure `(All Procedures)`. Every
Context column was blank. "Not defined" stands for `<Expression not defined in context>` / Empty.

| Watch | Design mode | Stop in the Let | Stop in the Get | Stop in Form_Load |
|---|---|---|---|---|
| `g`, Module1's Public | 0 / Long | 7 / Long | 7 / Long | 7 / Long |
| `p`, Module1's Private (3) | not defined | not defined | not defined | not defined |
| `zq`, Form_Load's local (8 at the last stop) | not defined | not defined | not defined | **not defined** |
| `loc`, shown as **`Loc`** | not defined | not defined | not defined | not defined |
| `1 + 1` | 2 / Integer | 2 / Integer | 2 / Integer | 2 / Integer |

The blank Context column was seen on every `(All Modules)` watch, across all three runs and both projects.

**What it shows:**
- **Each accessor is its own context.** A watch scoped to `X(Get)` reads `<Out of context>` in the Let, and one scoped to
  `X(Let)` reads it in the Get. A watch on the module, `(All Procedures)`, evaluates in any of the module's procedures
  and nowhere else.
- **`(All Modules)` sees Public module-level names only.**
  - A standard module's Public variable is found, in design mode and at every stop.
  - A standard module's Private variable is not, nor a class's Private variable, nor a procedure's local. That holds
    even where execution is stopped in their own scope: `zq` was not defined at the stop inside Form_Load, where it
    held 8, and `m` was not defined inside Class1's own Let.
  - The message for a name it can't see, `<Expression not defined in context>`, differs from `<Out of context>`.
  - Such a watch evaluates in design mode too, where every scoped watch reads `<Out of context>`.
- **The guesses this overturns.** A watch on `X(Get)` could have applied in any procedure named `X`; it doesn't. The
  natural reading of `(All Modules)`, "wherever execution is stopped", is wrong: it is the project's Public names, from
  anywhere.
- **The locals row rests on `zq`, not `loc`.** VB6 recased the watch expression `loc` to `Loc`, the case of the intrinsic
  `Loc(filenumber)` function, though the code window kept `loc`. So `Loc`'s result may be the function's rather than
  the local's. `zq` collides with nothing, and was added for that reason.

**What resisted explanation:**
- **The order of the Watches window's rows.**
  - In the first two runs, rows came out ordered by Expression, then by Context, with a blank Context first: `1 + 1`
    before `g`, though `g` was added first. Those runs couldn't tell that order from others.
  - The third run's order discriminates. Added as `p`, `loc`, `zq`, `g`, `1 + 1`, the rows came out as `1 + 1`, `Loc`,
    `g`, `p`, `zq`. That's neither the order they were added in nor its reverse. A case-insensitive sort would put `g`
    before `Loc`. It fits a binary (case-sensitive) sort by Expression.
  - That's one discriminating probe, so it's a candidate rule, not an established one. How rows with the same
    Expression are ordered is unmeasured.
  - **Overturned by the next section's run:** the rows sort by Context first, then by Expression. These rows all had a
    blank Context, which is why Expression alone fitted them.
- **The recasing.** `loc` becoming `Loc` matches an intrinsic's case. Whether the first section's `inner` becoming
  `Inner` has the same cause isn't established.

**Unmeasured:**
- under `(All Modules)`, a standard module's Public constant, a form's Public variable, and a call to a Public function;
- `m` under `(All Modules)` in design mode;
- a `Property Set`.

## A watch scoped to another module, and the Watches window's row order (2026-11-17, #4481)

**The questions.**
- Paused in one module, does a watch scoped to another module's procedure, or to a class, read that module's frame?
- Is a watch scoped to a standard module live while that module isn't on the call stack?
- In what order does the Watches window list its rows?

**The probe:**
- **The software:** VB6's IDE, SP6, on the oracle VM (Windows 10). Measured by the hexide-int session, at hexide-mcp's
  request.
- **The project:** a Standard EXE with Form1, Module1 and Class1.

  ```vb
  ' Module1
  Public g As Long

  Public Sub Helper()
      Stop
  End Sub

  ' Class1
  Private m As Long

  Public Property Let X(ByVal v As Long)
      Helper
      m = v
  End Property

  ' Form1
  Private Sub Form_Load()
      g = 7
      Dim c As New Class1
      c.X = 5
      Stop
  End Sub
  ```

- **The watches,** added in this order: `v` at `Class1.X(Let)`, `m` at `Class1` (All Procedures), and `g` at `Module1`
  (All Procedures).
- **The run:** read in design mode, then at the `Stop` in Helper and at the `Stop` in Form_Load. At the first stop,
  Helper is the paused frame in Module1, and Class1's `X(Let)` is the frame directly below it. At the second, neither
  Module1 nor Class1 is on the call stack.
- **How it was read:** from the Watches window, with screenshots kept by hexide-int.

**What it showed** (Value / Type):

| Watch, and its Context column | Design mode | Stop in Helper | Stop in Form_Load |
|---|---|---|---|
| `v`, `Class1.X(Let)` | `<Out of context>` / Empty | **5 / Long** | `<Out of context>` / Empty |
| `m`, `Class1` | `<Out of context>` / Empty | **0 / Long** | `<Out of context>` / Empty |
| `g`, `Module1` | **0 / Long** | 7 / Long | **7 / Long** |

The rows were listed `m`, `v`, `g`: Context `Class1`, then `Class1.X(Let)`, then `Module1`.

**What it shows:**
- **A procedure's watch, and a class's, read their frame one level below the paused one, across modules.** Paused in
  Module1's Helper, `v` reads X(Let)'s argument, and `m` reads the instance, before `m = v` has run. With neither on the
  stack, both read `<Out of context>`.
- **A standard module's watch is live whenever the program runs,** with its module off the stack, and in design mode too.
- **The rows sort by Context, then Expression.** Added as `v`, `m`, `g`, they came out as `m`, `v`, `g`, which isn't
  Expression order. All four runs on this page fit Context first:
  - a blank Context sorts first;
  - then `Class1`, `Class1.X(Get)`, `Class1.X(Let)`;
  - Expression breaks ties within one Context, case-sensitively, as in the previous section's `Loc` and `g` row.
- **The guess this overturns:** the previous section's candidate rule, a binary sort by Expression. It held only because
  those rows shared a Context. Two runs now discriminate between the two rules, and both fit Context first.

**Unmeasured:**
- a frame more than one level below the paused one, in another module;
- a procedure on the call stack twice;
- a form's watch with the form not on the stack;
- the row order between Contexts that differ only in case.
