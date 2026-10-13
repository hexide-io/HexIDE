using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// What a condition is, in every form that has one (#847). Measured on vb6.exe, native and in the IDE, which agreed on
/// every row: a condition is read as <c>CBool</c> reads it, except that Null is False rather than error 94.
/// HexIDE refused anything but a Boolean in a block <c>If</c>, with an error whose Number was 0, so under
/// <c>On Error Resume Next</c> <c>If Len(s) Then</c> skipped its body and said nothing.
/// </summary>
public class ConditionTests : BaseVBTestFixture
{
    // The probe's own function: each form tests `v`, and the loops count their passes up to a limit of their own.
    private const string Probe = """
        Function P(ByVal form As Integer, v As Variant) As String
            Dim n As Integer, c As String
            On Error GoTo Fail
            Select Case form
            Case 1
                If v Then c = "T" Else c = "E"
            Case 2
                If v Then
                    c = "T"
                Else
                    c = "E"
                End If
            Case 3
                If False Then
                    c = "X"
                ElseIf v Then
                    c = "T"
                Else
                    c = "E"
                End If
            Case 4
                While v
                    n = n + 1
                    If n >= 2 Then GoTo Done
                Wend
            Case 5
                Do While v
                    n = n + 1
                    If n >= 2 Then Exit Do
                Loop
            Case 6
                Do Until v
                    n = n + 1
                    If n >= 2 Then Exit Do
                Loop
            Case 7
                Do
                    n = n + 1
                    If n >= 3 Then Exit Do
                Loop While v
            Case 8
                Do
                    n = n + 1
                    If n >= 3 Then Exit Do
                Loop Until v
            End Select
        Done:
            P = c & n
            Exit Function
        Fail:
            P = "err " & Err.Number
        End Function

        """;

