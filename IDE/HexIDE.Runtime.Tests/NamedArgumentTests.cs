using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Named arguments, `Foo c:=5, a:=1` (#1465), which stopped the program with "Assign is not implemented". Each transcript is
/// what <c>vb6.exe</c> logged for the same program under en-GB, native and p-code agreeing (the oracle's "Named arguments").
/// The answer people do not expect is the order: named arguments are evaluated in parameter order, not the order written.
/// </summary>
public class NamedArgumentTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    private const string Procedures = """
        Private Function F(ByVal tag As String, ByVal v As Variant) As Variant
            Debug.Print "  evaluated " & tag
            F = v
        End Function
        Private Sub Foo(a As Integer, Optional b As Integer = 2, Optional c)
            If IsMissing(c) Then
                Debug.Print "  a=" & a & " b=" & b & " c missing"
            Else
                Debug.Print "  a=" & a & " b=" & b & " c=" & c
            End If
        End Sub
        Private Function G(a As Long, b As Long) As Long
            G = a - b
        End Function
        Private Sub Inc(n As Long)
            n = n + 1
        End Sub

        """;

    [Fact]
    public async Task A_named_argument_goes_to_its_parameter_and_is_evaluated_in_parameter_order()
    {
        await Run(Procedures + """
            Dim x As Variant, v As Long
            Debug.Print "Foo 1, c:=5": Foo 1, c:=5
            Debug.Print "Foo a:=1": Foo a:=1
            Debug.Print "Foo c:=3, a:=1": Foo c:=3, a:=1
            Debug.Print "Foo 1, b:=4": Foo 1, b:=4
            Debug.Print "Foo B:=7, A:=8": Foo B:=7, A:=8
            Debug.Print "Foo 1, , c:=9": Foo 1, , c:=9
            Debug.Print "Call Foo(c:=1, a:=2)": Call Foo(c:=1, a:=2)
            Debug.Print "Foo c:=F(c), a:=F(a)": Foo c:=F("c", 3), a:=F("a", 1)
            Debug.Print "Foo F(a), c:=F(c), b:=F(b)": Foo F("a", 1), c:=F("c", 3), b:=F("b", 2)
            x = G(b:=2, a:=10): Debug.Print "G(b:=2, a:=10) = " & x
            Debug.Print "in an expression, G = " & G(b:=2, a:=10)
            x = G(b:=F("b", 2), a:=F("a", 10)): Debug.Print "G(b:=F(b), a:=F(a)) = " & x
            v = 4: Inc n:=v: Debug.Print "Inc n:=v, v was 4: " & v

            """);

        AssertDebugLog(Lines("""
            Foo 1, c:=5
              a=1 b=2 c=5
            Foo a:=1
              a=1 b=2 c missing
            Foo c:=3, a:=1
              a=1 b=2 c=3
            Foo 1, b:=4
              a=1 b=4 c missing
            Foo B:=7, A:=8
              a=8 b=7 c missing
            Foo 1, , c:=9
              a=1 b=2 c=9
            Call Foo(c:=1, a:=2)
              a=2 b=2 c=1
            Foo c:=F(c), a:=F(a)
              evaluated a
              evaluated c
              a=1 b=2 c=3
            Foo F(a), c:=F(c), b:=F(b)
              evaluated a
              evaluated b
              evaluated c
              a=1 b=2 c=3
            G(b:=2, a:=10) = 8
            in an expression, G = 8
              evaluated a
              evaluated b
            G(b:=F(b), a:=F(a)) = 8
            Inc n:=v, v was 4: 5
            """));
    }

    [Fact]
    public async Task A_method_takes_named_arguments_too()
    {
        await RunClasses("""
            Private Function F(ByVal tag As String, ByVal v As Variant) As Variant
                Debug.Print "  evaluated " & tag
                F = v
            End Function
            Dim k As Class1, x As Variant
            Set k = New Class1
            Debug.Print "k.M y:=1, x:=2": k.M y:=1, x:=2
            Debug.Print "k.M y:=F(y), x:=F(x)": k.M y:=F("y", 1), x:=F("x", 2)
            x = k.Twice(n:=21): Debug.Print "k.Twice(n:=21) = " & x
            Debug.Print "in an expression, k.Twice(n:=21) = " & k.Twice(n:=21)

            """, ("Class1", """
            Public Sub M(x As Integer, Optional y As Integer = 5)
                Debug.Print "  M x=" & x & " y=" & y
            End Sub
            Public Function Twice(Optional ByVal n As Long = 1) As Long
                Twice = n * 2
            End Function

            """));

        AssertDebugLog(Lines("""
            k.M y:=1, x:=2
              M x=2 y=1
            k.M y:=F(y), x:=F(x)
              evaluated x
              evaluated y
              M x=2 y=1
            k.Twice(n:=21) = 42
            in an expression, k.Twice(n:=21) = 42
            """));
    }

    [Fact]
    public async Task An_intrinsic_takes_its_own_parameter_names_and_evaluates_them_in_parameter_order()
    {
        await Run("""
            Private Function F(ByVal tag As String, ByVal v As Variant) As Variant
                Debug.Print "  evaluated " & tag
                F = v
            End Function
            Dim x As Variant
            x = Mid(String:="abcdef", Start:=2, Length:=3): Debug.Print "Mid(String, Start, Length) = " & x
            x = Mid(Start:=2, String:="abcdef"): Debug.Print "Mid(Start, String) = " & x
            Debug.Print "in an expression, Mid = " & Mid(String:="abcdef", Start:=2, Length:=3)
            x = Left(String:="abc", Length:=2): Debug.Print "Left = " & x
            x = Replace(Expression:="aXbX", Find:="X", Replace:="-"): Debug.Print "Replace = " & x
            x = Format(Expression:=1.5, Format:="0.00"): Debug.Print "Format = " & x
            x = DateSerial(Year:=2003, Month:=2, Day:=1): Debug.Print "DateSerial = " & Format(x, "yyyy-mm-dd")
            x = DateAdd(Interval:="d", Number:=1, Date:=#2/1/2003#): Debug.Print "DateAdd = " & Format(x, "yyyy-mm-dd")
            x = UCase(String:="abc"): Debug.Print "UCase = " & x
            x = Trim(String:=" a "): Debug.Print "Trim = [" & x & "]"
            x = Chr(CharCode:=65): Debug.Print "Chr = " & x
            x = String(Number:=3, Character:="z"): Debug.Print "String = " & x
            x = Space(Number:=2): Debug.Print "Space = [" & x & "]"
            x = InStrRev(StringCheck:="abcabc", StringMatch:="b"): Debug.Print "InStrRev = " & x
            Debug.Print "Mid(Start:=F, String:=F)"
            x = Mid(Start:=F("start", 2), String:=F("string", "abcdef")): Debug.Print "  = " & x
            Debug.Print "Replace(Replace:=F, Find:=F, Expression:=F)"
            x = Replace(Replace:=F("replace", "-"), Find:=F("find", "X"), Expression:=F("expression", "aXb")): Debug.Print "  = " & x
            Debug.Print "Mid(F, Length:=F, Start:=F)"
            x = Mid(F("string", "abcdef"), Length:=F("length", 2), Start:=F("start", 3)): Debug.Print "  = " & x
            x = IIf(Expression:=True, TruePart:=1, FalsePart:=2): Debug.Print "IIf = " & x
            Debug.Print "IIf(FalsePart:=F, TruePart:=F, Expression:=F)"
            x = IIf(FalsePart:=F("false", 2), TruePart:=F("true", 1), Expression:=F("expression", False)): Debug.Print "  = " & x

            """);

        AssertDebugLog(Lines("""
            Mid(String, Start, Length) = bcd
            Mid(Start, String) = bcdef
            in an expression, Mid = bcd
            Left = ab
            Replace = a-b-
            Format = 1.50
            DateSerial = 2003-02-01
            DateAdd = 2003-02-02
            UCase = ABC
            Trim = [a]
            Chr = A
            String = zzz
            Space = [  ]
            InStrRev = 5
            Mid(Start:=F, String:=F)
              evaluated string
              evaluated start
              = bcdef
            Replace(Replace:=F, Find:=F, Expression:=F)
              evaluated expression
              evaluated find
              evaluated replace
              = a-b
            Mid(F, Length:=F, Start:=F)
              evaluated string
              evaluated start
              evaluated length
              = cd
            IIf = 1
            IIf(FalsePart:=F, TruePart:=F, Expression:=F)
              evaluated expression
              evaluated true
              evaluated false
              = 2
            """));
    }

    // vb6.exe compiled each of these, with every name in it, but they are modal, so it did not run them.
    [Fact]
    public async Task MsgBox_and_InputBox_take_their_names()
    {
        await Run("""
            Dim x As Variant
            MsgBox Title:="t", Prompt:="p", Buttons:=vbOKCancel
            x = MsgBox(Prompt:="q", Title:="u", HelpFile:="h", Context:=1)
            x = InputBox(Default:="d", Prompt:="p", Title:="t")
            x = InputBox(Prompt:="p", Title:="t", Default:="d", XPos:=1, YPos:=1, HelpFile:="h", Context:=1)

            """);

        dialogs.Should().Equal(
            "MsgBox p | t | OkCancel",
            "MsgBox q | u | Ok",
            "InputBox p | t | d",
            "InputBox p | t | d");
    }

    [Fact]
    public async Task An_event_takes_no_named_arguments()
    {
        var run = () => RunClasses("""
            Dim s As Sink
            Set s = New Sink
            s.Go

            """, ("Source", """
            Public Event E(a As Integer, b As Integer)
            Public Sub Fire()
                RaiseEvent E(b:=1, a:=2)
            End Sub

            """), ("Sink", """
            Private WithEvents k As Source
            Public Sub Go()
                Set k = New Source
                k.Fire
            End Sub
            Private Sub k_E(x As Integer, y As Integer)
                Debug.Print "k_E x=" & x & " y=" & y
            End Sub

            """));

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Syntax error*");
    }

    [Theory]
    [InlineData("Foo d:=1", "*Named argument not found*")]
    [InlineData("Foo a:=1, a:=2", "*Named argument already specified*")]
    [InlineData("Foo 1, a:=2", "*Named argument already specified*")]
    [InlineData("Foo 1, , b:=4", "*Named argument already specified*")]
    [InlineData("Foo b:=3", "*Argument not optional*")]
    [InlineData("Foo a:=1, 2", "*Syntax error*")]
    [InlineData("Foo a:=1, , c:=3", "*Syntax error*")]
    [InlineData("Foo 1, 2, 3, 4, c:=5", "*Wrong number of arguments or invalid property assignment*")]
    [InlineData("x = Mid(\"abc\", 1, 1, 1, Start:=1)", "*Wrong number of arguments or invalid property assignment*")]
    [InlineData("Pa 1, x:=2", "*Argument in ParamArray may not be named*")]
    [InlineData("x = Choose(Index:=1, Choice:=\"a\")", "*Argument in ParamArray may not be named*")]
    [InlineData("x = Array(ArgList:=1)", "*Argument in ParamArray may not be named*")]
    [InlineData("x = Left(Str:=\"abc\", Length:=2)", "*Named argument not found*")]
    [InlineData("MsgBox Message:=\"x\"", "*Named argument not found*")]
    [InlineData("x = Mid(String:=\"abc\", Length:=2)", "*Argument not optional*")]
    [InlineData("x = String(Number:=3)", "*Syntax error*")]
    [InlineData("x = Len(Expression:=\"abc\")", "*Syntax error*")]
    [InlineData("x = CStr(Expression:=5)", "*Syntax error*")]
    [InlineData("x = InStr(String1:=\"abc\", String2:=\"c\")", "*Syntax error*")]
    [InlineData("x = Abs(Number:=-3)", "*Syntax error*")]
    [InlineData("x = UBound(ArrayName:=arr)", "*Syntax error*")]
    [InlineData("Bar a:=1", "*Sub or Function not defined*")]
    [InlineData("x = Baz(a:=1)", "*Sub or Function not defined*")]
    public async Task What_VB6_refuses_to_compile_is_refused(string statement, string message)
    {
        var run = () => Run(Procedures + """
            Private Sub Pa(a As Integer, ParamArray x())
            End Sub
            Dim x As Variant, arr(3)

            """ + statement + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }

    [Fact]
    public async Task Val_Round_Split_and_the_rest_take_their_names()
    {
        await Run("""
            Dim p As Variant, arr(2) As String
            arr(0) = "ab": arr(1) = "cd": arr(2) = "AE"
            Debug.Print "Val " & Val(String:="12")
            Debug.Print "StrConv " & StrConv(String:="a", Conversion:=1, LocaleID:=0) & " " & StrConv("b", Conversion:=1)
            Debug.Print "Error " & Error(ErrorNumber:=6) & " " & Error$(ErrorNumber:=11)
            Debug.Print "Round " & Round(Number:=2.567, NumDigitsAfterDecimal:=1) & " " & Round(Number:=2.5)
            p = Split(Expression:="a b c"): Debug.Print "Split " & UBound(p) & " " & p(1)
            p = Split(Expression:="a,b,c", Delimiter:=",", Limit:=2, Compare:=vbBinaryCompare): Debug.Print "Split " & UBound(p) & " " & p(1)
            Debug.Print "Join " & Join(SourceArray:=arr, Delimiter:="-") & " / " & Join(SourceArray:=arr)
            p = Filter(SourceArray:=arr, Match:="a", Include:=True, Compare:=vbTextCompare): Debug.Print "Filter " & UBound(p) & " " & p(1)
            p = Filter(SourceArray:=arr, Match:="a"): Debug.Print "Filter " & UBound(p) & " " & p(0)
            Debug.Print "MonthName " & MonthName(Month:=2, Abbreviate:=True) & " " & MonthName(Month:=2) & " " & MonthName(Abbreviate:=True, Month:=3)
            Debug.Print "WeekdayName " & WeekdayName(Weekday:=1, Abbreviate:=True, FirstDayOfWeek:=vbMonday) & " " & WeekdayName(Weekday:=2, FirstDayOfWeek:=vbSunday)
            Debug.Print "DateValue " & CDbl(DateValue(Date:="2000-01-02")) & ", TimeValue " & CDbl(TimeValue(Time:="12:00"))
            Debug.Print "CDec " & CDec(Expression:=1.5) & ", CVDate " & CDbl(CVDate(Expression:=2))
            Debug.Print "Hex " & Hex(Number:=255) & ", Oct " & Oct(Number:=8) & ", Str [" & Str(Number:=5) & "]"
            Debug.Print "Hex$ " & Hex$(Number:=255) & ", Str$ [" & Str$(Number:=5) & "]"
            Debug.Print "FreeFile " & FreeFile(RangeNumber:=1)

            """);

        AssertDebugLog(Lines("""
            Val 12
            StrConv A B
            Error Overflow Division by zero
            Round 2.6 2
            Split 2 b
            Split 1 b,c
            Join ab-cd-AE / ab cd AE
            Filter 1 AE
            Filter 0 ab
            MonthName Feb February Mar
            WeekdayName Mon Monday
            DateValue 36527, TimeValue 0.5
            CDec 1.5, CVDate 2
            Hex FF, Oct 10, Str [ 5]
            Hex$ FF, Str$ [ 5]
            FreeFile 256
            """));
    }

    // Measured for #1475, each in a project of its own.
    [Theory]
    [InlineData("x = Round(NumDigitsAfterDecimal:=1)", "*Argument not optional*")]
    [InlineData("x = Split(Delimiter:=\",\")", "*Argument not optional*")]
    [InlineData("x = Join(Delimiter:=\"-\")", "*Argument not optional*")]
    [InlineData("x = Filter(Match:=\"a\")", "*Argument not optional*")]
    [InlineData("x = Filter(SourceArray:=x)", "*Argument not optional*")]
    [InlineData("x = MonthName(Abbreviate:=True)", "*Argument not optional*")]
    [InlineData("x = WeekdayName(Abbreviate:=True)", "*Argument not optional*")]
    [InlineData("x = Val(Foo:=\"12\")", "*Named argument not found*")]
    [InlineData("x = Round(Number:=2.5, Digits:=1)", "*Named argument not found*")]
    [InlineData("x = Hex(Foo:=255)", "*Named argument not found*")]
    [InlineData("x = CDec(Foo:=1.5)", "*Named argument not found*")]
    [InlineData("x = EOF(Foo:=1)", "*Named argument not found*")]
    [InlineData("x = StrConv(String:=\"a\", Conversion:=1, LCID:=0)", "*Named argument not found*")]
    [InlineData("x = StrConv(Conversion:=1)", "*Argument not optional*")]
    [InlineData("x = StrConv(String:=\"a\")", "*Argument not optional*")]
    [InlineData("x = Error(Number:=6)", "*Named argument not found*")]
    [InlineData("x = Switch(VarExpr:=True)", "*Argument in ParamArray may not be named*")]
    [InlineData("x = Now(X:=1)", "*Named arguments not allowed*")]
    [InlineData("x = Time(X:=1)", "*Named arguments not allowed*")]
    [InlineData("x = Command(X:=1)", "*Named arguments not allowed*")]
    [InlineData("x = Date(X:=1)", "*Syntax error*")]
    [InlineData("x = Timer(X:=1)", "*Wrong number of arguments or invalid property assignment*")]
    [InlineData("x = DoEvents(X:=1)", "*Wrong number of arguments or invalid property assignment*")]
    public async Task What_VB6_refuses_of_those_is_refused(string statement, string message)
    {
        var run = () => Run("""
            Dim x As Variant

            """ + statement + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }
}
