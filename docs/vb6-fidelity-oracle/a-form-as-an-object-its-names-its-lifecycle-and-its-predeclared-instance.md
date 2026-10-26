## A form as an object: its names, its lifecycle and its predeclared instance (2026-10-14, #822)

**The question.** #822 makes a running form an instance of its module, with a real `Me`, where HexIDE has had a form's
code run as a standard module with `Me` bound to the window. The design turns on four things nobody had measured: how
a bare name resolves inside a form, whether a form's members are visible outside it, when a form's declarations exist
relative to its events, and what the form's own name refers to.

**The probes.** Each is a Standard EXE built with `/make` as native code and as p-code, the two agreeing on every row,
and run in session 0. Every log line is written by opening, appending and closing the file, as in [`End` itself, from
three places](end-fires-no-class_terminate.md#end-itself-from-three-places-2026-10-12-775). Each lookup question is a project of its own, so that a
compile error in one could not hide another. `Form1` has a TextBox `Text1`, and `Module1` is a standard module.

### Names inside a form module

| Form code | Also declared | VB6 |
|---|---|---|
| `Caption = "set by form"`, then `Caption`, `Me.Caption` and `Top = Me.Top` | nothing | sets the form's caption; reads `set by form` twice; `True` |
| `Caption = "set by form"` | `Public Caption As String` in Module1 | `Me.Caption` is `set by form`; `Module1.Caption` is still empty |
| `Tag` | `Public Function Tag()` in Module1, returning `module tag` | the form's `Tag`, empty; `Module1.Tag()` is `module tag` |
| `TypeName(Text1)` | `Public Text1 As String` in Module1 | `TextBox` |
| `Score = 5` | `Public Score As Long` in the form and in Module1 | `Me.Score` is 5; `Module1.Score` is 0 |
| `Left("abc", 1)` | nothing | `a`, from VBA's `Left` |
| `Left` | nothing | the form's `Left`, equal to `Me.Left` |
| `Dim Tag As String` at form level | nothing | does not compile: "Member already exists in a object module from which this object module derives" |
| `TypeName(Me)`, `TypeName(Form1)` and `Me Is Form1` | nothing | `Form1`, `Form1`, `True` |

So inside a form, a bare name is the form's own before it is anything a standard module declares: its variables, its
controls, and its built-in members, which are implicit `Me`. A form may not declare a member its Form base already has.
VB6 gives no order to settle between the two, because a program that has both doesn't compile.

**One exception, measured for one function only:** `Left("abc", 1)`, called with arguments, is VBA's `Left`, while a bare
`Left` is the form's property. The form's `Left` takes no arguments. Whether arguments are what decides it was not
measured beyond this one name, so it is recorded here as a fact about `Left`, not as a rule.

**Measured further for #995, and the guess was wrong.** The obvious reading, that a call with arguments simply skips
a property that takes none, is not what VB6 does. The fallback is VBA's function, and only when the call fits it: in a
form `Left(5)` and `Left("abc", 1, 2)` are "Wrong number of arguments or invalid property assignment", where in a
standard module `Left(5)` is VBA's own "Argument not optional". And a project's Function is never the fallback:
`Caption(1)` and `Tag(2)` in a form are the same "Wrong number" error though Module1 declares `Function Caption(n)` and
`Function Tag(n)`. `Left` is the only Form member that is also a VBA function, so this is as far as the rule can be
measured. See *Bare names in a form, for implicit `Me`* below.

### The form from a standard module

| Module1 code | VB6 |
|---|---|
| `Form1.Caption`, `Form1.Score` (a Public of the form) and `Form1.Text1.Text` | `cap`, `5`, `t1` |
| `Score`, unqualified, where `Score` is a Public of the form | Empty: a new variable, there being no Option Explicit |
| `Hello`, unqualified, where `Hello` is a Public Sub of the form | does not compile: "Sub or Function not defined" |
| `Ping`, unqualified, where Module2 and the form each have a Public Sub `Ping` | Module2's runs: `module2 ping` |

A form's Public members are members of the form, not project globals. They are reached through its name, and not
without it. The `Ping` row is the same rule seen from the other side: a form's Public Sub is not a candidate for an
unqualified name at all, so it cannot make a module's Sub of the same name ambiguous. The probe was Form1, Module1 and
Module2 in one Standard EXE, with Form1's Load calling Module1's `CallPing`, whose body is the bare `Ping`. [A form-based game's runtime surface](a-form-based-games-runtime-surface.md#a-form-based-games-runtime-surface-2026-10-12) had the same refusal
for a class calling a form's Public Sub unqualified, and the other direction too: a standard module's Public Const and
variable are reachable unqualified from a form.

### The form's lifecycle

| Probe | Log |
|---|---|
| The startup form declares `Const K = 5`, `Dim a(1 To K) As Integer`, `Dim n As Integer` and `Private o As Class1`, and sets `o` in Load. A Timer does `Unload Me` | `initialize: UBound(a)=5 n=0`, `load`, `class initialize`, `resize`, `activate`, `timer`, `queryunload, mode 1`, `unload`, `terminate`, `class terminate` |
| `Form_Initialize` sets `Text1.Text` | `initialize start`, `load`, `initialize end`, `resize`, `activate`, `timer`, `queryunload, mode 1`, `unload`, `terminate` |
| `Sub Main` logs, calls `Form1.Show` and logs again. The form unloads from a Timer | `main start`, `initialize`, `load`, `resize`, `main end`, `activate`, `timer`, `queryunload, mode 1`, `unload`, `terminate` |

What these show:
- **A form's module-level declarations exist before `Form_Initialize`:** sized, so an array bounded by the form's own
  Const is `1 To 5`, and zeroed. Initialize can read them.
- **Touching a control in `Form_Initialize` loads the form there and then.** `Form_Load` runs inside Initialize, before
  Initialize's next statement.
- **`Show` returns after Load and Resize, and before Activate**, which comes once `Sub Main` has returned. The run
  outlives `Sub Main` while a form is loaded.
- **The form's object variables are released after `Form_Terminate`.** In the first probe, the object `o` held is the
  last thing to go.

The order on show, and `Unload Me`'s order, agree with [A form-based game's runtime
surface](a-form-based-games-runtime-surface.md#a-form-based-games-runtime-surface-2026-10-12), which measured both first.

### The form's name is a predeclared, auto-instantiating variable

Every `.frm` carries `Attribute VB_PredeclaredId = True`. The probe asks what that means in use: `Sub Main` reaches a
second form, `Form2`, only through its name, and `Form2` logs its events.

| Step in `Sub Main` | Log |
|---|---|
| 1. read `Form2.Score`, a Public of the form | `initialize`, then `0` |
| 2. `Form2.Score = 5` | nothing |
| 3. read `Form2.Tag`, a built-in property | `load`, then empty |
| 4. read `Form2.Text1.Text`, a control | `t1` |
| 5. `Unload Form2` | `queryunload, mode 1`, `unload` |
| 6. read `Form2.Score` after the Unload | `5` |
| 7. read `Form2.Tag` after the Unload | `load`, then empty |
| 8. `Unload Form2`, then `Set Form2 = Nothing` | `queryunload, mode 1`, `unload`, then `terminate` |
| 9. read `Form2.Score` after `Nothing` | `initialize`, then `0` |
| 10. `Form2 Is Nothing` | `False` |
| 11. the end of `Sub Main` | `terminate` |

So the form's name names an instance that VB6 creates on first use:
- **Reaching a Public of the form creates the instance** and raises Initialize, but does not load it.
- **Reaching a built-in member or a control loads it** and raises Load. A Public of the form does not.
- **`Unload` unloads the form, not the instance.** No Terminate is raised, the form's variables keep their values
  (`Score` is still 5), and the next built-in member loads the same instance again, with Load and no Initialize.
- **`Set Form2 = Nothing` ends the instance.** Terminate is raised, and the next use creates a new one, with Initialize
  and a `Score` of 0. So `Form2 Is Nothing` is never True: naming the form creates it.
- **At the end of the run,** the instance still alive is terminated.

### A form's declarations, an event from Load, and its name set to Nothing (#1010 review)

The same harness, native and p-code agreeing. The project holds Form1 (with `Text1`), an empty Form2, Module1, and a
`Reference=` to stdole, as a new Standard EXE has.

| Probe | Log |
|---|---|
| Form1 declares `Private` variables `As Form`, `As Object`, `As Collection`, `As StdFont`, `As OLE_COLOR`, `As TextBox`, `As Form2`, `As Form1`, `As IPictureDisp` and `As Control`; Load logs each `TypeName`, and `Is Nothing` for five | `Nothing Nothing Nothing Nothing Long Nothing Nothing Nothing Nothing Nothing`, then `True True True True True` |
| Load logs, sets `Text1.Text = "x"`, logs again; `Text1_Change` logs | `load start`, `change x`, `load end` |
| Load sets a form `Public Score` to 5 and calls Module1's `Drop`, which does `Set Form1 = Nothing`; Load then logs `Me.Score` and `TypeName(Me)`, and unloads | `dropped`, `after drop: 5 Form1`, `after unload`, then `terminate` |

What these show:
- **An object declaration is Nothing whatever its type:** VB's own classes and interfaces, another form, the form
  itself, and stdole's dispinterface alias `IPictureDisp`. stdole's `OLE_COLOR` is an alias for a Long.
- **A control event raised from Load runs inside it,** before the statement that raised it returns.
- **Clearing the name doesn't end a loaded form.** The instance lives on, and Terminate waits for the unload and the
  end of the procedure that was running in it.

### Bare names in a form, for implicit `Me` (2026-10-14, #995)

The same harness, native and p-code agreeing on every row; one project per row, so a compile error stays its own.
Form1 holds `Text1`; Module1 holds the logger.

| Form code | VB6 |
|---|---|
| `hWnd = 5` (bare, and as `Me.hWnd = 5`) | does not compile: "Function or interface marked as restricted, or the function uses an Automation type not supported in Visual Basic" |
| `Count = 1`, `Me.Count = 1`, `HasDC = True`, `hDC = 5` | the same message, for each |
| `Set ActiveControl = Text1` | compiles; the run stops at a modal dialog before logging, which the harness's timeout cut off |
| `Option Explicit`, then `Caption = "set"` and `Caption & "\|" & Me.Caption` | `set\|set` |
| `Cls`, then `TextWidth("abc") > 0` and its `TypeName` | `cls ok`, then `True Single` |
| `Dim Caption As String` in Load, `Caption = "local"`, then `Caption & "\|" & Me.Caption` | `local\|Form1` |
| `TypeName(ScaleWidth)`, `ScaleWidth > 0`, `TypeName(Controls)`, `Controls.Count` | `Single True`, `Object 1` |
| `Hide` in Load, then `Visible` | `hidden False` |
| `Name`, `Left("abc", 2)`, `Left = Me.Left` | `Form1 ab True` |
| `Caption(1)`, where Module1 has `Public Function Caption(ByVal n As Integer) As String` | does not compile: "Wrong number of arguments or invalid property assignment" |
| `Tag(2)`, where Module1 has `Public Function Tag(ByVal n As Integer) As String` | the same |
| `Left(5)` | the same |
| `Left("abc", 1, 2)` | the same |
| `Left$("abc", 2)` and `VBA.Left("abc", 1)` | `ab a` |
| `Left(5)` in Module1, not the form | does not compile: "Argument not optional" |

What these show:
- **Assigning a member the Form only reads is refused at compile time,** with a message about restricted functions,
  not one about read-only properties. That held for every read-only member tried, bare and through `Me`.
- **A bare member is the form's under `Option Explicit` too,** and a local of the same name shadows it.
- **A bare method runs,** as `Cls` does, and one called for its value returns its value, as `TextWidth` does.
- **A property called with arguments** is VBA's function when the call fits it, and otherwise refused as the
  property's, never a project Function of the name.

The Form's members themselves were read out of VB98\VB6.OLB by `scripts/dump-typelib-members.ps1`, into
`vb6-form-members.json`: 87 names on `_Form`, its default interface, and 31 events on `FormEvents`. `Print`, a method
of a form in VB6 code, is not in the type library: VB6 compiles it as syntax.

### HexIDE

Read from the code at `upstream/main` c933fbd, not run. A form's code runs as the primary standard module of an
interpreter of its own, and `Me` and the form's name are bound to the window (`VBLoader.RunForm`). So:
- **Implicit `Me`:** a bare name is never looked up on the form: `Caption = "x"` makes a new variable (#995).
- **A form's Public Subs:** the form is a standard module, so they are found unqualified from another module, where VB6
  refuses (#822). And a name the form shares with another module's Public Sub is "Ambiguous name detected: Ping"
  (`TryResolveProcedure` counts the form as a second standard module with a `Ping`), where VB6 runs the module's.
- **The form from outside:** its name is bound in its own module only.
- **`TypeName(Me)`:** it is the window's .NET class name with `VB` taken off, `FormRuntime`.
- **The lifecycle:** Initialize and Activate are never raised, and Resize comes before Load (#994). A form's declarations
  never run (#256). `Unload` stops the run (#973).

#822 carries the design that makes the form an instance. #995 follows it for implicit `Me`, and the predeclared,
auto-instantiating name belongs to the slices that bring `Load`, `Show` and a second form.

#822's first slice makes the startup form an instance of its module, with `Me` that instance. It brings the rows
above into line for `Me`, `TypeName(Me)`, the form reached from a module, both of the Public Sub rows, the
declarations at Initialize, and Initialize and Load before Resize. A control used in Initialize (#1009), Activate, the
predeclared name and `Unload` remain.

#994 then brings in Activate and Deactivate, and Resize once on the first show rather than for each size the
window reports: [A form's Resize, Activate and Deactivate](a-forms-resize-activate-and-deactivate.md#a-forms-resize-activate-and-deactivate-2026-10-23-994).

#995 then brings implicit `Me` into line: a bare name in a form's code is the form's member, after its locals, variables,
controls and procedures and before the project's globals, for reads, assignments and calls. A member VB6's Form has and
HexIDE does not is refused as not implemented, rather than made a variable. A read-only one assigned is VB6's compile
error. A property called with arguments is VBA's only for `Left` with its two, and otherwise refused, never a project
procedure: refused as not implemented, because the error VB6 gives depends on whether the property's type takes an index.

