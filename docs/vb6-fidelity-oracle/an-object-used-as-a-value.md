## An object used as a value (2026-10-12, #174)

**The question.** What a class instance gives where a value is wanted, with and without a default member. HexIDE
does not call a class's default member yet. It raised a trappable 438 or 13 in some places and gave a wrong value
in others, and a trappable error there lets `On Error Resume Next` carry on without VB6's value.

**Probe.** A Sub Main project built with `vb6.exe /make` and run under `On Error Resume Next`, logging each result
with `TypeName` and `Err.Number`. Every probe ran as native code, and every probe also ran in the IDE, with three
kinds of row read in one mode only: a compile error can only be read from `/make`, so those rows are native only; a
native build drops `Debug.Print`, so its rows are IDE only; and a row that would show a box (`MsgBox(w)`) was not run
and says so. Where a row was read in both modes the two agreed, except where it says otherwise. The first two probes
were native only until the review's second round, which ran them in the IDE too: identical, except the Select Case and
For rows under Resume Next (below).

**How the IDE runs were made.** `VB6.EXE /runexit probe.vbp` runs a project in the IDE, as F5 does, and closes VB when
the program ends. It was started in the Win10 guest's session 0 over PowerShell Direct, with the project's files
copied into a fresh folder first. The program writes its log with `Print #` to a file beside the project, because
`Debug.Print` output goes to the Immediate window and is gone when VB closes. Three things to know when repeating it:
- VB exits with 0xC000041D after the program finishes. That is normal.
- A compile error or an unhandled run-time error leaves a modal dialog that nobody can answer in session 0, so a run
  still going after a timeout is killed, and its log is missing or incomplete. Every use is therefore made under
  `On Error Resume Next` or `On Error GoTo`, and a compile error shows up as a missing log.
- A `probe.exe` left in the folder means the run was a `/make` build, not an IDE run.
#842 tracks making this, and the native `/make` harness, part of the tree.
- `w` is a `Widget`, whose `Value` carries `Attribute Value.VB_UserMemId = 0` and returns 42;
- `k` is a `Plain`, a class with only `Public Tag As Long` and no default member.

| Use | `w` | `k` |
|---|---|---|
| `x = w`, `x` a Long | 42, Long | 438, `x` unchanged |
| `s = w`, `s` a String | "42", String | 438, `s` unchanged |
| `d = w`, `d` a Double | 42, Double | 438, `d` unchanged |
| `b = w`, `b` a Boolean | True | 438, `b` unchanged |
| `vv = w`, `vv` a Variant, no `Set` | 42, **Long** | 438, `vv` still Empty |
| `w + 1`, `w * 2`, `-w`, `w \ 5` | 43, 84, -42, 8, all Long | `k + 1`: **438** |
| `w = 42`, `w > 41`, `w = "42"` | True, True, True | `k = 1`: 438 |
| `"v=" & w` | "v=42" | **438** |
| `CStr(w)`, `CDbl(w)`, `CBool(w)`, `CVar(w)`, `Val(w)` | "42", 42 Double, True, 42 Long, 42 Double | `CStr(k)`: 438 |
| a ByVal Long argument, a ByVal String argument | 42 Long, "42" | 438 |
| a ByVal **Variant** argument | the object itself: `TypeName` "Widget" | the object |
| `If w Then` | the Then branch runs | 438, **and the Then branch runs** |
| `Select Case w` … `Case 42` | matches | 438 at the selector |
| `For i = 40 To w` | 3 passes | 438 at the bound |
| `Print #f, w` | ` 42 ` | 438, after the text before it was written |

`Debug.Print`'s output cannot be read this way: it goes to the Immediate window, which neither a built program nor a
`/runexit` run leaves anywhere to read. Its **error** can be, in the IDE: `Debug.Print k` raises 438 there, and
`Debug.Print w` raises nothing. A native build removes `Debug.Print` statements altogether, so there both give 0. What
`Debug.Print w` prints is taken to follow `Print #`, which shares its formatting, and is **inferred**, not measured.
The Immediate window's `? k` is inferred from `Debug.Print k` the same way.

`Len(w)` does not compile: "Variable required - can't assign to this expression". (Every compile error in this section
was read from `/make`. An IDE run of a project that does not compile leaves no log, so those rows are native only.)

**A second probe, through a Variant.** `o` is a Variant holding the object (`Set o = w`), so `Len` compiles:

