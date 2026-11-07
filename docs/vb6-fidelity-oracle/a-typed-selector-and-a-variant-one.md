## A typed selector and a Variant one (2026-10-13, #839 review round two)

**The question.** Review round one made a `Case Is` or `To` value convert into the selector's type, as an equality
Case's does, and measured it with 20 rows. Every selector in them was a literal or a declared variable except one
Variant holding a string, whose answer the two rules share. The review of that round ran the same programs on main and
on the head and found 14 results changed, every one away from VB6: a Variant holding 5 was no longer `Case Is < 5.4`,
and `Case Is > "a"` against it stopped the run with 13. So: which selectors convert, and how does VB6 compare the ones
that do not?

**The probe.** Seven Sub Main projects holding 111 Select Case rows. Each row is its own Sub under `On Error Resume Next`,
writing the arm it took and `Err.Number`, so no row's error reaches the next. The same logs carry three typed-parameter
rows, eight arithmetic lines and six lines about `Err`, which makes 123 lines across the first six projects. All seven
were built as native code and as p-code, and the first six p-code projects were run in the IDE too, as described under
"What a condition is": **every line agrees across all three.** The seventh, six rows pinning Empty as `""` against text,
was added when a mutant that read it as `" "` survived, and was built as native code and as p-code only, the two agreeing.

### What decides it is the selector's type

A selector with a fixed type converts each Case value into it, as a store would (*Select Case matching*). A Variant
converts nothing. Against 5, `Case Is < 5.4`:

| Selector, holding 5 | VB6 |
|---|---|
| `Dim v As Integer`; the literal `5`; `i + 1`, `Dim i As Integer` holding 4 | Case Else: typed |
| `F()`, a Function `As Integer`; `CInt(v)`; `ByVal p As Integer` | Case Else: typed |
| `a(0)`, `Dim a(1) As Integer`; `u.n`, a Type's `n As Integer`; `o.N`, a class's `Public N As Integer` | Case Else: typed |
| `Dim v`; `v = CLng(5)`; `CVar(5)` | that Case: a Variant |
| `v + 1`, `v` holding 4; `i + v`, `i` an Integer 4 and `v` a Variant 1 | that Case: a Variant |
| `v(0)`, `Dim v(1)`; `u.x`, a Type's `x As Variant`; `o.V`, a class's `Public V As Variant` | that Case: a Variant |
| `FV()`, a Function with no `As` | that Case: a Variant |

The same holds for text. Holding `"10"`, `Select Case "10"`, a Function `As String`, `CStr(v)`, `Mid$("10", 1)` and a
`ByVal s As String` are not `Case Is > 9`, compared as text; `Mid("10", 1)` is, a Variant. A `ByVal b As Boolean`
holding False is not `Case Is < 1`.

