using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class EmptyCallParenthesesTests
{
    [Theory]
    [InlineData("Call Helper()")]
    [InlineData("Call Real.F()")]
    [InlineData("Call k.M()")]
    [InlineData("Call Helper")]
    [InlineData("Call Helper(1)")]
    [InlineData("Call Real.G(1, 2)")]
    [InlineData("Call Real.F")]
    public void Explicit_call_has_no_diagnostics(string statement)
    {
        // Accepted forms measured in #3779 and measuring PR #3780.
        VbDiagnosticsProvider.GetDiagnostics($"Sub Test()\n{statement}\nEnd Sub\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void Missing_closing_parenthesis_still_has_a_diagnostic()
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Test()\nCall Helper(\nEnd Sub\n")
            .Should().NotBeEmpty();
    }
}
