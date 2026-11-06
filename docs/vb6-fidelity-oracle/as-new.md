## As New (2026-10-22 and 23, #435)

**The obvious guess is that `Dim o As New C1` makes its object when the declaration runs. It makes it on first use, and
every use counts.**
- `o Is Nothing` makes the object, and is therefore always False.
- So do `TypeName(o)`, `VarType(o)`, `IsObject(o)` and `TypeOf o Is C1`.
- So do `Set p = o` and `Set v = o`, passing `o` ByRef, ByVal or as a Variant, `With o`, and either side of `o Is p`.
- So does `v = o`, which then raises 438 for a class with no default member.
- Nothing is made by the Dim, by `Set o = Nothing` on an untouched variable, or by `Set o = New C1`, which makes the one
  object it names.

**The second guess is that after `Set o = Nothing` the variable is Nothing. It is, until the next use makes a new
object**: a new instance, with Initialize run again and its fields back at their defaults.

**The third, which nobody would guess: For Each touches its loop variable before the first item.** With
`o As New C1`, `For Each o In c` makes an object, then releases it as it stores the first item. Over an empty Collection
it makes one, and releases it when the loop leaves `o` Nothing.

**The question.** `Dim o As New C1` stopped the program before it ran, in the pre-pass, so the whole module was refused.
#435 asked when VB6 makes the object, what makes another, and which declarations take `New`.

**The probe.**
- Five Sub Main programs, made native and p-code under en-GB.
- Eleven one-statement compile projects.
- The 12 programs `AsNewTests` runs, made the same way. They logged exactly what the tests pin, and gave exactly the
  compile errors the tests expect.

Native code and p-code agreed on every line. `C1` numbers each object it makes and logs its Initialize and Terminate, so
the logs say which object each line is.

### What makes the object

Each row is a procedure with `Dim o As New C1, p As C1, v As Variant`, the statement, then the procedure's end:

| Statement | VB6 |
|---|---|
| none | nothing made |
| `o.Go` | made, then Go |
| `L o Is Nothing` | made; **False** |
| `TypeName(o)`; `VarType(o)`; `IsObject(o)`; `TypeOf o Is C1` | made; C1; 9; True; True |
| `Set p = o`, then `p Is Nothing` | made; False |
| `Set v = o`, then `TypeName(v)` | made; C1 |
| `v = o` | made, then 438 |
| `TakesRef o`, `x As C1` ByRef; `TakesVal o`, ByVal; `TakesVariant o`, ByVal Variant | made before the call; the callee sees it |
| `With o: .Name = "w": End With` | made; it terminates as "w" |
| `Set o = New C1` | one object, the New's |
| `Set o = Nothing`, untouched | nothing made |
| `o Is p` and `p Is o`, `p` Nothing | made; False |
| `a Is b`, both `As New` and untouched | both made; False |

Every object made terminates when the procedure ends.

### After Nothing, and through another name

| Program | VB6 |
|---|---|
| `o.Name = "first": o.Go`, `Set o = Nothing`, `o.Go` | the first terminates as "first"; the next use makes a new one, its Name "" |
| `Set o = p`, `p` Nothing, then `o.Go` | a new one |
| `Set p = o`, `Set o = Nothing`, then `o.Go` | `p` keeps the first; `o` gets a new one; `p Is o` is False |
| `Clear o`, a Sub doing `Set x = Nothing` on its ByRef `x As C1` | the caller's object terminates there; **in the callee `x Is Nothing` is True**; back in the caller, the next use makes a new one |
| the same with the parameter named `o`, as the caller's variable is | the same: True in the callee |
| `Dim a As New C1, b As New C1`, using `b` then `a` | each made on its use; they terminate in declaration order, `a` then `b` |
| `Dim q As New C1` inside a loop run three times, `q.Go` each pass | one object, reached all three times |

So a ByRef parameter reads the Nothing its caller's variable holds: the `As New` belongs to the name it was declared with.

### Through a member, a module and a field

| Program | VB6 |
|---|---|
| a class `H` with `Private m As New C1` and `Public c As New Collection`; `Set h = New H` | neither field made |
| `h.Touch`, which uses `m` | `m` made |
| `Set h = Nothing` | H's Terminate, then `m`'s |
| `Public o As New C1` in `H`; `h.o.Go`, `Set h.o = Nothing`, `h.o.Go` | a new one on the second use, through the member |
| `Set h.c = Nothing`, then `h.c.Count` | 0, a new Collection |
| `Public pm As New C1` in Module2; `Module2.pm.Go`, then `pm.Go` | one object |
| `Dim mo As New C1` at module level, used in Sub Main | made on first use; terminates when the program ends |

