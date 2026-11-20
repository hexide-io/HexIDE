using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6 native and p-code measurements: https://github.com/hexide-io/HexIDE/issues/4415.
public class ParseFailureMessageTests : BaseVBTestFixture
{
    public static TheoryData<string, string, int> Rows => new()
    {
        { "x = ", "Syntax error", 3 }, { "x = = 1", "Syntax error", 3 },
        { "x = 1 2", "Syntax error", 3 }, { "x = 1 +", "Syntax error", 3 },
        { "x = (1 + 2", "Syntax error", 3 }, { "x = 1)", "Syntax error", 3 },
        { "x = Mid$(\"abc\", 1", "Syntax error", 3 },
        { "If x = 1\nx = 2\nEnd If", "Syntax error", 3 },
        { "ReDim", "Syntax error", 3 }, { "Do\nLoop Until", "Syntax error", 4 },
        { "If x 1 Then x = 2", "Syntax error", 3 }, { "For i = 1 10", "Syntax error", 3 },
        { "For i 1 To 10", "Syntax error", 3 }, { "Dim", "Syntax error", 3 },
        { "Dim y As", "Syntax error", 3 },
        { "On Error", "Syntax error", 3 }, { "GoTo", "Syntax error", 3 },
        { "Const K 5", "Syntax error", 3 },
        { "If x = 1 Then", "Block If without End If", 4 },
        { "For i = 1 To 3", "For without Next", 4 }, { "Do", "Do without Loop", 4 },
        { "Select Case x\nCase 1", "Select Case without End Select", 5 },
        { "With c", "Expected End With", 4 },
        { "Next", "Next without For", 3 }, { "Loop", "Loop without Do", 3 },
        { "End If", "End If without block If", 3 }, { "Case 1", "Case without Select Case", 3 },
        { "Else", "Else without If", 3 }, { "Wend", "Wend without While", 3 },
        { "End With", "End With without With", 3 }
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Procedure_failures_keep_the_measured_message_and_line(string body, string message, int line)
    {
        await Check("Private Function PF() As Variant\nDim x As Variant, i As Long, c As New Collection\n"
            + body + "\nEnd Function\n", message, line);
    }

    [Theory]
    [InlineData("Private Sub Foo()\nx = 1\n", "Expected End Sub", 3)]
    [InlineData("End Sub\n", "Invalid outside procedure", 1)]
    [InlineData("Private Sub Foo(\nEnd Sub\n", "Expected: identifier", 1)]
    [InlineData("Private Function G() As\nEnd Function\n", "Expected: type name", 1)]
    public async Task Module_failures_keep_the_measured_message_and_line(string code, string message, int line)
        => await Check(code, message, line);

    // The decision uses Syntax error for unmeasured inputs. These guard keyword pairing, not new VB6 measurements.
    [Theory]
    [InlineData("If True Then x = 1")]
    [InlineData("If True Then: x = 1")]
    [InlineData("10 If True Then\nx = 1\n20 End If")]
    [InlineData("Debug.Print \"If x Then\"")]
    public async Task Balanced_or_inline_blocks_do_not_replace_the_default_message(string before)
    {
        var code = "Sub Main()\n" + before + "\nx = = 1\nEnd Sub\n";
        await Check(code, "Syntax error", before.Count(c => c == '\n') + 3);
    }

    private async Task Check(string code, string message, int line)
    {
        var run = () => Run(code);
        var error = (await run.Should().ThrowAsync<VBCompileErrorException>()).Which;
        AssertMessage(error, message, line);
        var check = () => new SyntaxChecker().Run(code);
        AssertMessage(check.Should().Throw<VBCompileErrorException>().Which, message, line);
    }

    private static void AssertMessage(VBCompileErrorException error, string message, int line)
    {
        error.Line.Should().Be(line);
        var lines = error.Message.ReplaceLineEndings("\n")["Compile error:\n\n".Length..].Split('\n');
        lines[0].Should().Be(message);
        lines[1].Should().StartWith("Parser: ");
    }
}