**Arithmetic agrees**, since it reads the same type (#122): `F() * 7000`, `CInt(v) * 7000`, `a(0) * 7000`,
`u.n * 7000` and `o.N * 7000` are all 6, Overflow, and `v(0) * 7000`, `u.x * 7000`, `o.V * 7000` and
`CVar(5) * 7000` are all 35000.

### A Variant selector against a typed Case value

A literal, a declared variable or a typed Function's result. The selector is read as that value's type.

| Selector | Case | VB6 |
|---|---|---|
| a Variant 5 | `Is < "10"`; `Is > "a"`; `"a" To "c"`; `Case "abc"` | Case Else, each: text, `"5"` |
| a Variant 5 | `Is < "a"`; `Is = "5"`; `Is <> "abc"` | that Case, each: text |
| a Variant 10; 100 | `Is > "9"` | Case Else, both: text |
| a Variant 1 | `Case "1.0"` | Case Else: text |
| a Variant 0 | `Is <> "abc"` | that Case |
| a Variant 5, against `Dim w As String` holding `"10"` | `Is < w` | Case Else: text |
| a Variant 5, against `FS()`, a Function `As String` returning `"10"` | `Is > FS()` | that Case: text |
| a Variant True | `Case "True"`; `Case "-1"` | that Case; Case Else: True is written `"True"` |
| a Variant `"3"`; `"10"`; `"10"` | `Is < 9`; `Is > 9`; `Is < 9` | that Case; that Case; Case Else: a number |
| a Variant `"3"`, against `Dim k As Integer` holding 9 | `Is > k` | Case Else: a number |
| a Variant `"2.5"`; `"2.6"`; `"70000"` | `Case 2`; `Is < 3`; `Is > 5` | Case Else; that Case; that Case: not rounded into the Integer, and no Overflow |
| a Variant `"1.0"`; `"2.5"`; `"5"` | `Case 1`; `Case 2.5`; `Case 5&` | that Case, each |
| a Variant `"0.5"`; `"1.5"` | `Is < 1`; `Is > 1` | that Case, both |
| a Variant `"abc"` | `Is > 5`; `Is < 5`; `Case 5` | that Case; Case Else; Case Else: above any number, Err 0 |
| a Variant `"True"` | `Case -1`; `Is < 0` | Case Else, both: not a number, so above |
| a Variant `"1/2/1900"` | `Is < 5` | Case Else: not read as a date |
| a Variant `"True"`; `"-1"`; `"5"` | `Case True` | that Case, each: read as CBool reads it |
| a Variant `"abc"` | `Case True` | Case Else, Err 0 |
| a Variant 2 | `Case 1.7`; `Is = 1.7` | Case Else, both: numbers compared as they are |
| a Variant 5.5; 2.5 | `Is < 6`; `Case 2` and `2 To 2` | that Case; Case Else: not rounded either |
| a Variant True; False | `Is < 0`; `Is < 1` | that Case, both: -1 and 0 |
| a Variant True | `Case 1`; `Case -1` | Case Else; that Case |
| a Variant 5 | `Is > True` | that Case |
| a Variant Date `#1/2/1900#` | `Is > 2` | that Case: its serial, 3 |

### A Variant selector against a Variant Case value

Any number is below any string, whether or not the string reads as a number. Each `w` here is an undeclared Variant.

| Selector | `w` | Case | VB6 |
|---|---|---|---|
| 10 | `"9"` | `Is > w` | Case Else |
| 5 | `"10"`; `"abc"` | `Is < w` | that Case, both |
| `"10"`; `"abc"` | 9; 5 | `Is > w` | that Case, both |
| `"3"` | 9 | `Is < w` | Case Else |
| 5; `"5"` | `"5"`; 5 | `Case w` | Case Else, both |
| 5 | 5 | `Case w` | that Case |

A typed selector converts a Variant Case value as it converts a literal: `Dim n As Integer` holding 5 matches `Case w`
with `w` holding `"5"`, and `Dim s As String` holding `"5"` matches it with `w` holding 5.

### Empty, Null and Booleans

| Selector | Case | VB6 |
|---|---|---|
| 5; `"a"` | `Is > w`, `w` Empty | that Case, both: 0 and `""` |
| 0 | `Case w`, `w` Empty | that Case |
| 5; `"abc"`; `" "` | `Is > Empty` | that Case, each |
| `""` | `Is < Empty` | Case Else: `""` is not below `""` |
| Empty | `Case ""`; `Is < " "` | that Case, both: Empty is `""` |
| `""`; `" "` | `Case w`; `Is > w`, `w` Empty | that Case, both |
| Null | `Case Null`; `Is < 1`; `Is <> 1` | Case Else, each: Null is equal to, below, above and different from nothing |

A Boolean is -1 or 0, so **True is below False**:

| `Dim b As Boolean` | Case | VB6 |
|---|---|---|
| False | `Is < 1` | Case Else: 1 is True, which is below |
| True | `Is < 0`; `Is < False` | that Case, both |
| False | `Is > True` | that Case |
| True | `3 To 6`; `Is >= 5.6`; `Is <= 4.6`; `4.6 To 5.4`; `Case 5` | that Case, each: each converts to True |
| True | `Is > "a"` | that Case, Err 13 |

### Err across a procedure (for #877)

Read in passing, since the probe gives each row its own Sub. A Sub under `On Error Resume Next` that raises 11 and then
returns leaves `Err.Number` at 11 in its caller, and the next Sub still reads 11 until its own `On Error Resume Next`,
which clears it to 0. A Sub whose `On Error GoTo` handler ran to `End Sub` leaves 0. HexIDE gives 11 in both places.

### What was overturned, and what resisted explanation

**Overturned, both ours.**
- **The 2026-09-03 rule:** "VB6 coerces the CASE EXPRESSION to the SELECTOR's runtime type". It was measured on declared
  selectors and one unassigned Variant, and written for every selector.
- **Round one's extension of it to `Is` and `To`:** measured on literal selectors, its 20 rows agreed with it.
- **Main's accidental answers:** a String compared with a number matched nothing on main. That happens to be VB6's
  answer for `Is > "a"` against a Variant 5, so main was right there for the wrong reason. So it was for four rows the
  Variant rule would have broken had their sources not been marked typed with it: a Function `As String`, `CStr`, and a
  String and a Boolean parameter. Two more, a Variant array element and a Variant Type field holding 5, main got right
  because it compared numbers as numbers, and they would have broken had they kept the literal's type.
- **Round two's own rule, in its review:** a value counted as typed only where its source marked it, so every source
  nobody had marked flipped. See *Measured in the review of round two*.

**Resisted explanation.** A Variant string against a typed Boolean is read as CBool reads it: `"True"`, `"-1"` and
`"5"` all equal True. Against a typed number the same string is parsed as a number, so `"True"` does not equal -1. That
fits every row and is not a mechanism understood. A Variant `"1/2/1900"` is not read as a date against a number;
against a typed Date it was not run.

### Measured in the review of round two

The supervisor's probes, `/make` as native code, run against main and against round two's code, found three more
families. Each one round two's rule got wrong, because a value counted as typed only where its source marked it:

| Program | VB6 |
|---|---|
| `Const K = 5`, `Select Case K`, `Case 4.6`; `Case "a"` | that Case; 13: a Const is typed |
| `Const ID_OPEN = 1`, a Variant `cmd` holding `"1"`, `Select Case cmd`, `Case ID_OPEN` | that Case |
| the same for an Enum member, a `Const KI As Integer`, a Single, Double or Currency result, `/`, `&` and a comparison | typed |
| `Len`, `Abs`, `Sgn`, `Asc`, `UBound`, `InStr` | typed |
| `Hour(#5:00:00 AM#) * 7000`; `Day` | 35000, and `Case 4.6` takes Else: a Variant, both |
| `Dim vo: Set vo = New Foo`, `Property Get P() As Integer` returning 5, `vo.P * 7000`; `vo.M()`; `With vo: .P` | 35000, each: late-bound, so a Variant |
| `Select Case vo.P`, `Case Is > "a"` | Case Else, Err 0 |
| `Dim o As Foo`, the same `o.P` | typed: early-bound |
| `va = a`, with `a(0)` an Integer 5, then `Select Case va(0)`, `Case Is < 5.4` | that Case: an element of a Variant holding a typed array is a Variant |
| `Function G(p)`, called `G(q)` with `q As Integer` holding 5, `Select Case p`, `Case Is > "a"` | Case Else, Err 0: an untyped ByRef parameter is a Variant |

### A typed parameter in arithmetic (review round four)

#839 gives a numeric parameter a typed slot, so that it is a typed For counter, and a typed slot is a ceiling in
arithmetic. Measured, native, p-code and in the IDE, each in its own Sub under `On Error Resume Next`:

| Parameter, given | Expression | Native | P-code and the IDE |
|---|---|---|---|
| `ByVal i As Integer`, 5 | `i * 7000` | 6 | 6 |
| `Optional i As Long = 7`, omitted | `i * 400000000` | 6 | 6 |
| `i As Integer`, ByRef, given a declared Integer 5; given the literal 5 | `i * 7000` | 6, both | 6, both |
| `ByVal v`, untyped, given 5 | `v * 7000` | 35000, a Long | 35000, a Long |
| `ByVal b As Byte`, 20 | `b * b` | **144**, a Byte, Err 0 | **6** |

A typed parameter is a ceiling, as a declared variable is. Main widened the first three. The Byte row is the
`Byte * Byte` split recorded under *Numeric type pairs*: native code wraps, p-code and the IDE raise.

### Which intrinsics are typed (2026-10-14, #987)

**The question.** The review of round two found `Len`, `Abs`, `Sgn`, `Asc`, `UBound` and `InStr` typed and `Hour` and
`Day` Variants. #987 needs the rest before it can mark a result either way, because a mark that is wrong flips a result
main got right.

**The probe.** One project, each row its own Sub under `On Error Resume Next`, built as native code and as p-code, the
two agreeing on every line. A numeric result holding N is tried against `Case Is < N.4`: a typed Integer or Long
converts N.4 into N, which it is not below, and takes Case Else; a Variant takes the Case. It is then multiplied past
its ceiling, 7000 for an Integer and 500000000 for a Long: a typed one overflows, a Variant widens. A text result holding
"10" (or "11") is tried against `Case Is > 9`: a typed String converts 9 into "9" and compares text, Case Else; a Variant
is read as a number and takes the Case. `v` is a Variant and `n` a `Dim n As Integer`.

| Source | `Case` | Times the multiplier | So |
|---|---|---|---|
| `Len("abcde")` | Else | Overflow | typed |
| `Len(v), v "abcde"` | Case | a Double | a Variant |
| `InStr("abcd5", "5")` | Else | Overflow | typed |
| `InStr(v, "5"), v "abcd5"` | Case | a Double | a Variant |
| `Abs(-5)` | Else | Overflow | typed |
| `Abs(v), v -5` | Case | a Long | a Variant |
| `Abs(n), n As Integer -5` | Else | Overflow | typed |
| `Sgn(5)` | Else | a Integer | typed |
| `Sgn(v), v 5` | Else | a Integer | typed |
| `Asc("A")` | Else | Overflow | typed |
| `Asc(v), v "A"` | Else | Overflow | typed |
| `UBound(arr), Dim arr(5)` | Else | Overflow | typed |
| `LBound(arr5), Dim arr5(5 To 6)` | Else | Overflow | typed |
| `CInt(5.2)` | Else | Overflow | typed |
| `CInt(v), v 5.2` | Else | Overflow | typed |
| `CLng(5.2)` | Else | Overflow | typed |
| `CLng(v), v 5.2` | Else | Overflow | typed |
| `CByte(5.2)` | Else | Overflow | typed |
| `Hour(#5:00:00 AM#)` | Case | a Long | a Variant |
| `Minute(#12:05:00 AM#)` | Case | a Long | a Variant |
| `Second(#12:00:05 AM#)` | Case | a Long | a Variant |
| `Day(#1/5/2000#)` | Case | a Long | a Variant |
| `Month(#5/1/2000#)` | Case | a Long | a Variant |
| `Year(#1/1/2000#)` | Case | a Long | a Variant |
| `Weekday(#1/6/2000#)` | Case | a Long | a Variant |
| `IIf(True, 5, 6)` | Case | a Long | a Variant |
| `Choose(1, 5, 6)` | Case | a Long | a Variant |
| `Switch(True, 5)` | Case | a Long | a Variant |
| `Int(5.7)` | Case | a Double | neither: a Double, which the probe cannot tell |
| `Fix(5.7)` | Case | a Double | neither: a Double, which the probe cannot tell |
| `Int(n), n As Integer 5` | Else | Overflow | typed |
| `Fix(n), n As Integer 5` | Else | Overflow | typed |
| `Round(5.2)` | Case | a Double | neither: a Double, which the probe cannot tell |
| `Val("5")` | Case | a Double | neither: a Double, which the probe cannot tell |
| `Len(n), n As Integer` | Else | a Long | typed |
| `Mid("10", 1)` | Case | | a Variant |
| `Mid$("10", 1)` | Else | | typed |
| `Left("10", 2)` | Case | | a Variant |
| `Left$("10", 2)` | Else | | typed |
| `Right("10", 2)` | Case | | a Variant |
| `Right$("10", 2)` | Else | | typed |
| `Trim(" 10 ")` | Case | | a Variant |
| `Trim$(" 10 ")` | Else | | typed |
| `LCase("10")` | Case | | a Variant |
| `LCase$("10")` | Else | | typed |
| `UCase("10")` | Case | | a Variant |
| `Hex(16)` | Case | | a Variant |
| `Hex$(16)` | Else | | typed |
| `Oct(8)` | Case | | a Variant |
| `String(2, "1")` | Case | | a Variant |
| `String$(2, "1")` | Else | | typed |
| `Format(10)` | Case | | a Variant |
| `Format$(10)` | Else | | typed |
| `CStr(10)` | Else | | typed |
| `Replace("1x", "x", "0")` | Else | | typed |
| `StrReverse("01")` | Else | | typed |
| `LTrim("10")` | Case | | a Variant |
| `Chr(49) & "0"` | Case | | a Variant |
| `"1" & "0"` | Else | | typed |

**What it says.** Three families, not one rule:
- **Always typed:** `Sgn` and `Asc`, even of a Variant; `CInt`, `CLng`, `CByte` and `CStr`; `UBound` and `LBound`; and
  `Replace` and `StrReverse`, which have no `$` form.
- **Always a Variant:** the date parts (`Hour`, `Minute`, `Second`, `Day`, `Month`, `Year`, `Weekday`); `IIf`, `Choose`
  and `Switch`; and every string function with a `$` form, called without it (`Mid`, `Left`, `Right`, `Trim`, `LTrim`,
  `LCase`, `UCase`, `Hex`, `Oct`, `String`, `Format`), whose `$` form is typed.
- **As their argument:** `Len`, `InStr` and `Abs` are typed when given a typed argument and a Variant when given a
  Variant. `Int` and `Fix` of a typed Integer are typed; of a Variant they were not run.
- **Concatenation** follows its operands: `"1" & "0"` is typed and `Chr(49) & "0"` a Variant, `Chr` being one.

**Not determined:** `Int(5.7)`, `Fix(5.7)`, `Round(5.2)` and `Val("5")` return Doubles, which convert 5.4 into 5.4 and
take the Case either way, and do not overflow. The row for `Err.Number` measured nothing (the probe's helper returned
0) and is left out.

**And one more thing it found:** `Len` of a typed variable is its size, 2 for the Integer holding 5 here, and `Len` of a
literal or an expression does not compile ("Variable required - can't assign to this expression"). That is #780's.

### What a class, an Enum, an Object and a Const read as (2026-10-14, #1005's review)

**The question.** #1005 first counted a parameter or a return as a Variant's whenever its declared type had no base type,
which took in a class, an Enum and a Collection as well as an untyped one. Its review asked what each of them is.

**The probe.** As above: one project, each row its own Sub under `On Error Resume Next`, built as native code and as
p-code, the two agreeing on every line. `Foo` is a class with `Property Get P() As Integer` returning 5, and `MyEnum` has
`mFive = 5`. A source holding 5 is tried three ways:
- against `Case 4.6`: typed converts 4.6 into 5 and matches ("eq"); a Variant compares 5 with 4.6 ("ne");
- against `Case Is < 5.4`: typed converts 5.4 into 5, which it is not below ("ge"); a Variant is below ("lt");
- times a typed variable past its ceiling (`m As Integer` holding 7000 for an Integer source, `m As Long` holding
  500000000 for a Long one): typed overflows, a Variant widens. The multiplier is a variable, so nothing folds.

The constants are each in a project of their own, so a compile error in one could not hide the rest. None had one.

| Source | `Case 4.6` | `Case Is < 5.4` | Times the multiplier | So |
|---|---|---|---|---|
| `mFive, an Enum member` | eq | ge | Overflow | typed |
| `K, Const K = 5` | eq | ge | Overflow | typed |
| `KI, Const KI As Integer = 5` | eq | ge | Overflow | typed |
| `MK, a module Const MK = 5` | eq | ge | Overflow | typed |
| `MKL, a module Const MKL As Long = 5` | eq | ge | Overflow | typed |
| `s.P, Dim s As Foo` | eq | ge | Overflow | typed |
| `s.P, s As Foo` | eq | ge | Overflow | typed |
| `s.P, ByVal s As Foo` | eq | ge | Overflow | typed |
| `GetFoo().P, Function GetFoo() As Foo` | eq | ge | Overflow | typed |
| `c, c As MyEnum given mFive` | eq | ge | Overflow | typed |
| `c, ByVal c As MyEnum given mFive` | eq | ge | Overflow | typed |
| `GetColor(), Function GetColor() As MyEnum` | eq | ge | Overflow | typed |
| `e, Dim e As MyEnum` | eq | ge | Overflow | typed |
| `t.c, c As MyEnum in a Type` | eq | ge | Overflow | typed |
| `f.N, Public N As MyEnum in Foo` | eq | ge | Overflow | typed |
| `f.Q, Property Get Q() As MyEnum` | eq | ge | Overflow | typed |
| `o.P, o As Object` | ne | lt | a Long | a Variant |
| `o.P, ByVal o As Object` | ne | lt | a Long | a Variant |
| `o.P, Dim o As Object` | ne | lt | a Long | a Variant |
| `GetObj().P, Function GetObj() As Object` | ne | lt | a Long | a Variant |
| `o.P, o As Variant holding a Foo` | ne | lt | a Long | a Variant |
| `k.Count, k As Collection of 5` | eq | ge | Overflow | typed |
| `k.Count, Dim k As Collection of 5` | eq | ge | Overflow | typed |

Then the Consts that are not Integers or Longs, each in its own project. `Case Is > 9` against a String "10" is text for a
typed one ("10" is not above "9") and numeric for a Variant; `Case Is > "a"` against a typed Double raises 13, where a
Variant number is below any string; `Case 3 To 6` against a typed Boolean converts 3 and 6 into True, so True is in range,
where a Variant True is -1. A Case that raises runs, as #839 measured.

| Source | Result, then `TypeName` |
|---|---|
| `KS, Const KS = "10"` | Else err 0 String |
| `KSS, Const KSS As String = "10"` | Else err 0 String |
| `KD, Const KD = 5.5` | Case err 13 Double |
| `KB, Const KB = True` | Case err 0 Boolean |
| `KV, Const KV As Variant = 5` | Else err 0 Integer |
| `KV, Const KV As Variant = 5, Is < 5.4` | Case err 0 Integer |
| `MS, a module Const MS = "10"` | Else err 0 String |
| `mFive against Is > "a"` | Case err 13 Long |

**What it says.**
- **A class, `As Foo`**, as a parameter (ByRef or ByVal), a variable or a return: early-bound, so its members read as
  they are declared. `s.P` is a typed Integer.
- **An Enum**, wherever it is declared (a parameter, a return, a variable, a Type field, a class field, a Property Get):
  a typed Long.
- **`As Collection`** is early-bound too: `k.Count` is a typed Long.
- **`As Object`**, as a parameter (ByRef or ByVal), a variable or a return: late-bound, as a Variant is, so `o.P` is a
  Variant whatever `P` is declared as.
- **A Const** is typed as its value is, whatever that is and wherever it is declared: an Integer, a Long, a String, a
  Double, a Boolean, and an Enum member, which is a Long. The one exception is a Const declared `As Variant`, which is a
  Variant: `Const KV As Variant = 5` is "ne" and "lt".

**What #1005 had wrong, and does now.** A class, Enum or Collection parameter was read as a Variant's, and so was a
member reached through it: `Select Case s.P` with `s As Foo` compared by the Variant rule, so `Case 4.6` stopped
matching. Main had answered these through its old comparison, which matched there. A declaration now reads as a Variant's
only when it says so (no As clause, `As Variant` or `As Object`); an Enum parameter, return or Type field is a typed Long,
and a Const and an Enum member are typed unless the Const is `As Variant`. Main had a Const and an Enum member as neither,
so `K * m` widened to a Long 35000 where VB6 overflows.

**Not reachable in HexIDE:** `As Collection` and a local `Dim o As Object`, which it does not declare yet, and
`Property Get Q() As MyEnum`, whose `Q = mFive` reads another module's Enum member unqualified, which was Empty here
until #889 and is read now, though that row is not pinned. The other rows are pinned in `SelectCaseMatchingTests`.

### HexIDE

**None of this is implemented by #839.** Round two built it: Case values converted into a typed selector's type, a
Variant selector compared as VB6 compares Variants, Booleans as -1 and 0, and the typed flag set where a value is read
from a typed Function, a conversion function, a typed array element or Type field, or a typed parameter. That made 111
rows right and broke the families above, all of them sources nobody had marked: a Const read as a Variant, a late-bound
member as typed. Round three took it out, and Select Case and arithmetic are main's again. #987 holds the work, starting
from these rows, and proposes the other polarity: a value is a Variant only where its source positively is one, so a
source nobody has looked at keeps main's answer instead of flipping.

What main does with these rows, for #987's reference:
- **A typed selector:** an equality Case is converted into its type, as VB6 does, but `Is` and `To` compare the value
  as it is, so a declared 2 matches `Case Is < 2.4`, and `Case Is > "abc"` against 5 matches nothing and raises nothing.
- **A Variant selector:** an equality Case is converted into whatever it holds, so a Variant 2 matches `Case 1.7`.
  `Is` and `To` compare numbers as numbers, which is right, and a string with a number not at all, which is right by
  accident for `Is > "a"` against a Variant 5 and wrong for `"abc"` against `Is > 5`.
- **A Boolean** orders False below True.
- **The flag** is unmarked for a typed Function's result and the conversion functions, so `F() * 7000` and
  `CInt(v) * 7000` widen where VB6 raises 6. A Variant array element or Type field holding a literal keeps the
  literal's type, so `w(0) * 7000` raises 6 where VB6 widens.
- **A Type field's store** was not converted until #221, so a typed field read was only as good as its store: `u.b = 200`,
  `x = u.b + u.b` with `b As Byte` was 400 here, where VB6 raises 6. It raises 6 now (*A store into a UDT field*).
- **Not run here:** `Mid$` (#742), and `CVar`, which HexIDE does not have.

**HexIDE, after #987.** A value is typed, a Variant's, or neither, and *neither* is what main did, in both observers, so a
source nobody has measured cannot flip:
- **Arithmetic** keeps #122's rule, a ceiling only when both operands are typed, so only the new typed marks change it:
  a typed Function's or Property Get's result, a typed array element or Type field, the typed intrinsics, and `Len`,
  `InStr`, `Abs`, `Int` and `Fix` of typed arguments; and, after #1005's review, a Const, an Enum member, and an Enum
  parameter, return or Type field. `F() * 7000`, `CInt(v) * 7000` and `K * m` now overflow, as in VB6. A Variant array
  element or Type field reads as a Variant's whatever it holds, so `w(0) * 7000` widens.
- **Select Case:** a typed selector converts each Case value into its type, `Is` and `To` as well as equality, and a
  Variant selector compares as VB6 compares Variants (round two's rules), against a Case value that is typed or a
  Variant's. A selector or Case value that is neither, such as the result of an intrinsic nobody has run, compares as
  main did. A Boolean is -1 or 0, so True is below False.
- **Variant sources:** a slot marked Variant, an untyped, Variant or Object parameter even when it shares a typed
  variable (#1001's read rule), a Variant array's element or any element read through a Variant, a Variant Type field,
  an untyped, `As Variant` or `As Object` Function's result, a Const declared `As Variant`, a member reached through a
  Variant or an Object (late-bound), and the Variant intrinsics. An operator's result is a Variant's when either operand
  is. A class, Enum or Collection declaration is not one: those are typed or early-bound.
- **Every measured row is pinned:** round two's 111 and its generated arithmetic rows, the review's families, the
  intrinsic table above, and #1005's review rows, except `IIf`, `Choose` and `Switch`, which HexIDE does not implement,
  `Len` of a typed Integer, whose value is #780's, and the three review rows HexIDE cannot reach.
- **Left as main, by design:** every intrinsic not run here, and the reference itself where a class or Collection
  declares it: its members are early-bound and read as they are declared.

