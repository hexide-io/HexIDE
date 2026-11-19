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

### LoadPicture and SavePicture (2026-10-27, for #2254)

**The question.** `NamedParameterParityTests` needs LoadPicture's names before the StatusBar's PR can register it. Which
names does it take, how many are required, and what does a wrong one say?

**The probe.**
- **The names** were read first from VB6.OLB's type information, where both are members of `VBGlobal`:
  - `LoadPicture([FileName], [Size], [ColorDepth], [X], [Y])`, all Variants, all optional, returning an `IPictureDisp`;
  - `SavePicture(Picture As IPictureDisp, FileName As String)`.
- **The projects:** 22 of one call each, made native and p-code. Native code and p-code agreed on every line.
- **The files:** an icon, `ARW01DN.ICO`, and a bitmap, `BELL.BMP`, from VB6's own Graphics folder.

| Call | VB6 |
|---|---|
| `LoadPicture(FileName:=ico, Size:=vbLPCustom, ColorDepth:=vbLPColor, X:=32, Y:=32)` | an icon, Type 3, 847 HIMETRIC square: 32 pixels |
| the same names in reverse order, X and Y 16 | Type 3, 423: 16 pixels |
| `LoadPicture(filename:=bmp, SIZE:=vbLPSmall)` | the bitmap, Type 1: a name matches in any case |
| `LoadPicture(ico, X:=16, Y:=16)`; `LoadPicture(ico, , , X:=16, Y:=16)` | Type 3, both |
| `LoadPicture(Y:=F(…), X:=F(…), FileName:=F(…), Size:=F(…))` | evaluates FileName, Size, X, Y: parameter order |
| `LoadPicture()`, and `LoadPicture` with no parentheses | **an empty picture:** Type 0, Handle 0. **None is required** |
| `LoadPicture(Size:=vbLPLarge)`; `LoadPicture(X:=16, Y:=16)` | an empty picture, no error |
| `LoadPicture(FileName:=bmp, Size:=vbLPLarge)` | the bitmap as it is, no error: Size is for icons and cursors |
| `SavePicture Picture:=p, FileName:=f`, and in reverse order | saved, 382 bytes both ways |

**The compile errors:**

| Call | VB6's compile error |
|---|---|
| `LoadPicture(File:=bmp)`; `LoadPicture(FileName:=bmp, Width:=16)`; `SavePicture Pic:=p, FileName:=f` | Named argument not found |
| `LoadPicture(FileName:=bmp, FileName:=ico)` | **Named argument already specified**, which no earlier row had met |
| `LoadPicture(FileName:=ico, 16)`, positional after named | Syntax error |
| `LoadPicture(ico, vbLPCustom, vbLPColor, 16, 16, 1)`, six | Wrong number of arguments or invalid property assignment |
| `SavePicture FileName:=f`, Picture left out | Argument not optional |

So the rows are:

| Parameters, in order | Required | Intrinsic |
|---|---|---|
| `FileName, Size, ColorDepth, X, Y` | 0 | LoadPicture |
| `Picture, FileName` | 2 | SavePicture |

### The intrinsics #1465 left unmeasured, and the type library's names (2026-11-16, #4145)

vb6.exe SP6, `/make`. Each named call ran beside its positional twin, native and p-code agreeing. A compile error needs a build of its own, so those were built one at a time. The names come from the VBA type library inside `msvbvm60.dll`, read with `LoadTypeLibEx`, since that, not the documentation, is what the compiler matches.

They come from the VBA type library inside `msvbvm60.dll`, read with `LoadTypeLibEx`. **Where they differ from the documentation, the type library wins.** The financial functions' last flag is `Due`, not `Type`, and the arrays are `ValueArray`. A documented name is "Named argument not found".

