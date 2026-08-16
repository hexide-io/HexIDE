using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression for the adversarial bug-hunt HIGH: a degenerate deeply-nested input overflowed the recursive-descent
/// parser's C# stack — an UNCATCHABLE <see cref="System.StackOverflowException"/> that killed the whole process.
/// The <c>ParseDepthGuard</c> now aborts such a parse as a trappable compile error, well before the overflow.
/// Depth is calibrated against the real parser: normal code peaks near rule-depth ~50, ~600 overflows, the guard
/// trips at 300. Each "too deep" test uses ~400 levels — past the guard, below the overflow, so the guard is what
/// stops it (without it, running either would abort the whole test run).
/// </summary>
public class ParserDepthGuardTests : BaseVBTestFixture
{
    [Fact]
    public async Task DeeplyNestedParens_RaiseCompileError_NotStackOverflow()
    {
        var code = "Dim x\nx = " + new string('(', 400) + "1" + new string(')', 400) + "\n";
        var act = () => Run(code);
        await act.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task DeeplyNestedBlocks_RaiseCompileError_NotStackOverflow()
    {
        // Deep statement-block nesting is a DISTINCT parser recursion path from parenthesised expressions — the
        // per-rule guard covers it too.
        var sb = new StringBuilder();
        for (int i = 0; i < 400; i++) sb.Append("If True Then\n");
        sb.Append("Dim x\n");
        for (int i = 0; i < 400; i++) sb.Append("End If\n");
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
