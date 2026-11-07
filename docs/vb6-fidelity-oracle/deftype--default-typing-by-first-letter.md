## DefType — default typing by first letter (2026-09-02, #169)

Measured with `scripts/vb6-oracle.ps1 -Declarations`, which this work added: `DefType` is a declarations-
section directive, so it cannot be probed as an expression, and it **cannot be measured with
`Option Explicit` on at all** — it is a rule about undeclared variables.

| probe (under `DefInt A-M`) | result | rule |
|---|---|---|
| `TypeName(apple)` | `Integer` | first letter in range |
| `TypeName(mango)` | `Integer` | range is **inclusive at both ends** |
| `TypeName(zebra)` | `Empty` | outside the range: an ordinary Variant |
| `Dim explicitVar As String` | `String` | an explicit `Dim` **overrides** the directive |
| `kilos$ = "text"` | `String` | a **type suffix overrides** it too |
| `quantity = 2.6` | **3**, `Integer` | coerces on store, exactly like `Dim x As Integer` |
| `amount = 2.5` | **2** | half-to-even, as everywhere else in VB6 |

### Overlapping ranges are a compile error

`DefInt A-M` followed by `DefStr A-C` does **not** resolve by last-wins or first-wins. `vb6.exe` refuses
the module:

```
Compile Error in File 'Module1.bas', Line 1 : Duplicate Deftype statement
```

Discovered by a probe that was trying to measure precedence and could not compile — which is the answer.
Any letter covered by two directives is rejected before the program runs, so an interpreter never has to
decide which wins.

### Why this is a lookup and not a map

The whole implementation is a letter-range → type table consulted when an undeclared variable is created.
No relationship between symbols appears anywhere in it, which is why it sits inside the pre-pass boundary
in `CLAUDE.md` rather than against it.

### Everything a DefType types, measured (2026-10-23, #169)

Measured on vb6.exe, native and p-code identical, under en-GB. Each program `DefTypeTests` pins was made as written.

**Overturned, and it was ours.** The section above says DefType "cannot be measured with `Option Explicit` on at all",
being a rule about undeclared variables. It is wider than that: it types every declaration with no As clause, and those
exist under `Option Explicit`. With `Option Explicit` and `DefInt A-Z`, `Dim x` then `x = 2.6` is an Integer 3, and
`Function F()` returns an Integer.

Under `DefBool B`, `DefByte Y`, `DefInt I-J, K`, `DefLng N`, `DefCur C`, `DefSng S`, `DefDbl D`, `DefDate T`, `DefStr Z`,
`DefObj O` and `DefVar V`:

| probe | result |
|---|---|
| `b1 = 1` | Boolean True |
| `y1 = 300`, then `y1 = 200` | 6, then Byte 200 |
| `i1 = 2.6`, `j1 = 2.5` | Integer 3, Integer 2 |
| `k1 = 40000` | 6 |
| `n1 = 70000` | Long 70000 |
| `c1 = 1.23456` | Currency 1.2346 |
| `s1 = 1.5`, `d1 = 1.5` | Single, Double |
| `t1 = 2` | Date, serial 2 |
| `z1 = 5` | String "5" |
| `Set o1 = New Collection` | Collection |
| `v1 = 5` | a Variant holding an Integer |
| `a1 = 2.6`, no DefType covering A | Double |
| `i9`, `z9`, `o9`, `b9`, `t9`, `v9`, never assigned | Integer 0, String "", Nothing, Boolean, Date, Empty |
| `Dim i5, z5, a5, i6(2)` | Integer, String, Empty, `Integer()` |
| module-level `Dim im, zm, am` | Integer, String, Empty |
| `Const I8 = 2.6`, `Const Z8 = 5` | **Double 2.6, Integer**: a Const keeps its value's type |
| `Function IFun()` returning 2.6 | Integer 3 |
| `Function ZFun()` returning 5 | String |
| `Sub ITake(i7)` given 2.6 | Integer 3 inside it |
| `Sub ZTake(ByVal z7)` given 5 | String "5" inside it |
| `Static is1`, then `is1 = is1 + 1` twice | Integer 1, then 2 |
| `For i3 = 1 To 2` | Integer 3 after it |
| `Set a2 = New Collection`, `a2` never declared, no DefType covering A | Collection: a Set declares as a Let does |
| `i2 = 2.6` in another module with no DefType | Double: a DefType is its own module's |
| a class's own `DefInt A-Z`: field `cnt`, local `q`, `Function Count()` | Integer, Integer 3, Integer |
| `DefStr Z-X`, then `x = 5` | String: a range written backwards covers X to Z |
| `DefInt a-c`, then `b = 2.6` | Integer 3: lower case names the same letters |