| Function | Names, `[optional]` | Named call |
|---|---|---|
| `AscB` | `String` | accepted |
| `ChrB` | `CharCode` | accepted |
| `CurDir` | `[Drive]` | accepted |
| `LeftB`, `RightB` | `String`, `Length` | accepted |
| `MidB` | `String`, `Start`, `[Length]` | accepted, in any order |
| `Dir` | `[PathName]`, `[Attributes]` | accepted |
| `FileAttr` | `FileNumber`, `[ReturnType]` | accepted |
| `FileDateTime`, `FileLen`, `GetAttr` | `PathName` | accepted |
| `FormatCurrency`, `FormatNumber`, `FormatPercent` | `Expression`, `[NumDigitsAfterDecimal]`, `[IncludeLeadingDigit]`, `[UseParensForNegativeNumbers]`, `[GroupDigits]` | accepted |
| `FormatDateTime` | `Expression`, `[NamedFormat]` | accepted |
| `Partition` | `Number`, `Start`, `Stop`, `Interval` | accepted |
| `SLN` | `Cost`, `Salvage`, `Life` | accepted |
| `SYD` | `Cost`, `Salvage`, `Life`, `Period` | accepted |
| `FV` | `Rate`, `NPer`, `Pmt`, `[PV]`, `[Due]` | accepted |
| `PV` | `Rate`, `NPer`, `Pmt`, `[FV]`, `[Due]` | accepted |
| `Pmt` | `Rate`, `NPer`, `PV`, `[FV]`, `[Due]` | accepted |
| `IPmt`, `PPmt` | `Rate`, `Per`, `NPer`, `PV`, `[FV]`, `[Due]` | accepted |
| `NPer` | `Rate`, `Pmt`, `PV`, `[FV]`, `[Due]` | accepted |
| `Rate` | `NPer`, `Pmt`, `PV`, `[FV]`, `[Due]`, `[Guess]` | accepted |
| `IRR` | `ValueArray`, `[Guess]` | accepted |
| `MIRR` | `ValueArray`, `FinanceRate`, `ReinvestRate` | accepted |
| `NPV` | `Rate`, `ValueArray` | accepted |
| `StrPtr`, `VarPtr`, `ObjPtr` | `Ptr` | accepted |
| `InStrB` | `[Start]`, `[String1]`, `[String2]`, `[Compare]` | **compile error "Syntax error"**, with or without `Start` |
| `CallByName` | `Object`, `ProcName`, `CallType`, then a ParamArray | **compile error "Argument in ParamArray may not be named"**, even with `Args` not named |
| `IMEStatus` | none | **compile error "Wrong number of arguments or invalid property assignment"** |

- **Every accepted named call gave its positional twin's value.** `FV(Pmt:=-100, NPer:=12, Rate:=0.01)`, out of order with its optionals omitted, equals `FV(0.01, 12, -100)`.
- **A wrong name is a compile error, "Named argument not found."** That's `LeftB(Foo:=…)`, `ChrB(Code:=…)`, `CurDir(Path:=…)`, `Dir(Path:=…)`, `GetAttr(Path:=…)`, `StrPtr(x:=…)`, `FV(…, Type:=1)` and `NPV(…, Values:=…)`.
- **An omitted required argument is a compile error, "Argument not optional."** That's `LeftB(Length:=2)`, `MidB(String:=…, Length:=2)`, `FV(Rate:=…, NPer:=…)`, `MIRR` without `ReinvestRate`, `Partition` without `Interval`, and `SLN` without `Life`.
- **`LeftB`, `RightB`, `MidB`, `ChrB` and `CurDir`** have no parameters in the type library. They're the compiler's own, and these names are what it accepted.

### DDB's names (2026-11-17, #2524)

vb6.exe SP6, `/make`, every call built native and as p-code, which logged the same lines. `DDB` wasn't registered in HexIDE when #4145 measured the others, so its names were measured with #2524's review. Each named call ran beside its positional twin; a compile error needed a build of its own.

| Expression | Result |
|---|---|
| `DDB(2400, 300, 10, 1)` | `480`, Double |
| `DDB(Cost:=2400, Salvage:=300, Life:=10, Period:=1)` | `480`, Double |
| `DDB(cost:=2400, salvage:=300, life:=10, period:=1)` | `480`, Double |
| `DDB(2400, 300, 10, 2, 1.5)` | `306`, Double |
| `DDB(Cost:=2400, Salvage:=300, Life:=10, Period:=2, Factor:=1.5)` | `306`, Double |
| `DDB(Factor:=1.5, Period:=2, Life:=10, Salvage:=300, Cost:=2400)` | `306`, Double |
| `DDB(2400, 300, Life:=10, Period:=2, Factor:=1.5)` | `306`, Double |
| `DDB(Cost:=2400, Salvage:=300, Life:=10)` | **compile error "Argument not optional"** |
| `DDB(Cost:=2400, Salvage:=300, Life:=10, Per:=1)` | **compile error "Named argument not found"** |
| `DDB(Cost:=2400, Salvage:=300, Life:=10, Period:=1, Type:=0)` | **compile error "Named argument not found"** |

