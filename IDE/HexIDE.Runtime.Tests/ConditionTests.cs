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

    [Fact]
    public async Task A_condition_of_Err_waits_for_Err_to_be_its_Number()
    {
        // VB6 reads `If Err Then` as `If Err.Number Then` (measured: Then after an error, Else after Clear). Err used as
        // a value is #840's, which makes it its Number; until then CBool of it raises 13. Whichever of the two merges
        // second turns this into the measured answer.
        var run = () => Run("Err.Number = 11\nIf Err Then Debug.Print 1\n");

        await run.Should().ThrowAsync<VBRunTimeException>();
    }
}
