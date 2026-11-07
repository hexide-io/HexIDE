## Indexing a call's result (2026-10-23, #1476)

Measured with `/make` in four batches, native and p-code identical on every row. A row that printed something is a
run-time result of a built EXE on SP6's MSVBVM60. A compile error came from the oracle's release-build compiler (#1524).
Each line printed `TypeName` and the value, or the error.

### Reading

| expression | vb6.exe |
|---|---|
| `Split("x y z")(0)`, `(2)`, `(3)` | x, z, 9 |
| `Split(s, " ")(1)` | y |
| `Array(1, 2)(1)` | Integer 2 |
| `V3()(2)` and `V3()(-1)`, with `V3() As Variant` returning `Array(10, 20, 30)` | 30, and 9 |
| `T3()(1)`, with `T3() As Long()` | Long 200: the typed array's element |
| `Jag()(1)(0)` and `Jag()(1)(1)(1)`, with `Jag` returning `Array(Array(1, 2), Array(3, Array(4, 5)))` | 3 and 5 |
| `WithArg(4)(2)`, with `WithArg(n)` returning `Array(n, n * 2, n * 3)` | 12 |
| `NoArgs()(1)`, with `NoArgs() As Variant` returning `Array(5, 6)` | 6 |
| `Col()(2)` and `Col()("a")`, with `Col() As Collection` | beta, alpha: the Collection's Item |
| `VScalar()(0)` and `VStr()(0)`, Variant Functions returning 42 and "abc" | 13 |
| `Split(s)(0, 1)` | 9: the wrong number of subscripts |
| `Split(s)("1")`, `Split(s)(1.5)` | y, z: an index converts as any subscript does, 1.5 rounding to 2 |
| `k.Items()(1)`, `k.Typed()(2)`, `k.Bag()(2)`, `k.Echo("m n")(1)`, `k.Prop()(0)` | the method's or Property Get's result, indexed |

### A parameterless procedure given arguments

Each was its own project: a `Private Function F` in the same module, called from `Sub Main` as `r = F(...)` under
On Error Resume Next.

| F | call | vb6.exe |
|---|---|---|
| `Function F()` or `Function F() As Variant`, returning `Array(5, 6)` | `F(1)` | Integer 6 |
| the same | `F(5, 6)` | 9 |
| `Function F()` or `As Variant`, returning 1 | `F(1)`, and `F(5, 6)` with no `As` | 13 |
| `Function F() As Variant`, returning a 2-D array with `m(1, 0) = 7` | `F(1, 0)` | Integer 7 |
| `Function F() As Collection` | `F(1)` | the Item |
| `Function F() As Long`, `As String`, `As Long()` | `F(1)` | compile error, "Wrong number of arguments or invalid property assignment" |
| `Function Opt(Optional n As Integer = 0)` returning `Array(n, n + 1)` | `Opt(1)`, `Opt(1)(1)`, `Opt()(1)` | the array for 1, then 2, then 1: an Optional parameter takes the argument |
| `NoArgs` as above | `NoArgs(1)(0)`, `NoArgs(5)` | 13, 9 |

So a parameterless procedure that returns a Variant or an object reads its arguments as an index into its result, and
`F(1)` is `F()(1)`. A class's method and Property Get do the same: `k.Items(2)`, `k.Prop(1)`, `k.Bag(1)`. A typed
return does not compile, even a typed array. #741's table had `Function F(): F = 1`, then `F(5, 6)`, as "Expected
array" at compile time; in this shape it compiles and raises 13, which agrees with its TE_04 row. Both rows stand.

### Writing

| statement | vb6.exe |
|---|---|
| `a(1)(0) = 9`, with `a = Array(Array(1, 2), Array(3, 4))` | `a(1)(0)` is 9: written through to the inner array |
| `v = a(1): v(1) = 99` | `a(1)(1)` is still 4: `v` holds a copy |
| `V3()(1) = 5` and `NoArgs()(1) = 7` | compile, run the Function, and change nothing else |

### What does not compile

| expression | compile error |
|---|---|
| `Scalar()(0)`, with `Scalar() As Integer` | Type mismatch |
| `Left$("abc", 2)(0)` | Type mismatch |
| `Len("abc")(0)` | **Syntax error** |
| `a(0)(1) = 5`, with `Dim a(1) As Integer` | Type mismatch |

### The order of evaluation

Each argument and subscript was a call that logged a tag.

| expression | order logged |
|---|---|
| `F3(a1, a2, a3)` | a1, a2, a3, then the body |
| `F3(a1, a2, a3)(i)` | a1, a2, **i**, a3, body |
| `F2(a1, a2)(i1)(i2)` | a1, **i2, i1**, a2, body |
| `F1(a1)(i1)(i2)` | **i2, i1**, a1, body |
| `G2(a1, a2)(i1, i2)` | a1, **i1, i2**, a2, body |
| `TwoArgs(a1, a2)(i)` | a1, i, a2 |
| `Jag(...)(i1)(i2)(i3)` | i3, i2, i1, then the body |
| `Split(t)(i)` | **i, t** |
| `Split(s, d)(i)` | **s, d, i** |
| `arr(i1, i2)`, read | i1, i2 |
| `arr(i1, i2) = rhs` | rhs, i1, i2 |
| `a(i1)(i2)(i3)`, read | i3, i2, i1 |
| `a(w1)(w2) = rhs` | w2, rhs, w1 |
| `a(w1)(w2)(w3) = rhs` | w3, rhs, w2, w1 |

The rule for a Function's call and an array's element: all but the last of the first group's arguments or subscripts,
left to right; then the later groups, the last group first, each left to right; then the first group's last; then the
call. The read of `a(i1)(i2)(i3)` follows it, its first group being `(i1)`.

**A library function does not follow it, and no rule I would defend covers both rows.** `Split(t)(i)` evaluates the
index first, as the rule says. `Split(s, d)(i)` evaluates both arguments first, where the rule says s, i, d. Two rows
were measured, nothing more.

**A chain's Let** evaluates its last group, then the value, then the other groups from the last, then the first. A plain
`arr(i1, i2) = rhs` evaluates the value first.

### In HexIDE (#1476)

- **The groups.** `ExpressionExecutor.ParenGroups` gives each parenthesised group, an empty `()` as a group of its own.
  The grammar gives an argsCall only to a group with something in it, so `T3()(1)` used to read as `T3(1)`.
- **Reading.** `EvaluateProcedureOrArrayCall` evaluates the later groups through `LaterGroups`. The argument loops call it
  before their last argument (`ResolveCallArgs`, `EvaluateCallArgs`), which is the rule above. A library function calls
  it before one argument and after two, as the two Split rows did, and that is no more than those two rows.
  `IndexResult` then indexes, as the reading table says.
- **Member chains.** A member segment's later groups go the same way (`ResolveMemberIndexed`). They used to be dropped
  together with the first, so `k.Echo("m n")(1)` called Echo with no arguments.
- **Parameterless procedures.** `IndexesItsResult` makes one returning a Variant or an object, given arguments, a call
  with none and an index. A typed return given arguments is #741's.
- **Writing.** `StatementExecutor.LetIntoIndexChain` writes in the Let order above. `Len("abc")(0)` is "Type mismatch"
  here, a different message for a program both refuse.

