using System.Linq;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Under <c>On Error Resume Next</c>, a statement whose condition or header raises does not skip its block (#839). VB6 goes
/// on at the next statement, and for a block statement that is the first one inside it: the branch the condition guarded,
/// the body of a loop, the body of a With.
///
/// <para>
/// Every expectation here was measured on vb6.exe, built both as native code and as p-code; the probes and their output
/// are in docs/vb6-fidelity-oracle.md, "Resume Next into a block". Where the two builds agree, the test pins VB6. Where
/// they differ, which is only a For whose header raised and a Select Case whose selector raised, the test pins the model
/// HexIDE follows and says so in its name.
/// </para>
/// </summary>
public class ResumeNextIntoABlockTests : BaseVBTestFixture
{
    private const string Plain = "Public Tag As Long\n";

    private const string Procedures = """

        Function Boom() As Long
            Boom = 1 / 0
        End Function

        Function BoomV()
            Dim d As Double
            d = 1 / 0
        End Function

        Function BoomArr() As Long()
            Dim d As Double
            d = 1 / 0
        End Function

        Function Ask() As Long
            Debug.Print "ask"
            Ask = 1 / 0
        End Function

        Function T(ByVal label, ByVal v)
            Debug.Print label
            T = v
        End Function

        Function LimitOr(ByVal pass) As Long
            If pass = 2 Then
                LimitOr = 1 / 0
            Else
                LimitOr = 5
            End If
        End Function

        Function StepOr(ByVal pass) As Long
            If pass = 2 Then
                StepOr = 1 / 0
            Else
                StepOr = 2
            End If
        End Function

        Function StartOr(ByVal pass) As Long
            If pass = 2 Then
                StartOr = 1 / 0
            Else
                StartOr = 1
            End If
        End Function

        Sub ForInSub(ByVal pass)
            Dim i As Long, n, trail
            On Error Resume Next
            i = 3
            For i = 1 To LimitOr(pass)
                n = n + 1
                trail = trail & " " & i
                If n >= 8 Then Exit For
            Next
            Debug.Print n & ":" & trail & ":" & i & "|" & Err.Number
        End Sub

        """;

    // Declared as the probes declared them. A String `s` matters: VB6 compares a Variant "abc" with a number without
    // raising (#211), so `s > 1` raises 13 only for a String.
    private Task RunResumingNext(string body) =>
        Run("Dim c, n As Long, trail, s As String, z, x, v\ns = \"abc\"\nz = 0\nOn Error Resume Next\n" + body + "\n" + Procedures);

    private void AssertLog(string[] lines) => AssertDebugLog(lines.Select(line => new Vb6Value(line)).ToList());

    public static TheoryData<string, string, string[]> Conditions => new()
    {
        {
            "a block If enters its Then branch", """
            c = "none"
            If Boom() > 0 Then
                c = "Then"
            Else
                c = "Else"
            End If
            Debug.Print c & "|" & Err.Number
            """, ["Then|11"]
        },
        {
            "an ElseIf enters its own branch", """
            c = "none"
            If False Then
                c = "Then"
            ElseIf Boom() > 0 Then
                c = "ElseIf"
            Else
                c = "Else"
            End If
            Debug.Print c & "|" & Err.Number
            """, ["ElseIf|11"]
        },
        {
            "an ElseIf enters its own branch, and no later condition is asked", """
            c = "none"
            If False Then
                c = "Then"
            ElseIf s > 1 Then
                c = "ElseIf 1"
            ElseIf T("e2", True) Then
                c = "ElseIf 2"
            Else
                c = "Else"
            End If
            Debug.Print c & "|" & Err.Number
            """, ["ElseIf 1|13"]
        },
        {
            "a one-line If runs its Then part", """
            c = "none"
            If Boom() > 0 Then c = "Then" Else c = "Else"
            Debug.Print c & "|" & Err.Number
            """, ["Then|11"]
        },
        {
            "a one-line If runs every statement of its Then part", """
            c = "none"
            If Boom() > 0 Then c = "Then": c = c & "+second"
            Debug.Print c & "|" & Err.Number
            """, ["Then+second|11"]
        },
        {
            "a one-line If's Then part may jump", """
            c = "none"
            If Boom() > 0 Then GoTo Jumped Else c = "Else"
            c = c & "+fell"
            GoTo Done
            Jumped:
            c = "jumped"
            Done:
            Debug.Print c & "|" & Err.Number
            """, ["jumped|11"]
        },
        {
            "a comparison that raises enters the branch", """
            c = "none"
            If s > 1 Then
                c = "Then"
            Else
                c = "Else"
            End If
            Debug.Print c & "|" & Err.Number
            """, ["Then|13"]
        },
        {
            "an operator that raises enters the branch", """
            c = "none"
            If 1 \ z > 0 Then
                c = "Then"
            Else
                c = "Else"
            End If
            Debug.Print c & "|" & Err.Number
            """, ["Then|11"]
        },
        {
            "the condition is evaluated up to the raise", """
            c = "none"
            If T("a", 1) = Boom() Then
                c = "Then"
            Else
                c = "Else"
            End If
            Debug.Print c & "|" & Err.Number
            """, ["a", "Then|11"]
        },
    };

    [Theory]
    [MemberData(nameof(Conditions))]
    public async Task A_condition_that_raises_enters_its_branch(string shape, string body, string[] expected)
    {
        _ = shape;
        await RunResumingNext(body);
        AssertLog(expected);
    }

    public static TheoryData<string, string, string[]> SelectCases => new()
    {
        {
            "a Case test that raises runs its body, and no later Case is tested", """
            c = "none"
            Select Case 5
                Case Boom()
                    c = "Case Boom()"
                Case T("next", 5)
                    c = "Case 5"
                Case Else
                    c = "Case Else"
            End Select
            Debug.Print c & "|" & Err.Number
            """, ["Case Boom()|11"]
        },
        {
            "a test in a list raises, and the rest of the list is not tested", """
            c = "none"
            Select Case 5
                Case T("a", 1), Boom(), T("b", 5)
                    c = "Case 1, Boom(), 5"
                Case Else
                    c = "Case Else"
            End Select
            Debug.Print c & "|" & Err.Number
            """, ["a", "Case 1, Boom(), 5|11"]
        },
        {
            "a range test that raises", """
            c = "none"
            Select Case 5
                Case 1 To Boom()
                    c = "Case 1 To Boom()"
                Case Else
                    c = "Case Else"
            End Select
            Debug.Print c & "|" & Err.Number
            """, ["Case 1 To Boom()|11"]
        },
        {
            "an Is test that raises", """
            c = "none"
            Select Case 5
                Case Is > Boom()
                    c = "Case Is > Boom()"
                Case Else
                    c = "Case Else"
            End Select
            Debug.Print c & "|" & Err.Number
            """, ["Case Is > Boom()|11"]
        },
        {
            "a selector that raises skips the statement, with constant Cases (both builds)", """
            c = "none"
            Select Case Boom()
                Case 1
                    c = "Case 1"
                Case 0
                    c = "Case 0"
                Case Else
                    c = "Case Else"
            End Select
            Debug.Print c & "|" & Err.Number
            """, ["none|11"]
        },
        {
            "a selector that raises skips even a Case Else (both builds)", """
            c = "none"
            Select Case Boom()
                Case Else
                    c = "Case Else"
            End Select
            Debug.Print c & "|" & Err.Number
            """, ["none|11"]
        },
        {
            "a selector that raises skips the statement, with evaluated Cases (p-code; native tests them against 0)", """
            c = "none"
            Select Case Boom()
                Case T("c1", 1)
                    c = "Case 1"
                Case T("c0", 0)
                    c = "Case 0"
                Case Else
                    c = "Case Else"
            End Select
            Debug.Print c & "|" & Err.Number
            """, ["none|11"]
        },
        {
            "a string selector against a number does not raise", """
            c = "none"
            Select Case s
                Case 1
                    c = "Case 1"
                Case T("cabc", "abc")
                    c = "Case abc"
                Case Else
                    c = "Case Else"
            End Select
            Debug.Print c & "|" & Err.Number
            """, ["cabc", "Case abc|0"]
        },
    };

    [Theory]
    [MemberData(nameof(SelectCases))]
    public async Task A_Select_Case_that_raises(string shape, string body, string[] expected)
    {
        _ = shape;
        await RunResumingNext(body);
        AssertLog(expected);
    }

    public static TheoryData<string, string, string[]> Loops => new()
    {
        {
            "Do While runs its body, and asks again each time round", """
            n = 0
            Do While Ask() > 0
                n = n + 1
                If n >= 3 Then Exit Do
            Loop
            Debug.Print n & "|" & Err.Number
            """, ["ask", "ask", "ask", "3|11"]
        },
        {
            "Do Until runs its body, and asks again each time round", """
            n = 0
            Do Until Ask() > 0
                n = n + 1
                If n >= 3 Then Exit Do
            Loop
            Debug.Print n & "|" & Err.Number
            """, ["ask", "ask", "ask", "3|11"]
        },
        {
            "Loop While ends the loop", """
            n = 0
            Do
                n = n + 1
                If n >= 3 Then Exit Do
            Loop While Ask() > 0
            Debug.Print n & "|" & Err.Number
            """, ["ask", "1|11"]
        },
        {
            "Loop Until ends the loop", """
            n = 0
            Do
                n = n + 1
                If n >= 3 Then Exit Do
            Loop Until Ask() > 0
            Debug.Print n & "|" & Err.Number
            """, ["ask", "1|11"]
        },
        {
            "While runs its body, and asks again each time round", """
            n = 0
            While Ask() > 0
                n = n + 1
                If n >= 3 Then GoTo AfterWend
            Wend
            AfterWend:
            Debug.Print n & "|" & Err.Number
            """, ["ask", "ask", "ask", "3|11"]
        },
        {
            "a comparison that raises runs the body", """
            n = 0
            Do While s > n
                n = n + 1
                If n >= 3 Then Exit Do
            Loop
            Debug.Print n & "|" & Err.Number
            """, ["3|13"]
        },
    };

    [Theory]
    [MemberData(nameof(Loops))]
    public async Task A_loop_condition_that_raises(string shape, string body, string[] expected)
    {
        _ = shape;
        await RunResumingNext(body);
        AssertLog(expected);
    }

    private const string ForLoopTail = """
            n = n + 1
            trail = trail & " " & i
            If n >= 4 Then Exit For
        Next
        Debug.Print n & ":" & trail & ":" & i & "|" & Err.Number
        """;

    public static TheoryData<string, string, string[]> ForLoops => new()
    {
        // Both builds agree on these.
        { "a raising start, counter past the limit (both builds)", "Dim i As Long\ni = 7\nFor i = Boom() To 3\n" + ForLoopTail, ["1: 7:8|11"] },
        { "a raising step, counter past the limit (both builds)", "Dim i As Long\ni = 7\nFor i = 1 To 3 Step Boom()\n" + ForLoopTail, ["1: 7:7|11"] },
        { "a raising step is a Step 0, which loops for ever within the limit (both builds)", "Dim i As Long\ni = 0\nFor i = 1 To 3 Step Boom()\n" + ForLoopTail, ["4: 0 0 0 0:0|11"] },
        { "a Variant counter's Next raises 92 (both builds)", "Dim vi\nvi = 7\nFor vi = 1 To Boom()\n" + ForLoopTail.Replace(" & i", " & vi"), ["1: 7:7|92"] },
        { "an Empty Variant counter's Next raises 92 (both builds)", "Dim vi\nFor vi = 1 To Boom()\n" + ForLoopTail.Replace(" & i", " & vi"), ["1: :|92"] },

        // P-code's answers, which native code does not share.
        { "a raising limit, counter at 0 (p-code; native loops for ever)", "Dim i As Long\ni = 0\nFor i = 1 To Boom()\n" + ForLoopTail, ["1: 0:1|11"] },
        { "a raising limit, counter below 0 (p-code; native loops for ever)", "Dim i As Long\ni = -5\nFor i = 1 To Boom()\n" + ForLoopTail, ["4: -5 -4 -3 -2:-2|11"] },
        { "a raising limit, counter past it (p-code; native leaves it at 7)", "Dim i As Long\ni = 7\nFor i = 1 To Boom()\n" + ForLoopTail, ["1: 7:8|11"] },
        { "a raising start, counter at 0 (p-code; native counts on to 3)", "Dim i As Long\ni = 0\nFor i = Boom() To 3\n" + ForLoopTail, ["1: 0:1|11"] },
        { "a raising start, counting down (p-code; native counts down to 1)", "Dim i As Long\ni = 7\nFor i = Boom() To 1 Step -1\n" + ForLoopTail, ["1: 7:7|11"] },
        {
            "the start is evaluated before the limit raises, and the step after it is not (p-code; native evaluates the start last)",
            "Dim i As Long\ni = 7\nFor i = T(\"start\", 1) To Boom() Step T(\"step\", 2)\n" + ForLoopTail, ["start", "1: 7:7|11"]
        },
        {
            "the three are evaluated start, limit, step (p-code; native evaluates limit, step, start)", """
            Dim i As Long
            n = 0
            For i = T("start", 1) To T("limit", 3) Step T("step", 1)
                n = n + 1
            Next
            Debug.Print n & ":" & i & "|" & Err.Number
            """, ["start", "limit", "step", "3:4|0"]
        },
    };

    [Theory]
    [MemberData(nameof(ForLoops))]
    public async Task A_For_header_that_raises_runs_the_body_once_then_Next_carries_on(string shape, string body, string[] expected)
    {
        _ = shape;
        await RunResumingNext(body);
        AssertLog(expected);
    }

    // One For statement run twice in one call: what a raising header's Next uses is what the statement last evaluated
    // (measured in both builds).
    private const string RunTwice = """
        Dim i As Long, pass
        For pass = 1 To 2
            Err.Clear: n = 0: trail = ""
            If pass = 2 Then i = PRESET
            HEADER
                n = n + 1
                trail = trail & " " & i
                If n >= 8 Then Exit For
            Next
            Debug.Print n & ":" & trail & ":" & i & "|" & Err.Number
        Next
        """;

    [Theory]
    [InlineData("For i = 1 To LimitOr(pass)", "3", "5: 1 2 3 4 5:6|0", "3: 3 4 5:6|11")]
    [InlineData("For i = 1 To 3 Step StepOr(pass)", "0", "2: 1 3:5|0", "2: 0 2:4|11")]
    [InlineData("For i = StartOr(pass) To 5", "3", "5: 1 2 3 4 5:6|0", "3: 3 4 5:6|11")]
    public async Task A_raising_header_carries_on_with_the_limit_and_step_this_statement_last_had(
        string header, string preset, string firstRun, string secondRun)
    {
        await RunResumingNext(RunTwice.Replace("HEADER", header).Replace("PRESET", preset));
        AssertLog([firstRun, secondRun]);
    }

    [Fact]
    public async Task Each_call_starts_with_no_loop_state_p_code()
    {
        // P-code's answer for the second call; native code, which keeps a step of 0 there, leaves i at 3.
        await RunResumingNext("ForInSub 1\nForInSub 2\n");
        AssertLog(["5: 1 2 3 4 5:6|0", "1: 3:4|11"]);
    }

    private const string ForEachTail = """
            n = n + 1
            trail = trail & " [" & x & "]"
            If n >= 4 Then Exit For
        Next
        Debug.Print n & ":" & trail & ":" & x & "|" & Err.Number
        """;

    public static TheoryData<string, string, string[]> ForEachLoops => new()
    {
        { "an unallocated array, which leaves the element alone", "Dim dyn() As Long\nx = \"pre\"\nFor Each x In dyn\n" + ForEachTail, ["1: [pre]:pre|92"] },
        { "a Variant holding a number, which empties the element", "v = 5\nx = \"pre\"\nFor Each x In v\n" + ForEachTail, ["1: [pre]:|92"] },
        { "an Empty Variant", "x = \"pre\"\nFor Each x In v\n" + ForEachTail, ["1: [pre]:|92"] },
        { "Null", "v = Null\nx = \"pre\"\nFor Each x In v\n" + ForEachTail, ["1: [pre]:|92"] },
        { "Nothing", "Set v = Nothing\nx = \"pre\"\nFor Each x In v\n" + ForEachTail, ["1: [pre]:|92"] },
        { "a Variant Function that raises, which empties the element", "x = \"pre\"\nFor Each x In BoomV()\n" + ForEachTail, ["1: [pre]:|92"] },
        { "a Function declared as an array that raises, which leaves the element alone", "x = \"pre\"\nFor Each x In BoomArr()\n" + ForEachTail, ["1: [pre]:pre|92"] },
    };

    [Theory]
    [MemberData(nameof(ForEachLoops))]
    public async Task A_For_Each_collection_that_cannot_be_walked_runs_the_body_once_then_Next_raises_92(
        string shape, string body, string[] expected)
    {
        _ = shape;
        await RunResumingNext(body);
        AssertLog(expected);
    }

    [Theory]
    [InlineData("Dim dyn() As Long\nFor Each x In dyn\nNext\n", 92)]
    [InlineData("Set v = Nothing\nFor Each x In v\nNext\n", 424)]
    [InlineData("v = 5\nFor Each x In v\nNext\n", 13)]
    [InlineData("v = Null\nFor Each x In v\nNext\n", 13)]
    public async Task A_For_Each_collection_that_cannot_be_walked_raises_without_a_handler(string body, int number)
    {
        var run = () => Run("Dim x, v\n" + body);

        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(number);
    }

    [Fact]
    public async Task A_raising_For_Each_collection_whose_declared_type_cannot_be_known_is_refused()
    {
        // VB6 empties the element afterwards unless the collection is declared as an array, which only a direct call of
        // one of the project's own Functions lets HexIDE see.
        var run = () => RunResumingNext("For Each x In (BoomV())\nNext\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#969*");
    }

    [Fact]
    public async Task A_For_Each_over_an_object_is_refused_rather_than_stepped_over()
    {
        var run = () => RunClasses("Dim o, x\nOn Error Resume Next\nSet o = New Plain\nFor Each x In o\nNext\n", ("Plain", Plain));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*only arrays*");
    }

    [Fact]
    public async Task A_With_target_that_raises_runs_the_body_with_no_object()
    {
        await RunClasses("""
            Dim c
            On Error Resume Next
            With BoomObj()
                c = "body, err " & Err.Number
                .Tag = 5
                c = c & ", then the member, err " & Err.Number
            End With
            Debug.Print c & "|" & Err.Number

            Function BoomObj() As Plain
                Dim d As Double
                d = 1 / 0
            End Function
            """, ("Plain", Plain));

        AssertLog(["body, err 11, then the member, err 91|91"]);
    }

    [Fact]
    public async Task A_With_over_Nothing_runs_its_body_and_each_member_raises_91()
    {
        await RunClasses("""
            Dim np As Plain, c
            On Error Resume Next
            With np
                c = "body"
                .Tag = 5
                c = c & ", member done"
            End With
            Debug.Print c & "|" & Err.Number
            """, ("Plain", Plain));

        AssertLog(["body, member done|91"]);
    }

    [Theory]
    [InlineData("For i = 1 To 2 Step 0.5")]
    [InlineData("For i = 1 To 2.5 Step Boom()")]   // the limit is kept, then the raised step sends it to Next
    public async Task A_fractional_For_is_refused_rather_than_stepped_over(string header)
    {
        var run = () => RunResumingNext($"Dim i As Long\n{header}\nNext\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*fractional*");
    }

    [Fact]
    public async Task A_refusal_inside_a_condition_is_not_stepped_over()
    {
        // Resume Next traps VB6's errors, not HexIDE's refusals: an untrappable refusal raised while a condition is
        // evaluated must still end the run.
        var run = () => RunResumingNext("If Frac() Then c = \"Then\"\nFunction Frac()\n    Dim i\n    For i = 1 To 2 Step 0.5\n    Next\nEnd Function\n");

        await run.Should().ThrowAsync<NotImplementedException>();
    }

    // A condition is read as CBool reads it, except that Null and Empty are False (measured).
    [Theory]
    [InlineData("5", "Then|Then|2")]
    [InlineData("0", "Else|Else|0")]
    [InlineData("0.5", "Then|Then|2")]
    [InlineData("\"True\"", "Then|Then|2")]
    [InlineData("\"1\"", "Then|Then|2")]
    [InlineData("Null", "Else|Else|0")]
    [InlineData("Empty", "Else|Else|0")]
    [InlineData("#1/1/2000#", "Then|Then|2")]
    [InlineData("-1@", "Then|Then|2")]
    public async Task A_condition_holds_by_VB6s_rule(string literal, string expected)
    {
        await Run($"""
            Dim v, c, d, n
            v = {literal}
            n = 0
            If v Then
                c = "Then"
            Else
                c = "Else"
            End If
            If v Then d = "Then" Else d = "Else"
            Do While v
                n = n + 1
                If n >= 2 Then Exit Do
            Loop
            Debug.Print c & "|" & d & "|" & n
            """);

        AssertLog([expected]);
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("\"\"")]
    public async Task A_condition_that_is_not_a_number_or_True_or_False_raises_13(string literal)
    {
        var run = () => Run($"Dim v, c\nv = {literal}\nIf v Then c = 1\n");

        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(13);
    }
}
