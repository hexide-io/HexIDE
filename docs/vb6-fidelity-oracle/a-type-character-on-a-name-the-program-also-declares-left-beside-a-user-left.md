## A type character on a name the program also declares: `Left$` beside a user `Left` (2026-10-11)

**The question.** The library's string functions come in pairs, `Left` and `Left$`. When a program declares
its own `Left` (a variable, a constant, a parameter, a function), does `Left$` mean the program's `Left`,
with the `$` checked against its type, or the library's `Left$`? And what does a type-declaration character
do on a library function that has no `$` form at all, such as `Len$`?

Two readings were in play, and the probes were written so that they predict different results:

- **Scope first.** `Left$` is searched for as `Left` by the ordinary scope search, and the `$` is then
  checked against whatever the search found.
- **Written name first.** `Left$` is a name of its own, matched to the library's `Left$` before any `Left`
  the program declares is considered.

The second is not a straw man: it has been implemented elsewhere.

**The probe.** VB6.EXE 6.00.8176 (the original release) with runtime MSVBVM60 6.00.9848, in the oracle guest. Three Standard
EXE projects, built from standard modules, every one of them `Option Explicit`, so that a name binding
nothing is a compile error rather than an implicit Variant. Each probe is a `Public Sub` placed in the module
whose declarations set up its scenario:

```vb
Public Sub TF_03()
    Dim r As Variant
    On Error Resume Next
    r = Left$("abc", 1)          ' the statement under test
    TwRec "TF_03", ..., r, Err.Number, Err.Description
End Sub
```

`TwRec` records `TypeName(r)/VarType(r)` and the value, or the error number and description. A harness
module's `Sub Main` calls every probe in turn. One compile error fails the whole build, so a driver read the
`/make` log, deleted the offending Sub together with its call in `Sub Main`, and rebuilt, until the build
succeeded. A project row below that says *compile error* is that log's message, verbatim.

- **Project 1**: eleven scenario modules beside the harness. Nine of them declare a module-level `Left` or
  `Trim`, every one `Private`, so none sees another's; `TwinLocal` declares its `Left`s inside the probes,
  and `TwinParam` as a parameter. Ten builds: the first nine each failed, its log naming one compile error,
  and the tenth succeeded.
- **Project 2**: a `Public Function Left` in module `TwinPublic`, and the probes that use it in a second
  module. It built first time.
- **A third project**: the harness and `TwinProcStr` alone, so only the four `TP_` probes. It ran before
  the other two: its log is stamped 19:19:47, project 1's 19:20:21 and project 2's 19:22:08. Its one build
  succeeded. Its `TwinProcStr.bas` is byte-identical to project 1's, and project 1's four `TP_` rows are
  identical to its four.

