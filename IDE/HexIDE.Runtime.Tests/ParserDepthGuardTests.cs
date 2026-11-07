using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>Deep parser input must be refused with a catchable error, leaving the process alive.
/// Reference-depth acceptance is covered by SizedParsingTests (#3210).</summary>
public class ParserDepthGuardTests : BaseVBTestFixture
{
    [Fact]
    public async Task DeeplyNestedParens_RaiseCompileError_NotStackOverflow()
    {
        var code = "Dim x\nx = " + new string('(', 10000) + "1" + new string(')', 10000) + "\n";
        var act = () => Run(code);
        await act.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task DeeplyNestedBlocks_RaiseCompileError_NotStackOverflow()
    {
        // Deep statement-block nesting is a DISTINCT parser recursion path from parenthesised expressions — the
        // per-rule guard covers it too.
        var sb = new StringBuilder();
        for (int i = 0; i < 8000; i++) sb.Append("If True Then\n");
        sb.Append("Dim x\n");
        for (int i = 0; i < 8000; i++) sb.Append("End If\n");
        var act = () => Run(sb.ToString());
        await act.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task ModeratelyNestedCode_RunsNormally()
    {
        // A realistically nested expression (well under the guard) must be unaffected — the guard must not
        // false-positive on ordinary code.
        await Run(
            "Dim r\n" +
            "r = ((1 + 2) * (3 - (4 + 1)))\n" +
            "Debug.Print r\n");
        AssertDebugLog([new Vb6Value(-6)]);   // (3) * (3 - 5) = 3 * -2 = -6
    }
}
