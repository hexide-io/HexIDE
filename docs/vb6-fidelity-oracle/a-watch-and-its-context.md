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
- What does the Context column read for a watch added with Module `(All Modules)`, and where does that watch evaluate?

**The probe:**
- **The software:** VB6's IDE, SP6, on the oracle VM (Windows 10). Measured by the hexide-int session, at hexide-mcp's
  request.
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
- **How it was read:** from the Watches window, at each stop, with screenshots kept by hexide-int.

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

The blank Context column was seen on 3 watches across 2 projects.

**What it shows:**
- **Each accessor is its own context.** A watch scoped to `X(Get)` reads `<Out of context>` in the Let, and one scoped to
  `X(Let)` reads it in the Get. A watch on the module, `(All Procedures)`, evaluates in any of the module's procedures
  and nowhere else.
- **`(All Modules)` is project scope, not the scope execution is stopped in.** A class's Private variable isn't found
  even while stopped inside that class, and the message, `<Expression not defined in context>`, differs from
  `<Out of context>`. A standard module's Public variable is found. Such a watch also evaluates in design mode, where
  every scoped watch reads `<Out of context>`.
- **The guesses this overturns.** A watch on `X(Get)` could have applied in any procedure named `X`; it doesn't. The
  natural reading of `(All Modules)`, "wherever execution is stopped", is wrong.

**What resisted explanation:**
- **The order of the Watches window's rows.** In all 3 runs the rows came out ordered by Expression, then by Context,
  with a blank Context first: `1 + 1` before `g`, though `g` was added first. Three runs can't tell that from other
  orders, so it is not a rule. A row added in an order that discriminates would settle it. Don't build on it.

**Unmeasured:**
- under `(All Modules)`, a Private module-level variable of a standard module, and a procedure's local;
- `m` under `(All Modules)` in design mode;
- `g` and `1 + 1` under `(All Modules)` at the stops in the Get and in Form_Load;
- a `Property Set`.
