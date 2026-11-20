## A Let through a default member that returns an object, and a Let of Nothing (2026-11-20, #4906, SP6)

**The question.** `c(1) = 5`, where `c`'s default member `Item(i)` returns an object: what does VB6 do with the 5? The
section "When the default member can't take the arguments" of
[with-new-default-instances-and-set-through-members.md](with-new-default-instances-and-set-through-members.md) measured
a default that returns a number, and found 424 early-bound and 451 late-bound. Here the default returns an object, and
that object's own default member varies. Also: a default that is only a `Property Let` or a `Property Set`, given
arguments; and a Let whose value is Nothing.

**The probe.** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code, run in session 0 of the oracle
guest. The two builds agreed on every row.
- **Early-bound** (`Dim c As <class>`): one project per row, because a compile error stops the whole build.
- **Late-bound:** one project for `Dim c As Object`, and one for `Dim c As Variant`.
- **Each row** runs under `On Error Resume Next`, sets `c` to a new instance, clears `Err`, runs its statement, then
  logs `Err.Number` and `x`. Every procedure logs its own name when it runs, so the log shows what was called, and in
  what order.

**The objects `Item` returns:**
- **`Thing`:** a default `Property Get Value()` and `Property Let Value(ByVal v)`, over a Variant that
  `Class_Initialize` sets to 4.
- **`ThingG`:** a default `Property Get Value()` returning 7, with no Let.
- **`ThingN`:** no default member, only `Function F() As Long`.
- **`ThingA`:** a default `Property Get Item(ByVal k)` returning 3, and `Property Let Item(ByVal k, ByVal v)`. Both
  take an argument.

**The collections.** Each has a default `Item(ByVal i As Long)` (`Attribute Item.VB_UserMemId = 0`) returning the
object it made in `Class_Initialize`:

| Class | `Item` is | Returns |
|---|---|---|
| `Co` | `Property Get Item(...) As Thing` | a `Thing` |
| `CoF` | `Function Item(...) As Thing` | a `Thing` |
| `CoO` | `Property Get Item(...) As Object` | a `Thing` |
| `CoV` | `Property Get Item(...) As Variant` | a `Thing` |
| `CoG` | `Property Get Item(...) As ThingG` | a `ThingG` |
| `CoNd` | `Property Get Item(...) As ThingN` | a `ThingN` |
| `CoA` | `Property Get Item(...) As ThingA` | a `ThingA` |
| `CoZ` | `Property Get Item(...) As Thing` | Nothing |

Two more classes have a default with no Get: **`LetOnly`**, a default `Property Let Item(ByVal i As Long, ByVal v)`;
and **`SetOnly`**, a default `Property Set Item(ByVal i As Long, ByVal v As Object)`.

### `c(1) = 5`

| Row | Class | Early-bound | `As Object` | `As Variant` |
|---|---|---|---|---|
| A1 | `Co` | `Item 1`, `Thing.Let 5`, no error | the same | the same |
| A2 | `CoF` | `Item 1`, `Thing.Let 5`, no error | the same | **450**, and `Item` isn't called |
| A3 | `CoO` | `Item 1`, `Thing.Let 5`, no error | the same | the same |
| A4 | `CoV` | `Item 1`, `Thing.Let 5`, no error | the same | the same |
| A5 | `CoG` | compile error **"Invalid use of property"** | `Item 1`, `ThingG.Get`, then **451** | the same as `As Object` |
| A6 | `CoNd` | `Item 1`, then **438** | `Item 1` **twice**, then 438 | `Item 1`, then 438 |
| A7 | `CoA` | compile error **"Argument not optional"** | `Item 1`, then **449**; `ThingA`'s Let isn't called | the same as `As Object` |
| A8 | `CoZ` | `Item 1`, then **91** | `Item 1` **twice**, then **438** | `Item 1`, then **438** |

Wherever A1 to A4 raised no error, the `Thing` read back 5 afterwards.

### `x = c(1)`, with `x` a Variant

| Row | Class | All three bindings |
|---|---|---|
| B1 | `Co` | `Item 1`, `Thing.Get`; `x` is 4, an Integer |
| B2 | `CoG` | `Item 1`, `ThingG.Get`; `x` is 7, an Integer |
| B3 | `CoNd` | `Item 1`, then 438 |
| B4 | `CoZ` | `Item 1`, then **91** |

### A default with no Get

