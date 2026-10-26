## Named arguments (2026-10-22, #1465)

**The obvious guess is that named arguments are evaluated in the order they are written. They are evaluated in parameter
order.**
- `Foo c:=F("c", 3), a:=F("a", 1)` calls `F("a", …)` first.
- This holds into the program's own Subs, its Functions and its class methods, and into intrinsics:
  - `Mid(Start:=F(…), String:=F(…))` evaluates String first.
  - `Replace(Replace:=…, Find:=…, Expression:=…)` evaluates Expression, then Find, then Replace.
- Positional arguments come before every named one, and they fill the first parameters. So parameter order is the order of
  the whole call.

**The second guess worth recording is that a name the editor's signature help shows is one VB6 takes.** For twenty
intrinsics it is not. `Len(Expression:="abc")` does not compile, and neither does any named argument to `Abs`, the `C…`
conversions, `InStr` or `UBound`, although signature help names their parameters (list below).

**The question.** HexIDE stopped at every `name:=value` with "Assign is not implemented". #1465 asked:
- how a named argument binds, beside positional ones and beside a skipped one, with `IsMissing`;
- which compile errors VB6 gives;
- what an intrinsic, a class method and an event take.

**The probe.**
- **52 projects of one statement each.** One project per row, so that a compile error names its own row. These were Sub
  Main programs whose helpers log what they were given, and an `F(tag, v)` that logs its tag and returns `v`, to show the
  order.
- **79 compile-only projects, one per intrinsic.** Each took an intrinsic that has a signature entry whose parameter names
  are plain identifiers, and named every parameter.
- **132 projects on the required parameters.** For each intrinsic that compiled, one project named only the parameters
  claimed to be required, and one more left out each of those in turn.
- **Three RaiseEvent projects**, and four that give more positional arguments than there are parameters.

Every project was made native and p-code under en-GB, and every line was identical. The 28 programs that
`NamedArgumentTests` runs were then made the same way. They logged exactly what the tests pin, and gave exactly the compile
errors the tests expect.

The procedures used below are:
- `Foo(a As Integer, Optional b As Integer = 2, Optional c)`, which logs `a`, `b`, and `c` or "c missing";
- `G(a As Long, b As Long) As Long`, which returns `a - b`;
- `Inc(n As Long)`, which adds 1;
- `M(x As Integer, Optional y As Integer = 5)` and `Twice(Optional ByVal n As Long = 1) As Long`, both methods of a class.

| Call | VB6 |
|---|---|
| `Foo 1, c:=5` | a=1 b=2 c=5 |
| `Foo a:=1` | a=1 b=2, c missing (`IsMissing` True) |
| `Foo c:=3, a:=1` | a=1 b=2 c=3 |
| `Foo 1, b:=4` | a=1 b=4, c missing |
| `Foo B:=7, A:=8` | a=8 b=7: a name matches in any case |
| `Foo 1, , c:=9` | a=1 b=2 c=9 |
| `Call Foo(c:=1, a:=2)` | a=2 b=2 c=1 |
| `x = G(b:=2, a:=10)`, and `G(b:=2, a:=10)` inside an expression | 8, both |
| `Inc n:=v`, `v` 4 | `v` 5: ByRef through a name |
| `k.M y:=1, x:=2`; `k.Twice(n:=21)`, also inside an expression | M x=2 y=1; 42 |
| `Bar 1`, `Bar a:=1`, `Bar 1, c:=2`, with `Bar(a As Integer, Optional c)` | `IsMissing(c)` True, True, False |
| `Foo c:=F("c", 3), a:=F("a", 1)` | evaluates a, then c |
| `Foo F("a", 1), c:=F("c", 3), b:=F("b", 2)` | a, b, c |
| `x = G(b:=F("b", 2), a:=F("a", 10))`; `k.M y:=F("y", 1), x:=F("x", 2)` | a, then b; x, then y |
| `Mid(Start:=F("start", 2), String:=F("string", "abcdef"))` | string, start; "bcdef" |
| `Mid(F("string", "abcdef"), Length:=F("length", 2), Start:=F("start", 3))` | string, start, length; "cd" |
| `Replace(Replace:=F(…), Find:=F(…), Expression:=F(…))` | expression, find, replace |
| `Left(Length:=F(…), String:=F(…))`; `Round(NumDigitsAfterDecimal:=F(…), Number:=F(…))` | string, length; number, digits |
| `IIf(FalsePart:=F("false", 2), TruePart:=F("true", 1), Expression:=F("expression", False))` | expression, true, false; 2 |
| `Mid(String:="abcdef", Start:=2, Length:=3)`; `Mid(Start:=2, String:="abcdef")` | "bcd"; "bcdef" |
| `Left(String:="abc", Length:=2)`; `Replace(Expression:="aXbX", Find:="X", Replace:="-")` | "ab"; "a-b-" |
| `Format(Expression:=1.5, Format:="0.00")`; `DateSerial(Year:=2003, Month:=2, Day:=1)` | "1.50"; 2003-02-01 |
| `DateAdd(Interval:="d", Number:=1, Date:=#2/1/2003#)`; `Round(Number:=2.567, NumDigitsAfterDecimal:=1)` | 2003-02-02; 2.6 |
| `Trim(String:=" a ")`, `UCase(String:="abc")`, `Chr(CharCode:=65)`, `Val(String:="12")` | "a", "ABC", "A", 12 |
| `String(Number:=3, Character:="z")`, `Space(Number:=2)`, `InStrRev(StringCheck:="abcabc", StringMatch:="b")` | "zzz", two spaces, 5 |
| `IIf(Expression:=True, TruePart:=1, FalsePart:=2)`; `Split(Expression:="a b c")(1)` | 1; "b" |
| `MsgBox` with all five of its names; `InputBox` with all seven of its own | compile (being modal, these were not run) |