- **`DDB` takes `Cost`, `Salvage`, `Life`, `Period` and `[Factor]`,** in any order, after positional arguments, and without regard to case, as `SLN` and `SYD` do. Each named call gave its positional twin's value.
- **Not measured:** a positional call with too few or too many arguments.

### Named arguments to a control's, a form's and a Collection's methods (2026-11-18, #1474, SP6)

**The rig:**
- **Build and run:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0 of the oracle guest. Native code and p-code gave identical output in every row.
- **The program:** a startup form with a ListBox `List1`, a ComboBox `Combo1`, a TextBox `Text1` and a PictureBox `Picture1`. Its `Form_Load` ran each statement under `On Error Resume Next` and logged the error or `ok`.
- **The evaluation-order tracer:** `F(tag, v)` is a module function that logs `eval tag` and returns `v`.
- **Compile errors:** a statement expected not to compile ran in a project of its own, so its error doesn't stop the others. The message is the one in `/make`'s error log.

**Early-bound, on the control's or the form's own name:**

| Statement | VB6 |
|---|---|
| `List1.AddItem Item:="a"` | ok; the list is `[a]` |
| `List1.AddItem Item:="b", Index:=0` | ok; `[b][a]` |
| `List1.AddItem Index:=F("index", 1), Item:=F("item", "c")` | ok; `[b][c][a]`. **Evaluated `item`, then `index`**: parameter order, not the order written |
| `List1.AddItem "d", Index:=0` | ok; `[d][b][c][a]` |
| `List1.RemoveItem Index:=0` | ok |
| `Combo1.AddItem Item:="x", Index:=0` | ok |
| `Me.Move Left:=F("left", 600), Top:=F("top", 700)` | ok; evaluated `left`, `top`; the form is at 600, 700 |
| `Me.Move Height:=…, Width:=…, Top:=…, Left:=…`, each through `F` | ok; **evaluated `left`, `top`, `width`, `height`**; the form is at 900, 800, 5000 × 4000 |
| `Text1.Move Left:=120, Top:=2900, Width:=1000, Height:=300` | ok |
| `Text1.Move 200, Top:=400` | ok; at 200, 400 |
| `Me.Move 300, Top:=500` | ok |
| `Text1.Move 220, 2600, 1100, Height:=360` | ok |
| `List1.ZOrder Position:=1` | ok |
| `x = Me.TextWidth(Str:="abc")` | 270, as `TextWidth("abc")` |
| `x = Picture1.ScaleX(Width:=1440, FromScale:=vbTwips, ToScale:=vbPixels)`, and the same reordered | 96 |

**An optional parameter left out before a supplied one, on `Move` and `ScaleX`:** **448** "Named argument not found", **at run time**, and nothing moves.

| Statement | VB6 |
|---|---|
| `Text1.Move 200, Height:=400` | **448** |
| `Text1.Move 200, Width:=1500` | **448** |
| `Text1.Move 210, 2500, Height:=350` | **448** |
| `Text1.Move Left:=240, Height:=380`, all named | **448** |
| `Text1.Move 230, , , 370`, **all positional** | **448** |
| `Me.Move 300, Width:=5200` and `Me.Move 310, Height:=4100` | **448** |
| `Picture1.ScaleX(1440, ToScale:=vbPixels)` | **448** |
| `Picture1.ScaleX(1440, , vbPixels)`, all positional | **448** |
| `Picture1.ScaleX(Width:=1440, ToScale:=vbPixels)` | **448** |

- **What the rows support:**
  - On these two methods, VB6 accepts only a prefix of the parameters. Leaving out the trailing ones is fine.
  - A gap before a supplied argument raises 448, whether the gap is a missing name or an empty positional slot.
- **What they don't:** the rule isn't measured on any other method, and it can't be VB6's rule for every method. `PopupMenu mnu, , x, y`, with its Flags left out, is a common idiom. Don't extend it past `Move` and `ScaleX` without measuring.

**Late-bound, through `Dim o As Object`:**