| Use | Widget | Plain |
|---|---|---|
| `Left(o, 1)`, `InStr(o, "2")`, `Chr(o)`, `Len(o)`, `UCase(o)` | "4", 2, "*", 2, "42" | 438 each |
| `IIf(o, "t", "f")` | "t" | 438 |
| `CDate(o)` | 10/02/1900, a Date | 438 |
| a ByVal Double argument | 42, Double | 438 |
| `Format(o)`, `Format(o, "0.0")`, `Abs(o)`, `Not o` | **450** each | 438 each |

The last row resisted explanation until #840's review, which measured the same uses against a class whose default
member is a **Property Get** rather than a Function:

| Through a Variant | Function default member | Property Get default member |
|---|---|---|
| `Format(o)` | 450 | "42" |
| `Abs(o)` | 450 | 42, Long |
| `Not o` | 450 | -43, Long |
| `wp + 1`, `"v=" & wp`, typed | | 43, "v=42" |

So with a Function default member, `Format`, `Abs` and `Not` through a Variant give 450, "Wrong number of arguments or
invalid property assignment", and a Property Get default member gives 42 and -43 on the same uses. Every other use in
the two probes above gave the Function's value. The IDE adds one more use that gives 450: a Case value compared with an Empty or Null
selector (below). The rows say which uses give 450, not why: no rule beyond them was measured. HexIDE refuses every one
of them, so no answer here can become a wrong value, and a default member that HexIDE later calls (#174) must reproduce
the 450 for a Function on those uses.

**What it says.**
- An object used as a value is its default member in every place this probe used one: a Let into any scalar or a
  Variant, an operator, a conversion, a condition, a bound, a ByVal scalar argument, `Print #`. The exception there
  is the 450 above, which a Function default member gives on a few paths through a Variant. The rounds below find the
  places that take the object rather than a value: `TypeName` and `IsObject`, `VarType` and the Is functions, and the
  array that `LBound`, `UBound`, `Join` and `Filter` take first.
- An object with no default member raises **438 in every one of those places**. HexIDE raised 13 for `k + 1`,
  `-k`, a comparison, `CBool(k)`, `If k Then` and a For bound. It gave the CLR type name for `"v=" & k`, `CStr(k)`
  and `Debug.Print k` (#436), 0 for `Val(k)` and a ByVal Long argument, and no error at all for `Select Case k`.
- Of the arguments this probe passed, only a ByVal Variant parameter takes the object itself.

**And an overturn about `On Error Resume Next`.** Measured in the second probe for every statement with a condition,
with `k` a Plain, so each condition raises 438:

| Statement | What ran |
|---|---|
| `If k Then ... Else ... End If` | the Then branch |
| `If False Then ... ElseIf k Then ... Else ...` | the ElseIf branch |
| `If k Then c = "Then" Else c = "Else"`, one line | the Then part |
| `Select Case k`, `Case 1`, `Case Else` | native: **Case Else**; the IDE: neither (see below) |
| `Do While k`, `Do Until k`, `While k` | the body, every pass, until the probe's own `Exit` after 3 |
| `Do ... Loop While k` | the body once, then out |
| `For i = 1 To k` | native: the body, every pass, until `Exit For` after 3, `i` still 0 afterwards; the IDE: one pass, `i` 1 afterwards |

So for most of these, Resume Next goes on into the block, at the first statement inside it.
A bottom-tested loop's next statement is the one after `Loop`. Select Case and For are the exceptions, and differ by
mode.

The Select Case and For rows are the two where native code and the IDE disagree, and the table gives each answer.
They are one probe shape apiece (`Case 1` then `Case Else`; `For i = 1 To k`), so nothing more general is claimed
here. Why they differ, and the other shapes, are #839's.

The earlier reading here, that each Case raised in turn, was wrong.

The first probe's finding, in its own words: `If k Then`, whose condition raises 438, went on into the Then
branch. Resume Next resumes at the next statement, and the next statement after the `If` line is the first one
inside the block. The obvious reading, that the failed condition skips the block, is wrong. HexIDE skips it, and
does the same for every statement in the table except `Do ... Loop While`, where it already runs the body once and
leaves, as VB6 does. #839 has the rest.

**HexIDE, since #839** (merged after this section was written): every row above gives VB6's answer.
Where native code and the IDE differ, Select Case and For, HexIDE gives the IDE's answer, because it follows p-code.
"Resume Next into a block" has the measurements behind that, and what each statement does.

### Found in #840's review (2026-10-12)

The review found routes that still read an object as a value, and measured several; each answer was measured again
here, native and in the IDE, with `sm` a class whose default member returns 2. `w` and `k` are as above.

**`VarType` and the Is functions read the default member, and `TypeName` and `IsObject` do not.** Classes whose default
member returns a value the function would answer about:

| Call | Native and IDE |
|---|---|
| `VarType(w)`, `IsNumeric(w)` | 3, True |
| `IsEmpty`, `IsNull`, `IsDate`, `IsNumeric`, `IsArray`, `IsError` of a class whose member returns Empty, Null, a Date, "12", an array, `CVErr(5)` | True for each |
| `VarType` of those | 0, 1, 7, 8, 8204, 10 |
| `TypeName(w)`, `IsObject(w)`, `TypeName` of those | "Widget", True, the class's name |
| `VarType(k)`, and each Is function of `k` | 9, and False, **with no error**: a class with no default member answers about the object |

**Select Case:**

| Statement | Native | IDE |
|---|---|---|
| `Select Case 42`, `Case 40 To w`; `Case w To 50` | matches | matches |
| `Select Case 42`, `Case 40 To k` | 438 | 438 |
| `Select Case 43`, `Case Is > w`; `Select Case 41`, `Case Is < w` | matches | matches |
| `Select Case 43`, `Case Is > k` | 438 | 438 |
| `Select Case Empty` or `Null`, `Case w` | Case Else, no error | **450**, then `Case w`'s body: a Function default member, as above |
| the same with a Property Get default member | Case Else | Case Else |
| `Select Case Empty` or `Null`, `Case k` | 438 | 438 |

**Statements:**

| Statement | `w` (42) or `sm` (2) | `k` |
|---|---|---|
| `Randomize w` | the same first `Rnd` as `Randomize 42` | 438 |
| `Err.Raise w`, `Error sm` | error 42, error 2 | 438 |
| `ChDir w`, `ChDrive w` | 76 and 5: it tried "42" | 438 |
| `Mid(s, sm, 1) = "X"`, `Mid(s, 2, sm) = "XYZ"` on "abcdef" | "aXcdef", "aXYdef" | 438, `s` unchanged |
| `Mid(s, w, 1) = "X"` | 5, the start past the end | |

**The array functions.** The array that `LBound`, `UBound`, `Join` and `Filter` take first is not read at all. Any
object there raises 13, including a class whose default member returns an array, and so does every other value that
is not an array. Through a Variant `v`, because a typed object variable there does not compile ("Expected array",
from `/make`):

| First argument | `UBound(v)`, `LBound(v)`, `Join(v, "-")`, `Filter(v, "b")` |
|---|---|
| a `Widget`, a `Plain`, an `Arr` whose default member returns `Array("a", "b", "c")` | 13 each |
| `"abc"`, 5, Empty, Null | 13 each |

HexIDE's `Join` and `Filter` raised 5 for each of those, and now raise 13. **Nothing is 13 here too**, not the 91 every
conversion gives: `Join(v, "-")`, `Filter(v, "a")` and `UBound(v)` with `v` set to Nothing (round three).

Every other argument reads the member:

| Call | `sm` | `k` |
|---|---|---|
| `UBound(m, sm)`, `LBound(m, sm)` of `m(1 To 3, 5 To 9)` | 9, 5 | 438 |
| `Split("a,b,c", ",", sm)`, `Split(w)` | two elements, `"42"` | 438 |
| `Join(arrS, sm)`, `Filter(arrS, "a", sm)` | "2" as the delimiter, an include of True | 438 |

**An array never allocated** (`Dim ds() As String`, `Dim dv()`), measured in round three, both modes:

| Call | VB6 |
|---|---|
| `Join(ds, "-")`, `Join(dv, "-")`, and through a Variant holding `ds` | `""`, no error |
| `Filter(ds, "a")`, `Filter(dv, "a")` | no error: an unallocated **String** array (VarType 8200) for both, whose `LBound` and `UBound` raise 9 |

HexIDE indexed a bounds list that was not there, an exception no On Error could trap, and now gives both answers.

**Join and Filter, round four.** Measured as native code and in the IDE, identical on every row. `ds` is
`Dim ds() As String` never allocated, `d1` is `Dim d1(1) As String` holding "apple" and "pear", `al` is
`Dim al(1) As Long` holding 1 and 12, and `m` is `Dim m(1, 1) As String`.

An array never allocated answers the same whatever its element type, and so does an erased one:

| Call | VB6 |
|---|---|
| `Join(dl, "-")` with `Dim dl() As Long`; `Join(de, "-")` after `ReDim de(1) : Erase de` | `""`, no error |
| `Filter(dl, "a")`, `Filter(de, "a")` | an unallocated String array, whose `LBound` and `UBound` raise 9 |

Filter reads its match and its switch before it looks at the array, so these raise even where the array alone would
give no error:

| Call | with `ds` | with `d1` |
|---|---|---|
| `Filter(x, "a", "x")` | 13 | 13 |
| `Filter(x, Null)` | 94 | 94 |
| `Filter(x, "a", Null)` | 94 | 94 |

Its result is always a String array, VarType 8200 and `TypeName` "String()", whatever went in:

| Call | Bounds of the result |
|---|---|
| `Filter(d1, "a")` | 0..1 |
| `Filter(d1, "zzz")`, no match | 0..-1 |
| `Filter(Array("apple", "pear"), "a")`, a Variant array | 0..1 |
| `Filter(Array(1, 12), "1")`, a Variant array of numbers | 0..1 |

HexIDE returned the unallocated result before reading the match and the switch, so all three of the first table gave
no error for `ds`. Its `Filter(d1, Null)` gave no error either, and its result was a Variant array whenever the input
was allocated. All three now answer as above.

An allocated array that is not a 1-D String or Variant array. HexIDE previously joined and filtered the Long array `al` as
if it held strings, and raised a trappable 9, Subscript out of range, for the 2-D array `m`; #875 now raises the measured errors below:

| Call | VB6 |
|---|---|
| `Join(al, "-")` | 5 |
| `Filter(al, "1")` | 13 |
| `Join(m, "-")` | 5 |
| `Filter(m, "a")` | 13 |
| `Join(Array(1, 12), "-")` | "1-12": a Variant array of numbers is fine |

So the allocation check comes before the element-type check: an unallocated Long array joins to `""`, and an allocated
one raises 5.

**When Join reads its delimiter, round five.** Measured as native code and in the IDE, identical on every row. `d0` is
`Dim d0(0) As String` holding "x", and `k` and `w` are the classes above:

| Call | VB6 |
|---|---|
| `Join(ds, Null)`, and the same for `dv`, `dl` and an erased `de` | `""`, no error |
| `Join(Array(), Null)`, `Join(Split(""), Null)`: allocated, 0 To -1 | `""`, no error |
| `Join(Array("a"), Null)`, `Join(d0, Null)`: one element | 94 |
| `Join(Array("a", "b"), Null)`, `Join(d1, Null)` | 94 |
| `Join(ds, k)`, `Join(Array(), k)`, `Join(ds, w)` | `""`, no error |
| `Join(Array("a"), k)`, `Join(d0, k)`, `Join(d1, k)` | 438 |
| `Join(d1, w)` | "apple42pear" |

So Join reads its delimiter only when the array has an element, even a single one that needs no delimiter. HexIDE
read a Null delimiter as `""`, and read an object delimiter for an array with no element. Both now answer as above.

**Each element, round five.** Measured as native code and in the IDE, identical on every row. Each element sits in a
Variant array beside "ab", and the exclude-"zz" filter keeps every element, so the result shows what each became:

| Element `e` | `Join(Array("ab", e), "-")` | `Filter(Array("ab", e), "zz", False)` |
|---|---|---|
| Null | 94 | 94 |
| Empty | "ab-" | "ab", "" |
| True | "ab-True" | "ab", "True" |
| 1.5 | not probed | "ab", "1.5" |
| `#1/2/2000#` | not probed | "ab", "02/01/2000" (an en-GB guest) |
| `CVErr(5)` | 13 | 13 |
| a `Collection` | 450 | 450 |
| `w`, default member 42 | "ab-42" | "ab", "42" |
| `k`, no default member | 438 | 438 |

Three more: `Filter(Array("ab", Null), "ul")` raises 94, so Null is not matched as the text "Null";
`Filter(Array("ab", True), "ru")` gives one element, the String "True"; and `Filter(Array("ab", Empty), "")` gives both
elements. Every element of the result is a String.

An array as an element, measured in a later probe, both modes: `Filter(Array(Array(1, 2), "b"), "b", False)` and
`Join(Array(Array(1), "b"), "-")` each raise 13.

So each element is converted as a store into a String converts it: Null is 94, an array or an Error value 13, an
object its default member, and a Collection, whose default member `Item` needs an argument, 450. HexIDE read a Null
element as `""` with no error, and an array element as the CLR type name; it now raises 94 and 13. It has no Error
values (`CVErr` is not implemented) and no `Collection`.

**Filter's switch and compare, and the order of its checks, round five.** Measured as native code and in the IDE,
identical on every row, with `ee` an Empty Variant:

| Call | VB6 |
|---|---|
| `Filter(d1, "a", ee)`, `Filter(d1, "a", "False")` | no match kept: bounds 0..-1, since both elements contain "a" |
| `Filter(d1, "a", "True")`, `Filter(d1, "a", 2)` | both elements |
| `Filter(ds, "a", ee)`, `Filter(ds, "a", "True")`, `Filter(ds, "a", True, ee)` | no error, an unallocated String array |
| `Filter(d1, "A", True, ee)` | bounds 0..-1: a binary compare |
| `Filter(d1, "A", True, 1)`, `"1"`, `5` | both elements: a text compare |
| `Filter(d1, "A", True, "x")`, `Filter(ds, "a", True, "x")` | 13 |
| `Filter(d1, "A", True, Null)`, `Filter(ds, "a", True, Null)` | 94 |
| `Filter("abc", Null)`, `Filter("abc", "a", Null)`, `Filter("abc", "a", True, Null)`, `Filter(Null, Null)` | **94**, not the 13 a non-array gives |
| `Filter(5, "a", "x")`, `Filter("abc", "a", "x")`, `Filter("abc", "a", True, "x")`, `Filter(Null, "a")` | 13 |
| `Join("abc", Null)` | 13 |

So the switch reads as `CBool` reads it, and the compare as a number. Round five read the 5 row as "any value but 0 is
a text compare". **Round six overturned that** (below): 5 is a text compare, and round six's rows are consistent with
that being because 5 is a valid Windows locale identifier. Filter checks every argument for Null before it looks at the array, so a Null match beside a string is 94;
Join looks at the array first, so a Null delimiter beside a string is 13. HexIDE read both as plain numbers, so an
Empty switch or compare raised 13 and only 1 was a text compare; its Null compare was 13, and `Filter("abc", Null)`
was 13. All now answer as above, except the compare of 5, which HexIDE refuses as it refuses every value from 3 up
(#881).

**Filter's compare, and the order of its conversions, round six.** Measured as native code and in the IDE (`p24`, a
Sub Main project), identical on every row. `d1` holds "Apple" and "pear", so a binary compare for "a" keeps only
"pear" and a text compare keeps both:

| Compare | VB6 |
|---|---|
| 0, 0.5, Empty | binary: "pear" |
| 1, 1.4, "1" | text: both |
| 2, "2", 1.5 | 5 |
| -1, `True` | 5 |
| 3, 4, 5, 1033, 2057 | text: both |
| 3000, 65535 | 5 |
| 2, -1, 3000 or 1033, with `ds` never allocated, or with `Array()` | no error, an unallocated String array |

So the compare is read as a Long, with banker's rounding. 0 is binary and 1 text. 2 is `vbDatabaseCompare`, which
needs an Access database, and is 5, and so is any negative value. From 3 up, the rows are consistent with a Windows
locale identifier: valid ones compare as text, and invalid ones are 5. Whether a valid one compares in that locale, or
simply as text, is not measured; nothing here tells the two apart. None of that is checked when the array has no
element, nor, round seven found, whenever no comparison is made. And `Filter(Array(), "a")`, an allocated array of no elements, gives back an **unallocated**
String array, not 0 To -1, though a match that keeps nothing from an array that has elements is 0 To -1 (above).

| Call | VB6 |
|---|---|
| `Filter(d1, "a", "x", 3000)` | 13: the switch, so the compare's 5 comes later |
| `Filter(d1, Null, "x")` | 13: the switch before the match |
| `Filter(d1, "a", "x", Null)` | 94: the compare before the switch |
| `Filter(d1, Null, True, 3000)`, `Filter(d1, "a", Null, 3000)` | 94 |
| `Filter("abc", "a", True, 3000)` | 13: the array check before the compare's |

So Filter converts its arguments right to left: the compare, then the switch, then the match, each Null 94 and each
non-number 13. Then it checks the array. Round six read the rest as "and only after that whether the compare is one it
accepts"; round seven (below) found that Filter converts every element next, and checks the compare only when it
compares an element with the match.

**Nothing**, both modes (`p24`): `Filter(Array("ab", Nothing), "zz", False)`, `Join(Array("ab", Nothing), "-")`,
`Join(d1, o)` and `Filter(d1, o)`, with `o` Nothing, each raise 91. `Join(ds, o)` with `ds` never allocated is `""`.

**HexIDE, after round six.**
- 0 is binary and 1 text; 2 and negative values are 5.
- 3 and above are refused loudly, naming #881, because HexIDE can't tell which of those values VB6 accepts,
  and its text compare uses no locale.
- Before, every value but 1 was binary.
- The conversions run right to left, and the compare is checked only for an array that has elements.
- `Filter(Array(), ...)` gives an unallocated array.
- Nothing is 91 as an element, a delimiter or a match, where it read as `""`.

**The other functions' compare** (`p24`, both modes): `InStr` and `StrComp` raise 5 for 2, while `Split` and
`Replace` take 2 as text. All four raise 5 for -1 and 3000, and take 1033 as text. HexIDE reads all of them as
"1 is text, anything else binary" in `InStr`, `InStrRev`, `Split` and `Replace`; `StrComp` is not implemented. That's
#881.

**Filter's compare, round seven.** Measured as native code and in the IDE (`p41`, a Sub Main project), identical on
every row. `s` is `Split("apple pear")`, so a binary compare for "A" keeps nothing and a text compare keeps both:

| Compare | VB6 |
|---|---|
| 0.6 | text: it rounds to 1 |
| 4096, 1033 | text |
| 4095, 32767, 65536, 3000, -2 | 5 |
| 3E+10 | 6 |
| `Array(1)` | 13 |

So 4096 is accepted and 4095 is not, as 1033 is and 3000 is not. That is still consistent with a locale identifier, and
still not proof of one.

**When the compare is checked, round seven.** Measured as native code and in the IDE (`p40` and `p41`), identical on
every row:

| Call | VB6 |
|---|---|
| `Filter(Array("x", Null), "a", True, 2)` | 94 |
| `Filter(Array(Array(1)), "a", True, 2)` | 13 |
| `Filter(Array("x", Null), "")` | 94 |
| `Filter(Array("x"), "", True, Null)`; with `"zz"` as the compare | 94; 13 |
| `Filter(Array("x"), "", True, 2)`; with 3000 as the compare | bounds 0..0, no error |
| `Filter(Array("x", "y"), "", True, -1)` | 0..1 |
| `Filter(Array("x"), "", False, 2)` | 0..-1 |
| `Filter(Array(""), "a", True, 2)` | 0..-1 |
| `Filter(Array(""), "")`; with `True, 2` | 0..0 |
| `Filter(Array(""), "", False)` | 0..-1 |
| `Filter(Array("", "x"), "", True, 2)`; with `False, 2` | 0..1; 0..-1 |
| `Filter(Array("x"), "xyz", True, 2)` | 5 |

So Filter converts every element before it compares any, and checks the compare only when it compares an element
with the match. An empty match is in every element, an empty one included, and an empty element holds no other match,
so neither needs a comparison: `Filter(list, "", True, 2)` is the whole list. The compare itself is still converted
first, so a Null or a non-number there raises whatever else happens.

**Each object argument is read as its turn comes, round seven** (`p41`, both modes). `d1` holds "apple" and "pear",
`k` and `w` are the classes above, `o` is Nothing and `ds` is never allocated:

| Call | VB6 |
|---|---|
| `Filter(d1, k, "x")`, `Filter(d1, "a", k, "x")` | 13 |
| `Filter(d1, k, Null)`, `Filter(d1, "a", k, Null)` | 94 |
| `Filter(d1, k, True, o)` | 91 |
| `Filter(d1, w, "x")` | 13: `w` is never read |
| `Filter(d1, "a", True, o)`, `Filter(ds, "a", True, o)` | 91 |

**Err as an argument** (`p40`, both modes), with `s` holding "Apple", "pear" and "plum":
- as the compare, with Err.Number 1, `Filter(s, "a", True, Err)` is a text compare, keeping "Apple" and "pear"; with 0,
  binary, keeping "pear";
- as the switch, 5 includes and 0 excludes;
- as the match, with Err.Number 1, it is "1", which nothing holds.

None raised; Err.Number was unchanged after each.

**Other built-ins show the same order** (`p41`, both modes):

| Call | VB6 |
|---|---|
| `Left(k, "x")`, `Mid(k, "x")`, `Replace(k, "a", "b", "x")`, `Split(k, ",", "x")`, `InStr(1, k, "a", "x")`, `Left(w, "x")` | 13 |
| `Left(k, Null)` | 94 |
| `Left(k, 1)` | 438 |
| `Left(w, 1)` | "4" |

So VB6 converts a typed value argument before it reads an object in a Variant or String slot. HexIDE now checks
the measured numeric value slots before reading the class object (#959); those rows previously raised 438 or
refused `w` under #174. A valid `Left(w, 1)` still requires default-member execution (#174).

**HexIDE, after round seven.**
- Filter converts every element first, and checks the compare only when it compares: an empty search returns the
  whole list, where it raised 5.
- It reads its match, switch and compare itself, each as its turn comes, rather than letting the dispatcher read every
  object first: `Filter(d1, k, "x")` is 13, where it was 438.
- A Nothing compare is 91, where it read as 0, a binary compare.
- Err as the compare or the switch is its Number, where it reached the conversion as an object.
- #959 checks the numeric value slots in the five calls above before reading the class object. Other object/value
  combinations retain their existing order pending measurements.

**A default member on a typed variable, round five.** With `w` declared `As Widget`, whose default member is a
Function returning 42, both modes: `Not w` is -43, a Long, and `CDate(w)` is 10/02/1900 (an en-GB guest). Through a
Variant, `Not` of the same object raises 450 (above). So the typed `w` answers where the Variant gave 450, and a
Property Get default member answers through the Variant too (above). In the IDE, a Case value compared with an Empty or
Null selector gives 450 for the typed `w` as well. The rows say which uses give 450, not why.

**A Let of App into a typed target, round five.** Both modes, under Resume Next, with each target holding a value
first:

| Target | VB6 after `x = App` |
|---|---|
| `Dim k As Plain`, holding an instance | 438, `k` still that instance |
| `ByVal x As Long`, holding 5 | 438, VarType 3, 5 |
| a `Function ... As Long`'s own name, holding 7 | 438, and the function returns 7 |
| a `Function ... As String`'s own name, holding "x" | 438, and the function returns "x" |

So only a Variant is emptied by `v = App`. HexIDE leaves a Variant as it was too (#878): a parameter's and a return
name's slots carry no declared type, so a Variant cannot yet be told from them, and emptying every slot without one
emptied all four of these.

**Found on the way, both modes** (#876, #877):

| Statement | VB6 |
|---|---|
| `For Each x In Filter(ds, "a")`, over a copy of that result, and over `ds` itself, under Resume Next | 92, and the body runs once |
| `Err.Raise 5`, then `On Error Resume Next` | `Err.Number` is 0 |
| a helper whose first line is `On Error Resume Next`, called after `Err.Raise 6` | the helper reads 0, and so does the caller after it returns |
| `Err.Raise 7`, then `On Error GoTo 0` | 0 |

**An array used as text, found on the way** (#879; `p19`, both modes, and the last row from `p42`, by `/make`):

| Use | VB6 |
|---|---|
| `c = "v=" & Array(1)`, `c = CStr(Array(1))`, `c = Len(Array(1))`, `c = UCase(Array(1))`, `Print #f, Array(1)` | 13 |
| `s = Array(1)`, `s` a String | 13, `s` unchanged |
| `c = "v=" & a`, with `Dim a(1) As Long` | does not compile: "Type mismatch" |

HexIDE now raises 13 in its shared text conversion (#879), where these routes gave the CLR type name
(except Join and Filter elements, already refused by #840). The typed Long array is refused when executed
rather than at compile time. The debugger's own array display remains separate.

**The in-box objects:**

| Used as a value | VB6 |
|---|---|
| `Err` | its `Number`: `"v=" & Err` is "v=0" or "v=11", `If Err Then` takes the Then branch after an error, `x = Err` stores 11, `Err + 1` is 12, `VarType(Err)` is 3, `Print #f, Err` writes 11 |
| `App`, `Screen`, `Clipboard` | 438: none has a default member. For App, measured: `"v=" & App`; `s = App`, leaving a String `s` as it was; `v = App`, which **empties** a Variant `v` first (VarType 0), where `v = k` for a class with no default member leaves it as it was (HexIDE does not empty it yet, #878); and `Debug.Print App` and `Debug.Print Screen` in the IDE |
| `Forms` | 450: its default member, `Item`, needs an argument |
| `Debug` | a compile error, "Syntax error" (`/make` only, as is the next row) |
| a control array without an index, `"v=" & Cmd` | a compile error, "Argument not optional" |

`TypeName` of each: Err "ErrObject", App "App", Screen "Screen", Clipboard "Clipboard", Printer "Printer", and
Forms **"Object"**. HexIDE gave "VbErr" and "VbApp", its own class names, and now gives VB6's; it has no Screen,
Clipboard, Printer or Forms yet. Round three, on a form with a control array `Cmd`: `TypeName(Cmd)` is "Object" and
`TypeName(Cmd(0))` "CommandButton"; `TypeName` of `Dim wa(1) As Widget`, and of an unallocated `Dim wd() As Widget`, is
**"Object()"**, not "Widget()", with VarType 8201. `TypeName(Debug)` does not compile ("Syntax error", `/make`).

**The rows the review found unprobed,** measured in the second round:

| Use | `w` (42) | `k` |
|---|---|---|
| `If False Then ... ElseIf w Then` | the ElseIf branch | 438, and the ElseIf branch runs (#839) |
| `For i = w To 45`; `For i = 1 To 100 Step w` | 4 passes, `i` 46; 3 passes, `i` 127 | 438 at the bound or the step |
| `Do ... Loop While w`; `Loop Until w` | loops; leaves after one pass | 438, and leaves after one pass |
| `c = MsgBox(k)` | | 438, before any box shows |
| `Debug.Print k`, `Debug.Print "v=" & k` | | 438 in the IDE |

`MsgBox(w)` would show a box, which session 0 cannot dismiss, so it was not run. That it shows "42" is inferred.

**HexIDE, after #174's refusal (this change).** One rule, `ObjectAsValue`, applied wherever a value is wanted:
- every binary operator's operands (`GetTwoValues`), negation and `Not`;
- every text coercion (`VbDisplay.ToText`: concatenation, `CStr`, `Val`, `Str` and the string functions), for a class
  instance and an in-box object. **Not for a control**: `"v=" & Text1`, `CStr(Text1)` and `Debug.Print Text1` still give
  the control's .NET type name, which is #790's;
- a Let, a store into a declared scalar (`CoerceOnStore`), a ByVal argument for a declared scalar parameter;
- every built-in function's arguments (`EvaluateBuiltInFunction`), once the name is found to be a built-in: an
  unknown name stays "Sub or Function not defined" whatever it is given. `TypeName`, `IsObject`, `IsMissing` and `Array`
  take the argument itself, and so does the array that `LBound`, `UBound`, `Join` and `Filter` take first. `Join`'s
  delimiter and `Filter`'s match, switch and compare are passed on unread, for those functions to read in their own
  order (round seven). `VarType` and the Is functions refuse a class with a default member and answer about one without;
- the numeric conversions' own check (`RefuseNonScalar`, in `NarrowNum` and `CBool`), which reads an object wherever
  the dispatcher has not: Filter's compare and switch, and since #873 every subscript, bound and control-array index.
  Of those, only the subscript is measured and pinned: `a(k)` is 438, `a(Nothing)` 91, and `a(w)` is 7 in VB6 and
  refused naming #174 here (#873's round five);
- the conditions of `If`, `ElseIf`, `While` and `Do`, a For bound and step, `Select Case`, a Case value, a range's
  bounds and a `Case Is` value;
- `Debug.Print`, `Randomize`, `Err.Raise`, `Error`, `ChDir`, `ChDrive`, and the Mid statement's start and length;
- the Immediate window's `? x`: a class with a default member is refused as a call into the program, as `? obj.Prop`
  is (D15), and one with none is 438. It used to print the class's name.

A class with a default member is refused, untrappably, naming #174. One with none raises 438. Err stands for its
Number, App raises 438, and `Debug` and a control array used as values are the compile errors VB6 gives.