**The compile errors:**

| Call | VB6's compile error |
|---|---|
| `Foo d:=1` | Named argument not found |
| `Foo a:=1, a:=2`; `Foo 1, a:=2`; `Foo 1, , b:=4` | Named argument already specified |
| `Foo b:=3`, with the required `a` left out | Argument not optional |
| `Foo a:=1, 2`, positional after named; `Foo a:=1, , c:=3`, a blank after named | Syntax error |
| `Foo 1, 2, 3, 4, c:=5`, and `Foo 1, 2, 3, 4`; `Mid("abc", 1, 1, 1, Start:=1)`, and `Mid("abc", 1, 1, 1)` | Wrong number of arguments or invalid property assignment |
| `Pa 1, x:=2`, with `Pa(a As Integer, ParamArray x())`; `Array(ArgList:=1)`; `Choose(Index:=1, Choice:="a")` | Argument in ParamArray may not be named |
| `RaiseEvent E(b:=1, a:=2)`, and `RaiseEvent E(1, b:=5)` | Syntax error |
| `Left(Str:="abc", Length:=2)`; `MsgBox Message:="x"` | Named argument not found |
| `Mid(String:="abc", Length:=2)` | Argument not optional |
| `String(Number:=3)`, and `String(Character:="z")` | **Syntax error**, where every other intrinsic missing a required parameter gives "Argument not optional" |
| `Bar a:=1` and `x = Baz(a:=1)`, neither defined; `CLDbl(Expression:=x)` | Sub or Function not defined |

**The intrinsics that take names.** Every intrinsic with an entry in the language server's `VbSignatures.cs` was named in
full. These 57 compile, with exactly the names the signature table gives, and require the number shown:

| Names, in order | Required | Intrinsics |
|---|---:|---|
| `Number` | 1 | Atn, Cos, Exp, Log, Sin, Space, Sqr, Tan |
| `Number` | 0 | Rnd |
| `String` | 1 | Asc, AscW, LCase, LTrim, RTrim, Trim, UCase |
| `CharCode` | 1 | Chr, ChrW |
| `Date` | 1 | Day, Month, Year |
| `Time` | 1 | Hour, Minute, Second |
| `Expression` | 1 | Environ, IsDate, IsEmpty, IsError, IsNull, IsNumeric, IsObject, StrReverse |
| `VarName` | 1 | IsArray, TypeName, VarType |
| `ArgName` | 1 | IsMissing |
| `Color` | 1 | QBColor |
| `Class, ServerName` | 1 | CreateObject |
| `Pathname, Class` | 0 | GetObject |
| `Pathname, WindowStyle` | 1 | Shell |
| `Interval, Number, Date` | 3 | DateAdd |
| `Interval, Date1, Date2, FirstDayOfWeek, FirstWeekOfYear` | 3 | DateDiff |
| `Interval, Date, FirstDayOfWeek, FirstWeekOfYear` | 2 | DatePart |
| `Year, Month, Day` | 3 | DateSerial |
| `Hour, Minute, Second` | 3 | TimeSerial |
| `Date, FirstDayOfWeek` | 1 | Weekday |
| `Expression, Format, FirstDayOfWeek, FirstWeekOfYear` | 1 | Format |
| `Expression, TruePart, FalsePart` | 3 | IIf |
| `Prompt, Buttons, Title, HelpFile, Context` | 1 | MsgBox |
| `Prompt, Title, Default, XPos, YPos, HelpFile, Context` | 1 | InputBox |
| `String, Length` | 2 | Left, Right |
| `String, Start, Length` | 2 | Mid |
| `StringCheck, StringMatch, Start, Compare` | 2 | InStrRev |
| `Expression, Find, Replace, Start, Count, Compare` | 3 | Replace |
| `Red, Green, Blue` | 3 | RGB |
| `Number, Character` | 2 | String |

**The intrinsics that refuse every name.** Any named argument to these 20 is "Syntax error": Abs, CBool, CByte, CCur, CDate,
CDbl, CInt, CLng, CSng, CStr, CVar, CVErr, Fix, InStr, Int, LBound, Len, Sgn, StrComp and UBound. Each has a full parameter
tooltip, with names.

Val (`String`), Round (`Number, NumDigitsAfterDecimal`) and Split (`Expression`) took names in the single-row projects.
Their required counts, and thirteen more intrinsics', are measured below (#1475).

**What resisted explanation.**
- **Why those twenty refuse**, and no rule I would defend. Most of them are the conversions and the numeric primitives,
  which suggests intrinsics compiled inline rather than called through the library's type information. But that reading
  leaves InStr and StrComp unexplained, so I record the list and not a rule.
- **Why String alone gives "Syntax error"** for a missing required parameter, which is the same oddity.

