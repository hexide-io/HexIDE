using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class MemberCallParenthesesTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("k.Bump(x)", 7)]
    [InlineData("k.Bump (x)", 7)]
    [InlineData("k.Bump x", 8)]
    [InlineData("Call k.Bump(x)", 8)]
    public async Task Parenthesised_statement_argument_is_a_copy(string call, int expected)
    {
        // #1064: the unspaced form runs as the spaced form does; parentheses pass a copy.
        await RunClasses($"Dim k As Counter, x As Long\nSet k = New Counter\nx = 7\n{call}\nDebug.Print x\n",
            ("Counter", "Public Sub Bump(ByRef x As Long)\nx = x + 1\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value((long)expected)]);
    }
}