| Row | Class | Statement | Early-bound | `As Object` and `As Variant` |
|---|---|---|---|---|
| C1 | `LetOnly` | `x = c(1)` | compile error "Invalid use of property" | **450**, and nothing is called |
| C2 | `LetOnly` | `c(1) = 5` | `LetOnly.Let 1 5`, no error | the same |
| D1 | `SetOnly` | `x = c(1)` | compile error "Invalid use of property" | **450**, and nothing is called |
| D2 | `SetOnly` | `c(1) = 5` | compile error "Invalid use of property" | **450**, and nothing is called |

### A Let of Nothing, and what the target keeps

One project. Each row sets its target to `"keep"`, clears `Err`, runs the statement, then logs `Err.Number`,
`Err.Description` and the target. `Thing`, `Co`, `CoNd` and `CoZ` are the classes above.

| Row | Declarations | Statement | VB6 | The target afterwards |
|---|---|---|---|---|
| E1 | `Dim o As Object`, Nothing | `x = o` | **91**, "Object variable or With block variable not set" | `keep` |
| E2 | `Dim t As Thing`, Nothing | `x = t` | 91 | `keep` |
| E3 | `Dim v As Variant`, `Set v = Nothing` | `x = v` | 91 | `keep` |
| E4 | `Dim c As CoZ` | `x = c(1)` | `Item 1`, then 91 | **Empty** |
| E5 | `Dim co As Object`, a `CoZ` | `x = co(1)` | `Item 1`, then 91 | **Empty** |
| E6 | `Dim cv As Variant`, a `CoZ` | `x = cv(1)` | `Item 1`, then 91 | **Empty** |
| E7 | `Dim c2 As Co` | `x = c2(1)` | `Item 1`, `Thing.Get`; 4, an Integer | 4 |
| E8 | `Dim c3 As CoNd` | `x = c3(1)` | `Item 1`, then 438 | **Empty** |
| E9 | `Dim t9 As Thing`, Nothing; `Dim s As String` | `s = t9` | 91 | `keep` |

**Readings:**
- **A Let through a default that returns an object goes to that object's default member,** with the value and no
  arguments. That holds for a Get or a Function, typed as the class, `As Object` or `As Variant`, early-bound or late.
  The one exception is the rule the earlier section found: held in a Variant, a Let through a default Function is 450,
  and the Function isn't called (A2).
- **When that object can't take the Let:**
  - **Its default is Get-only:** early-bound, the compile error "Invalid use of property". Late-bound, VB6 calls the
    Get, then raises 451 (A5).
  - **Its default needs an argument:** early-bound, "Argument not optional". Late-bound, 449, without calling the
    object's Let (A7).
  - **It has no default member:** 438 (A6).
  - **It is Nothing:** 91 early-bound, but **438 late-bound** (A8).
- **Through `As Object`, `Item` runs twice** before the 438 of A6 and A8. Held in a Variant, or early-bound, it runs
  once. This is the only place the log shows a call repeated.
- **A default with no Get, given arguments,** is "Invalid use of property" early-bound, and 450 late-bound, read or
  assigned, without calling anything. A Let goes to a default `Property Let` as usual (C2).
- **A Let of Nothing is 91, not 438,** wherever the Nothing came from (B4, E1 to E6, E9).
- **What the target keeps after a failed Let depends on the right side, not the error.**
  - A variable holding Nothing leaves it as it was (E1 to E3, E9).
  - A default member's result, Nothing (E4 to E6) or an object with no default (E8), leaves a Variant target Empty.
  - This matches "A Let into a Variant from a temporary object" in [collection.md](collection.md) (#1485). There the
    temporary was `New X` or a Function's result, and the errors 438 and 450. Here it is a default member's result, and
    91 behaves the same way.

**In HexIDE,** on main `c5da3001d` (scratch tests in `HexIDE.Runtime.Tests`, 2026-11-20):
- **Every A row but A2's Variant refused** as unmeasured, citing #4906, after calling `Item`. A2 held in a Variant was
  already 450.
- **B1 to B3 matched.** B4 was 438.
- **C1, D1 and D2 refused,** citing #4906. C2 matched.
- **E1 and B4 were 438.** The E rows weren't run in HexIDE beyond E1 and B4.

**Not measured:**
- a default whose result is held `As Object` or `As Variant` and whose object's default is Get-only, needs an
  argument, or is missing, early-bound;
- `Set c(1) = obj` through `SetOnly`;
- a default reached with more than one argument.