### Collection

`Dim c As New Collection` behaves as a class does:
- `c.Add 1: c.Add 2`, `Set c = Nothing`, then `c.Count` is 0;
- For Each over an untouched one runs no pass and raises nothing;
- `e Is Nothing` is False;
- `TypeName(f)` is "Collection" and `f.Count` 0.

`c(1) = 5` on an untouched one makes it, then raises 5, as an empty Collection does (#1492). `d = 5` does not compile:
"Argument not optional", as for `As Collection`.

### An Initialize that raises

`C2`'s Class_Initialize raises 5:
- Under Resume Next, `e.Go` runs Initialize and gives 5. A second `e.Go` runs Initialize again and gives 5 again, and so
  does `e Is Nothing`.
- The variable stays Nothing, so every use tries again.
- With `On Error GoTo`, the handler sees 5.

### Arrays, a Type's field, and a Static

- **Arrays.**
  - `Dim a(1) As New C1`: each element is made on its own first use. `a(1) Is Nothing` makes `a(1)`, and after
    `Set a(0) = Nothing` the next use makes another.
  - `ReDim b(1) As New C1` works the same way. It needs no Dim, even under Option Explicit (#1495).
  - For Each over the array, with a Variant loop variable, sees each untouched element as Nothing and makes none.
    After `Erase a`, the next use makes a new one.
  - An object loop variable over an array does not compile: "For Each control variable on arrays must be Variant".
- **A Type's field `m As New C1`** compiles. `t.m.Go` makes it, and `t.m Is Nothing` is False. `u = t` copies the
  reference, so `u.m Is t.m` is True and one object terminates. The #1488 issue records the same
  native/p-code probe from #435: `Dim t As T` and `t.n = 1` make nothing; `t.m.Go` initialises C1 before Go,
  and the shared object terminates on leaving the procedure. `AsNewTests` now covers this with and without
  the copy. Other field-specific edges were not measured there.
- **`Static s As New C1`** in a Sub called twice makes one object, which terminates when the program ends.
- **In a class method, `Static s As New Collection`** is per instance: on one instance it counts 1 then 2, and on a second
  instance 1.

### What VB6 refuses to compile

| Declaration | VB6's compile error |
|---|---|
| `Dim x As New Integer`, `As New String`, `As New Variant`, `As New Object` | Syntax error |
| `Dim x As New E`, an Enum; `Dim x As New U`, a Type | Invalid use of New keyword |
| `Sub S(x As New C1)`; `Function F() As New C1` | Syntax error |
| `Dim d As New Collection`, then `d = 5` | Argument not optional |

### When the program ends

When Sub Main returns, the objects still held terminate: the Static's first, then the module variable's (#1490).

### What this overturned

- **That the declaration makes the object.** It was the issue's own assumption to check, and the reason to measure
  before pinning anything.
- **That For Each only writes its variable.** It reads it first, so an `As New` loop variable makes and drops an
  object, even when the loop runs no pass.

### What resisted explanation

- Why For Each reads a variable it is about to overwrite.
- Why a Type's field may be `As New` when a variable of a Type may not be.

### In HexIDE (#435)

**One mechanism, shared with a form's default instance (#822).**
- A Dim, Private or Public `As New` marks its slot with what it makes: a class's name, or "Collection".
- A read of the slot while it holds Nothing raises a request, `AutoInstanceNeeded`. The read path's retry loop, the one
  that already makes a form's default instance, answers it: `New` makes the object, it is stored, and the read is made
  again.
- An Initialize that raises leaves the slot Nothing, so the next use tries again.
- A read through a parameter, the one other name a slot is reached by, makes nothing.
- Locals and Watch read without asking, so looking makes no object. That is HexIDE's choice: what VB6's Locals window
  shows for an untouched `As New` variable was not measured.

**What reads count.**
- Every expression read counts. So do the Let target's read, `!` and `c(1) =` on a Collection, a module-qualified read,
  and For Each's first read of its variable.
- For's and For Each's own looks at the variable, to see it exists or release what it held, do not.

**Refused, by issue:**
- an array `As New` (#1482);
- a Type's field `As New`, until #1488 made it run;
- `Static`, until #1491 made it run;
- `ReDim` of an undeclared name (#1495);
- `As New` of a type HexIDE has no class for, such as a form, by #435's own message.

**Known divergences the probes met, none of them `As New`'s:**
- #877: an `On Error` statement doesn't clear Err;
- #1006: `Set Module2.pm = Nothing` stores nothing;
- #1490: objects held at the end of the program don't terminate;
- #1493: `o = 5` released the object, until #1493.

**`AsNewTests`** pins five programs, seven compile errors and five refusals.