**DefObj**, under `DefObj O`:

| probe | result |
|---|---|
| `Dim o5`, and module-level `Dim om` | Nothing, Nothing |
| `Set o5 = New Collection`, `Set om = New Collection` | Collection, Collection |
| `o5 = 5`, `o5` holding a Collection | 438 |
| `Dim o8`, then `o8 = 5` | 91 |
| `o6 = 5`, `o6` never declared | 91 |
| `Function OFun()` with `Set OFun = New Collection` | Collection |
| `Sub OTake(o7)` given a Collection, then Nothing | Collection, Nothing |
| `o7 = 5` in `Sub OLet(o7)` given Nothing | 91 |
| `OBad = 5` in `Function OBad()` before any Set | 91, and the call returns Nothing |

So DefObj makes each of these an `As Object`. A `Function F() As Object` and a `Function F() As Collection` that are
never Set behave the same way: `TypeName` is Nothing and `Is Nothing` is True. A Let into the first's name before a Set
is 91. A Let into the second's does not compile ("Argument not optional"), as for a variable.

**A DefType'd variable is a typed operand** (#122), as a Dim'd one is. Under `DefInt P`:

| probe | result |
|---|---|
| `p1 = 30000`, then `p1 * 3` | 6 |
| `p9 + 32767 + 1`, `p9` never assigned | 6 |
| `Dim p5`, then `p5 = 30000: p5 * 3` | 6 |
| `PFun() * 3`, `Function PFun()` returning 30000 | 6 |
| `b1 = 30000: b1 * 3`, no DefType covering B | Long 90000 |

**Where it must sit.** A DefType after any declaration is "Deftype statements must precede declarations". That holds
after a `Dim`, a `Public` or `Private` variable, a `Const` (Public or not), a `Type`, an `Enum` and a `Declare`. After
`Option Base`, `Option Compare` or another DefType it compiles. Inside a procedure it is "Invalid inside procedure".
After a procedure it is VB6's general "Only comments may appear after End Sub, End Function, or End Property", as
for a label there (see "Line labels", below). HexIDE gives that error for nothing at module level, so it runs such a module
with the DefType applied.

**The other compile errors:**
- `DefInt AB` is "Expected: letter".
- `DefDec E` is "Expected: line number or label or statement or end of statement". VB6 has no DefDec, though the
  grammar lexes it.
- `DefInt A-C` then `DefInt B` is "Duplicate Deftype statement", even though both are Integer. So is `DefInt A-Z` then
  `DefStr A-Z`.

**Two things HexIDE does not do yet**, measured here and filed:
- **A ReDim that declares.** Under `DefInt I`, `ReDim i4(2)` on a name never declared is `Integer()`, and `ReDim a4(2)`
  is `Variant()`. HexIDE refuses both as "Unknown variable" (#1495).
- **An undeclared variable passed ByRef.** With `Sub Bump(x As Integer)` and `i12` never declared, `Bump i12` twice
  leaves `i12` an Integer 2. Without DefType, `Bump a12` with `Sub Bump(x)` leaves an Integer 2, and `Fill b12` with
  `s = "filled"` leaves a String. HexIDE loses the write in all three (#1505). **Since fixed by #1505**; see "An
  undeclared variable passed ByRef".

### In HexIDE (#169)

`PrePass.VisitDeftypeStmt` collects each module's DefType statements into a 26-letter table, raising the compile errors
above, and `PrePass.DefaultTypeOf(name)` reads it. It is consulted wherever a declaration has no As clause:
- `StatementExecutor.AllocImplicitVariant`, for a variable made on first use, whether by a Let, a Set, a For or a
  statement that stores;
- `ExpressionExecutor`'s read of a name never assigned;
- `StatementExecutor.DeclareLocal`, for a `Dim`, a `Static`, a module's variable and a class's field;
- `PrePass.ParseParams` and `PrePass.ReturnOf`, for parameters and returns.

The slot the pre-pass hoists for a module-level variable is not typed. The module's Dim statement, run when the module
starts, makes the slot the program uses, and `DeclareLocal` types that one.

A type character on such a variable is #742 and #739.