**Not measured:**
- a `Property` with arguments, which HexIDE refuses with or without names;
- the methods of controls, forms and other objects (#1474).

### In HexIDE (#1465)

**How a call takes its arguments:**
- **The program's own Subs, Functions and class methods.** A named argument reaches the call unevaluated, as its name and
  a thunk. At the top of `RunProcedure`, before anything of the call is set up, `NamedArguments.Arrange` raises VB6's
  compile errors from the table above, as the statement runs. Then it evaluates the named arguments in parameter order,
  in the caller, and leaves an omitted Optional to bind as it always has.
- **Intrinsics.** These take their names from `VB6BuiltIns.NamedParameters.cs`, the 57 above. The twenty above that
  refuse every name give "Syntax error", Array and Choose give the ParamArray error, and any other intrinsic stopped the
  run naming #1475, until #1475 measured them all (below). A name that is no procedure and no intrinsic is "Sub or Function not defined", as before.
- **RaiseEvent** gives "Syntax error" for any named argument.
- **A control's, a form's or an object's method** stops the run naming #1474.

**The two tables.** The editor's signature help and the runtime keep separate tables. The runtime's is the one that runs.
`NamedParameterParityTests` reads `VbSignatures.cs` as text and fails closed:
- every intrinsic the runtime takes names for must have an entry there, with the same names in the same order;
- the file must yield entries at all.

It checks one direction only, because the tooltip shows names for the twenty that VB6 refuses.

**Checked against the tests and the corpus.**
- **The tests.** Three mutants each fail the tests they should: a renamed parameter in the runtime table, evaluating named
  arguments as written, and a reshaped `VbSignatures.cs`.
- **The corpus.** Two corpus cases that carried a line continuation inside a named argument now run as VB6 ran them. They
  were removed from `KnownDivergences`.

### The intrinsics signature help had no entry for (2026-10-23, #1475)

**The question.** The 16 intrinsics HexIDE implements without a tooltip had their named arguments refused. Which names
does VB6 take for each, how many are required, and what do the ones with no parameters say to a name?

**The probe.**
- 53 one-call projects, made native and p-code under en-GB.
- The 19 programs the #1475 tests run, made the same way. Their compile errors and transcript are exactly what the tests
  pin.

The names were read first from the VBA library's type information in msvbvm60.dll, then confirmed one by one. For each
intrinsic there was:
- a project naming every parameter;
- one naming the required ones only;
- one leaving out each required one in turn;
- one with a wrong name.

Native code and p-code agreed on every line.

| Parameters, in order | Required | Intrinsic | Measured with every name |
|---|---|---|---|
| `String` | 1 | Val | `Val(String:="12")` 12 |
| `Number, NumDigitsAfterDecimal` | 1 | Round | 2.6; `Round(Number:=2.5)` 2 |
| `Expression, Delimiter, Limit, Compare` | 1 | Split | `Limit:=2` gives `a` and `b,c`; `Expression:="a b c"` alone splits on spaces |
| `SourceArray, Delimiter` | 1 | Join | `ab-cd-AE`; with no Delimiter, `ab cd AE` |
| `SourceArray, Match, Include, Compare` | 2 | Filter | `vbTextCompare` matches `ab` and `AE` |
| `Month, Abbreviate` | 1 | MonthName | Feb; February. `MonthName(Abbreviate:=True, Month:=3)` is Mar |
| `Weekday, Abbreviate, FirstDayOfWeek` | 1 | WeekdayName | `Weekday:=1, Abbreviate:=True, FirstDayOfWeek:=vbMonday` is Mon; Abbreviate can be skipped |
| `Date` | 1 | DateValue | `Date:="2000-01-02"` is 36527 |
| `Time` | 1 | TimeValue | `Time:="12:00"` is 0.5 |
| `FileNumber` | 1 | EOF | True on an empty file |
| `RangeNumber` | 0 | FreeFile | `RangeNumber:=1` is 256; `:=0` is 1 |
| `Expression` | 1 | CDec, CVDate | 1.5; 2 |
| `Number` | 1 | Hex, Oct, Str, and Hex$, Oct$, Str$ | FF; 10; " 5" |

Leaving out a required parameter is "Argument not optional", in every case measured. A wrong name is "Named argument
not found", for every one of them.

**Six have no parameters, and refuse a name three ways:**

| `X(X:=1)` | VB6's compile error |
|---|---|
| Command, Now, Time | Named arguments not allowed |
| Date | Syntax error |
| DoEvents, Timer | Wrong number of arguments or invalid property assignment |

**Aside.** `WeekdayName(Weekday:=1)`, with no FirstDayOfWeek, is Monday under en-GB. The day a week starts on there comes
from the locale. That makes it a fact about WeekdayName's default, not about names, so the tests give FirstDayOfWeek
explicitly.

**What this overturned.**
- **That the C-conversions all refuse names.** CDec and CVDate take `Expression`, where CBool through CVar and CVErr are
  "Syntax error" (#1465).
- **That the type library is the whole story.** It lists Hex, Oct and Str on a hidden interface with no parameters, yet each
  takes `Number`, and a wrong name is caught.
- **That the VBA documentation's names are VB6's.** It calls Round's parameters `expression` and `numdecimalplaces`. VB6
  takes `Number` and `NumDigitsAfterDecimal`, as its type library says.

**What resisted explanation.**
- **Why the no-parameter six split three ways.** Date's "Syntax error" fits Date being a keyword as well as a function.
  Command, Now and Time against DoEvents and Timer I can't explain.

#### In HexIDE (#1475)

- **The table.** `VB6BuiltIns.NamedParameters.cs` now lists 73 intrinsics with their names and required counts.
- **The six with no parameters** raise the errors above.
- **The tooltips.** All 16 have a signature-help entry, Command and DoEvents included.
- **The guard.** `NamedParameterParityTests` now also fails when a registered intrinsic is missing from every table, or
  has no tooltip. An intrinsic added later is then measured in the same change. The sweep the issue asked for is kept
  as that test.
- **An intrinsic HexIDE doesn't implement at all** is answered, with names or without, by the "not implemented" its
  caller already gives.

