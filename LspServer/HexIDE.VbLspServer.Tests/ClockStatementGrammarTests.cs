using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class ClockStatementGrammarTests
{
    // vb6.exe SP6, native and p-code, #4681.
    [Theory]
    [InlineData("Date$")]
    [InlineData("Time$")]
    public void StringTwinAssignmentParses(string statement)
    {
        VbDiagnosticsProvider.GetDiagnostics($"Sub Main()\n{statement} = Now\nEnd Sub\n")
            .Should().BeEmpty();
    }
}