| Statement | VB6 |
|---|---|
| `Set o = List1: o.AddItem Item:="late"` | **448** "Named argument not found", and nothing is added |
| `o.AddItem Index:=0, Item:="late0"`, `o.AddItem Itemx:="bad"`, `o.AddItem Index:=0` | **448** each |
| `o.AddItem Item:="a", Item:="b"`, a repeated name | compiles; **448** at run time |
| `o.AddItem "p", Index:=0` | **448** |
| `Set o = Text1: o.Move Left:=300, Top:=2600` | **448** |
| `Set o = Me: o.Move Left:=1000, Top:=1100` | **448** |
| `Set o = c` (a Collection)`: o.Add Item:="lv", Key:="lk"` | ok |
| `o.Add Itemx:=1`, on the Collection | **448** |
| `x = o.Item(Index:="lk")`, on the Collection | ok |

- **A late-bound control or form takes no named argument at all,** not even a correct one. A late-bound Collection takes its own names.

**Collection, early-bound** (`Dim c As New Collection`):

| Statement | VB6 |
|---|---|
| `c.Add Item:="v1", Key:="k1"` | ok |
| `c.Add Key:="k0", Item:="v0", Before:=1` | ok; `c(1)` is v0 |
| `c.Add F("item", "v2"), After:=F("after", 1)` | ok; evaluated `item`, `after`; `c(2)` is v2 |
| `x = c.Item(Index:=2)` | v2 |
| `c.Remove Index:="k0"` | ok |

**Compile errors,** each in a project of its own:

| Statement | `/make` |
|---|---|
| `List1.AddItem Itemx:="x"` | "Named argument not found" |
| `List1.AddItem Item:="a", Item:="b"` | "Named argument already specified" |
| `Me.Move Top:=100`, the required Left left out | "Argument not optional" |
| `c.Add Itemx:=1`, on a Collection declared `As New Collection` | "Named argument not found" |
| `List1.AddItem Index:=0, "x"`, positional after named | "Syntax error" |
| `x = Me.TextWidth(Text:="abc")` | "Named argument not found": its parameter is `Str` |

**The parameter names these rows confirm:**
- `AddItem(Item, Index)`;
- `RemoveItem(Index)`;
- `Move(Left, Top, Width, Height)`;
- `ZOrder(Position)`;
- `TextWidth(Str)`;
- `ScaleX(Width, FromScale, ToScale)`;
- the Collection's `Add(Item, Key, Before, After)`, `Item(Index)` and `Remove(Index)`.

**Not measured:**
- the other built-in methods' names;
- whether a gap before a supplied argument raises 448 on any method but `Move` and `ScaleX`;
- `Show`, `PopupMenu` and `PaintPicture`;
- an Automation object's methods.

#### Through a Variant, and two more controls (2026-11-18, second run, #1474)

**The rig:** the same as above.

| Statement | VB6 |
|---|---|
| `Combo1.RemoveItem Index:=1`, on `[c0][c1][c2]` | ok; `[c0][c2]` |
| `Text1.ZOrder Position:=0` | ok |
| `Dim v As Variant: Set v = List1: v.AddItem Item:="viaVariant"` | **448**, and nothing is added |
| `v.AddItem "pos"`, positional | ok; `[pos]` |
| `Set v = Me: v.Move Left:=500` | **448**, and the form doesn't move |
| `Set v = c` (a Collection)`: v.Add Item:="x", Key:="k"` | ok |

- **A Variant behaves as an Object does:** a late-bound control or form takes no named argument, and a Collection takes its own.
- **`RemoveItem` on a ComboBox and `ZOrder` on a TextBox take the same names** as on a ListBox.

#### A variable declared As Control, or as the control's own class (2026-11-18, third run, #4737)

**The rig:** the same as above.

| Statement | VB6 |
|---|---|
| `Dim c As Control: Set c = List1: c.AddItem Item:="viaControl"` | **448**, and nothing is added |
| `c.AddItem "posControl"`, positional | ok |
| `Set c = Text1: c.Move Left:=300, Top:=2600` | **448**, and nothing moves |
| `Dim lb As ListBox: Set lb = List1: lb.AddItem Item:="viaListBox"` | ok: the name binds |
| `Dim tb As TextBox: Set tb = Text1: tb.Move Left:=400, Top:=2700` | ok; at 400, 2700 |
| `Dim f As Form: Set f = Me: f.Move Left:=700, Top:=800` | ok; at 700, 800 |
| `Dim f1 As Form1: Set f1 = Me: f1.Move Left:=900, Top:=1000` | ok; at 900, 1000 |

- **`As Control` is late-bound for names,** as `As Object` and a Variant are.
- **A variable declared as the control's or form's own class is early-bound,** and binds the names as the control's own name does. `As Form`, VB's generic form type, counts too.