    // Columns: one-line If, block If, ElseIf, While, Do While, Do Until, Loop While, Loop Until. CVErr(5), measured as
    // 13 in every form, is left out: HexIDE has no CVErr yet.
    [Theory]
    [InlineData("5", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("0", "E0|E0|E0|0|0|2|1|3")]
    [InlineData("0.5", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("-1@", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("#1/1/2000#", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("CLng(11)", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("CByte(0)", "E0|E0|E0|0|0|2|1|3")]
    [InlineData("\"True\"", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("\"False\"", "E0|E0|E0|0|0|2|1|3")]
    [InlineData("\"1\"", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("\"0\"", "E0|E0|E0|0|0|2|1|3")]
    [InlineData("\" 5 \"", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("\"&H10\"", "T0|T0|T0|2|2|0|3|1")]
    [InlineData("\"abc\"", "err 13|err 13|err 13|err 13|err 13|err 13|err 13|err 13")]
    [InlineData("\"\"", "err 13|err 13|err 13|err 13|err 13|err 13|err 13|err 13")]
    [InlineData("Null", "E0|E0|E0|0|0|2|1|3")]
    [InlineData("Empty", "E0|E0|E0|0|0|2|1|3")]
    public async Task Every_form_reads_its_condition_as_CBool_with_Null_and_Empty_False(string value, string expected)
    {
        await Run($"Dim k As Integer, s As String\nFor k = 1 To 8\n    s = s & \"|\" & P(k, {value})\nNext\nDebug.Print Mid(s, 2)\n" + Probe);

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Dim n As Long\nn = 11", "n", "T")]
    [InlineData("Dim i As Integer\ni = 0", "i", "E")]
    [InlineData("Dim d As Double\nd = 0.5", "d", "T")]
    [InlineData("Dim s As String\ns = \"-1\"", "s", "T")]
    [InlineData("Dim s As String\ns = \"abc\"", "Len(s)", "T")]
    [InlineData("Dim s As String\ns = \"\"", "Len(s)", "E")]
    [InlineData("", "InStr(\"abc\", \"b\")", "T")]
    [InlineData("", "InStr(\"abc\", \"x\")", "E")]
    [InlineData("Dim x As Long\nx = 6", "x And 4", "T")]
    [InlineData("Dim x As Long\nx = 6", "x And 1", "E")]
    [InlineData("Err.Number = 11", "Err.Number", "T")]
    [InlineData("", "Null", "E")]
    [InlineData("Dim w\nw = CLng(0)", "w", "E")]
    public async Task The_idioms_hold_as_in_VB6(string setup, string condition, string expected)
    {
        await Run($"""
            {setup}
            Dim a, b
            If {condition} Then a = "T" Else a = "E"
            If {condition} Then
                b = "T"
            Else
                b = "E"
            End If
            Debug.Print a & b
            """);

        AssertDebugLog([new Vb6Value(expected + expected)]);
    }

    [Fact]
    public async Task A_typed_String_that_is_not_a_number_raises_13()
    {
        var run = () => Run("Dim s As String\ns = \"abc\"\nIf s Then Debug.Print 1\n");

        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(13);
    }

    [Fact]
    public async Task Under_Resume_Next_a_block_If_with_a_Long_condition_runs_its_body()
    {
        // It used to skip the body with Err.Number 0: the refusal raised an error with no number.
        await Run("On Error Resume Next\nDim n As Long\nn = 11\nIf n Then\n    Debug.Print \"T\"\nEnd If\nDebug.Print Err.Number\n");

        AssertDebugLog([new Vb6Value("T"), new Vb6Value(0L)]);
    }

    [Theory]
    [InlineData("If Err Then a = \"T\" Else a = \"E\"")]
    [InlineData("If Err Then\na = \"T\"\nElse\na = \"E\"\nEnd If")]
    [InlineData("If False Then\na = \"F\"\nElseIf Err Then\na = \"T\"\nElse\na = \"E\"\nEnd If")]
    public async Task A_condition_of_Err_reads_its_Number(string form)
    {
        // Measured, native and in the IDE: Then after an error, Else after Err.Clear. It was a trappable 13, which
        // overwrote Err.Number and ran neither branch.
        await Run($"On Error Resume Next\nDim a, r\nErr.Raise 53\n{form}\nr = a\nErr.Clear\n{form}\nDebug.Print r & a\n");

        AssertDebugLog([new Vb6Value("TE")]);
    }

    [Theory]
    [InlineData("Err.Raise 53\nWhile Err\nErr.Clear\nc = c + 1\nIf c > 5 Then Exit Sub\nWend", 0L)]
    [InlineData("Err.Raise 53\nDo While Err\nErr.Clear\nc = c + 1\nIf c > 5 Then Exit Do\nLoop", 0L)]
    [InlineData("Err.Raise 53\nDo Until Err = 0\nErr.Clear\nc = c + 1\nIf c > 5 Then Exit Do\nLoop", 0L)]
    [InlineData("Do Until Err\nc = c + 1\nErr.Raise 53\nIf c > 5 Then Exit Do\nLoop", 53L)]
    [InlineData("Do\nc = c + 1\nIf c > 5 Then Exit Do\nLoop While Err", 0L)]
    public async Task A_loop_on_Err_reads_its_Number(string form, long error)
    {
        // Measured, native and in the IDE: one pass each. The While row needed #882, which parsed `Err.Clear` in its body
        // as two statements. While has no Exit, so its guard leaves the Sub, which skips the prints and fails the test.
        await Run($"Probe\nSub Probe()\nOn Error Resume Next\nDim c\nc = 0\n{form}\nDebug.Print c\nDebug.Print Err.Number\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(1), new Vb6Value(error)]);
    }

    private const string Plain = "Public Tag As Long\n";
    private const string Widget = "Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\n    Value = 42\nEnd Function\n";

    public static TheoryData<string> EveryForm => new()
    {
        "If v Then c = 1",
        "If v Then\nc = 1\nEnd If",
        "If False Then\nc = 0\nElseIf v Then\nc = 1\nEnd If",
        "While v\nc = 1\nWend",
        "Do While v\nc = 1\nLoop",
        "Do Until v\nc = 1\nLoop",
        "Do\nc = 1\nLoop While v",
        "Do\nc = 1\nLoop Until v",
    };

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task An_object_with_no_default_member_raises_438(string form)
    {
        // Measured, native and in the IDE, for If and Do While. It was a trappable 13.
        var run = () => RunClasses($"Dim v, c\nSet v = New Plain\n{form}\n", ("Plain", Plain));

        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(438);
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task Nothing_raises_91(string form)
    {
        // Measured, native and in the IDE, for a Variant set to Nothing and an Object variable never set. It was 13.
        var run = () => Run($"Dim v, c\nSet v = Nothing\n{form}\n");

        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(91);
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task A_class_with_a_default_member_is_refused(string form)
    {
        // VB6 reads the member: `If w Then` holds and `Do While w` loops for a member returning 42 (measured, native and
        // in the IDE). HexIDE does not call it yet (#174), so it refuses, untrappably.
        var run = () => RunClasses($"Dim v, c\nSet v = New Widget\nOn Error Resume Next\n{form}\n", ("Widget", Widget));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
    }

    [Theory]
    [InlineData("If v Then c = 1", 3)]
    [InlineData("If v Then\nc = 1\nEnd If", 3)]
    [InlineData("If False Then\nc = 0\nElseIf v Then\nc = 1\nEnd If", 5)]   // the ElseIf clause, not the whole If
    [InlineData("While v\nc = 1\nWend", 3)]
    [InlineData("Do While v\nc = 1\nLoop", 3)]
    [InlineData("Do Until v\nc = 1\nLoop", 3)]
    [InlineData("Do\nc = 1\nLoop While v", 3)]   // the whole loop, from its Do: main's granularity for a statement
    [InlineData("Do\nc = 1\nLoop Until v", 3)]
    public async Task An_error_in_a_condition_names_its_statement(string form, int line)
    {
        // CBool has no statement of its own, so the condition's is attached, for the run-time error dialog and
        // get_last_runtime_error. The old Boolean-only checks raised with it.
        var run = () => Run($"Dim v, c\nv = \"abc\"\n{form}\n");

        var thrown = (await run.Should().ThrowAsync<VBRunTimeException>()).Which;
        thrown.Error.ErrNo.Should().Be(13);
        thrown.Context.Should().NotBeNull();
        thrown.Context!.Start.Line.Should().Be(line);
    }
}