Nineteen further questions went through [the scripted harness](README.md#the-scripted-harness--scriptsvb6-oracleps1),
one expression per `/make`, with any declaration placed in the module's declarations section. They are
marked *(script)* below. For those, the record kept is the expression, its declaration and the answer. Ten
of them are compile errors, and their message is as that record gives it: no `/make` log for any of those
nineteen is in the record. The later script runs in this entry kept theirs. The record's header says the script ran under `Option Explicit`, which the harness
supplies when it is given no declaration. Five rows carried one, and for those the harness replaces its
default `Option Explicit` with the lines given; the record keeps each declaration but does not show whether
`Option Explicit` was among them. Every name those five use is declared.

Every compiled result below is a Variant holding a String (`String/8`) unless a type is shown.

### The program's `Left` answers, at every scope probed, with one exception further down

| Probe | Declaration in force | Code | VB6 |
|---|---|---|---|
| TF_01 | `Private Left As String` (module) | `Left$ = "x": r = Left` | `"x"` |
| TF_02 | the same | `Left = "y": r = Left$` | `"y"` |
| TL_01 | `Dim Left As String` (local) | `Left$ = "x": r = Left` | `"x"` |
| TL_02 | the same | `Left = "y": r = Left$` | `"y"` |
| TL_04 | `Dim Left$` | `Left$ = "z": r = Left & "/" & TypeName(Left)` | `"z/String"` |
| TL_06 | `Const Left As String = "k"` (local) | `r = Left$` | `"k"` |
| TA_01 | a parameter: `Function TwP(ByVal Left As String) As Variant`, body `TwP = Left$` | `r = TwP("p")` | `"p"` |
| TP_01 | `Private Function Left(ByVal s As String, ByVal n As Long) As String`, returning `"user:" & s & "/" & n` | `r = Left$("abc", 1)` | `"user:abc/1"` |
| TP_02 | the same | `r = Left("abc", 1)` | `"user:abc/1"` |
| TR_01 | `Private Function Left$(ByVal s As String, ByVal n As Long)`, returning `"ret:" & …` | `r = Left$("abc", 1)` | `"ret:abc/1"` |
| TR_02 | the same | `r = Left("abc", 1)` | `"ret:abc/1"` |
| TB_01 | `Private Function Left$(…)` whose body assigns `Left$ = "body:" & s & "/" & n` | `r = Left("abc", 1)` | `"body:abc/1"` |
| TV_02 | `Private Function Left(ByVal s As String, ByVal n As Long)`, no `As` clause, returning `"var:" & …` | `r = Left("abc", 1)` | `"var:abc/1"` |
| TX_01 | project 2: `Public Function Left(ByVal s As String, ByVal n As Long) As String` in another module, returning `"pub:" & …` | `r = Left$("abc", 1)` | `"pub:abc/1"` |
| TX_02 | the same | `r = Left("abc", 1)` | `"pub:abc/1"` |

Where the program's `Left` is a **`String` variable**, at module level or local, calling it does not fall
back to the library, with the `$` or without it (no variable of another type was called with arguments):

| Probe | Declaration in force | Code | VB6 |
|---|---|---|---|
| TF_03 | `Private Left As String` | `r = Left$("abc", 1)` | compile error: `Expected array` |
| TF_04 | the same | `r = Left("abc", 1)` | compile error: `Expected array` |
| TL_05 | `Dim Left As String` | `r = Left$("abc", 1)` | compile error: `Expected array` |
| TL_07 | the same | `r = Left("abc", 1)` | compile error: `Expected array` |

`Trim` was probed as a control, a name the parameterless-function rule further down was not written for.
It has two declarations, both at module level:

| Probe | Declaration in force | Code | VB6 |
|---|---|---|---|
| TT_01 | `Private Trim As String` (module) | `Trim$ = "x": r = Trim` | `"x"` |
| TT_02 | the same | `Trim = "y": r = Trim$` | `"y"` |
| TT_03 | the same | `r = Trim$(" a ")` | compile error: `Expected array` |
| TT_04 | the same | `r = Trim(" a ")` | compile error: `Expected array` |
| TU_01 | `Private Function Trim(ByVal s As String) As String`, returning `"user:" & s & "!"` | `r = Trim$(" a ")` | `"user: a !"` |
| TU_02 | the same | `r = Trim(" a ")` | `"user: a !"` |

One unqualified control sits beside `Private Left As String`: TF_06, `r = Right$("abc", 1)`, a twin the
program does not declare, gives the library's `"c"`.

And qualified:

| Probe | Declaration in force | Code | VB6 |
|---|---|---|---|
| TF_05 | `Private Left As String` | `r = VBA.Left$("abc", 1)` | `"a"` |
| TP_03 | `Private Function Left(…) As String` | `r = VBA.Left$("abc", 1)` | `"a"` |
| TX_03 | project 2's `Public Function Left` | `r = VBA.Left$("abc", 1)` | `"a"` |
| TP_04 | `Private Function Left(…) As String`, in module `TwinProcStr` | `r = TwinProcStr.Left$("abc", 1)` | `"user:abc/1"` |
| TX_04 | project 2's `Public Function Left`, in module `TwinPublic` | `r = TwinPublic.Left$("abc", 1)` | `"pub:abc/1"` |

So `Left$` is searched for as `Left`. Nineteen rows, from these tables and the next (TV_01, TL_03), are ones
where the two readings predict differently: TF_01–03, TL_01–06, TT_01–03, TP_01, TR_01, TB_01, TA_01,
TU_01, TV_01 and TX_01. VB6 did what scope first predicts in every one of them, and what written name first
predicts in none. The assignment rows matter as much as the reads: `Left$ = …` stores into the program's variable (TF_01, TL_01, TL_04), and
inside `Function Left$` it sets the function's return value (TB_01).

A `Public Function Left` in another standard module outranks the library, with the `$` and without it
(TX_01, TX_02). Under [The project's own declarations win](in-box-constants-the-library-qualifier-selects.md#the-projects-own-declarations-win), the in-box
constants lost to a `Private Enum` and a `Private Const`, so what that showed is the libraries losing to
the module's own declarations. TX_01 and TX_02 put the declaration in another module, and the library
loses there too. Only that one shape was probed at that tier.

A module qualifier with a `$` reaches the module's `Left`, which is declared without one (TP_04, TX_04).
`VBA.Left$` reaches the library past each of the three declarations it was written beside: `Private Left
As String` (TF_05), `Private Function Left(…) As String` (TP_03) and project 2's `Public Function Left`
(TX_03). Each qualifier had a single candidate, so these rows show that qualification works, not any order
within it.

### Then the `$` is checked against what was found

| Probe | Declaration in force | Code | VB6 |
|---|---|---|---|
| TV_01 | `Private Function Left(ByVal s As String, ByVal n As Long)`, no `As` clause | `r = Left$("abc", 1)` | compile error: `Type-declaration character does not match declared data type` |
| TL_03 | `Dim Left As Long` | `r = Left$` | the same compile error |
| *(script)* | `Private vv As Variant`, at module level | `vv$` | the same |
| *(script)* | `Private Function VF(ByVal s As String)`, no `As` clause, `VF = s` | `VF$("x")` | the same |
| *(script)* | the same | `Module1.VF$("x")` | the same |

A Variant declaration gets no exemption: `$` against Variant is a mismatch exactly as `$` against Long is.
Nor does the check pass a contradicted declaration over and carry on searching, or TV_01 would have reached
the library's `Left$` and answered `"a"`. And it applies through a module qualifier.

### An assignment through a type character reaches the declared variable, for every character

The rows above assign only through `$`, only to a name the library also uses, and only under
`Option Explicit`. Nine further `/make` runs of [the scripted harness](README.md#the-scripted-harness--scriptsvb6-oracleps1)
took the rest, each case a `Private Function` that assigns and returns what it reads back. The runs covered
every character, into a module variable and into a local, with and without `Option Explicit`, plus
undeclared names, which exist only without it.

Every assignment through a character to a declared variable reached that variable:

| Character | Declared as | Module variable, `Option Explicit` | Module variable, without | Local, `Option Explicit` | Local, without |
|---|---|---|---|---|---|
| `%` | Integer | `n% = 5`: `5` | `5` | `n% = 7`: `7` | `7` |
| `&` | Long | `l& = 70000`: `70000` | `70000` | `m& = 9`: `9` | `9` |
| `!` | Single | `f! = 1.5`: `1.5` | `1.5` | `f! = 2.5`: `2.5` | `2.5` |
| `#` | Double | `d# = 2.5`: `2.5` | `2.5` | `d# = 3.5`: `3.5` | `3.5` |
| `@` | Currency | `c@ = 1.25`: `1.25` | `1.25` | `c@ = 4.25`: `4.25` | `4.25` |
| `$` | String | `s$ = "x"`: `"x"` | `s2$ = "w"`: `"w"` | `t$ = "y"`: `"y"` | `s$ = "z"`: `"z"` |

Each value came back read through the bare name, and as the declared type. A `For` counter behaves the
same way: a local `Dim i As Integer` and then `For i% = 1 To 2: Next` leave `i` at `3`, Integer, with and
without `Option Explicit`. Only `%` was tried on a counter, and only in a local.

Without `Option Explicit`, an undeclared name takes the type of the character it is first used with, and
the bare name then reads the same variable:

| Code | VB6 |
|---|---|
| `a$ = "x"`, then `a & "|" & TypeName(a)` | `"x|String"` |
| `c$ = "z"`, then `c$ & "|" & TypeName(c$)` | `"z|String"` |
| `k% = 3`, then `TypeName(k) & "|" & k` | `"Integer|3"` |
| `l2& = 8`, then the same | `"Long|8"` |
| `f2! = 1.5` | `"Single|1.5"` |
| `d2# = 2.5` | `"Double|2.5"` |
| `c2@ = 1.25` | `"Currency|1.25"` |
| `b = "y"`, then `b$` | compile error: `Type-declaration character does not match declared data type` |

So `a$` and `a` are one variable, and `Option Explicit` makes no difference to which one an assignment
reaches. The first use decides the type: through a character, that character's type; bare, a Variant,
against which a later `$` is the mismatch above. The obvious guess from VB6's QuickBASIC ancestry, where
`a$` and `a` are two different variables, is overturned.

The compile error came back as `Line 2`, which is the `b$` statement under the
[`Line N` rule](what-make-writes-to-out-and-what-line-n-counts.md#what-make-writes-to-out-and-what-line-n-counts-2026-09-19): one `Attribute` line, then the
`Private Function` line at index 0.

### On library names the `$` is enforced on some and ignored on others

All *(script)*, one expression per `/make`, except TL_08 and TL_09, which ran in project 1.

| Written | VB6 |
|---|---|
| `Asc$("a")` (TL_09) | compile error: `Type-declaration character does not match declared data type` |
| `InStrRev$("abc", "b")` | the same |
| `IIf$(True, "a", "b")` | the same |
| `Choose$(1, "a")` | the same |
| `Val$("12")` | the same |
| `Array$(1)` | the same |
| `InStr$("abc", "b")` (TL_08, and again as a script) | `2`, Long |
| `InStrB$("abc", "b")` | `3`, Long |
| `Len$("abc")` | `3`, Long |
| `LenB$("abc")` | `6`, Long |
| `CLng$(1)` | `1`, Long |
| `Abs$(-1)` | `1`, Integer |
| `Sgn$(-2)` | `-1`, Integer |
| `Int$(1.5)` | `1`, Double |
| `Fix$(1.5)` | `1`, Double |

On the nine that compile, the `$` is ignored rather than applied: none of them comes back as a String. Where
this file had already measured the function without the character, the type matches it: `Len` and `InStr`
Long, `Sgn` Integer, `Int` and `Fix` following their operand
([Return widths](omitted-optional-arguments-and-string-operands-to-numeric-intrinsics.md#return-widths-of-the-integer-returning-intrinsics-2026-09-02)), and `Abs` keeping its
operand's type (overturned assumption 8). `CLng` giving a Long was a positive control in
[Numeric type pairs](numeric-type-pairs--five-arithmetic-tables-and-comparison-is-a-sixth.md#numeric-type-pairs--five-arithmetic-tables-and-comparison-is-a-sixth-2026-09-03).
`InStrB` and `LenB` had no unhinted control, in this run or before it.

### A parameterless String `Left` is passed over when given two arguments

| Probe | Declaration in force | Code | VB6 |
|---|---|---|---|
| TE_01 | `Private Function Left() As String`, returning `"noparams"` | `r = Left("abc", 1)` | `"a"` |
| TE_02 | the same | `r = Left$("abc", 1)` | `"a"` |
| TE_03 | the same | `r = Left` | `"noparams"` |
| TE_04 | `Private Function Left()`, no `As` clause, returning `"v"` | `r = Left("abc", 1)` | compiles; at run time `Err 13` (`Type mismatch`) |
| *(script)* | `Private Function Right() As String`, returning `"noparams"` | `Right("abc", 1)` | compile error: `Wrong number of arguments or invalid property assignment` |
| *(script)* | the same | `Right$("abc", 1)` | the same |

TE_01 and TE_02 are the only unqualified `Left` calls in this entry where the program declares `Left` and the
library answers anyway. TE_03 shows the function is there to be found when it is not given arguments. The probes for this
shape were written from MS-VBAL §5.6.10, which, as the probes' author read it, singles out the name `Left`:
a parameterless function returning `String`, followed by two arguments, is dropped as a match and the search
goes on. VB6 did what that reading predicts in all four `TE_` rows. The Variant version is not dropped; its
`Err 13` is most likely the attempt to index the String it returned, which the row does not show directly.

Three more script runs, each under `Option Explicit`, gave surplus arguments to functions named neither
`Left` nor `Right`:

| Declaration in force | Code | VB6 |
|---|---|---|
| `Private Function H(ByVal a As Long) As Long`, `H = a` | `H(5, 6)` | compile error: `Wrong number of arguments or invalid property assignment` |
| `Private Function G() As Long`, `G = 1` | `G(5, 6)` | the same |
| `Private Function F()`, no `As` clause, `F = 1` | `F(5, 6)` | compile error: `Expected array` |

`F` has TE_04's shape, a parameterless function with no `As` clause given two arguments, and is refused
at compile time where TE_04 compiles and fails at run time. `F` ran in the scripted harness and TE_04 in
project 1, so, as with the `Right` rows below, these rows cannot say whether the name or the harness made
the difference.

The same String shape named `Right` is not dropped, and is refused at compile time. That would make this a
rule about the name rather than about parameterless String functions in general, except that the two
`Right` rows went through the scripted harness rather than project 1, so they differ from TE_01 in harness
as well as in name, and these rows cannot say which of the two made the difference. Within project 1, the
only parameterless function given two arguments and not dropped is TE_04's, and it differs from TE_01 in
its return type, not its name.

### What this overturned

Every probe's expected column is a string in the probe's own source, so it was fixed before VB6 compiled
the probe, and it names each answer its author could not yet rule out. VB6 refuted these:

- **Written name first**, in all nineteen rows where it differed from scope first. It predicted the
  library's `"a"` for TF_03, TL_05, TT_03, TP_01, TR_01, TU_01, TV_01 and TX_01; "a compile error such as
  `Argument not optional`" for TL_03; and a compile error for each assignment or argument-less read through
  the `$` (TF_01, TF_02, TL_01, TL_02, TL_04, TL_06, TT_01, TT_02, TB_01, TA_01). TL_03 is the one row of
  the nineteen where both readings predict a compile error, and its expected column notes: "Only the message
  tells the two compile errors apart". Written name first gave its message only as an example; scope first named
  `Type-declaration character does not match declared data type` outright, and that is what VB6 gave. So
  TL_03 separates the two by the kind of message, not its wording. VB6's is about the `$` contradicting a
  declaration, and written name first never reaches the `Dim Left As Long`: it reads `Left$` as the
  library's, called with no arguments.
- **Scope first with the `$` never checked**, which predicted `"var:abc/1"` for TV_01 and a Long `0` for
  TL_03. Both are compile errors.
- **A third reading**, in which a declaration the `$` contradicts is skipped and the search continues. It
  predicted `"a"` for TV_01, which is a compile error.
- **The `$` checked on every library name** predicted a compile error for `InStr$` (TL_08), which returns a
  Long `2`. **Never checked on a library name** predicted `97` for `Asc$` (TL_09), which is a compile error.
  Each half was right about some names and wrong about others.

The first table invites a simpler rule than the entry supports: wherever the program declares a `Left`, an
unqualified `Left$` means the program's. TE_02 overturns it. Beside a parameterless `Function Left() As
String`, `Left$("abc", 1)` gives the library's `"a"`. It sits with the parameterless-function rows rather
than the scope tables, which is what makes it easy to miss.

### What resisted explanation

- **Which library names ignore the `$`.** The type each result arrives as does not separate the two lists:
  `InStr` and `InStrRev` both arrive as Long, and `Sgn` and `Asc` both as Integer (the 2026-09-02 table), yet
  in each pair one ignores the `$` and the other refuses it. That does not rule out the declared return
  type. Every type here, in these rows and in that table, is the subtype of the Variant the result was
  assigned to, and the rows above show the `$` is checked against the declaration, not the value: TV_02's
  function hands back a String, and TV_01's `$` on that same function, declared Variant, is refused. A
  library function declared `As Variant` that returns a Long looks the same in these rows as one declared
  `As Long`. TL_08's expected column took `InStr` to return a Variant, which nothing here measured. If it
  does, `InStr$` compiling means the check does not treat it as it treats a declared Variant (`vv$`).
  Seven of the nine that ignore the `$` (`Abs`, `CLng`, `Fix`, `Int`, `Len`, `LenB`, `Sgn`) are in MS-VBAL's
  `reserved-name` production (section "Reserved Identifiers and IDENTIFIER", read 2026-10-11), and `InStr`
  and `InStrB` are not. It misses the other way too: `Array`, one of the six that refuse the `$`, is in the
  same section's `special-form` production, and the other five that refuse it (`Asc`, `InStrRev`, `IIf`,
  `Choose`, `Val`) are reserved in neither. Read as a rule, "a reserved name ignores the `$`, any other
  name has it checked", that fits twelve of the fifteen: the seven, and the five that are reserved in
  neither production. It misfits three: `InStr` and `InStrB`, which ignore the `$` without being reserved,
  and `Array`, which refuses it although it is reserved as a special form. So reservation is the best
  single predictor here, and it is not the rule.
- **Why `Left` and not `Right`.** Both are library functions with a `$` form and the same parameters. The
  rows fit a rule specific to the name, for this shape, but do not isolate the name: the `Right` rows ran in
  the scripted harness, in its `Module1` and called from its `Sub Main`, while TE_01 ran in project 1. Whether
  it is the name, and if so why, is open.

### Not measured

- `Left$` with **no** arguments against the parameterless `Function Left() As String`. TE_03 is the bare
  `Left`, so whether the hinted read reaches that function is open.
- `Trim` anywhere but a module variable and a module function: no local, parameter, `Const`, `Dim Trim$`,
  return-value or project-level `Trim`.
- A `Left` variable called with arguments when it is not a `String`: no Variant or array `Left`.
- `VBA.Left$` beside any declaration but the three in TF_05, TP_03 and TX_03: none beside a local, a
  parameter, a `Const`, `Function Left$`, or a Variant or parameterless `Function Left`. No `VBA.Trim$` at
  all.
- The parameterless `Function Right() As String` inside project 1, beside the `TE_` probes, which is what
  would separate the name from the harness. Likewise a parameterless Variant `F` given two arguments inside
  project 1, beside TE_04.
- The `Left` rule with a return type other than `String` or Variant, a `Property Get`, an argument count
  other than two, or in a class or form module.
- The project tier beyond one `Public Function` in a standard module: no `Public` variable or `Const`, no
  two project-level `Left`s, no class or form module.
- A type character on any library name not listed above, including every conversion function but `CLng`.
- VBA7. The probes were written for Office VBA and only VB6 answered them. The two share a language engine's
  lineage and are known to diverge at the edges.

### Consequences for the interpreter

HexIDE's interpreter was run on `main` (`f44df30`) against the project probes, one scenario module at a
time behind a harness of its own, and against short programs of the same shape for the scripted cases.
Four kinds of divergence came out, each filed:

- An unqualified read through a type character throws `NotImplementedException`, for every character
  and for the library's `$` functions. A module-qualified one does not: the qualifier path drops the
  character unchecked, so `Module1.VF$("x")` answers `"x"` where VB6 refuses to compile it (#742, which
  also carries the rules above as the design to implement). A declaration through one throws as well:
  `Dim Left$` gives "DIM type hints not implemented" where VB6 compiles TL_04.
- Before #739, without `Option Explicit`, an assignment through a type character wrote a second variable, so `n% = 5`
  left a declared `n` at 0 with no error. With it, the same line raised `Variable not defined (n%)`,
  which VB6 does not. A `For i%` loop ran on a counter nobody reads either way (#739).
  #739 now resolves scalar assignment targets to the bare slot, checks the declared type and creates a typed slot
  on first suffixed assignment. No new VB6 measurement was needed for those: the assignment tables above supply the
  values and types. The For counter was measured at every scope, with `Next` too, in "A For counter written with a
  type character" (2026-10-27), and follows it.
- A `Function` declared with a type character did not parse, so its module did not load. #740 fixed it, measured in "A
  Function declared with a type character".
- Surplus arguments are dropped, so `H(5, 6)`, `G(5, 6)` and `F(5, 6)` above run and answer, and a
  `String` variable that shares a library function's name (`Left`, `Trim`) is passed over to the library
  when called with arguments. TE_01 answers the program's
  parameterless `Left` where VB6 answers `"a"`, and TE_04 answers where VB6 raises `Err 13` (#741).

### The `/make` line numbers agree with the 2026-09-19 measurement

Each failing build's `/out` log held one line in the form recorded under
[What `/make` writes to `/out`, and what "Line N" counts](what-make-writes-to-out-and-what-line-n-counts.md#what-make-writes-to-out-and-what-line-n-counts-2026-09-19):

```
Compile Error in File '<path>\TwinField.bas', Line 30 : Expected array
```

| Build | File | VB6 printed | File line `N + 2` holds | Message |
|---|---|---|---|---|
| 1 | `TwinField.bas` | Line 30 | 32: `r = Left$("abc", 1)` (TF_03) | `Expected array` |
| 2 | `TwinField.bas` | Line 30 | 32: `r = Left("abc", 1)` (TF_04, eight lines higher once TF_03 was gone) | `Expected array` |
| 3 | `TwinProcVar.bas` | Line 14 | 16: `r = Left$("abc", 1)` (TV_01) | `Type-declaration character does not match declared data type` |
| 4 | `TwinLocal.bas` | Line 28 | 30: `r = Left$` (TL_03) | the same |
| 5 | `TwinLocal.bas` | Line 37 | 39: `r = Left$("abc", 1)` (TL_05) | `Expected array` |
| 6 | `TwinLocal.bas` | Line 46 | 48: `r = Left("abc", 1)` (TL_07) | `Expected array` |
| 7 | `TwinLocal.bas` | Line 54 | 56: `r = Asc$("a")` (TL_09) | `Type-declaration character does not match declared data type` |
| 8 | `TwinTrimField.bas` | Line 30 | 32: `r = Trim$(" a ")` (TT_03) | `Expected array` |
| 9 | `TwinTrimField.bas` | Line 30 | 32: `r = Trim(" a ")` (TT_04) | `Expected array` |
| 10 | | | | `Build of 'twins1.exe' succeeded.` |

Every module is a `.bas` whose only hidden line is `Attribute VB_Name`, so that section's
`file line = N + 1 + hidden lines above` gives `N + 2`, and it holds in all nine. The driver found each
statement from the line number, so the arithmetic alone would be circular. The content is what pins it: the
lines either side of each statement are `On Error Resume Next` and the recording call, which cannot raise
these messages. Replaying the deletions against the original modules reproduces every
reported line, and ends at exactly the files that built. So these line numbers need no rule of their own:
they are that section's rule applied to a `.bas`. The nine project builds add two message kinds the section did not have, both binding errors, and exercise
nothing else in it: no procedure-level attribute, no continued statement. The later script runs' logs carry a
third, `Wrong number of arguments or invalid property assignment`; their line numbers point into the
harness's own generated module and were not reconciled with the rule here.

Two things the logs show that the earlier section does not state:

- **One error per build.** Project 1 held nine compile errors at its first build, and the log named one.
  Each later build named the next. The logs cannot tell "stopped at the first error" from "reported only the
  first"; they fit both. [Qualified type names](qualified-type-names-a-type-never-shadows-the-qualifier.md#qualified-type-names-a-type-never-shadows-the-qualifier-2026-09-19)
  already relies on one failure failing the whole program.
- **The order.** The nine arrived in the `.vbp`'s `Module=` order, and by line within a file. That is not
  alphabetical (`TwinProcVar` came before `TwinLocal`), but it is also the order in which `Sub Main` calls the
  probes, so this run cannot separate the two. One project, nine failing builds: observed, not established as
a rule.

---

